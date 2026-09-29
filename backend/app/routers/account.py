"""Area do cliente no site: licenca, PCs, pagamentos, benchmarks e historico."""
from fastapi import APIRouter, Depends, HTTPException

from app import auth
from app.services import licenses, payments, plans, telemetry

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


@router.get("/benchmarks")
def my_benchmarks(user: dict = Depends(auth.current_user)):
    return {"benchmarks": telemetry.benchmarks_for_user(user["id"])}


@router.get("/history")
def my_history(user: dict = Depends(auth.current_user)):
    return {"events": telemetry.history_for_user(user["id"])}
