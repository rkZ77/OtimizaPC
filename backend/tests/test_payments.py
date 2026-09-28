import hashlib
import hmac
from datetime import datetime, timedelta, timezone

import pytest

from app.services import payments

NOW = datetime(2026, 9, 25, tzinfo=timezone.utc)
PRO = {"key": "pro", "name": "Pro", "tier": "pro", "price_cents": 2490, "days": 30, "max_devices": 2, "active": True}


def _sign(secret, data_id, request_id, ts):
    manifest = payments.mp_manifest(data_id, request_id, ts)
    return f"ts={ts},v1=" + hmac.new(secret.encode(), manifest.encode(), hashlib.sha256).hexdigest()


def test_assinatura_do_mercado_pago():
    sig = _sign("segredo", "123", "req-1", "1700000000")
    assert payments.verify_mp_signature(sig, "req-1", "123", "segredo")
    assert not payments.verify_mp_signature(sig, "req-1", "124", "segredo")
    assert not payments.verify_mp_signature(sig, "req-1", "123", "outro")
    assert not payments.verify_mp_signature("", "req-1", "123", "segredo")


def test_manifesto_omite_campo_ausente_e_mantem_ponto_e_virgula_final():
    assert payments.mp_manifest("1", "", "9") == "id:1;ts:9;"


def test_referencia_ida_e_volta():
    ref = payments.parse_reference(payments.make_reference(42, "pro", "BEMVINDO"))
    assert (ref.user_id, ref.plan_key, ref.coupon) == (42, "pro", "BEMVINDO")
    assert payments.parse_reference("42:pro:").coupon is None
    assert payments.parse_reference("x:pro:") is None
    assert payments.parse_reference("lixo") is None


def test_cupom():
    ok = {"active": True, "expires_at": None, "max_uses": 10, "used_count": 3, "percent_off": 20}
    assert payments.coupon_percent(ok, NOW) == 20
    assert payments.coupon_percent({**ok, "used_count": 10}, NOW) == 0
    assert payments.coupon_percent({**ok, "expires_at": NOW - timedelta(days=1)}, NOW) == 0
    assert payments.coupon_percent({**ok, "active": False}, NOW) == 0
    assert payments.discounted_cents(2490, 20) == 1992
    assert payments.discounted_cents(2490, 100) == 0


@pytest.fixture
def fake_env(monkeypatch):
    events = []
    monkeypatch.setattr(payments, "record_event", lambda *a, **k: events.append(a))
    monkeypatch.setattr(payments.plans, "get", lambda key: PRO if key == "pro" else None)
    monkeypatch.setattr(payments, "get_coupon", lambda code: None)
    return events


def _payment(**kw):
    base = dict(provider="mercadopago", provider_payment_id="p1", status="approved", amount_cents=2490, reference="7:pro:")
    return payments.NormalizedPayment(**{**base, **kw})


def test_pagamento_nao_aprovado_nao_ativa(fake_env):
    assert payments.apply_approved_payment(_payment(status="pending"), "webhook")["applied"] is False


def test_valor_menor_que_o_preco_nao_ativa(fake_env):
    r = payments.apply_approved_payment(_payment(amount_cents=100), "webhook")
    assert r == {"applied": False, "reason": "amount_mismatch"}
    assert fake_env[-1][1] == "amount_mismatch"


def test_referencia_e_plano_invalidos_nao_ativam(fake_env):
    assert payments.apply_approved_payment(_payment(reference="lixo"), "webhook")["reason"] == "bad_reference"
    assert payments.apply_approved_payment(_payment(reference="7:inexistente:"), "webhook")["reason"] == "unknown_plan"


class _FakeCursor:
    def __init__(self, already):
        self.already = already
        self.sql = []
        self._next = None

    def execute(self, sql, params=()):
        self.sql.append(sql)
        if "INSERT INTO payments" in sql:
            self._next = None if self.already else {"id": 99}
        else:
            self._next = None

    def fetchone(self):
        return self._next


def _fake_transaction(cursor):
    from contextlib import contextmanager

    @contextmanager
    def tx():
        yield cursor

    return tx


def test_pagamento_repetido_e_idempotente(fake_env, monkeypatch):
    cur = _FakeCursor(already=True)
    monkeypatch.setattr(payments.database, "transaction", _fake_transaction(cur))
    granted = []
    monkeypatch.setattr(payments.licenses, "grant", lambda *a: granted.append(a))
    assert payments.apply_approved_payment(_payment(), "webhook")["reason"] == "already_applied"
    assert granted == []


def test_pagamento_novo_cria_licenca_na_mesma_transacao(fake_env, monkeypatch):
    cur = _FakeCursor(already=False)
    monkeypatch.setattr(payments.database, "transaction", _fake_transaction(cur))
    monkeypatch.setattr(payments.licenses, "grant", lambda c, uid, plan, at: {"id": 5, "expires_at": NOW} if c is cur and uid == 7 else None)
    r = payments.apply_approved_payment(_payment(), "retorno")
    assert r["applied"] is True and r["license_id"] == 5
    assert any("UPDATE payments SET license_id" in s for s in cur.sql)


class _RevokeCursor:
    def __init__(self, row):
        self.row = row
        self.calls = []

    def execute(self, sql, params=()):
        self.calls.append((sql, params))

    def fetchone(self):
        return self.row


def test_reembolso_tira_os_dias_do_pagamento_da_licenca(fake_env, monkeypatch):
    cur = _RevokeCursor({"license_id": 5, "plan_key": "pro", "user_id": 7})
    monkeypatch.setattr(payments.database, "transaction", _fake_transaction(cur))
    r = payments.revoke_payment(_payment(status="refunded"), "webhook")
    assert r["revoked"] is True
    sql, params = cur.calls[-1]
    assert "UPDATE licenses SET expires_at = expires_at - make_interval" in sql and params == (30, 5)
    assert fake_env[-1][1] == "refunded"


def test_reembolso_repetido_ou_de_pagamento_nao_aplicado_nao_mexe_na_licenca(fake_env, monkeypatch):
    cur = _RevokeCursor(None)
    monkeypatch.setattr(payments.database, "transaction", _fake_transaction(cur))
    assert payments.revoke_payment(_payment(status="charged_back"), "webhook")["reason"] == "not_applied"
    assert not any("UPDATE licenses" in s for s, _ in cur.calls)


def test_webhook_de_estorno_vai_para_revogacao(fake_env, monkeypatch):
    monkeypatch.setattr(payments, "verify_mp_signature", lambda *a: True)

    class _Prov:
        def fetch_payment(self, pid):
            return _payment(status="charged_back")

    monkeypatch.setattr(payments, "provider", lambda: _Prov())
    seen = []
    monkeypatch.setattr(payments, "revoke_payment", lambda p, s: seen.append(p.status) or {"revoked": True})
    monkeypatch.setattr(payments, "apply_approved_payment", lambda *a: {"applied": True})
    assert payments.handle_webhook("p1", "ts=1,v1=x", "r")["revoked"] is True
    assert seen == ["charged_back"]
