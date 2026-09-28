from datetime import datetime, timedelta, timezone

import pytest
from fastapi.testclient import TestClient

from app import signing
from app.main import app
from app.routers import agent as agent_router
from app.services import gameplay, licenses, telemetry

USER = {"id": 7, "email": "cliente@fpsx.app", "name": "Cliente", "role": "user", "active": True}
DEVICE = {"id": 3, "agent_version": "0.4.2", "windows_build": "26200"}
HASH = "a" * 64

MATCH = {
    "session_key": "20260927-201845-cs2", "game_id": "cs2",
    "started_at": "2026-09-27T20:18:45-03:00", "ended_at": "2026-09-27T20:50:00-03:00",
    "measured_seconds": 1774, "avg_fps": 169.1, "low1_fps": 111.7, "low01_fps": 77.4,
    "p99_frametime_ms": 9.0, "stutters_per_minute": 7.3, "display_hz": 240, "agent_version": "0.4.2",
    "hardware": {"tier": "MID", "gpu": "Radeon RX 580 Series", "threads": 8, "ram_gb": 15.9},
    "timeline": [[100, 170, 150], [102, 168, 20]], "drops": 1, "drop_causes": {"app": 1},
}


@pytest.fixture
def device_client(monkeypatch):
    monkeypatch.setattr(agent_router.users, "get_by_id", lambda uid: USER)
    monkeypatch.setattr(licenses, "device_by_hash", lambda uid, h: DEVICE)
    saved = []
    monkeypatch.setattr(gameplay, "record", lambda device, m: saved.append((device, m)))
    client = TestClient(app)
    client.headers["X-Device-Token"] = signing.sign({"uid": 7, "device": HASH})
    return client, saved


def test_partida_sobe_com_o_pc_autenticado(device_client):
    client, saved = device_client
    r = client.post("/api/agent/gameplay", json=MATCH)
    assert r.status_code == 200
    device, m = saved[0]
    assert device["id"] == 3 and m["game_id"] == "cs2" and m["drop_causes"] == {"app": 1}


def test_partida_sem_token_de_pc_e_recusada():
    assert TestClient(app).post("/api/agent/gameplay", json=MATCH).status_code == 401


@pytest.mark.parametrize("field,value", [
    ("game_id", "cs2; drop table users"), ("session_key", "../../etc"), ("avg_fps", -1),
    ("avg_fps", 99999), ("timeline", [[1, 2, 3]] * 901), ("display_hz", 5000),
])
def test_partida_com_campo_fora_do_formato_e_422(device_client, field, value):
    client, saved = device_client
    assert client.post("/api/agent/gameplay", json={**MATCH, field: value}).status_code == 422
    assert saved == []


def test_servidor_limpa_hardware_grafico_e_causas():
    assert gameplay.clean_hardware({"gpu": "RX 580", "threads": "8", "user": "joao", "pc_name": "RKZ", "tier": None}) == {"gpu": "RX 580", "threads": 8}
    assert gameplay.clean_timeline([[1, 60, 30], ["x", 1, 1], [2, -5, 1], [3, 60]]) == [[1, 60.0, 30.0]]
    assert gameplay.clean_causes({"app": 2, "Chrome": 5, "cpu": -1, "gpu": "3"}) == {"app": 2}


def test_pcs_parecidos_so_com_grupo_minimo(monkeypatch):
    calls = []

    def fetch_one(sql, params=()):
        calls.append(params)
        if "LIMIT 1" in sql:
            return {"hardware": {"gpu": "RX 580", "tier": "MID"}}
        # Mesma placa: só 2 PCs (pouco). Mesmo nível: 12 PCs.
        return {"devices": 2, "avg_fps": 150.0, "low1_fps": 90.0} if params[2] == "RX 580" else {"devices": 12, "avg_fps": 140.0, "low1_fps": 80.0}

    monkeypatch.setattr(gameplay.database, "fetch_one", fetch_one)
    r = gameplay.peers(DEVICE, "cs2")
    assert r == {"scope": "tier", "label": "MID", "devices": 12, "avg_fps": 140.0, "low1_fps": 80.0}
    # O próprio PC fica fora da conta.
    assert all(p[1] == DEVICE["id"] for p in calls[1:])


def test_telemetria_guarda_hora_e_versao_do_evento_se_plausiveis():
    now = datetime.now(timezone.utc)
    e = telemetry.sanitize({"event": "scan_completed", "occurred_at": (now - timedelta(days=2)).isoformat(), "agent_version": "0.4.1"})
    assert e["occurred_at"] is not None and e["event_agent_version"] == "0.4.1"
    for bad in [(now + timedelta(days=5)).isoformat(), (now - timedelta(days=400)).isoformat(), "ontem", "2026-09-27T10:00:00", 123]:
        assert telemetry.sanitize({"event": "scan_completed", "occurred_at": bad})["occurred_at"] is None
    assert telemetry.sanitize({"event": "scan_completed", "agent_version": "<script>"})["event_agent_version"] is None
