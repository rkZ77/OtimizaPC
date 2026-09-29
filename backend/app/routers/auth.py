import psycopg2
from fastapi import APIRouter, Depends, HTTPException, Request, Response
from pydantic import BaseModel, EmailStr, Field

from app import auth, email_templates
from app.services import app_settings, emails, licenses, password_reset, referrals, users

router = APIRouter(prefix="/api/auth", tags=["auth"])


class RegisterIn(BaseModel):
    email: EmailStr
    name: str = Field(default="", max_length=80)
    password: str = Field(min_length=8, max_length=128)
    # Codigo de quem indicou (link /r/CODIGO). Codigo errado e' ignorado, nao barra o cadastro.
    ref: str | None = Field(default=None, max_length=20)


class LoginIn(BaseModel):
    email: EmailStr
    password: str = Field(max_length=128)


class ForgotIn(BaseModel):
    email: EmailStr


class ResetIn(BaseModel):
    email: EmailStr
    code: str = Field(pattern=r"^\d{6}$")
    password: str = Field(min_length=8, max_length=128)


def _client_ip(request: Request) -> str:
    return request.client.host if request.client else "?"


@router.post("/register")
def register(body: RegisterIn, request: Request, response: Response):
    auth.rate_limit("register", _client_ip(request), limit=10, window_seconds=3600)
    try:
        user = users.create(body.email, body.name, auth.hash_password(body.password), referrals.referrer_id(body.ref))
    except psycopg2.errors.UniqueViolation:
        raise HTTPException(409, "Já existe uma conta com este e-mail.")
    # Trial nasce com a conta, mas so' vale num PC que ainda nao usou trial
    # (conferido na ativacao do app).
    trial = licenses.create_trial(user["id"])
    trial_days = int(app_settings.get("trial_days") or 0) if trial else 0
    emails.send_later("welcome", user["email"], email_templates.boas_vindas(user["name"], emails.site_url(), trial_days),
                      user_id=user["id"])
    token = auth.create_access_token(user["id"], user["role"])
    auth.set_session_cookie(response, token)
    return {"user": user, "access_token": token}


@router.post("/login")
def login(body: LoginIn, request: Request, response: Response):
    auth.rate_limit("login", f"{_client_ip(request)}:{body.email.lower()}", limit=8)
    # Teto por conta, de qualquer IP: freia forca bruta distribuida em muitos
    # enderecos contra um e-mail so', folgado o bastante para o dono nao travar.
    auth.rate_limit("login-email", body.email.lower(), limit=30, window_seconds=900)
    user = users.get_with_password(body.email)
    # Mesma mensagem para e-mail inexistente e senha errada: nao revela quem tem conta.
    if not user or not auth.verify_password(body.password, user["password_hash"]):
        raise HTTPException(401, "E-mail ou senha incorretos.")
    if not user["active"]:
        raise HTTPException(403, "Conta desativada. Fale com o suporte.")
    user.pop("password_hash")
    token = auth.create_access_token(user["id"], user["role"])
    auth.set_session_cookie(response, token)
    return {"user": user, "access_token": token}


@router.post("/forgot-password")
def forgot_password(body: ForgotIn, request: Request):
    # Dois limites: por IP freia varredura de e-mails, por e-mail impede
    # encher a caixa de alguem com codigos.
    auth.rate_limit("forgot-ip", _client_ip(request), limit=10, window_seconds=900)
    auth.rate_limit("forgot-email", body.email.lower(), limit=3, window_seconds=900)
    user = users.get_by_email(body.email)
    # Resposta igual exista ou nao a conta: a tela nao revela quem e' cliente.
    if user and user["active"]:
        code = password_reset.create(user["id"])
        emails.send_later("password_reset", user["email"],
                          email_templates.codigo_senha(user["name"], code, emails.site_url(), user["email"], password_reset.CODE_MINUTES),
                          user_id=user["id"])
    return {"ok": True, "minutes": password_reset.CODE_MINUTES}


@router.post("/reset-password")
def reset_password(body: ResetIn, request: Request):
    auth.rate_limit("reset", _client_ip(request), limit=15, window_seconds=900)
    user = users.get_by_email(body.email)
    if not user or not user["active"] or not password_reset.consume(user["id"], body.code):
        raise HTTPException(400, "Código inválido ou expirado. Peça um novo.")
    users.set_password(user["id"], auth.hash_password(body.password))
    emails.send_later("password_changed", user["email"], email_templates.senha_alterada(user["name"], emails.site_url()),
                      user_id=user["id"])
    return {"ok": True}


@router.post("/logout")
def logout(response: Response):
    auth.clear_session_cookie(response)
    return {"ok": True}


@router.get("/me")
def me(user: dict = Depends(auth.current_user)):
    return {"user": {k: v for k, v in user.items() if k != "password_changed_at"}}
