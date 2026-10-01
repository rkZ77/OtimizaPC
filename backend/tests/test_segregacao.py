"""Cada plano so' recebe o que e' dele, e admin so' para admin.

A regra vale na API, nao so' na tela: um assinante do Basico que chama a rota
direto nao pode receber o antes e depois nem o benchmark, que sao do Pro."""
from datetime import datetime, timedelta, timezone

import pytest
from fastapi.testclient import TestClient

from app import auth
from app.main import app
from app.routers import account

USER = {"id": 7, "email": "cliente@rkzfps.app", "name": "Ana", "role": "user", "active": True}
RECAP = {"optimizations": 4, "matches": 9, "hours": 3.5,
         "games": [{"game_id": "cs2", "matches_before": 3, "matches_after": 3, "avg_fps_before": 140,
                    "avg_fps_after": 180, "low1_fps_before": 80, "low1_fps_after": 110}]}
BENCH = [{"id": 1, "device_name": "PC", "game_id": "cs2", "label": "x", "avg_fps": 1, "low1_fps": 1,
          "low01_fps": 1, "frametime_ms": 1, "created_at": "2026-10-01T00:00:00Z"}]


def _lic(tier: str) -> dict:
    return {"id": 1, "plan_key": tier, "tier": tier, "status": "active", "max_devices": 1,
            "expires_at": datetime.now(timezone.utc) + timedelta(days=10)}


@pytest.fixture
def client(monkeypatch):
    app.dependency_overrides[auth.current_user] = lambda: USER
    monkeypatch.setattr(account.gameplay, "recap_for_user", lambda uid: dict(RECAP))
    monkeypatch.setattr(account.telemetry, "benchmarks_for_user", lambda uid: BENCH)
    yield TestClient(app)
    app.dependency_overrides.clear()


@pytest.mark.parametrize("lic", [None, _lic("starter")])
def test_sem_pro_nao_recebe_antes_e_depois_nem_benchmark(client, monkeypatch, lic):
    monkeypatch.setattr(account.licenses, "current", lambda uid: lic)
    r = client.get("/api/account/recap").json()
    # A contagem e' do Free (medir FPS e' de todo plano); a comparacao nao.
    assert r["matches"] == 9 and r["games"] == [] and r["comparison_locked"] is True
    b = client.get("/api/account/benchmarks").json()
    assert b == {"benchmarks": [], "locked": True}


@pytest.mark.parametrize("tier", ["pro", "ultimate", "custom"])
def test_pro_ou_acima_recebe_a_comparacao(client, monkeypatch, tier):
    monkeypatch.setattr(account.licenses, "current", lambda uid: _lic(tier))
    r = client.get("/api/account/recap").json()
    assert r["games"] and r["comparison_locked"] is False
    assert client.get("/api/account/benchmarks").json()["benchmarks"] == BENCH


def test_usuario_comum_nao_entra_no_admin(monkeypatch):
    # Sem sobrescrever require_admin: e' a trava real que tem que recusar.
    app.dependency_overrides[auth.current_user] = lambda: USER
    try:
        assert TestClient(app).get("/api/admin/users").status_code == 403
    finally:
        app.dependency_overrides.clear()
