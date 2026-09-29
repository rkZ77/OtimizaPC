"""Licencas, dispositivos e o token assinado que o app desktop guarda.

A regra de negocio (qual licenca vale, se cabe mais um PC, como estender)
fica em funcoes puras no topo do arquivo, testaveis sem banco. O SQL fica
embaixo, fino.
"""
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone

from app import database, signing
from app.services import app_settings, plans

TOKEN_VERSION = 1


def now() -> datetime:
    return datetime.now(timezone.utc)


# ─── regras puras ───────────────────────────────────────────────────────

def effective_status(lic: dict, at: datetime) -> str:
    """Status real: 'active'/'trial' vencida e' 'expired' mesmo que ninguem
    tenha rodado job de expiracao. Nao depende de cron para ser correto."""
    if lic["status"] == "blocked":
        return "blocked"
    if lic["expires_at"] <= at:
        return "expired"
    return lic["status"]


def best_license(licenses: list[dict], at: datetime) -> dict | None:
    """Licenca que vale agora: a de maior tier entre as utilizaveis; empate
    vai para a que vence mais tarde."""
    usable = [l for l in licenses if effective_status(l, at) in ("active", "trial")]
    if not usable:
        return None
    return max(usable, key=lambda l: (plans.rank(l["tier"]), l["expires_at"]))


@dataclass(frozen=True)
class ActivationDecision:
    outcome: str  # "existing", "new" ou "limit"
    device: dict | None = None


def decide_activation(active_devices: list[dict], device_hash: str, max_devices: int) -> ActivationDecision:
    for d in active_devices:
        if d["device_hash"] == device_hash:
            return ActivationDecision("existing", d)
    if len(active_devices) >= max_devices:
        return ActivationDecision("limit")
    return ActivationDecision("new")


def extended_expiration(current: dict | None, days: int, at: datetime) -> datetime:
    """Compra em cima de licenca ainda valida soma os dias ao que resta:
    quem renova antes de vencer nao perde os dias pagos."""
    base = at
    if current is not None and effective_status(current, at) in ("active", "trial") and current["expires_at"] > at:
        base = current["expires_at"]
    return base + timedelta(days=days)


def build_token_payload(user: dict, lic: dict | None, free_plan: dict, device_hash: str, grace_days: int, at: datetime,
                        ended: dict | None = None) -> dict:
    """O que vai assinado para o app. `valid_until` e' o limite offline: o app
    precisa falar com o servidor antes disso, ou cai para Free.

    `ended` e' a ultima licenca que a pessoa teve, quando nenhuma vale mais:
    o app recebe "expired" em vez de "free" e mostra o resumo do que o RKZFPS
    fez e o convite para renovar, em vez de tratar como quem nunca assinou.
    O plano efetivo continua Free: nada pago fica liberado."""
    if lic is None:
        tier, plan_key, status, expires = "free", free_plan["key"], "free", None
        if ended is not None:
            status = "blocked" if ended["status"] == "blocked" else "expired"
            expires = ended["expires_at"]
    else:
        tier, plan_key, status, expires = plans.agent_tier(lic["tier"]), lic["plan_key"], effective_status(lic, at), lic["expires_at"]

    valid_until = at + timedelta(days=grace_days)
    # So' a licenca vigente limita o uso offline: a data da que ja' acabou
    # esta' no passado e faria o token nascer vencido.
    if lic is not None and expires is not None and expires < valid_until:
        valid_until = expires

    return {
        "v": TOKEN_VERSION,
        "uid": user["id"],
        "email": user["email"],
        "plan": tier,
        "plan_key": plan_key,
        "status": status,
        "license_id": lic["id"] if lic else None,
        "device": device_hash,
        "issued_at": at.isoformat(),
        "expires_at": expires.isoformat() if expires else None,
        "valid_until": valid_until.isoformat(),
    }


# ─── SQL ────────────────────────────────────────────────────────────────

def for_user(user_id: int) -> list[dict]:
    return database.fetch_all("SELECT * FROM licenses WHERE user_id = %s ORDER BY expires_at DESC", (user_id,))


def current(user_id: int) -> dict | None:
    return best_license(for_user(user_id), now())


def last_license(user_id: int) -> dict | None:
    """A licenca que vence por ultimo, valendo ou nao: quando nenhuma vale, e'
    ela que diz que a pessoa ja' teve plano (e qual)."""
    todas = for_user(user_id)
    return todas[0] if todas else None


def active_devices(user_id: int) -> list[dict]:
    return database.fetch_all(
        "SELECT * FROM devices WHERE user_id = %s AND deactivated_at IS NULL ORDER BY last_seen_at DESC", (user_id,))


def max_devices_for(lic: dict | None) -> int:
    if lic is not None:
        return lic["max_devices"]
    free = plans.get("free")
    return free["max_devices"] if free else 1


def create_trial(user_id: int) -> dict | None:
    days = int(app_settings.get("trial_days") or 0)
    if days <= 0:
        return None
    plan = plans.get(app_settings.get("trial_plan") or "pro")
    if plan is None:
        return None
    return database.fetch_one(
        """INSERT INTO licenses (user_id, plan_key, tier, status, max_devices, expires_at)
           VALUES (%s, %s, %s, 'trial', 1, %s) RETURNING *""",
        # Trial vale para 1 PC, seja qual for o plano de referencia.
        (user_id, plan["key"], plan["tier"], now() + timedelta(days=days)),
    )


def trial_already_used(device_hash: str, user_id: int) -> bool:
    row = database.fetch_one("SELECT user_id FROM trial_devices WHERE device_hash = %s", (device_hash,))
    return row is not None and row["user_id"] != user_id


def mark_trial_device(device_hash: str, user_id: int) -> None:
    database.execute(
        "INSERT INTO trial_devices (device_hash, user_id) VALUES (%s, %s) ON CONFLICT DO NOTHING", (device_hash, user_id))


def grant(cur, user_id: int, plan: dict, at: datetime) -> dict:
    """Aplica uma compra: estende a licenca paga vigente ou cria uma nova.
    Recebe o cursor para rodar na MESMA transacao que registra o pagamento."""
    cur.execute(
        "SELECT * FROM licenses WHERE user_id = %s AND status IN ('active', 'trial') ORDER BY expires_at DESC FOR UPDATE",
        (user_id,),
    )
    existing = [dict(r) for r in cur.fetchall()]
    # Mesmo tier, qualquer periodo: quem tem o Pro mensal e compra o Pro anual
    # soma os dias na mesma licenca, em vez de ficar com duas.
    paid = [l for l in existing if l["status"] == "active" and l["tier"] == plan["tier"]]
    current_lic = best_license(paid, at)
    expires = extended_expiration(current_lic, plan["days"], at)

    # Trial acaba no momento da compra: senao o usuario teria duas licencas
    # e o limite de PCs do trial confundiria a conta.
    cur.execute("UPDATE licenses SET status = 'expired' WHERE user_id = %s AND status = 'trial'", (user_id,))

    if current_lic is not None:
        cur.execute(
            "UPDATE licenses SET expires_at = %s, max_devices = %s WHERE id = %s RETURNING *",
            (expires, plan["max_devices"], current_lic["id"]),
        )
    else:
        cur.execute(
            """INSERT INTO licenses (user_id, plan_key, tier, status, max_devices, expires_at)
               VALUES (%s, %s, %s, 'active', %s, %s) RETURNING *""",
            (user_id, plan["key"], plan["tier"], plan["max_devices"], expires),
        )
    lic = dict(cur.fetchone())
    cur.execute("UPDATE devices SET license_id = %s WHERE user_id = %s AND deactivated_at IS NULL", (lic["id"], user_id))
    return lic


def upsert_device(user_id: int, lic: dict | None, decision: ActivationDecision, device_hash: str, name: str, build: str, version: str) -> dict:
    if decision.outcome == "existing":
        return database.fetch_one(
            """UPDATE devices SET last_seen_at = now(), license_id = %s, name = %s, windows_build = %s, agent_version = %s
               WHERE id = %s RETURNING *""",
            (lic["id"] if lic else None, name, build, version, decision.device["id"]),
        )
    return database.fetch_one(
        """INSERT INTO devices (user_id, license_id, device_hash, name, windows_build, agent_version)
           VALUES (%s, %s, %s, %s, %s, %s) RETURNING *""",
        (user_id, lic["id"] if lic else None, device_hash, name, build, version),
    )


def device_by_hash(user_id: int, device_hash: str) -> dict | None:
    return database.fetch_one(
        "SELECT * FROM devices WHERE user_id = %s AND device_hash = %s AND deactivated_at IS NULL", (user_id, device_hash))


def deactivate_device(user_id: int, device_id: int) -> int:
    return database.execute(
        "UPDATE devices SET deactivated_at = now() WHERE id = %s AND user_id = %s AND deactivated_at IS NULL",
        (device_id, user_id),
    )


def touch_device(device_id: int, version: str, build: str) -> None:
    database.execute(
        "UPDATE devices SET last_seen_at = now(), agent_version = %s, windows_build = %s WHERE id = %s",
        (version, build, device_id))


def issue_token(user: dict, device_hash: str) -> tuple[str, dict]:
    lic = current(user["id"])
    ended = last_license(user["id"]) if lic is None else None
    payload = build_token_payload(user, lic, plans.get("free") or {"key": "free"}, device_hash,
                                  int(app_settings.get("offline_grace_days") or 7), now(), ended=ended)
    return signing.sign(payload), payload


def set_blocked(license_id: int, blocked: bool, reason: str) -> dict | None:
    if blocked:
        return database.fetch_one(
            "UPDATE licenses SET status = 'blocked', blocked_reason = %s WHERE id = %s RETURNING *", (reason, license_id))
    # Desbloquear volta para 'active'; se ja venceu, effective_status mostra expired.
    return database.fetch_one(
        "UPDATE licenses SET status = 'active', blocked_reason = NULL WHERE id = %s RETURNING *", (license_id,))


def admin_extend(license_id: int, days: int) -> dict | None:
    return database.fetch_one(
        "UPDATE licenses SET expires_at = GREATEST(expires_at, now()) + make_interval(days => %s) WHERE id = %s RETURNING *",
        (days, license_id))


def admin_list(limit: int, offset: int) -> list[dict]:
    return database.fetch_all(
        """SELECT l.*, u.email,
                  (SELECT count(*) FROM devices d WHERE d.license_id = l.id AND d.deactivated_at IS NULL) AS devices
           FROM licenses l JOIN users u ON u.id = l.user_id
           ORDER BY l.created_at DESC LIMIT %s OFFSET %s""",
        (limit, offset))
