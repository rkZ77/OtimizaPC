"""Area do cliente no site: licenca, PCs, pagamentos, benchmarks e historico."""
from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from app import auth
from app.services import gameplay, licenses, payments, plans, referrals, telemetry, users

router = APIRouter(prefix="/api/account", tags=["account"])


def license_view(lic: dict | None, ended: dict | None = None) -> dict:
    if lic is None:
        view = {"plan_key": "free", "tier": "free", "status": "free", "expires_at": None, "max_devices": licenses.max_devices_for(None)}
        # Quem ja' teve plano ve "vencido" com a data e o plano que tinha, para
        # a tela oferecer renovar o mesmo plano. O acesso segue o do Free.
        if ended is not None:
            view.update(status="blocked" if ended["status"] == "blocked" else "expired",
                        expires_at=ended["expires_at"], ended_plan_key=ended["plan_key"])
        return view
    return {
        "id": lic["id"],
        "plan_key": lic["plan_key"],
        "tier": lic["tier"],
        "status": licenses.effective_status(lic, licenses.now()),
        "expires_at": lic["expires_at"],
        "max_devices": lic["max_devices"],
    }


@router.get("/overview")
def overview(user: dict = Depends(auth.current_user)):
    lic = licenses.current(user["id"])
    ended = licenses.last_license(user["id"]) if lic is None else None
    plan = plans.get(lic["plan_key"]) if lic else plans.get("free")
    return {
        "user": user,
        "license": license_view(lic, ended),
        "plan_name": plan["name"] if plan else "Free",
        "devices": licenses.active_devices(user["id"]),
    }


class ProfileIn(BaseModel):
    # So' o nome: e-mail e' o login no app (trocar quebraria o PC ativado) e a
    # senha tem o proprio fluxo, com codigo por e-mail.
    name: str = Field(max_length=80)


@router.patch("/profile")
def update_profile(body: ProfileIn, user: dict = Depends(auth.current_user)):
    name = " ".join(body.name.split())
    updated = users.set_name(user["id"], name)
    if updated is None:
        raise HTTPException(404, "Conta não encontrada.")
    return {"user": updated}


@router.post("/devices/{device_id}/deactivate")
def deactivate(device_id: int, user: dict = Depends(auth.current_user)):
    """Libera a vaga de um PC ('Trocar PC'). O app daquele PC cai para Free
    na proxima verificacao."""
    if licenses.deactivate_device(user["id"], device_id) == 0:
        raise HTTPException(404, "PC não encontrado.")
    return {"ok": True}


@router.get("/payments")
def my_payments(user: dict = Depends(auth.current_user)):
    return {"payments": payments.list_for_user(user["id"])}


def _tem_pro(user_id: int) -> bool:
    """Plano que vale agora libera o que e' do Pro (antes e depois, benchmark)?

    Espelho de PlanFeatures.cs: "Antes e depois do FPS nas suas partidas" e
    "RKZFPS Benchmark e relatorios" sao do Pro. A regra vale aqui no servidor,
    e nao so' na tela: quem assinou o Basico nao recebe a comparacao nem pela
    API. O teste gratis e' do Pro, entao conta como Pro enquanto durar."""
    lic = licenses.current(user_id)
    return lic is not None and plans.rank(lic["tier"]) >= plans.rank("pro")


@router.get("/benchmarks")
def my_benchmarks(user: dict = Depends(auth.current_user)):
    if not _tem_pro(user["id"]):
        return {"benchmarks": [], "locked": True}
    return {"benchmarks": telemetry.benchmarks_for_user(user["id"]), "locked": False}


@router.get("/history")
def my_history(user: dict = Depends(auth.current_user)):
    return {"events": telemetry.history_for_user(user["id"])}


@router.get("/recap")
def my_recap(user: dict = Depends(auth.current_user)):
    """O que o RKZFPS fez e mediu nos PCs da pessoa (so' com telemetria ligada no app).

    Contagens (correcoes, partidas, horas) valem para todo plano: medir o FPS
    e' do Free. O antes e depois por jogo e' do Pro e so' sai para quem tem."""
    recap = gameplay.recap_for_user(user["id"])
    liberado = _tem_pro(user["id"])
    if not liberado:
        recap = {**recap, "games": []}
    return {**recap, "comparison_locked": not liberado}


@router.get("/referral")
def my_referral(user: dict = Depends(auth.current_user)):
    return referrals.summary(user["id"])
