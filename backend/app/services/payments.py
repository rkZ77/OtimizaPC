"""PaymentService + WebhookService.

Pagamento nao se espalha pelo codigo (secao 37): o resto do backend so' chama
`create_checkout` e `apply_approved_payment`. O provedor concreto (Mercado
Pago, com PIX e cartao no mesmo checkout) fica atras de `PaymentProvider`.

Como no Pickia, existe UM caminho de ativacao: webhook, retorno do checkout e
a reconciliacao do admin terminam todos em `apply_approved_payment`, que e'
idempotente pelo id do pagamento no provedor.
"""
import hashlib
import hmac
import logging
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Protocol

from app import database, settings
from app.services import licenses, plans

logger = logging.getLogger("fpsx.payments")


@dataclass(frozen=True)
class NormalizedPayment:
    provider: str
    provider_payment_id: str
    status: str  # approved, pending, rejected, refunded, cancelled
    amount_cents: int
    reference: str


@dataclass(frozen=True)
class Reference:
    user_id: int
    plan_key: str
    coupon: str | None


class PaymentProvider(Protocol):
    name: str

    def create_checkout(self, *, title: str, amount_cents: int, reference: str, payer_email: str) -> str: ...

    def fetch_payment(self, payment_id: str) -> NormalizedPayment: ...


# ─── regras puras ───────────────────────────────────────────────────────

def make_reference(user_id: int, plan_key: str, coupon: str | None) -> str:
    return f"{user_id}:{plan_key}:{coupon or ''}"


def parse_reference(reference: str) -> Reference | None:
    parts = reference.split(":")
    if len(parts) != 3 or not parts[0].isdigit() or not parts[1]:
        return None
    return Reference(int(parts[0]), parts[1], parts[2] or None)


def discounted_cents(price_cents: int, percent_off: int) -> int:
    # Arredonda a favor do cliente, em centavos inteiros.
    return max(0, price_cents - (price_cents * percent_off + 99) // 100)


def coupon_percent(coupon: dict | None, at: datetime) -> int:
    if coupon is None or not coupon["active"]:
        return 0
    if coupon["expires_at"] is not None and coupon["expires_at"] <= at:
        return 0
    if coupon["max_uses"] is not None and coupon["used_count"] >= coupon["max_uses"]:
        return 0
    return coupon["percent_off"]


def mp_manifest(data_id: str, request_id: str, ts: str) -> str:
    """Template do Mercado Pago: id:<id>;request-id:<rid>;ts:<ts>; com o ';'
    final. Campo ausente sai do template inteiro (licao do Pickia: sem isso
    toda notificacao legitima virava 403)."""
    return "".join(f"{k}:{v};" for k, v in (("id", data_id), ("request-id", request_id), ("ts", ts)) if v)


def verify_mp_signature(x_signature: str, x_request_id: str, data_id: str, secret: str) -> bool:
    if not secret or not x_signature:
        return False
    parts = dict(p.split("=", 1) for p in (s.strip() for s in x_signature.split(",")) if "=" in p)
    ts, v1 = parts.get("ts", ""), parts.get("v1", "")
    if not ts or not v1:
        return False
    for candidate in dict.fromkeys([data_id, data_id.lower()]):
        expected = hmac.new(secret.encode(), mp_manifest(candidate, x_request_id, ts).encode(), hashlib.sha256).hexdigest()
        if hmac.compare_digest(expected, v1):
            return True
    return False


# ─── provedor Mercado Pago ──────────────────────────────────────────────

class MercadoPagoProvider:
    name = "mercadopago"

    def __init__(self, access_token: str):
        import mercadopago

        self._sdk = mercadopago.SDK(access_token)

    def create_checkout(self, *, title: str, amount_cents: int, reference: str, payer_email: str) -> str:
        pref = self._sdk.preference().create({
            "items": [{"title": title, "quantity": 1, "currency_id": "BRL", "unit_price": amount_cents / 100}],
            "payer": {"email": payer_email},
            "external_reference": reference,
            # Nome na fatura do cartao (ate 13 caracteres): sem ele aparece o
            # nome da conta do Mercado Pago, e a pessoa nao reconhece a compra.
            "statement_descriptor": "RKZFPS",
            "back_urls": {
                "success": f"{settings.PUBLIC_URL}/conta?pagamento=aprovado",
                "pending": f"{settings.PUBLIC_URL}/conta?pagamento=pendente",
                "failure": f"{settings.PUBLIC_URL}/planos?pagamento=recusado",
            },
            "auto_return": "approved",
            "notification_url": f"{settings.PUBLIC_URL}/api/payments/webhook",
        })
        body = pref.get("response", {})
        url = body.get("init_point")
        if not url:
            raise RuntimeError(f"Mercado Pago não criou o checkout: {body}")
        return url

    def fetch_payment(self, payment_id: str) -> NormalizedPayment:
        body = self._sdk.payment().get(payment_id).get("response", {})
        return NormalizedPayment(
            provider=self.name,
            provider_payment_id=str(body.get("id", payment_id)),
            status=str(body.get("status", "unknown")),
            amount_cents=round(float(body.get("transaction_amount") or 0) * 100),
            reference=str(body.get("external_reference") or ""),
        )


def provider() -> PaymentProvider:
    if not settings.MERCADOPAGO_ACCESS_TOKEN:
        raise RuntimeError("Pagamentos não configurados (MERCADOPAGO_ACCESS_TOKEN).")
    return MercadoPagoProvider(settings.MERCADOPAGO_ACCESS_TOKEN)


# ─── fluxo ──────────────────────────────────────────────────────────────

def record_event(source: str, status: str, payment_id: str = "", detail: str = "") -> None:
    try:
        database.execute(
            "INSERT INTO payment_events (source, status, provider_payment_id, detail) VALUES (%s, %s, %s, %s)",
            (source, status, payment_id, detail[:500]))
    except Exception as e:  # trilha nunca derruba o fluxo principal
        logger.warning("[PAYMENTS] falha ao registrar evento: %s", e)


def get_coupon(code: str | None) -> dict | None:
    if not code:
        return None
    return database.fetch_one("SELECT * FROM coupons WHERE code = %s", (code.strip().upper(),))


def quote(plan_key: str, coupon_code: str | None) -> dict:
    plan = plans.get(plan_key)
    if plan is None or not plan["active"] or plan["price_cents"] <= 0:
        raise ValueError("Plano indisponível para compra.")
    coupon = get_coupon(coupon_code)
    pct = coupon_percent(coupon, datetime.now(timezone.utc))
    if coupon_code and pct == 0:
        raise ValueError("Cupom inválido ou expirado.")
    return {
        "plan": plan,
        "coupon": coupon["code"] if coupon and pct else None,
        "percent_off": pct,
        "amount_cents": discounted_cents(plan["price_cents"], pct),
    }


PERIOD_LABEL = {"monthly": "mensal", "quarterly": "trimestral", "annual": "anual"}


def create_checkout(user: dict, plan_key: str, coupon_code: str | None) -> dict:
    q = quote(plan_key, coupon_code)
    ref = make_reference(user["id"], plan_key, q["coupon"])
    url = provider().create_checkout(
        title=f"RKZFPS {q['plan']['name']} {PERIOD_LABEL.get(q['plan'].get('period', 'monthly'), '')}".strip(),
        amount_cents=q["amount_cents"], reference=ref, payer_email=user["email"])
    return {"checkout_url": url, "amount_cents": q["amount_cents"]}


def apply_approved_payment(payment: NormalizedPayment, source: str) -> dict:
    """Unico caminho que transforma pagamento em licenca. Idempotente."""
    if payment.status != "approved":
        record_event(source, f"ignored:{payment.status}", payment.provider_payment_id)
        return {"applied": False, "reason": payment.status}

    ref = parse_reference(payment.reference)
    if ref is None:
        record_event(source, "bad_reference", payment.provider_payment_id, payment.reference)
        return {"applied": False, "reason": "bad_reference"}

    plan = plans.get(ref.plan_key)
    if plan is None:
        record_event(source, "unknown_plan", payment.provider_payment_id, ref.plan_key)
        return {"applied": False, "reason": "unknown_plan"}

    # Valor pago tem que cobrir o preco com o cupom da referencia. Protege
    # contra checkout adulterado (preco trocado no cliente antes de pagar).
    coupon = get_coupon(ref.coupon)
    expected = discounted_cents(plan["price_cents"], coupon["percent_off"] if coupon else 0)
    if payment.amount_cents + 1 < expected:
        record_event(source, "amount_mismatch", payment.provider_payment_id,
                     f"pago {payment.amount_cents}, esperado {expected}")
        return {"applied": False, "reason": "amount_mismatch"}

    at = datetime.now(timezone.utc)
    with database.transaction() as cur:
        cur.execute(
            """INSERT INTO payments (user_id, plan_key, provider, provider_payment_id, status, amount_cents, coupon_code, approved_at)
               VALUES (%s, %s, %s, %s, 'approved', %s, %s, %s)
               ON CONFLICT (provider, provider_payment_id) DO NOTHING RETURNING id""",
            (ref.user_id, plan["key"], payment.provider, payment.provider_payment_id, payment.amount_cents, ref.coupon, at))
        inserted = cur.fetchone()
        if inserted is None:
            return {"applied": False, "reason": "already_applied"}

        lic = licenses.grant(cur, ref.user_id, plan, at)
        cur.execute("UPDATE payments SET license_id = %s WHERE id = %s", (lic["id"], inserted["id"]))
        if ref.coupon:
            cur.execute("UPDATE coupons SET used_count = used_count + 1 WHERE code = %s", (ref.coupon,))

    record_event(source, "applied", payment.provider_payment_id, f"user {ref.user_id} plano {plan['key']}")
    _send_receipt(ref.user_id, plan, lic, payment)
    return {"applied": True, "license_id": lic["id"], "expires_at": lic["expires_at"].isoformat()}


def _send_receipt(user_id: int, plan: dict, lic: dict, payment: NormalizedPayment) -> None:
    """E-mail de pagamento aprovado. Sai DEPOIS do commit: e-mail de "tudo
    certo" para uma transacao que depois voltou atras seria pior que nenhum.
    A chave de dedupe e' o pagamento, entao webhook repetido nao manda de novo."""
    from app import email_templates
    from app.services import emails, users

    try:
        user = users.get_by_id(user_id)
        if user is None:
            return
        label = f"{plan['name']} {PERIOD_LABEL.get(plan.get('period', 'monthly'), '')}".strip()
        email = email_templates.pagamento_aprovado(
            user["name"], label, emails.data_br(lic["expires_at"]), emails.reais(payment.amount_cents), emails.site_url())
        emails.send_later("payment_approved", user["email"], email, user_id=user_id,
                          dedupe_key=f"payment:{payment.provider}:{payment.provider_payment_id}")
    except Exception as e:  # recibo nunca desfaz a ativacao
        logger.warning("[PAYMENTS] recibo nao enviado para user %s: %s", user_id, e)


#: Estados do Mercado Pago em que o dinheiro voltou para o cliente.
REVERSED = ("refunded", "charged_back")


def revoke_payment(payment: NormalizedPayment, source: str) -> dict:
    """Reembolso ou chargeback de um pagamento ja' aplicado: tira da licenca
    os dias que aquele pagamento deu. Sem isso, quem pede o dinheiro de volta
    (7 dias do CDC) ou contesta no cartao seguiria com o plano pago.
    Idempotente: so' age sobre pagamento ainda 'approved'."""
    with database.transaction() as cur:
        cur.execute(
            """UPDATE payments SET status = %s
               WHERE provider = %s AND provider_payment_id = %s AND status = 'approved'
               RETURNING license_id, plan_key, user_id""",
            (payment.status, payment.provider, payment.provider_payment_id))
        row = cur.fetchone()
        if row is None:
            record_event(source, f"ignored:{payment.status}", payment.provider_payment_id)
            return {"applied": False, "reason": "not_applied"}
        plan = plans.get(row["plan_key"])
        if row["license_id"] is not None and plan is not None:
            # Subtrai os dias em vez de bloquear: a mesma licenca pode somar
            # outras compras que continuam valendo.
            cur.execute(
                "UPDATE licenses SET expires_at = expires_at - make_interval(days => %s) WHERE id = %s",
                (plan["days"], row["license_id"]))
    record_event(source, payment.status, payment.provider_payment_id, f"user {row['user_id']} plano {row['plan_key']}")
    return {"applied": False, "revoked": True}


def handle_webhook(data_id: str, x_signature: str, x_request_id: str) -> dict:
    if not verify_mp_signature(x_signature, x_request_id, data_id, settings.MERCADOPAGO_WEBHOOK_SECRET):
        record_event("webhook", "bad_signature", data_id)
        return {"ok": False, "reason": "bad_signature"}
    # Nunca confiar no corpo da notificacao: o estado vem de uma consulta
    # direta ao provedor.
    payment = provider().fetch_payment(data_id)
    if payment.status in REVERSED:
        return revoke_payment(payment, "webhook")
    return apply_approved_payment(payment, "webhook")


def list_for_user(user_id: int) -> list[dict]:
    return database.fetch_all(
        "SELECT id, plan_key, status, amount_cents, coupon_code, created_at, approved_at FROM payments WHERE user_id = %s ORDER BY id DESC",
        (user_id,))


def admin_list(limit: int, offset: int) -> list[dict]:
    return database.fetch_all(
        """SELECT p.*, u.email FROM payments p LEFT JOIN users u ON u.id = p.user_id
           ORDER BY p.id DESC LIMIT %s OFFSET %s""", (limit, offset))
