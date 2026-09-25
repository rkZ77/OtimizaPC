import psycopg2
from fastapi import APIRouter, Depends, HTTPException, Request, Response
from pydantic import BaseModel, EmailStr, Field

from app import auth
from app.services import licenses, users

router = APIRouter(prefix="/api/auth", tags=["auth"])


class RegisterIn(BaseModel):
    email: EmailStr
    name: str = Field(default="", max_length=80)
    password: str = Field(min_length=8, max_length=128)


class LoginIn(BaseModel):
    email: EmailStr
    password: str = Field(max_length=128)


def _client_ip(request: Request) -> str:
    return request.client.host if request.client else "?"


@router.post("/register")
def register(body: RegisterIn, request: Request, response: Response):
    auth.rate_limit("register", _client_ip(request), limit=10, window_seconds=3600)
    try:
        user = users.create(body.email, body.name, auth.hash_password(body.password))
    except psycopg2.errors.UniqueViolation:
        raise HTTPException(409, "Já existe uma conta com este e-mail.")
    # Trial nasce com a conta, mas so' vale num PC que ainda nao usou trial
    # (conferido na ativacao do app).
    licenses.create_trial(user["id"])
    token = auth.create_access_token(user["id"], user["role"])
    auth.set_session_cookie(response, token)
    return {"user": user, "access_token": token}


@router.post("/login")
def login(body: LoginIn, request: Request, response: Response):
    auth.rate_limit("login", f"{_client_ip(request)}:{body.email.lower()}", limit=8)
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


@router.post("/logout")
def logout(response: Response):
    auth.clear_session_cookie(response)
    return {"ok": True}


@router.get("/me")
def me(user: dict = Depends(auth.current_user)):
    return {"user": user}
