from datetime import datetime, timedelta, timezone

import pytest
from fastapi.testclient import TestClient

from app import auth, signing
from app.main import app
from app.routers import agent as agent_router
from app.services import licenses

USER = {"id": 7, "email": "cliente@fpsx.app", "name": "Cliente", "role": "user", "active": True}
ADMIN = {**USER, "id": 1, "role": "admin"}
HASH = "a" * 64


@pytest.fixture
def client():
    app.dependency_overrides.clear()
    yield TestClient(app)
    app.dependency_overrides.clear()


def as_user(u):
    app.dependency_overrides[auth.current_user] = lambda: u


def test_health(client):
    assert client.get("/api/health").json() == {"ok": True}


def test_rotas_exigem_login(client):
    assert client.get("/api/account/overview").status_code == 401
    assert client.post("/api/agent/activate", json={"device_hash": HASH}).status_code == 401


def test_admin_bloqueia_usuario_comum(client):
    as_user(USER)
    assert client.get("/api/admin/metrics").status_code == 403


@pytest.fixture
def lic_env(monkeypatch):
    state = {"lic": None, "devices": [], "trial_used": False, "upserted": []}
    monkeypatch.setattr(licenses, "current", lambda uid: state["lic"])
    monkeypatch.setattr(licenses, "active_devices", lambda uid: state["devices"])
    monkeypatch.setattr(licenses, "max_devices_for", lambda lic: lic["max_devices"] if lic else 1)
    monkeypatch.setattr(licenses, "trial_already_used", lambda h, uid: state["trial_used"])
    monkeypatch.setattr(licenses, "mark_trial_device", lambda h, uid: None)
    monkeypatch.setattr(licenses, "upsert_device", lambda *a: state["upserted"].append(a) or {})
    monkeypatch.setattr(licenses.plans, "get", lambda key: {"key": "free", "max_devices": 1})
    monkeypatch.setattr(licenses.app_settings, "get", lambda key: 7)
    return state


def _lic(status="active", max_devices=2):
    return {"id": 3, "tier": "pro", "plan_key": "pro", "status": status, "max_devices": max_devices,
            "expires_at": datetime.now(timezone.utc) + timedelta(days=20)}


def test_ativacao_devolve_token_assinado_verificavel(client, lic_env):
    as_user(USER)
    lic_env["lic"] = _lic()
    r = client.post("/api/agent/activate", json={"device_hash": HASH, "device_name": "PC-Sala"})
    assert r.status_code == 200
    payload = signing.verify(r.json()["token"])
    assert payload["plan"] == "pro" and payload["device"] == HASH and payload["uid"] == 7


def test_ativacao_acima_do_limite_lista_os_pcs(client, lic_env):
    as_user(USER)
    lic_env["lic"] = _lic(max_devices=1)
    lic_env["devices"] = [{"id": 1, "device_hash": "b" * 64, "name": "Notebook", "last_seen_at": datetime.now(timezone.utc)}]
    r = client.post("/api/agent/activate", json={"device_hash": HASH})
    assert r.status_code == 409
    assert r.json()["detail"]["devices"][0]["name"] == "Notebook"
    assert lic_env["upserted"] == []


def test_trial_nao_repete_no_mesmo_pc(client, lic_env):
    as_user(USER)
    lic_env["lic"] = _lic(status="trial")
    lic_env["trial_used"] = True
    assert client.post("/api/agent/activate", json={"device_hash": HASH}).status_code == 403


def test_hash_de_dispositivo_invalido(client, lic_env):
    as_user(USER)
    assert client.post("/api/agent/activate", json={"device_hash": "Z" * 64}).status_code == 400


def test_token_de_dispositivo_forjado_e_recusado(client):
    assert client.post("/api/agent/telemetry", json={"events": []}, headers={"X-Device-Token": "abc.def"}).status_code == 401


def test_token_de_pc_desativado_e_recusado(client, monkeypatch):
    monkeypatch.setattr(agent_router.users, "get_by_id", lambda uid: USER)
    monkeypatch.setattr(licenses, "device_by_hash", lambda uid, h: None)
    token = signing.sign({"uid": 7, "device": HASH})
    r = client.post("/api/agent/telemetry", json={"events": []}, headers={"X-Device-Token": token})
    assert r.status_code == 401
    assert "desativado" in r.json()["detail"]


def test_webhook_com_assinatura_invalida_e_403(client, monkeypatch):
    from app.services import payments

    monkeypatch.setattr(payments, "record_event", lambda *a, **k: None)
    r = client.post("/api/payments/webhook?data.id=123", headers={"x-signature": "ts=1,v1=00"})
    assert r.status_code == 403


def test_login_limita_tentativas(client, monkeypatch):
    from app.services import users

    monkeypatch.setattr(users, "get_with_password", lambda email: None)
    codes = [client.post("/api/auth/login", json={"email": "x@y.com", "password": "errada"}).status_code for _ in range(9)]
    assert codes[:8] == [401] * 8
    assert codes[8] == 429


def test_versao_do_app_vai_assinada_para_o_atualizador(client, monkeypatch):
    from app.services import catalog
    row = {"component": "agent", "version": "0.4.0", "url": "https://github.com/rkZ77/OtimizaPC/releases/download/v0.4.0/FPSX-Setup-0.4.0.exe",
           "sha256": "a" * 64, "notes": "Novidades", "published_at": None}
    monkeypatch.setattr(catalog, "latest_releases", lambda: [row])
    body = client.get("/api/agent/releases").json()
    payload = signing.verify(body["update"])
    assert payload["version"] == "0.4.0" and payload["sha256"] == "a" * 64 and payload["url"] == row["url"]


def test_sem_versao_publicada_nao_ha_update(client, monkeypatch):
    from app.services import catalog
    monkeypatch.setattr(catalog, "latest_releases", lambda: [])
    assert client.get("/api/agent/releases").json()["update"] is None