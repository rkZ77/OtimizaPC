"""Painel administrativo (secao 39). Toda escrita vai para o audit_log."""
from typing import Literal

from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel, Field

from app import auth, database, email_templates, settings
from app.services import admin_insights, app_settings, catalog, emails, gameplay, licenses, payments, plans, telemetry, users

router = APIRouter(prefix="/api/admin", tags=["admin"], dependencies=[Depends(auth.require_admin)])


def _page(limit: int = Query(50, ge=1, le=200), offset: int = Query(0, ge=0)) -> tuple[int, int]:
    return limit, offset


@router.get("/metrics")
def metrics():
    return telemetry.metrics()


Segment = Literal["subscriber", "trial", "free", "expired", "blocked", "admin", "expiring"]


@router.get("/users/stats")
def user_stats():
    """Quebra da base: assinantes, teste, free, vencidos, bloqueados, admins."""
    return admin_insights.user_stats()


@router.get("/users")
def list_users(q: str = "", segment: Segment | None = None, page=Depends(_page)):
    return admin_insights.list_users(q, segment, *page)


@router.get("/users/{user_id}")
def user_detail(user_id: int):
    detail = admin_insights.user_detail(user_id)
    if detail is None:
        raise HTTPException(404, "Usuário não encontrado.")
    return detail


class RoleIn(BaseModel):
    role: Literal["user", "admin"]


@router.post("/users/{user_id}/role")
def set_user_role(user_id: int, body: RoleIn, admin=Depends(auth.require_admin)):
    # Tirar o proprio admin deixaria o painel sem dono se ele for o unico.
    if user_id == admin["id"] and body.role != "admin":
        raise HTTPException(400, "Você não pode remover o próprio acesso de admin.")
    if admin_insights.set_role(user_id, body.role) == 0:
        raise HTTPException(404, "Usuário não encontrado.")
    catalog.audit(admin["id"], "user.role", str(user_id), body.model_dump())
    return {"ok": True}


@router.get("/finance")
def finance():
    return admin_insights.finance()


@router.get("/funnel")
def funnel(days: int = Query(30, ge=1, le=365)):
    return admin_insights.funnel(days)


@router.get("/engagement")
def engagement():
    return admin_insights.engagement()


@router.get("/usage")
def usage():
    return admin_insights.usage()


class ActiveIn(BaseModel):
    active: bool


@router.post("/users/{user_id}/active")
def set_user_active(user_id: int, body: ActiveIn, admin=Depends(auth.require_admin)):
    if user_id == admin["id"] and not body.active:
        raise HTTPException(400, "Você não pode desativar a própria conta.")
    users.set_active(user_id, body.active)
    catalog.audit(admin["id"], "user.active", str(user_id), {"active": body.active})
    return {"ok": True}


@router.get("/licenses")
def list_licenses(page=Depends(_page)):
    return {"licenses": licenses.admin_list(*page)}


class BlockIn(BaseModel):
    blocked: bool
    reason: str = Field(default="", max_length=200)


@router.post("/licenses/{license_id}/block")
def block_license(license_id: int, body: BlockIn, admin=Depends(auth.require_admin)):
    lic = licenses.set_blocked(license_id, body.blocked, body.reason)
    if lic is None:
        raise HTTPException(404, "Licença não encontrada.")
    catalog.audit(admin["id"], "license.block", str(license_id), body.model_dump())
    return {"license": lic}


class ExtendIn(BaseModel):
    days: int = Field(ge=1, le=3650)


@router.post("/licenses/{license_id}/extend")
def extend_license(license_id: int, body: ExtendIn, admin=Depends(auth.require_admin)):
    lic = licenses.admin_extend(license_id, body.days)
    if lic is None:
        raise HTTPException(404, "Licença não encontrada.")
    catalog.audit(admin["id"], "license.extend", str(license_id), body.model_dump())
    return {"license": lic}


class GrantIn(BaseModel):
    user_id: int
    plan_key: str


@router.post("/licenses/grant")
def grant_license(body: GrantIn, admin=Depends(auth.require_admin)):
    """Licenca cortesia/Custom sem pagamento (parcerias, suporte)."""
    plan = plans.get(body.plan_key)
    if plan is None:
        raise HTTPException(404, "Plano não encontrado.")
    with database.transaction() as cur:
        lic = licenses.grant(cur, body.user_id, plan, licenses.now())
    catalog.audit(admin["id"], "license.grant", str(body.user_id), body.model_dump())
    return {"license": lic}


@router.get("/plans")
def list_plans():
    return {"plans": plans.list_all()}


class PlanIn(BaseModel):
    key: str = Field(pattern=r"^[a-z0-9_-]{2,40}$")
    name: str = Field(max_length=60)
    tier: Literal["free", "starter", "pro", "ultimate", "custom"]
    description: str = Field(default="", max_length=300)
    price_cents: int = Field(ge=0, le=10_000_000)
    days: int = Field(ge=1, le=3650)
    max_devices: int = Field(ge=1, le=100)
    period: Literal["none", "monthly", "quarterly", "annual"] = "monthly"
    features: list[str] = Field(default_factory=list, max_length=20)
    active: bool = True
    sort: int = 0


@router.put("/plans")
def save_plan(body: PlanIn, admin=Depends(auth.require_admin)):
    saved = plans.upsert(body.model_dump())
    catalog.audit(admin["id"], "plan.save", body.key, body.model_dump())
    return {"plan": saved}


@router.get("/settings")
def get_settings():
    return {"settings": app_settings.get_all()}


class SettingIn(BaseModel):
    key: str
    value: int | bool | str


@router.put("/settings")
def save_setting(body: SettingIn, admin=Depends(auth.require_admin)):
    try:
        app_settings.set_value(body.key, body.value)
    except ValueError as e:
        raise HTTPException(400, str(e))
    catalog.audit(admin["id"], "setting.save", body.key, {"value": body.value})
    return {"ok": True}


@router.get("/payments")
def list_payments(page=Depends(_page)):
    return {"payments": payments.admin_list(*page)}


@router.get("/payment-events")
def payment_events(page=Depends(_page)):
    return {"events": database.fetch_all("SELECT * FROM payment_events ORDER BY id DESC LIMIT %s OFFSET %s", page)}


class CouponIn(BaseModel):
    code: str = Field(pattern=r"^[A-Z0-9_-]{3,40}$")
    percent_off: int = Field(ge=1, le=100)
    max_uses: int | None = Field(default=None, ge=1)
    expires_at: str | None = None
    active: bool = True


@router.get("/coupons")
def list_coupons():
    return {"coupons": database.fetch_all("SELECT * FROM coupons ORDER BY code")}


@router.put("/coupons")
def save_coupon(body: CouponIn, admin=Depends(auth.require_admin)):
    row = database.fetch_one(
        """INSERT INTO coupons (code, percent_off, max_uses, expires_at, active) VALUES (%(code)s, %(percent_off)s, %(max_uses)s, %(expires_at)s, %(active)s)
           ON CONFLICT (code) DO UPDATE SET percent_off = EXCLUDED.percent_off, max_uses = EXCLUDED.max_uses,
             expires_at = EXCLUDED.expires_at, active = EXCLUDED.active RETURNING *""", body.model_dump())
    catalog.audit(admin["id"], "coupon.save", body.code, body.model_dump())
    return {"coupon": row}


@router.get("/gameplay/summary")
def gameplay_summary():
    return gameplay.summary()


@router.get("/gameplay")
def gameplay_recent(page=Depends(_page)):
    return {"sessions": gameplay.recent(*page)}


@router.get("/gameplay/{session_id}/timeline")
def gameplay_timeline(session_id: int):
    row = gameplay.timeline(session_id)
    if row is None:
        raise HTTPException(404, "Partida não encontrada.")
    return row


@router.get("/devices")
def list_devices(page=Depends(_page)):
    return {"devices": database.fetch_all(
        """SELECT d.*, u.email FROM devices d JOIN users u ON u.id = d.user_id
           ORDER BY d.last_seen_at DESC LIMIT %s OFFSET %s""", page)}


@router.get("/catalog")
def list_overrides():
    return {"overrides": catalog.list_overrides(), "definitions": catalog.definitions()}


class OverrideIn(BaseModel):
    kind: Literal["optimization", "game_profile"]
    id: str = Field(max_length=80)
    enabled: bool | None = None
    risk: Literal["LOW", "MEDIUM", "HIGH"] | None = None
    description: str | None = Field(default=None, max_length=500)
    min_plan: Literal["free", "starter", "pro", "ultimate"] | None = None


@router.put("/catalog")
def save_override(body: OverrideIn, admin=Depends(auth.require_admin)):
    row = catalog.upsert_override(body.model_dump())
    catalog.audit(admin["id"], "catalog.override", f"{body.kind}:{body.id}", body.model_dump())
    return {"override": row}


@router.delete("/catalog/{kind}/{oid}")
def delete_override(kind: str, oid: str, admin=Depends(auth.require_admin)):
    catalog.delete_override(kind, oid)
    catalog.audit(admin["id"], "catalog.override.delete", f"{kind}:{oid}")
    return {"ok": True}


class ReleaseIn(BaseModel):
    component: Literal["agent", "engine", "catalog", "game_profiles"]
    version: str = Field(max_length=30)
    url: str = Field(default="", max_length=500)
    sha256: str = Field(default="", pattern=r"^([0-9a-f]{64})?$")
    notes: str = Field(default="", max_length=2000)


@router.get("/releases")
def list_releases():
    return {"releases": database.fetch_all("SELECT * FROM releases ORDER BY published_at DESC")}


@router.post("/releases")
def publish_release(body: ReleaseIn, admin=Depends(auth.require_admin)):
    try:
        row = catalog.publish_release(body.model_dump())
    except ValueError as e:
        raise HTTPException(400, str(e))
    catalog.audit(admin["id"], "release.publish", f"{body.component}:{body.version}")
    return {"release": row}


@router.get("/emails")
def list_emails(page=Depends(_page)):
    return {"configured": emails.configured(), "from": settings.RESEND_FROM,
            "emails": emails.admin_log(*page)}


@router.post("/emails/test")
def test_email(admin=Depends(auth.require_admin)):
    """Manda um e-mail para o proprio admin. Sincrono de proposito: a tela
    mostra na hora se o Resend aceitou ou o motivo da recusa."""
    status = emails.send("admin_test", admin["email"], email_templates.teste(emails.site_url()), user_id=admin["id"])
    catalog.audit(admin["id"], "email.test", admin["email"], {"status": status})
    if status == "skipped":
        raise HTTPException(409, "Envio não configurado: defina RESEND_API_KEY e RESEND_FROM no Railway.")
    if status == "failed":
        raise HTTPException(502, "O Resend recusou o envio. O motivo está no registro de e-mails abaixo.")
    return {"ok": True, "to": admin["email"]}


@router.get("/errors")
def list_errors():
    return {"errors": telemetry.errors()}


@router.get("/audit")
def audit_log(page=Depends(_page)):
    return {"entries": database.fetch_all(
        """SELECT a.*, u.email FROM audit_log a LEFT JOIN users u ON u.id = a.admin_id
           ORDER BY a.id DESC LIMIT %s OFFSET %s""", page)}
