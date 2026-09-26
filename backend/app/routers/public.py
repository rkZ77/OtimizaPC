from fastapi import APIRouter

from app import signing
from app.services import app_settings, catalog, plans

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
