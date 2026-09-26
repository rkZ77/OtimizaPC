"""Envio de e-mail pelo Resend, registro de cada tentativa e os avisos de plano.

Chamada direta a API HTTP do Resend (httpx ja' e' dependencia) em vez do SDK:
e' um POST so', e o SDK viraria mais uma dependencia para atualizar.

Envio nunca derruba o fluxo que o disparou: cadastro, pagamento e troca de
senha terminam certo mesmo com o Resend fora do ar. A falha fica no
email_log, que o admin ve.
"""
import logging
import math
import threading
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo

import httpx

from app import database, email_templates, settings
from app.email_templates import Email

logger = logging.getLogger("fpsx.emails")

RESEND_URL = "https://api.resend.com/emails"
BRT = ZoneInfo("America/Sao_Paulo")

#: Aviso de "vai vencer" sai uma vez, a partir de tantos dias antes.
EXPIRING_DAYS = 3
#: Aviso de "venceu" so' ate' tantos dias depois: quem venceu ha' um mes nao
#: recebe e-mail no primeiro deploy deste codigo.
EXPIRED_GRACE_DAYS = 2


def configured() -> bool:
    return bool(settings.RESEND_API_KEY and settings.RESEND_FROM)


def site_url() -> str:
    return settings.PUBLIC_URL


def data_br(when: datetime) -> str:
    return when.astimezone(BRT).strftime("%d/%m/%Y")


def reais(cents: int) -> str:
    inteiro, centavos = divmod(int(cents), 100)
    return f"R$ {inteiro:,}".replace(",", ".") + f",{centavos:02d}"


# ─── envio ──────────────────────────────────────────────────────────────

def _post(to: str, email: Email) -> str:
    payload = {"from": settings.RESEND_FROM, "to": [to], "subject": email.subject, "text": email.text, "html": email.html}
    if settings.EMAIL_REPLY_TO:
        payload["reply_to"] = settings.EMAIL_REPLY_TO
    r = httpx.post(RESEND_URL, json=payload, timeout=15,
                   headers={"Authorization": f"Bearer {settings.RESEND_API_KEY}"})
    if r.status_code >= 300:
        raise RuntimeError(f"Resend respondeu {r.status_code}: {r.text[:300]}")
    return str(r.json().get("id", ""))


def _log_start(user_id: int | None, to: str, kind: str, subject: str, dedupe_key: str | None) -> int | None:
    row = database.fetch_one(
        """INSERT INTO email_log (user_id, to_email, kind, subject, dedupe_key) VALUES (%s, %s, %s, %s, %s)
           ON CONFLICT (dedupe_key) DO NOTHING RETURNING id""",
        (user_id, to, kind, subject, dedupe_key))
    return row["id"] if row else None


def _log_end(log_id: int | None, status: str, detail: str = "") -> None:
    if log_id is None:
        return
    try:
        database.execute(
            "UPDATE email_log SET status = %s, detail = %s, sent_at = CASE WHEN %s = 'sent' THEN now() END WHERE id = %s",
            (status, detail[:500], status, log_id))
    except Exception as e:  # registro nunca derruba o envio
        logger.warning("[EMAIL] falha ao atualizar registro %s: %s", log_id, e)


def send(kind: str, to: str, email: Email, *, user_id: int | None = None, dedupe_key: str | None = None) -> str:
    """Envia agora e registra. Devolve sent, failed, skipped ou duplicate. Nunca levanta."""
    try:
        log_id = _log_start(user_id, to, kind, email.subject, dedupe_key)
        if log_id is None:
            # Outro worker (ou o staging, que usa o mesmo banco) ja' pegou este aviso.
            return "duplicate"
    except Exception as e:
        logger.warning("[EMAIL] sem registro para %s (%s): %s", kind, to, e)
        if dedupe_key:
            # Sem registro nao ha' como garantir envio unico: melhor nao mandar
            # do que mandar o mesmo aviso a cada hora.
            return "failed"
        log_id = None

    if not configured():
        _log_end(log_id, "skipped", "RESEND_API_KEY ou RESEND_FROM não configurado")
        logger.info("[EMAIL] %s para %s ignorado: envio não configurado", kind, to)
        return "skipped"
    try:
        provider_id = _post(to, email)
    except Exception as e:
        _log_end(log_id, "failed", str(e))
        logger.error("[EMAIL] falha em %s para %s: %s", kind, to, e)
        return "failed"
    _log_end(log_id, "sent", provider_id)
    logger.info("[EMAIL] %s enviado para %s", kind, to)
    return "sent"


def send_later(kind: str, to: str, email: Email, **kwargs) -> None:
    """Envia numa thread: a resposta do cadastro ou do webhook nao espera o Resend."""
    threading.Thread(target=send, args=(kind, to, email), kwargs=kwargs, daemon=True, name=f"email-{kind}").start()


# ─── avisos de plano ────────────────────────────────────────────────────

def notice_for(expires_at: datetime, at: datetime) -> tuple[str, int] | None:
    """Qual aviso uma licenca merece agora: ('plan_expiring', dias), ('plan_expired', 0) ou nada."""
    left = expires_at - at
    if left <= timedelta(0):
        return ("plan_expired", 0) if -left <= timedelta(days=EXPIRED_GRACE_DAYS) else None
    days = math.ceil(left / timedelta(days=1))
    return ("plan_expiring", days) if days <= EXPIRING_DAYS else None


def expiry_candidates() -> list[dict]:
    # Licenca que vai ser substituida por outra melhor ou mais longa (renovou,
    # comprou outro plano) nao gera aviso: "seu plano vai vencer" para quem
    # acabou de pagar e' o pior e-mail possivel.
    return database.fetch_all(
        f"""SELECT l.id, l.user_id, l.status, l.expires_at, p.name AS plan_name, u.email, u.name
            FROM licenses l
            JOIN users u ON u.id = l.user_id AND u.active
            JOIN plans p ON p.key = l.plan_key
            WHERE l.status IN ('active', 'trial')
              AND l.expires_at > now() - interval '{EXPIRED_GRACE_DAYS} days'
              AND l.expires_at <= now() + interval '{EXPIRING_DAYS} days'
              AND NOT EXISTS (
                  SELECT 1 FROM licenses o
                  WHERE o.user_id = l.user_id AND o.id <> l.id
                    AND o.status IN ('active', 'trial') AND o.expires_at > l.expires_at)""")


def run_expiry_notices(at: datetime | None = None) -> dict:
    at = at or datetime.now(timezone.utc)
    counts: dict[str, int] = {}
    for lic in expiry_candidates():
        notice = notice_for(lic["expires_at"], at)
        if notice is None:
            continue
        kind, days = notice
        email = email_templates.aviso_plano(
            lic["name"], lic["plan_name"], data_br(lic["expires_at"]), days, lic["status"] == "trial", site_url())
        # A data de vencimento entra na chave: quem renova e chega perto de
        # vencer de novo recebe um aviso novo, e nao fica bloqueado pelo antigo.
        key = f"{kind}:{lic['id']}:{lic['expires_at'].date().isoformat()}"
        status = send(kind, lic["email"], email, user_id=lic["user_id"], dedupe_key=key)
        counts[status] = counts.get(status, 0) + 1
    return counts


class ExpiryScheduler:
    """Roda os avisos de hora em hora dentro do proprio processo da API.

    Sem cron externo de proposito: no Railway seria mais um servico para
    manter. Varios workers rodando ao mesmo tempo nao duplicam envio, porque
    o email_log tem dedupe_key unica.
    """

    def __init__(self, interval_seconds: int = 3600):
        self._interval = interval_seconds
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        self._thread = threading.Thread(target=self._loop, daemon=True, name="email-expiry")
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()

    def _loop(self) -> None:
        # Espera um pouco no boot: as migrations e o primeiro healthcheck vem antes.
        if self._stop.wait(60):
            return
        while True:
            try:
                counts = run_expiry_notices()
                if counts:
                    logger.info("[EMAIL] avisos de plano: %s", counts)
            except Exception as e:
                logger.error("[EMAIL] avisos de plano falharam: %s", e)
            if self._stop.wait(self._interval):
                return


def admin_log(limit: int, offset: int) -> list[dict]:
    return database.fetch_all(
        """SELECT id, user_id, to_email, kind, subject, status, detail, created_at, sent_at
           FROM email_log ORDER BY id DESC LIMIT %s OFFSET %s""", (limit, offset))
