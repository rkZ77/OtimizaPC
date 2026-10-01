from datetime import datetime, timedelta, timezone

import pytest
from fastapi.testclient import TestClient

from app import auth, email_templates, settings
from app.main import app
from app.services import emails, password_reset, users

NOW = datetime(2026, 9, 26, 15, 0, tzinfo=timezone.utc)
USER = {"id": 7, "email": "cliente@rkzfps.app", "name": "Ana Souza", "role": "user", "active": True}


@pytest.fixture
def client():
    app.dependency_overrides.clear()
    yield TestClient(app)
    app.dependency_overrides.clear()


@pytest.fixture
def sent(monkeypatch):
    """Captura o que iria para a fila de envio, sem thread e sem Resend."""
    out = []
    monkeypatch.setattr(emails, "send_later", lambda kind, to, email, **kw: out.append((kind, to, email, kw)))
    return out


# ─── modelos ────────────────────────────────────────────────────────────

def _todos():
    return [
        email_templates.boas_vindas("Ana", "https://x", 7),
        email_templates.codigo_senha("Ana", "042913", "https://x", "a@b.com", 15),
        email_templates.senha_alterada("Ana", "https://x"),
        email_templates.pagamento_aprovado("Ana", "Pro", "26/10/2026", "R$ 24,90", "https://x"),
        email_templates.aviso_plano("Ana", "Pro", "29/09/2026", 3, False, "https://x"),
        email_templates.aviso_plano("Ana", "Pro", "25/09/2026", 0, True, "https://x"),
        email_templates.teste("https://x"),
    ]


def test_todo_email_tem_texto_puro_e_segue_a_regra_de_texto():
    for e in _todos():
        assert e.subject and e.text and e.html.startswith("<!DOCTYPE html>")
        # Regra do projeto: sem travessao e sem ponto do meio no texto de tela.
        for proibido in ("—", "·", "&middot;"):
            assert proibido not in e.html + e.text + e.subject, (e.subject, proibido)


def test_nome_do_usuario_e_escapado_no_html():
    e = email_templates.boas_vindas("<script>alert(1)</script>", "https://x", 7)
    assert "<script>" not in e.html
    assert "&lt;script&gt;" in e.html


def test_codigo_vai_com_o_email_no_link():
    e = email_templates.codigo_senha("Ana", "042913", "https://x", "a+b@c.com", 15)
    assert "042913" in e.text
    assert "https://x/redefinir-senha?email=a%2Bb%40c.com" in e.html


def test_aviso_muda_frase_entre_teste_e_plano_pago():
    assert "teste" in email_templates.aviso_plano("Ana", "Pro", "d", 1, True, "u").subject
    assert "amanhã" in email_templates.aviso_plano("Ana", "Pro", "d", 1, False, "u").subject
    assert "terminou" in email_templates.aviso_plano("Ana", "Pro", "d", 0, False, "u").subject


def test_formato_de_valor_e_data():
    assert emails.reais(2490) == "R$ 24,90"
    assert emails.reais(123456) == "R$ 1.234,56"
    # 02:00 UTC ainda e' o dia anterior em Brasilia.
    assert emails.data_br(datetime(2026, 10, 1, 2, 0, tzinfo=timezone.utc)) == "30/09/2026"


# ─── avisos de vencimento ───────────────────────────────────────────────

@pytest.mark.parametrize("delta, esperado", [
    (timedelta(days=10), None),
    (timedelta(days=3), ("plan_expiring", 3)),
    (timedelta(days=2, hours=1), ("plan_expiring", 3)),
    (timedelta(hours=5), ("plan_expiring", 1)),
    (timedelta(hours=-5), ("plan_expired", 0)),
    (timedelta(days=-3), None),
])
def test_qual_aviso_cada_licenca_recebe(delta, esperado):
    assert emails.notice_for(NOW + delta, NOW) == esperado


def test_avisos_usam_chave_por_licenca_e_vencimento(monkeypatch):
    lic = {"id": 3, "user_id": 7, "status": "active", "expires_at": NOW + timedelta(days=2),
           "plan_name": "Pro", "email": "a@b.com", "name": "Ana"}
    monkeypatch.setattr(emails, "expiry_candidates", lambda: [lic])
    calls = []
    monkeypatch.setattr(emails, "send", lambda kind, to, email, **kw: calls.append((kind, kw["dedupe_key"])) or "sent")
    assert emails.run_expiry_notices(NOW) == {"sent": 1}
    assert calls == [("plan_expiring", "plan_expiring:3:2026-09-28")]


# ─── envio ──────────────────────────────────────────────────────────────

@pytest.fixture
def log(monkeypatch):
    state = {"start": 11, "end": []}
    monkeypatch.setattr(emails, "_log_start", lambda *a: state["start"])
    monkeypatch.setattr(emails, "_log_end", lambda log_id, status, detail="": state["end"].append((status, detail)))
    return state


def test_sem_chave_nada_sai_e_fica_registrado(monkeypatch, log):
    monkeypatch.setattr(settings, "RESEND_API_KEY", "")
    monkeypatch.setattr(emails, "_post", lambda *a: pytest.fail("nao deveria chamar o Resend"))
    assert emails.send("welcome", "a@b.com", email_templates.teste("u")) == "skipped"
    assert log["end"][0][0] == "skipped"


def test_aviso_repetido_nao_sai_de_novo(monkeypatch, log):
    log["start"] = None
    monkeypatch.setattr(emails, "_post", lambda *a: pytest.fail("nao deveria chamar o Resend"))
    assert emails.send("plan_expiring", "a@b.com", email_templates.teste("u"), dedupe_key="k") == "duplicate"


def test_falha_do_resend_vira_registro_e_nao_excecao(monkeypatch, log):
    monkeypatch.setattr(settings, "RESEND_API_KEY", "re_x")
    monkeypatch.setattr(settings, "RESEND_FROM", "RKZFPS <a@b.com>")

    def boom(*a):
        raise RuntimeError("Resend respondeu 403: domain not verified")

    monkeypatch.setattr(emails, "_post", boom)
    assert emails.send("welcome", "a@b.com", email_templates.teste("u")) == "failed"
    assert log["end"] == [("failed", "Resend respondeu 403: domain not verified")]


def test_envio_ok(monkeypatch, log):
    monkeypatch.setattr(settings, "RESEND_API_KEY", "re_x")
    monkeypatch.setattr(settings, "RESEND_FROM", "RKZFPS <a@b.com>")
    monkeypatch.setattr(emails, "_post", lambda *a: "msg_1")
    assert emails.send("welcome", "a@b.com", email_templates.teste("u")) == "sent"
    assert log["end"] == [("sent", "msg_1")]


# ─── codigo de senha ────────────────────────────────────────────────────

def _row(code="123456", **kw):
    return {"id": 1, "code_hash": password_reset.hash_code(7, code), "expires_at": NOW + timedelta(minutes=5),
            "attempts": 0, "used_at": None, **kw}


def test_regras_do_codigo():
    assert password_reset.check(_row(), 7, "123456", NOW) == "ok"
    assert password_reset.check(_row(), 7, "654321", NOW) == "wrong"
    # Mesmo codigo, outro usuario: hash diferente.
    assert password_reset.check(_row(), 8, "123456", NOW) == "wrong"
    assert password_reset.check(_row(expires_at=NOW - timedelta(seconds=1)), 7, "123456", NOW) == "expired"
    assert password_reset.check(_row(attempts=password_reset.MAX_ATTEMPTS), 7, "123456", NOW) == "locked"
    assert password_reset.check(_row(used_at=NOW), 7, "123456", NOW) == "missing"
    assert password_reset.check(None, 7, "123456", NOW) == "missing"


def test_codigo_sempre_tem_6_digitos():
    assert all(len(c) == 6 and c.isdigit() for c in (password_reset.new_code() for _ in range(200)))


def test_token_anterior_a_troca_de_senha_nao_vale():
    changed = datetime(2026, 9, 26, 12, 0, 0, 500000, tzinfo=timezone.utc)
    antes = int(changed.timestamp()) - 1
    assert auth.issued_before_password_change({"iat": antes}, changed)
    assert not auth.issued_before_password_change({"iat": int(changed.timestamp())}, changed)
    assert not auth.issued_before_password_change({"iat": antes}, None)


# ─── rotas ──────────────────────────────────────────────────────────────

def test_esqueci_a_senha_responde_igual_com_ou_sem_conta(client, monkeypatch, sent):
    monkeypatch.setattr(users, "get_by_email", lambda e: USER if e == USER["email"] else None)
    monkeypatch.setattr(password_reset, "create", lambda uid: "042913")
    a = client.post("/api/auth/forgot-password", json={"email": "ninguem@rkzfps.app"})
    b = client.post("/api/auth/forgot-password", json={"email": USER["email"]})
    assert a.status_code == b.status_code == 200 and a.json() == b.json()
    assert [(k, to) for k, to, *_ in sent] == [("password_reset", USER["email"])]
    assert "042913" in sent[0][2].text


def test_esqueci_a_senha_limita_por_email(client, monkeypatch, sent):
    monkeypatch.setattr(users, "get_by_email", lambda e: None)
    codes = [client.post("/api/auth/forgot-password", json={"email": "x@y.com"}).status_code for _ in range(4)]
    assert codes == [200, 200, 200, 429]


def test_redefinir_com_codigo_errado_nao_troca_a_senha(client, monkeypatch, sent):
    monkeypatch.setattr(users, "get_by_email", lambda e: USER)
    monkeypatch.setattr(password_reset, "consume", lambda uid, code: False)
    monkeypatch.setattr(users, "set_password", lambda *a: pytest.fail("nao deveria trocar"))
    r = client.post("/api/auth/reset-password", json={"email": USER["email"], "code": "000000", "password": "novasenha1"})
    assert r.status_code == 400 and sent == []


def test_redefinir_com_codigo_certo_troca_e_avisa(client, monkeypatch, sent):
    trocas = []
    monkeypatch.setattr(users, "get_by_email", lambda e: USER)
    monkeypatch.setattr(password_reset, "consume", lambda uid, code: code == "123456")
    monkeypatch.setattr(users, "set_password", lambda uid, h: trocas.append(uid))
    r = client.post("/api/auth/reset-password", json={"email": USER["email"], "code": "123456", "password": "novasenha1"})
    assert r.status_code == 200 and trocas == [7]
    assert sent[0][0] == "password_changed"


def test_codigo_fora_do_formato_nem_chega_no_banco(client):
    r = client.post("/api/auth/reset-password", json={"email": USER["email"], "code": "12ab56", "password": "novasenha1"})
    assert r.status_code == 422


def test_cadastro_manda_boas_vindas_com_os_dias_de_teste(client, monkeypatch, sent):
    from app.services import app_settings, licenses

    monkeypatch.setattr(users, "create", lambda *a: dict(USER))
    monkeypatch.setattr(licenses, "create_trial", lambda uid: {"id": 1})
    monkeypatch.setattr(app_settings, "get", lambda key: 7)
    r = client.post("/api/auth/register", json={"email": USER["email"], "name": "Ana", "password": "senhaforte1"})
    assert r.status_code == 200
    kind, to, email, kw = sent[0]
    assert (kind, to, kw["user_id"]) == ("welcome", USER["email"], 7)
    assert "7 dias de teste" in email.text


def test_teste_de_envio_do_admin_sem_configuracao(client, monkeypatch):
    from app.services import catalog

    app.dependency_overrides[auth.current_user] = lambda: {**USER, "role": "admin"}
    monkeypatch.setattr(emails, "send", lambda *a, **k: "skipped")
    monkeypatch.setattr(catalog, "audit", lambda *a, **k: None)
    r = client.post("/api/admin/emails/test")
    assert r.status_code == 409 and "RESEND_API_KEY" in r.json()["detail"]
