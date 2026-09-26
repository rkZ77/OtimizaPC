"""Senha, JWT e dependencias de autenticacao.

Mesmo desenho do Pickia: cookie httpOnly para o site, e Bearer para o app
desktop, que nao e' navegador e nao guarda cookie.
"""
import threading
import time
from datetime import datetime, timedelta, timezone

import bcrypt
import jwt
from fastapi import Depends, HTTPException, Request, status

from app import settings

ALGORITHM = "HS256"
ACCESS_HOURS = 12
COOKIE_NAME = "fpsx_session"


def hash_password(password: str) -> str:
    return bcrypt.hashpw(password.encode(), bcrypt.gensalt()).decode()


def verify_password(plain: str, hashed: str) -> bool:
    try:
        return bcrypt.checkpw(plain.encode(), hashed.encode())
    except ValueError:
        return False


def create_access_token(user_id: int, role: str) -> str:
    now = datetime.now(timezone.utc)
    payload = {
        "sub": str(user_id),
        "role": role,
        "type": "access",
        "iat": int(now.timestamp()),
        "exp": now + timedelta(hours=ACCESS_HOURS),
    }
    return jwt.encode(payload, settings.JWT_SECRET, algorithm=ALGORITHM)


def decode_token(token: str) -> dict:
    try:
        data = jwt.decode(token, settings.JWT_SECRET, algorithms=[ALGORITHM])
    except jwt.PyJWTError:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Sessão inválida ou expirada.")
    if data.get("type") != "access":
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Sessão inválida.")
    return data


def set_session_cookie(response, token: str) -> None:
    response.set_cookie(
        COOKIE_NAME, token, httponly=True, secure=settings.IS_PRODUCTION,
        samesite="strict", max_age=ACCESS_HOURS * 3600, path="/",
    )


def clear_session_cookie(response) -> None:
    response.delete_cookie(COOKIE_NAME, path="/")


def _token_from(request: Request) -> str | None:
    header = request.headers.get("authorization", "")
    if header.lower().startswith("bearer "):
        return header[7:].strip()
    return request.cookies.get(COOKIE_NAME)


def current_user(request: Request) -> dict:
    """Usuario da sessao. Confere no banco se a conta segue ativa: conta
    bloqueada pelo admin para de responder na hora, sem esperar o token vencer."""
    from app.services import users

    token = _token_from(request)
    if not token:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Faça login para continuar.")
    data = decode_token(token)
    user = users.get_by_id(int(data["sub"]))
    if not user or not user["active"]:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Conta indisponível.")
    if issued_before_password_change(data, user.get("password_changed_at")):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Sua senha foi alterada. Entre de novo.")
    return user


def issued_before_password_change(token_data: dict, changed_at: datetime | None) -> bool:
    """Token emitido antes da troca de senha nao vale mais: quem trocou a senha
    porque perdeu o PC nao pode continuar logado nele. Compara em segundos
    inteiros, que e' a resolucao do iat: login no mesmo segundo da troca vale."""
    if changed_at is None:
        return False
    return int(token_data.get("iat", 0)) < int(changed_at.timestamp())


def require_admin(user: dict = Depends(current_user)) -> dict:
    if user["role"] != "admin":
        raise HTTPException(status.HTTP_403_FORBIDDEN, "Acesso restrito.")
    return user


# ─── limite de tentativas ────────────────────────────────────────────────
# Em memoria, por processo: suficiente para frear forca bruta de senha em
# um worker. Com mais workers o teto efetivo multiplica, e isso e' aceito.
_hits: dict[str, list[float]] = {}
_hits_lock = threading.Lock()


def rate_limit(bucket: str, key: str, limit: int, window_seconds: int = 300) -> None:
    now = time.monotonic()
    k = f"{bucket}:{key}"
    with _hits_lock:
        recent = [t for t in _hits.get(k, []) if now - t < window_seconds]
        if len(recent) >= limit:
            raise HTTPException(status.HTTP_429_TOO_MANY_REQUESTS, "Muitas tentativas. Aguarde alguns minutos.")
        recent.append(now)
        _hits[k] = recent


def reset_rate_limits() -> None:
    with _hits_lock:
        _hits.clear()
