from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, Field

from app import auth, settings, signing
from app.services import app_settings, assistant, catalog, plans

router = APIRouter(prefix="/api/public", tags=["public"])


@router.get("/plans")
def list_plans():
    """Catalogo de planos para visitante deslogado. Unica fonte de preco da tela."""
    return {"currency": "BRL", "trial_days": app_settings.get("trial_days"), "plans": plans.list_active()}


@router.get("/releases")
def releases():
    return {"releases": catalog.latest_releases()}


@router.get("/changelog")
def changelog():
    return {"versions": catalog.changelog()}


@router.get("/license-key")
def license_key():
    return {"algorithm": "ECDSA-P256-SHA256", "public_key_pem": signing.public_key_pem()}


def _visitor(request: Request) -> str:
    # Atras do proxy do Railway, request.client e' o proxy: o visitante e' o
    # primeiro endereco do X-Forwarded-For. Serve para limitar custo, nao para autenticar.
    forwarded = request.headers.get("x-forwarded-for", "")
    return forwarded.split(",")[0].strip() or (request.client.host if request.client else "?")


class AssistantIn(BaseModel):
    messages: list[dict] = Field(min_length=1, max_length=40)


@router.get("/assistant")
def assistant_status():
    """O site so' mostra o chat se houver chave configurada."""
    return {"enabled": bool(settings.OPENAI_API_KEY)}


@router.post("/assistant")
def ask_assistant(body: AssistantIn, request: Request):
    auth.rate_limit("assistant", _visitor(request), limit=20, window_seconds=600)
    try:
        return {"reply": assistant.ask(body.messages)}
    except ValueError as e:
        raise HTTPException(422, str(e)) from e
    except assistant.AssistantUnavailable as e:
        raise HTTPException(503, str(e)) from e
