"""Fluxos completos contra um PostgreSQL DESCARTAVEL.

So' roda com FPSX_TEST_DATABASE_URL definida, e recusa qualquer URL que nao
seja localhost: a trava do conftest continua valendo para o resto da suite, e
este arquivo nunca consegue apontar para o Supabase por engano.

    FPSX_TEST_DATABASE_URL=postgresql://fpsx@localhost:54329/fpsx_test pytest tests/test_integration_db.py
"""
import logging
import os
from urllib.parse import urlparse

import pytest
from fastapi.testclient import TestClient

URL = os.getenv("FPSX_TEST_DATABASE_URL", "")
pytestmark = pytest.mark.skipif(not URL, reason="FPSX_TEST_DATABASE_URL não definida")

H1, H2, H3 = "1" * 64, "2" * 64, "3" * 64


@pytest.fixture
def db(monkeypatch):
    host = urlparse(URL).hostname
    assert host in ("localhost", "127.0.0.1"), "integração só roda em banco local descartável"
    monkeypatch.setenv("DATABASE_URL", URL)
    monkeypatch.setenv("DB_SSLMODE", "disable")

    from app import database, migrate

    # Desfaz a trava do conftest SO' aqui, e recria o pool para a URL de teste.
    # Pega a vaga do semaforo como o get_connection real: o close() devolve a vaga.
    monkeypatch.setattr(database, "get_connection",
                        lambda: database._vagas.acquire() and database._ConexaoDoPool(database._obter_pool().getconn()))
    database._pool = None
    conn = database.get_connection()
    cur = conn.cursor()
    cur.execute("DROP SCHEMA public CASCADE; CREATE SCHEMA public;")
    conn.commit()
    conn.close()
    migrate.run(logging.getLogger("test"))
    yield database
    database._pool.closeall()
    database._pool = None


@pytest.fixture
def api(db, monkeypatch):
    from app import settings
    from app.main import app
    from app.services import emails

    # E-mail sincrono e sem Resend: a thread de envio nao pode sobreviver ao
    # DROP SCHEMA do proximo teste, e nenhum teste manda e-mail de verdade.
    monkeypatch.setattr(settings, "RESEND_API_KEY", "")
    monkeypatch.setattr(emails, "send_later", lambda kind, to, email, **kw: emails.send(kind, to, email, **kw))
    app.dependency_overrides.clear()
    return TestClient(app)


def _register(api, email):
    r = api.post("/api/auth/register", json={"email": email, "password": "senha-forte-1", "name": "Teste"})
    assert r.status_code == 200, r.text
    return {"Authorization": f"Bearer {r.json()['access_token']}"}


def _activate(api, headers, h):
    return api.post("/api/agent/activate", json={"device_hash": h, "device_name": f"PC-{h[0]}", "windows_build": "26100", "agent_version": "0.1.0"}, headers=headers)


def test_ciclo_completo_trial_compra_dispositivos_telemetria(api, db):
    from app import signing
    from app.services import payments

    # Cadastro cria trial PRO de 7 dias.
    alice = _register(api, "alice@fpsx.app")
    assert api.post("/api/auth/register", json={"email": "ALICE@fpsx.app", "password": "outra-senha-1"}).status_code == 409
    overview = api.get("/api/account/overview", headers=alice).json()
    assert overview["license"]["status"] == "trial" and overview["license"]["tier"] == "pro"

    # Ativa o PC 1 no trial.
    r = _activate(api, alice, H1)
    assert r.status_code == 200, r.text
    token = r.json()["token"]
    assert signing.verify(token)["status"] == "trial"

    # Outra conta no MESMO PC nao ganha outro trial.
    bob = _register(api, "bob@fpsx.app")
    assert _activate(api, bob, H1).status_code == 403

    # Trial vale para 1 PC.
    assert _activate(api, alice, H2).status_code == 409

    # Compra do Pro (webhook simulado pelo caminho unico de ativacao).
    uid = api.get("/api/auth/me", headers=alice).json()["user"]["id"]
    # Valor abaixo do preco atual (R$ 29,90) e' recusado: checkout adulterado.
    cheap = payments.NormalizedPayment("mercadopago", "mp-0", "approved", 2490, f"{uid}:pro:")
    assert payments.apply_approved_payment(cheap, "teste")["reason"] == "amount_mismatch"
    paid = payments.NormalizedPayment("mercadopago", "mp-1", "approved", 2990, f"{uid}:pro:")
    result = payments.apply_approved_payment(paid, "teste")
    assert result["applied"] is True
    assert payments.apply_approved_payment(paid, "teste")["reason"] == "already_applied"

    overview = api.get("/api/account/overview", headers=alice).json()
    assert overview["license"]["status"] == "active" and overview["license"]["max_devices"] == 1

    # O PC 1 continua ativo: refresh traz o plano novo (trial -> pro ativo).
    refreshed = api.post("/api/agent/refresh", json={"device_hash": H1}, headers={"X-Device-Token": token})
    assert refreshed.status_code == 200, refreshed.text
    assert signing.verify(refreshed.json()["token"])["status"] == "active"

    # Pro anual em cima do mensal: soma na MESMA licenca, nao cria outra.
    lic_before = overview["license"]
    annual = payments.NormalizedPayment("mercadopago", "mp-2", "approved", 26910, f"{uid}:pro-anual:")
    assert payments.apply_approved_payment(annual, "teste")["license_id"] == lic_before["id"]
    lic_after = api.get("/api/account/overview", headers=alice).json()["license"]
    from datetime import datetime, timedelta
    gained = datetime.fromisoformat(lic_after["expires_at"]) - datetime.fromisoformat(lic_before["expires_at"])
    assert gained == timedelta(days=365)

    # Todo plano vale 1 PC: o segundo recebe 409 com a lista para trocar.
    second = _activate(api, alice, H2)
    assert second.status_code == 409
    devices = second.json()["detail"]["devices"]
    assert [d["name"] for d in devices] == ["PC-1"]

    # "Trocar PC": desativa o PC 1 pelo site e o PC 2 entra.
    assert api.post(f"/api/account/devices/{devices[0]['id']}/deactivate", headers=alice).status_code == 200
    r2 = _activate(api, alice, H2)
    assert r2.status_code == 200
    # O PC 1 desativado perde acesso na hora.
    assert api.post("/api/agent/refresh", json={"device_hash": H1}, headers={"X-Device-Token": token}).status_code == 401
    token = r2.json()["token"]

    # Telemetria aceita so' o que reconhece.
    t = api.post("/api/agent/telemetry", headers={"X-Device-Token": token}, json={"events": [
        {"event": "optimization_applied", "optimization_id": "game-mode-enable", "success": True, "detail": {"profile": "gaming", "usuario": "x"}},
        {"event": "coisa_estranha"},
    ]})
    assert t.json() == {"accepted": 1}
    b = api.post("/api/agent/benchmarks", headers={"X-Device-Token": token}, json={
        "game_id": "cs2", "label": "antes", "avg_fps": 142, "low1_fps": 91, "low01_fps": 70, "frametime_ms": 7.04, "frames": 8520})
    assert b.status_code == 200
    assert len(api.get("/api/account/history", headers=alice).json()["events"]) == 1
    assert api.get("/api/account/benchmarks", headers=alice).json()["benchmarks"][0]["avg_fps"] == 142


def test_admin_quebra_da_base_financeiro_funil_e_ficha(api, db):
    from app.services import payments

    admin = _register(api, "dono@fpsx.app")
    db.execute("UPDATE users SET role = 'admin' WHERE email = 'dono@fpsx.app'")

    # Um de cada segmento.
    _register(api, "teste@fpsx.app")                      # trial (cadastro ganha trial)
    free = _register(api, "free@fpsx.app")
    db.execute("DELETE FROM licenses WHERE user_id = (SELECT id FROM users WHERE email = 'free@fpsx.app')")
    assinante = _register(api, "assinante@fpsx.app")
    uid = api.get("/api/auth/me", headers=assinante).json()["user"]["id"]
    payments.apply_approved_payment(payments.NormalizedPayment("mercadopago", "mp-9", "approved", 2990, f"{uid}:pro:"), "teste")
    _activate(api, assinante, H1)
    _register(api, "vencido@fpsx.app")
    db.execute("UPDATE licenses SET expires_at = now() - interval '2 days' WHERE user_id = (SELECT id FROM users WHERE email = 'vencido@fpsx.app')")
    _register(api, "bloqueado@fpsx.app")
    db.execute("UPDATE users SET active = FALSE WHERE email = 'bloqueado@fpsx.app'")
    assert free

    stats = api.get("/api/admin/users/stats", headers=admin).json()
    assert (stats["total"], stats["admins"], stats["subscribers"], stats["trial"], stats["free"], stats["expired"], stats["blocked"]) == (6, 1, 1, 1, 1, 1, 1)
    assert stats["subscribers_by_tier"] == [{"tier": "pro", "n": 1}]

    subs = api.get("/api/admin/users?segment=subscriber", headers=admin).json()
    assert subs["total"] == 1 and subs["users"][0]["email"] == "assinante@fpsx.app"
    assert subs["users"][0]["devices"] == 1
    assert api.get("/api/admin/users?q=venc", headers=admin).json()["users"][0]["segment"] == "expired"

    ficha = api.get(f"/api/admin/users/{uid}", headers=admin).json()
    assert len(ficha["licenses"]) == 2 and len(ficha["devices"]) == 1 and len(ficha["payments"]) == 1

    fin = api.get("/api/admin/finance", headers=admin).json()
    assert fin["total_cents"] == 2990 and fin["count"] == 1 and fin["by_plan"][0]["plan_key"] == "pro"

    funil = api.get("/api/admin/funnel?days=30", headers=admin).json()
    assert funil["signed_up"] == 5 and funil["activated_pc"] == 1 and funil["paid"] == 1
    assert funil["never_activated"] == 4

    eng = api.get("/api/admin/engagement", headers=admin).json()
    assert eng["active_7d"] == 1 and len(eng["lapsed_30d"]) == 1
    assert api.get("/api/admin/usage", headers=admin).status_code == 200

    # Promover e proteger o proprio acesso.
    me = api.get("/api/auth/me", headers=admin).json()["user"]["id"]
    assert api.post(f"/api/admin/users/{me}/role", json={"role": "user"}, headers=admin).status_code == 400
    assert api.post(f"/api/admin/users/{uid}/role", json={"role": "admin"}, headers=admin).status_code == 200
    assert api.get("/api/admin/users/stats", headers=admin).json()["admins"] == 2


def test_admin_planos_config_catalogo_e_auditoria(api, db):
    from app import signing

    admin = _register(api, "admin@fpsx.app")
    db.execute("UPDATE users SET role = 'admin' WHERE email = 'admin@fpsx.app'")

    assert api.get("/api/admin/metrics", headers=admin).json()["users"] == 1

    # 0006: 1 PC em todos, precos novos, trimestral e anual com economia
    # calculada no servidor.
    catalog = {p["key"]: p for p in api.get("/api/public/plans").json()["plans"]}
    assert {k: catalog[k]["price_cents"] for k in ("starter", "pro", "ultimate")} == {"starter": 1990, "pro": 2990, "ultimate": 3990}
    assert all(p["max_devices"] == 1 for p in catalog.values())
    assert (catalog["pro-trimestral"]["days"], catalog["pro-trimestral"]["savings_percent"]) == (90, 10)
    assert (catalog["pro-anual"]["days"], catalog["pro-anual"]["savings_percent"]) == (365, 25)
    assert catalog["free"]["period"] == "none"
    assert "Perfis de jogo: CS2, Fortnite e Minecraft" in catalog["pro"]["features"]

    plan = {"key": "pro", "name": "Pro", "tier": "pro", "description": "x", "price_cents": 2990, "days": 30,
            "max_devices": 3, "features": ["a"], "active": True, "sort": 2}
    assert api.put("/api/admin/plans", json=plan, headers=admin).status_code == 200
    public = api.get("/api/public/plans").json()
    assert next(p for p in public["plans"] if p["key"] == "pro")["price_cents"] == 2990

    assert api.put("/api/admin/settings", json={"key": "trial_days", "value": 3}, headers=admin).status_code == 200
    assert api.put("/api/admin/settings", json={"key": "trial_days", "value": "tres"}, headers=admin).status_code == 400
    assert api.get("/api/public/plans").json()["trial_days"] == 3

    ov = {"kind": "optimization", "id": "power-plan-high-performance", "enabled": False}
    assert api.put("/api/admin/catalog", json=ov, headers=admin).status_code == 200

    # O Agent recebe o override assinado.
    _activate(api, admin, H1)
    token = _activate(api, admin, H1).json()["token"]
    signed = api.get("/api/agent/catalog", headers={"X-Device-Token": token}).json()["token"]
    overrides = signing.verify(signed)["overrides"]
    assert overrides == [{"kind": "optimization", "id": "power-plan-high-performance", "enabled": False}]

    assert api.post("/api/admin/releases", headers=admin, json={"component": "agent", "version": "0.2.0", "url": "https://cdn.fpsx.app/FPSX-Setup-0.2.0.exe", "sha256": "a" * 64}).status_code == 200
    assert api.get("/api/public/releases").json()["releases"][0]["version"] == "0.2.0"

    actions = [e["action"] for e in api.get("/api/admin/audit", headers=admin).json()["entries"]]
    assert {"plan.save", "setting.save", "catalog.override", "release.publish"} <= set(actions)


def test_redefinir_senha_registro_de_email_e_avisos_de_plano(api, db, monkeypatch):
    from app.services import emails, password_reset

    headers = _register(api, "reset@fpsx.app")
    monkeypatch.setattr(password_reset, "new_code", lambda: "111111")

    assert api.post("/api/auth/forgot-password", json={"email": "reset@fpsx.app"}).status_code == 200
    body = {"email": "reset@fpsx.app", "code": "222222", "password": "senha-nova-1"}
    assert api.post("/api/auth/reset-password", json=body).status_code == 400
    assert api.post("/api/auth/reset-password", json={**body, "code": "111111"}).status_code == 200
    # Codigo usado nao vale de novo.
    assert api.post("/api/auth/reset-password", json={**body, "code": "111111"}).status_code == 400

    # Sessao de antes da troca cai; a senha nova entra.
    db.execute("UPDATE users SET password_changed_at = password_changed_at + interval '2 seconds'")
    assert api.get("/api/auth/me", headers=headers).status_code == 401
    assert api.post("/api/auth/login", json={"email": "reset@fpsx.app", "password": "senha-forte-1"}).status_code == 401
    assert api.post("/api/auth/login", json={"email": "reset@fpsx.app", "password": "senha-nova-1"}).status_code == 200

    # Cinco chutes errados travam o codigo, mesmo que o sexto seja o certo.
    api.post("/api/auth/forgot-password", json={"email": "reset@fpsx.app"})
    for _ in range(password_reset.MAX_ATTEMPTS):
        assert api.post("/api/auth/reset-password", json={**body, "code": "999999"}).status_code == 400
    assert api.post("/api/auth/reset-password", json={**body, "code": "111111"}).status_code == 400

    kinds = [r["kind"] for r in db.fetch_all("SELECT kind, status FROM email_log ORDER BY id") if r["status"] == "skipped"]
    assert kinds == ["welcome", "password_reset", "password_changed", "password_reset"]

    # Aviso de plano: trial vencendo em 2 dias sai uma vez so'.
    db.execute("UPDATE licenses SET expires_at = now() + interval '2 days'")
    assert emails.run_expiry_notices() == {"skipped": 1}
    assert emails.run_expiry_notices() == {"duplicate": 1}

    # Quem ja' tem outra licenca mais longa nao recebe "vai vencer".
    other = _register(api, "renovou@fpsx.app")
    assert other
    db.execute("UPDATE licenses SET expires_at = now() + interval '1 day' WHERE user_id = (SELECT id FROM users WHERE email = 'renovou@fpsx.app')")
    db.execute("""INSERT INTO licenses (user_id, plan_key, tier, status, max_devices, expires_at)
                  SELECT id, 'pro', 'pro', 'active', 3, now() + interval '30 days' FROM users WHERE email = 'renovou@fpsx.app'""")
    assert [c["email"] for c in emails.expiry_candidates()] == ["reset@fpsx.app"]

def test_ataques_com_banco_real_idor_e_sql_injection(api, db):
    """IDOR: a conta B tenta mexer no PC da conta A. SQL injection: texto de
    ataque na busca do admin e no nome do cadastro vira texto, nunca SQL."""
    a = _register(api, "vitima@fpsx.app")
    assert _activate(api, a, H1).status_code == 200
    pc_a = api.get("/api/account/overview", headers=a).json()["devices"][0]["id"]

    b = _register(api, "atacante@fpsx.app")
    r = api.post(f"/api/account/devices/{pc_a}/deactivate", headers=b)
    assert r.status_code in (403, 404)
    assert api.get("/api/account/overview", headers=a).json()["devices"][0]["id"] == pc_a  # continua ativo

    # Usuario comum no admin.
    assert api.get("/api/admin/users", headers=b).status_code == 403

    # SQL injection no nome: fica gravado como texto.
    evil = "Robert'); DROP TABLE users; --"
    r = api.post("/api/auth/register", json={"email": "bobby@fpsx.app", "password": "senha-forte-1", "name": evil})
    assert r.status_code == 200 and r.json()["user"]["name"] == evil
    assert db.fetch_one("SELECT count(*) AS n FROM users")["n"] == 3

    # SQL injection na busca do admin: nenhum resultado a mais, nenhuma tabela a menos.
    db.execute("UPDATE users SET role = 'admin' WHERE email = 'vitima@fpsx.app'")
    for q in ["' OR '1'='1", "%' OR 1=1 --", "'; DELETE FROM users; --"]:
        found = api.get("/api/admin/users", params={"q": q}, headers=a).json()["users"]
        assert found == [], q
    assert db.fetch_one("SELECT count(*) AS n FROM users")["n"] == 3

    # XSS no nome: a API devolve o texto como veio; quem escapa e' o React
    # (nenhum dangerouslySetInnerHTML no site, conferido no teste do front).
    xss = "<img src=x onerror=alert(1)>"
    r = api.post("/api/auth/register", json={"email": "xss@fpsx.app", "password": "senha-forte-1", "name": xss})
    assert r.json()["user"]["name"] == xss

def test_partidas_sobem_comparam_com_pcs_parecidos_e_aparecem_no_admin(api, db):
    from datetime import datetime, timedelta, timezone

    base = {
        "game_id": "cs2", "measured_seconds": 1200, "low01_fps": 60, "p99_frametime_ms": 9, "stutters_per_minute": 5,
        "timeline": [[100, 170, 150], [102, 168, 20]], "drops": 1, "drop_causes": {"app": 1},
    }
    tokens = []
    for i in range(7):
        headers = _register(api, f"jogador{i}@fpsx.app")
        h = format(i + 4, "x") * 64
        tokens.append(_activate(api, headers, h).json()["token"])
    start = datetime(2026, 9, 27, 20, 0, tzinfo=timezone.utc)
    # 6 PCs com a mesma placa (FPS 100, 110, ... 150) e 1 com outra placa.
    for i, token in enumerate(tokens):
        gpu = "Radeon RX 580 Series" if i < 6 else "GeForce RTX 4090"
        match = {**base, "session_key": f"m{i}", "started_at": start.isoformat(), "ended_at": (start + timedelta(minutes=20)).isoformat(),
                 "avg_fps": 100 + 10 * i, "low1_fps": 60 + i, "hardware": {"tier": "MID", "gpu": gpu, "threads": 8}}
        r = api.post("/api/agent/gameplay", headers={"X-Device-Token": token}, json=match)
        assert r.status_code == 200, r.text
    # Reenvio da mesma partida não duplica.
    assert api.post("/api/agent/gameplay", headers={"X-Device-Token": tokens[0]}, json={**base, "session_key": "m0",
        "started_at": start.isoformat(), "ended_at": start.isoformat(), "avg_fps": 100, "low1_fps": 60,
        "hardware": {"tier": "MID", "gpu": "Radeon RX 580 Series"}}).status_code == 200
    assert db.fetch_one("SELECT count(*) AS n FROM gameplay_sessions")["n"] == 7

    # PC 0 se compara com os outros 5 da mesma placa (ele mesmo fica fora): mediana de 110..150 = 130.
    peers = api.get("/api/agent/gameplay/peers?game_id=cs2", headers={"X-Device-Token": tokens[0]}).json()
    assert peers["scope"] == "gpu" and peers["devices"] == 5 and peers["avg_fps"] == 130
    # A RTX 4090 está sozinha: cai para o nível do PC, com os 6 da RX 580.
    peers = api.get("/api/agent/gameplay/peers?game_id=cs2", headers={"X-Device-Token": tokens[6]}).json()
    assert peers["scope"] == "tier" and peers["devices"] == 6

    _register(api, "analista@fpsx.app")
    db.execute("UPDATE users SET role = 'admin' WHERE email = 'analista@fpsx.app'")
    r = api.post("/api/auth/login", json={"email": "analista@fpsx.app", "password": "senha-forte-1"})
    admin = {"Authorization": f"Bearer {r.json()['access_token']}"}
    summary = api.get("/api/admin/gameplay/summary", headers=admin).json()
    assert summary["by_game"][0]["matches"] == 7 and summary["by_game"][0]["devices"] == 7
    assert summary["drop_causes"] == [{"cause": "app", "drops": 7}]
    recent = api.get("/api/admin/gameplay", headers=admin).json()["sessions"]
    assert len(recent) == 7 and "timeline" not in recent[0]
    tl = api.get(f"/api/admin/gameplay/{recent[0]['id']}/timeline", headers=admin).json()
    assert tl["timeline"] == [[100, 170.0, 150.0], [102, 168.0, 20.0]]
    # Usuário comum não vê o painel.
    assert api.get("/api/admin/gameplay/summary", headers=_register(api, "curioso@fpsx.app")).status_code == 403
