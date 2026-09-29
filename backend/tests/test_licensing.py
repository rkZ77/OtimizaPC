from datetime import datetime, timedelta, timezone

from app import signing
from app.services import licenses

NOW = datetime(2026, 9, 25, 12, 0, tzinfo=timezone.utc)


def lic(id=1, tier="pro", status="active", days=10, plan_key=None, max_devices=2):
    return {"id": id, "tier": tier, "plan_key": plan_key or tier, "status": status,
            "expires_at": NOW + timedelta(days=days), "max_devices": max_devices}


def test_licenca_vencida_e_expired_sem_depender_de_cron():
    assert licenses.effective_status(lic(days=-1), NOW) == "expired"
    assert licenses.effective_status(lic(status="trial", days=3), NOW) == "trial"
    assert licenses.effective_status(lic(status="blocked", days=30), NOW) == "blocked"


def test_melhor_licenca_e_o_maior_tier_utilizavel():
    best = licenses.best_license([lic(1, "ultimate", days=-1), lic(2, "starter", days=90), lic(3, "pro", days=5)], NOW)
    assert best["id"] == 3
    assert licenses.best_license([lic(status="blocked")], NOW) is None


def test_ativacao_reaproveita_o_mesmo_pc_e_respeita_o_limite():
    devices = [{"id": 1, "device_hash": "a" * 64}, {"id": 2, "device_hash": "b" * 64}]
    assert licenses.decide_activation(devices, "a" * 64, 2).outcome == "existing"
    assert licenses.decide_activation(devices, "c" * 64, 2).outcome == "limit"
    assert licenses.decide_activation(devices, "c" * 64, 3).outcome == "new"


def test_renovar_antes_de_vencer_nao_perde_os_dias():
    assert licenses.extended_expiration(lic(days=10), 30, NOW) == NOW + timedelta(days=40)
    assert licenses.extended_expiration(lic(days=-5), 30, NOW) == NOW + timedelta(days=30)
    assert licenses.extended_expiration(None, 30, NOW) == NOW + timedelta(days=30)


def test_token_limita_uso_offline_pela_carencia_e_pelo_vencimento():
    user = {"id": 7, "email": "a@b.com"}
    long = licenses.build_token_payload(user, lic(days=30), {"key": "free"}, "d" * 64, 7, NOW)
    assert long["valid_until"] == (NOW + timedelta(days=7)).isoformat()
    assert long["plan"] == "pro" and long["uid"] == 7

    short = licenses.build_token_payload(user, lic(days=2), {"key": "free"}, "d" * 64, 7, NOW)
    assert short["valid_until"] == (NOW + timedelta(days=2)).isoformat()

    free = licenses.build_token_payload(user, None, {"key": "free"}, "d" * 64, 7, NOW)
    assert free["plan"] == "free" and free["expires_at"] is None


def test_custom_vira_ultimate_para_o_agent():
    payload = licenses.build_token_payload({"id": 1, "email": "x@y.z"}, lic(tier="custom", plan_key="empresa"), {"key": "free"}, "d" * 64, 7, NOW)
    assert payload["plan"] == "ultimate"
    assert payload["plan_key"] == "empresa"


def test_assinatura_detecta_adulteracao():
    token = signing.sign({"plan": "free", "uid": 1})
    assert signing.verify(token) == {"plan": "free", "uid": 1}

    body, sig = token.split(".")
    forged = signing.sign({"plan": "ultimate", "uid": 1}).split(".")[0]
    assert signing.verify(f"{forged}.{sig}") is None
    assert signing.verify("lixo") is None


def test_quem_ja_teve_plano_recebe_expired_e_continua_no_free():
    user = {"id": 1, "email": "x@y.z"}
    antiga = {**lic(days=-3), "status": "active"}
    p = licenses.build_token_payload(user, None, {"key": "free"}, "d" * 64, 7, NOW, ended=antiga)
    # O app mostra "venceu" e o resumo, mas nada pago fica liberado.
    assert p["status"] == "expired" and p["plan"] == "free" and p["license_id"] is None
    assert p["expires_at"] == antiga["expires_at"].isoformat()
    # A data antiga nao encurta o uso offline: o token nao nasce vencido.
    assert p["valid_until"] == (NOW + timedelta(days=7)).isoformat()

    bloqueada = licenses.build_token_payload(user, None, {"key": "free"}, "d" * 64, 7, NOW, ended={**antiga, "status": "blocked"})
    assert bloqueada["status"] == "blocked"
    nunca = licenses.build_token_payload(user, None, {"key": "free"}, "d" * 64, 7, NOW)
    assert nunca["status"] == "free" and nunca["expires_at"] is None


def test_conta_mostra_plano_vencido_com_o_plano_que_a_pessoa_tinha(monkeypatch):
    from app.routers.account import license_view

    monkeypatch.setattr(licenses, "max_devices_for", lambda lic: 1)
    antiga = {**lic(days=-5, plan_key="pro-anual"), "status": "active"}
    v = license_view(None, antiga)
    assert v["status"] == "expired" and v["tier"] == "free"
    assert v["ended_plan_key"] == "pro-anual" and v["expires_at"] == antiga["expires_at"]
