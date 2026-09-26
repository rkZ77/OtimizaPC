"""Codigo de 6 digitos para redefinir a senha (mesmo gesto do Pickia).

O codigo so' existe em claro no e-mail: no banco fica o HMAC dele, amarrado
ao usuario. Um vazamento da tabela nao entrega codigo valido, e o mesmo
codigo de duas pessoas vira hashes diferentes.
"""
import hashlib
import hmac
import secrets
from datetime import datetime, timedelta, timezone

from app import database, settings

CODE_MINUTES = 15
#: Chutes por codigo. Com 5, a chance de acertar um codigo de 6 digitos no
#: chute e' de 1 em 200 mil, e o limite por IP no endpoint corta o resto.
MAX_ATTEMPTS = 5


def new_code() -> str:
    return f"{secrets.randbelow(1_000_000):06d}"


def hash_code(user_id: int, code: str) -> str:
    return hmac.new(settings.JWT_SECRET.encode(), f"{user_id}:{code}".encode(), hashlib.sha256).hexdigest()


def check(row: dict | None, user_id: int, code: str, at: datetime) -> str:
    """Regra pura: ok, missing, expired, locked ou wrong."""
    if row is None or row["used_at"] is not None:
        return "missing"
    if row["expires_at"] <= at:
        return "expired"
    if row["attempts"] >= MAX_ATTEMPTS:
        return "locked"
    return "ok" if hmac.compare_digest(row["code_hash"], hash_code(user_id, code)) else "wrong"


def create(user_id: int) -> str:
    code = new_code()
    with database.transaction() as cur:
        # Pedir outro codigo invalida o anterior: so' o ultimo e-mail vale.
        cur.execute("UPDATE password_resets SET used_at = now() WHERE user_id = %s AND used_at IS NULL", (user_id,))
        cur.execute(
            "INSERT INTO password_resets (user_id, code_hash, expires_at) VALUES (%s, %s, %s)",
            (user_id, hash_code(user_id, code), datetime.now(timezone.utc) + timedelta(minutes=CODE_MINUTES)))
    return code


def consume(user_id: int, code: str) -> bool:
    with database.transaction() as cur:
        cur.execute(
            """SELECT id, code_hash, expires_at, attempts, used_at FROM password_resets
               WHERE user_id = %s ORDER BY id DESC LIMIT 1 FOR UPDATE""", (user_id,))
        row = cur.fetchone()
        result = check(dict(row) if row else None, user_id, code, datetime.now(timezone.utc))
        if result == "wrong":
            cur.execute("UPDATE password_resets SET attempts = attempts + 1 WHERE id = %s", (row["id"],))
        elif result == "ok":
            cur.execute("UPDATE password_resets SET used_at = now() WHERE id = %s", (row["id"],))
        return result == "ok"
