"""Indicacao: quem traz um cliente pagante ganha dias de plano.

O premio sai so' na primeira compra APROVADA da pessoa indicada, dentro da
mesma transacao que registra o pagamento: sem pagamento nao ha' premio, entao
criar contas falsas pelo proprio link nao rende nada.
"""
import secrets
from datetime import datetime, timedelta

from app import database
from app.services import app_settings

# Sem 0/O e 1/I/L: o codigo e' lido em voz alta e digitado a partir de print.
_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"
CODE_LENGTH = 7


def new_code() -> str:
    return "".join(secrets.choice(_ALPHABET) for _ in range(CODE_LENGTH))


def normalize(code: str | None) -> str:
    return (code or "").strip().upper()[:20]


def reward_days() -> int:
    return int(app_settings.get("referral_days") or 0)


def code_for(user_id: int) -> str:
    """Codigo da pessoa, criado na primeira vez que ela abre a tela de indicacao."""
    row = database.fetch_one("SELECT referral_code FROM users WHERE id = %s", (user_id,))
    if row and row["referral_code"]:
        return row["referral_code"]
    # Colisao e' rarissima (31^7), mas o UNIQUE decide: tenta de novo.
    for _ in range(5):
        updated = database.fetch_one(
            """UPDATE users SET referral_code = %s WHERE id = %s AND referral_code IS NULL
               AND NOT EXISTS (SELECT 1 FROM users WHERE referral_code = %s) RETURNING referral_code""",
            (code := new_code(), user_id, code))
        if updated:
            return updated["referral_code"]
        row = database.fetch_one("SELECT referral_code FROM users WHERE id = %s", (user_id,))
        if row and row["referral_code"]:
            return row["referral_code"]
    raise RuntimeError("Não foi possível gerar o código de indicação.")


def referrer_id(code: str | None) -> int | None:
    code = normalize(code)
    if not code:
        return None
    row = database.fetch_one("SELECT id FROM users WHERE referral_code = %s AND active", (code,))
    return row["id"] if row else None


def summary(user_id: int) -> dict:
    row = database.fetch_one(
        """SELECT (SELECT count(*) FROM users WHERE referred_by = %s) AS signups,
                  (SELECT count(*) FROM referral_rewards WHERE referrer_id = %s) AS rewarded,
                  (SELECT coalesce(sum(days), 0) FROM referral_rewards WHERE referrer_id = %s) AS days_earned""",
        (user_id, user_id, user_id))
    return {"code": code_for(user_id), "reward_days": reward_days(), **(row or {})}


def reward_on_payment(cur, referred_id: int, payment_id: int, at: datetime) -> dict | None:
    """Premia quem indicou, na transacao do pagamento. Idempotente pelo UNIQUE."""
    days = reward_days()
    if days <= 0:
        return None
    cur.execute("SELECT referred_by FROM users WHERE id = %s", (referred_id,))
    row = cur.fetchone()
    referrer = row["referred_by"] if row else None
    if not referrer or referrer == referred_id:
        return None
    cur.execute("SELECT 1 FROM referral_rewards WHERE referred_id = %s", (referred_id,))
    if cur.fetchone():
        return None

    # Quem tem plano (pago ou teste) ganha os dias no plano atual. Quem esta'
    # no Free ganha os dias do plano de teste: e' o jeito de conhecer o pago.
    cur.execute(
        """SELECT * FROM licenses WHERE user_id = %s AND status IN ('active', 'trial') AND expires_at > %s
           ORDER BY (status = 'active') DESC, expires_at DESC LIMIT 1 FOR UPDATE""", (referrer, at))
    lic = cur.fetchone()
    if lic:
        cur.execute("UPDATE licenses SET expires_at = expires_at + %s WHERE id = %s RETURNING *",
                    (timedelta(days=days), lic["id"]))
    else:
        cur.execute("SELECT * FROM plans WHERE key = %s", (app_settings.get("trial_plan") or "pro",))
        plan = cur.fetchone()
        if plan is None:
            return None
        cur.execute(
            """INSERT INTO licenses (user_id, plan_key, tier, status, max_devices, expires_at)
               VALUES (%s, %s, %s, 'active', 1, %s) RETURNING *""",
            (referrer, plan["key"], plan["tier"], at + timedelta(days=days)))
    lic = dict(cur.fetchone())
    cur.execute("UPDATE devices SET license_id = %s WHERE user_id = %s AND deactivated_at IS NULL", (lic["id"], referrer))
    cur.execute(
        """INSERT INTO referral_rewards (referrer_id, referred_id, payment_id, license_id, days)
           VALUES (%s, %s, %s, %s, %s) ON CONFLICT (referred_id) DO NOTHING""",
        (referrer, referred_id, payment_id, lic["id"], days))
    return {"referrer_id": referrer, "days": days, "license_id": lic["id"]}
