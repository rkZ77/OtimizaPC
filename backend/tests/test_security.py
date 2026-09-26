"""Simulacao de ataques contra a API, rodando a cada teste.

Cada teste e' um ataque real e conhecido, com o resultado que o FPSX tem que
dar. Os que precisam de banco (SQL injection de verdade, IDOR entre contas)
estao em test_integration_db.py.
"""
import base64
import json
from datetime import datetime, timedelta, timezone

import jwt
import pytest
from fastapi.testclient import TestClient

from app import auth, settings
from app.main import app
from app.security_headers import build_csp, inline_script_hashes

USER = {"id": 7, "email": "cliente@fpsx.app", "name": "Cliente", "role": "user", "active": True, "password_changed_at": None}


@pytest.fixture
def client():
    app.dependency_overrides.clear()
    yield TestClient(app)
    app.dependency_overrides.clear()


def _b64(d: dict) -> str:
    return base64.urlsafe_b64encode(json.dumps(d).encode()).rstrip(b"=").decode()


# ─── sessao ─────────────────────────────────────────────────────────────

def test_token_com_alg_none_e_recusado(client, monkeypatch):
    from app.services import users
    monkeypatch.setattr(users, "get_by_id", lambda uid: USER)
    forged = _b64({"alg": "none", "typ": "JWT"}) + "." + _b64({"sub": "1", "role": "admin", "type": "access", "exp": 9999999999}) + "."
    assert client.get("/api/auth/me", headers={"Authorization": f"Bearer {forged}"}).status_code == 401


def test_token_assinado_com_outra_chave_e_recusado(client, monkeypatch):
    from app.services import users
    monkeypatch.setattr(users, "get_by_id", lambda uid: USER)
    forged = jwt.encode({"sub": "7", "role": "admin", "type": "access",
                         "exp": datetime.now(timezone.utc) + timedelta(hours=1)}, "chave-do-atacante", algorithm="HS256")
    assert client.get("/api/auth/me", headers={"Authorization": f"Bearer {forged}"}).status_code == 401


def test_papel_de_admin_no_token_nao_vale_sem_o_banco_dizer(client, monkeypatch):
    # Token legitimo de usuario comum, com "role" trocado: o admin confere o
    # papel NO BANCO, nao no token.
    from app.services import users
    monkeypatch.setattr(users, "get_by_id", lambda uid: USER)
    token = auth.create_access_token(7, "admin")
    assert client.get("/api/admin/metrics", headers={"Authorization": f"Bearer {token}"}).status_code == 403


def test_token_expirado_e_recusado(client, monkeypatch):
    from app.services import users
    monkeypatch.setattr(users, "get_by_id", lambda uid: USER)
    old = jwt.encode({"sub": "7", "role": "user", "type": "access", "exp": datetime.now(timezone.utc) - timedelta(minutes=1)},
                     settings.JWT_SECRET, algorithm="HS256")
    assert client.get("/api/auth/me", headers={"Authorization": f"Bearer {old}"}).status_code == 401


def test_conta_bloqueada_perde_acesso_na_hora(client, monkeypatch):
    from app.services import users
    monkeypatch.setattr(users, "get_by_id", lambda uid: {**USER, "active": False})
    token = auth.create_access_token(7, "user")
    assert client.get("/api/auth/me", headers={"Authorization": f"Bearer {token}"}).status_code == 401


def test_cookie_de_sessao_e_httponly_e_samesite(client, monkeypatch):
    from app.services import users
    monkeypatch.setattr(users, "get_with_password", lambda e: {**USER, "password_hash": auth.hash_password("senha-forte-1")})
    r = client.post("/api/auth/login", json={"email": USER["email"], "password": "senha-forte-1"})
    cookie = r.headers["set-cookie"].lower()
    assert "httponly" in cookie and "samesite=strict" in cookie


# ─── forca bruta e enumeracao ───────────────────────────────────────────

def test_forca_bruta_de_senha_e_barrada(client, monkeypatch):
    from app.services import users
    monkeypatch.setattr(users, "get_with_password", lambda e: {**USER, "password_hash": auth.hash_password("a-certa-123")})
    codes = [client.post("/api/auth/login", json={"email": USER["email"], "password": f"chute{i}"}).status_code for i in range(10)]
    assert 429 in codes


def test_login_nao_revela_se_o_email_existe(client, monkeypatch):
    from app.services import users
    monkeypatch.setattr(users, "get_with_password", lambda e: {**USER, "password_hash": auth.hash_password("a-certa-123")} if e == USER["email"] else None)
    a = client.post("/api/auth/login", json={"email": USER["email"], "password": "errada-1"})
    b = client.post("/api/auth/login", json={"email": "ninguem@x.com", "password": "errada-1"})
    assert a.status_code == b.status_code == 401 and a.json() == b.json()


# ─── entrada maliciosa ──────────────────────────────────────────────────

@pytest.mark.parametrize("email", ["' OR 1=1 --", "admin@fpsx.app' --", "a@b.com\"; DROP TABLE users; --"])
def test_sql_injection_no_login_nem_chega_ao_banco(client, email):
    # E-mail invalido e' recusado na validacao; o SQL e' parametrizado de
    # qualquer forma (conferido com banco real no teste de integracao).
    assert client.post("/api/auth/login", json={"email": email, "password": "x"}).status_code == 422


def test_senha_gigante_nao_trava_o_servidor(client):
    # bcrypt com entrada enorme e' vetor de negacao de servico: limite no schema.
    assert client.post("/api/auth/register", json={"email": "a@b.com", "password": "x" * 100_000}).status_code == 422


def test_webhook_de_pagamento_forjado_e_recusado(client, monkeypatch):
    from app.services import payments
    monkeypatch.setattr(payments, "record_event", lambda *a, **k: None)
    monkeypatch.setattr(settings, "MERCADOPAGO_WEBHOOK_SECRET", "segredo-real")
    r = client.post("/api/payments/webhook?data.id=999", headers={"x-signature": "ts=1,v1=" + "0" * 64, "x-request-id": "r"})
    assert r.status_code == 403


def test_caminho_com_ponto_ponto_nao_le_arquivo_do_servidor(client):
    for path in ["/../app/settings.py", "/..%2f..%2fetc%2fpasswd", "/assets/../../app/signing.py", "/%2e%2e/%2e%2e/.env"]:
        r = client.get(path)
        assert "JWT_SECRET" not in r.text and "PRIVATE KEY" not in r.text and "root:" not in r.text, path


def test_rota_de_api_inexistente_nao_devolve_o_site(client):
    assert client.get("/api/nao-existe").status_code == 404


# ─── navegador ──────────────────────────────────────────────────────────

def test_cors_nao_libera_site_estranho(client):
    r = client.options("/api/auth/me", headers={"Origin": "https://site-do-golpe.com", "Access-Control-Request-Method": "GET"})
    assert r.headers.get("access-control-allow-origin") != "https://site-do-golpe.com"


def test_cabecalhos_de_seguranca_em_toda_resposta(client):
    for path in ["/api/health", "/api/public/plans-inexistente", "/"]:
        h = client.get(path).headers
        assert h["x-content-type-options"] == "nosniff"
        assert h["x-frame-options"] == "DENY"
        assert "frame-ancestors 'none'" in h["content-security-policy"]
        assert "script-src 'self'" in h["content-security-policy"]
        assert "unsafe-inline" not in h["content-security-policy"].split("script-src")[1].split(";")[0]


def test_resposta_da_api_nao_fica_em_cache(client):
    assert client.get("/api/health").headers["cache-control"] == "no-store"


def test_csp_libera_so_o_script_inline_do_proprio_site():
    html = "<script>\n  (function () { tema() })()\n</script><script type=\"module\" src=\"/a.js\"></script>"
    hashes = inline_script_hashes(html)
    assert len(hashes) == 1 and hashes[0].startswith("'sha256-")
    assert hashes[0] in build_csp(hashes)


def test_hsts_so_em_producao(client, monkeypatch):
    assert "strict-transport-security" not in client.get("/api/health").headers
