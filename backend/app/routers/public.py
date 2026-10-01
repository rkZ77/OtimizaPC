import logging
import re

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import RedirectResponse
from pydantic import BaseModel, Field

from app import auth, database, settings, signing

logger = logging.getLogger("rkzfps")
from app.services import app_settings, assistant, catalog, gameplay, plans

router = APIRouter(prefix="/api/public", tags=["public"])


#: Limite do teste gratis no app (Rkzfps.Core TrialQuota.MaxOptimizations). O
#: app garante o limite; aqui so' vai o numero para o site descrever o teste.
TRIAL_MAX_FIXES = 2


@router.get("/plans")
def list_plans():
    """Catalogo de planos para visitante deslogado. Unica fonte de preco da tela."""
    return {"currency": "BRL", "trial_days": app_settings.get("trial_days"), "trial_max_fixes": TRIAL_MAX_FIXES,
            "plans": plans.list_active()}


@router.get("/stats")
def stats():
    """Numeros reais do conjunto para a home. Campo null = ainda abaixo do minimo."""
    return gameplay.public_stats()


@router.get("/releases")
def releases():
    return {"releases": catalog.latest_releases()}


_ROBO = re.compile(r"bot|crawl|spider|preview|facebookexternalhit|whatsapp|slurp", re.I)


@router.get("/download", include_in_schema=False)
def download(request: Request):
    """Botao "Baixar para Windows": conta o clique e manda para o instalador.

    A contagem nunca segura o download: banco fora do ar, o arquivo sai igual.
    Robo de busca e previa de link nao contam."""
    release = next((r for r in catalog.latest_releases() if r["component"] == "agent" and r.get("url")), None)
    if release is None:
        return RedirectResponse("/download", status_code=302)
    if not _ROBO.search(request.headers.get("user-agent", "")):
        try:
            database.execute(
                """INSERT INTO download_clicks (day, version, n) VALUES (current_date, %s, 1)
                   ON CONFLICT (day, version) DO UPDATE SET n = download_clicks.n + 1""", (release["version"],))
        except Exception as e:
            logger.warning("[DOWNLOAD] contagem falhou: %s", e)
    return RedirectResponse(release["url"], status_code=302)


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
