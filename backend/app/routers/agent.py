"""API que o app desktop (RKZFPS Agent) consome.

Nao existe endpoint que mande o Agent executar algo (secao 31): o servidor so'
entrega licenca assinada, catalogo assinado e versao disponivel. Quem decide
o que aplicar e' o motor local, com whitelist compilada.
"""
import re

from fastapi import APIRouter, Depends, Header, HTTPException, Query
from pydantic import BaseModel, Field

from app import auth, signing
from datetime import datetime

from app.services import assistant, catalog, gameplay, licenses, telemetry, users

router = APIRouter(prefix="/api/agent", tags=["agent"])

_HASH = re.compile(r"^[0-9a-f]{64}$")


class DeviceIn(BaseModel):
    device_hash: str = Field(min_length=64, max_length=64)
    device_name: str = Field(default="", max_length=60)
    windows_build: str = Field(default="", max_length=20)
    agent_version: str = Field(default="", max_length=20)


def _check_hash(h: str) -> str:
    if not _HASH.match(h):
        raise HTTPException(400, "Identificador de dispositivo inválido.")
    return h


def device_context(x_device_token: str = Header(default="")) -> tuple[dict, dict]:
    """Autentica o PC pelo proprio token assinado que ele recebeu na ativacao."""
    payload = signing.verify(x_device_token) if x_device_token else None
    if not payload or "uid" not in payload:
        raise HTTPException(401, "Dispositivo não autenticado.")
    user = users.get_by_id(int(payload["uid"]))
    if not user or not user["active"]:
        raise HTTPException(401, "Conta indisponível.")
    device = licenses.device_by_hash(user["id"], payload["device"])
    if device is None:
        raise HTTPException(401, "Este PC foi desativado na sua conta.")
    return user, device


@router.post("/activate")
def activate(body: DeviceIn, user: dict = Depends(auth.current_user)):
    """Ativa este PC na conta ('Ativar PC'). Idempotente para o mesmo PC."""
    device_hash = _check_hash(body.device_hash)
    lic = licenses.current(user["id"])

    # Trial so' vale num PC que ainda nao usou trial em outra conta.
    if lic is not None and lic["status"] == "trial":
        if licenses.trial_already_used(device_hash, user["id"]):
            raise HTTPException(403, "O período de teste já foi usado neste PC. Escolha um plano para continuar.")
        licenses.mark_trial_device(device_hash, user["id"])

    decision = licenses.decide_activation(licenses.active_devices(user["id"]), device_hash, licenses.max_devices_for(lic))
    if decision.outcome == "limit":
        raise HTTPException(409, {
            "message": "Limite de PCs do seu plano atingido. Desative um PC na sua conta para usar este.",
            "devices": [{"id": d["id"], "name": d["name"], "last_seen_at": d["last_seen_at"].isoformat()}
                        for d in licenses.active_devices(user["id"])],
        })

    licenses.upsert_device(user["id"], lic, decision, device_hash, body.device_name, body.windows_build, body.agent_version)
    token, payload = licenses.issue_token(user, device_hash)
    return {"token": token, "license": payload}


@router.post("/refresh")
def refresh(body: DeviceIn, ctx=Depends(device_context)):
    """Renova o token antes do fim da carencia offline e pega mudanca de plano."""
    user, device = ctx
    if body.device_hash != device["device_hash"]:
        raise HTTPException(401, "Token de outro PC.")
    licenses.touch_device(device["id"], body.agent_version, body.windows_build)
    token, payload = licenses.issue_token(user, device["device_hash"])
    return {"token": token, "license": payload}


@router.post("/deactivate")
def deactivate(ctx=Depends(device_context)):
    user, device = ctx
    licenses.deactivate_device(user["id"], device["id"])
    return {"ok": True}


class TelemetryIn(BaseModel):
    events: list[dict] = Field(default_factory=list, max_length=100)


@router.post("/telemetry")
def post_telemetry(body: TelemetryIn, ctx=Depends(device_context)):
    _, device = ctx
    return {"accepted": telemetry.record(device, body.events)}


class BenchmarkIn(BaseModel):
    game_id: str = Field(max_length=20)
    label: str = Field(default="", max_length=30)
    session_id: str | None = Field(default=None, max_length=40)
    avg_fps: float = Field(ge=0, le=10000)
    low1_fps: float = Field(ge=0, le=10000)
    low01_fps: float = Field(ge=0, le=10000)
    frametime_ms: float = Field(ge=0, le=10000)
    frames: int = Field(ge=0)


@router.post("/benchmarks")
def post_benchmark(body: BenchmarkIn, ctx=Depends(device_context)):
    _, device = ctx
    telemetry.record_benchmark(device, body.model_dump())
    return {"ok": True}


_GAME_ID = r"^[a-z0-9-]{1,30}$"


class GameplayIn(BaseModel):
    session_key: str = Field(min_length=1, max_length=60, pattern=r"^[A-Za-z0-9_-]+$")
    game_id: str = Field(pattern=_GAME_ID)
    started_at: datetime
    ended_at: datetime
    measured_seconds: float = Field(ge=0, le=86400)
    avg_fps: float = Field(ge=0, le=10000)
    low1_fps: float = Field(ge=0, le=10000)
    low01_fps: float = Field(ge=0, le=10000)
    p99_frametime_ms: float = Field(ge=0, le=100000)
    stutters_per_minute: float = Field(ge=0, le=100000)
    avg_cpu_percent: float | None = Field(default=None, ge=0, le=100)
    avg_gpu_percent: float | None = Field(default=None, ge=0, le=100)
    display_hz: int | None = Field(default=None, ge=0, le=1000)
    agent_version: str = Field(default="", max_length=40)
    hardware: dict = Field(default_factory=dict)
    timeline: list = Field(default_factory=list, max_length=gameplay.MAX_POINTS)
    drops: int = Field(default=0, ge=0, le=100000)
    drop_causes: dict = Field(default_factory=dict)


@router.post("/gameplay")
def post_gameplay(body: GameplayIn, ctx=Depends(device_context)):
    """Uma partida medida pelo app (so' com consentimento de telemetria no app)."""
    _, device = ctx
    gameplay.record(device, body.model_dump())
    return {"ok": True}


@router.get("/gameplay/peers")
def gameplay_peers(game_id: str = Query(pattern=_GAME_ID), ctx=Depends(device_context)):
    """Como PCs parecidos rodam o jogo (mediana, minimo de PCs no grupo)."""
    _, device = ctx
    return gameplay.peers(device, game_id)


class ExplainIn(BaseModel):
    hardware: dict = Field(default_factory=dict)
    findings: list[dict] = Field(default_factory=list, max_length=20)


@router.post("/explain")
def explain(body: ExplainIn, ctx=Depends(device_context)):
    """Diagnostico em palavras simples, com IA, a pedido da pessoa (botao no app)."""
    _, device = ctx
    auth.rate_limit("explain", str(device["id"]), limit=10, window_seconds=86400)
    try:
        return {"text": assistant.explain(body.hardware, body.findings)}
    except assistant.AssistantUnavailable as e:
        raise HTTPException(503, str(e)) from e


@router.get("/catalog")
def catalog_overrides(ctx=Depends(device_context)):
    return {"token": catalog.signed_overrides()}


@router.get("/releases")
def releases():
    rows = catalog.latest_releases()
    agent_release = next((r for r in rows if r["component"] == "agent" and r.get("url") and r.get("sha256")), None)
    # "update" vai ASSINADO com a mesma chave da licenca: o app so' baixa e
    # instala se a assinatura bater. Banco adulterado ou conexao interceptada
    # nao conseguem apontar o app para outro instalador.
    update = signing.sign({
        "component": "agent", "version": agent_release["version"], "url": agent_release["url"],
        "sha256": agent_release["sha256"], "notes": agent_release.get("notes") or "",
    }) if agent_release else None
    return {"releases": rows, "update": update}
