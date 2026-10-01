import logging
import re
from datetime import date

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import RedirectResponse
from pydantic import BaseModel, EmailStr, Field

from app import auth, database, email_templates, settings, signing

logger = logging.getLogger("rkzfps")
from app.services import app_settings, assistant, catalog, emails, gameplay, plans
from app.services.users import normalize_email

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


class LinkPcIn(BaseModel):
    email: EmailStr
    #: De onde veio o pedido (hero, download, barra...), so' para o registro.
    origem: str = Field(default="", max_length=30)


@router.post("/download-link", status_code=202)
def send_download_link(body: LinkPcIn, request: Request):
    """Celular: manda o link do instalador para o e-mail, para abrir no PC.

    A maior parte das visitas vem do celular, onde o app de Windows nao roda,
    e o botao "Baixar" ali era beco sem saida. Isto nao cria conta nem guarda
    nada alem do registro do envio (email_log, que o admin ja' ve).

    Limite por visitante contra abuso do envio, e um envio por e-mail por dia
    (dedupe_key): pedir de novo responde igual, mas nao manda outro. A
    resposta e' a mesma em todo caso, para nao servir de teste de e-mail."""
    auth.rate_limit("download-link", _visitor(request), limit=5, window_seconds=3600)
    email = normalize_email(body.email)
    try:
        trial = int(app_settings.get("trial_days") or 0)
    except Exception:  # banco fora do ar: o link sai igual, so' sem a frase do teste
        trial = 0
    emails.send_later(
        "download_link", email,
        email_templates.link_download(emails.site_url(), trial),
        dedupe_key=f"download_link:{email}:{date.today().isoformat()}",
    )
    logger.info("[DOWNLOAD] link pedido pelo celular (origem=%s)", body.origem or "-")
    return {"ok": True}


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
