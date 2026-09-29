"""Indicacao, numeros publicos e resumo do cliente: regras sem banco."""
from datetime import datetime, timedelta, timezone

from fastapi.testclient import TestClient

from app import auth
from app.main import app
from app.services import gameplay, referrals, users

T0 = datetime(2026, 9, 1, tzinfo=timezone.utc)


def test_numeros_publicos_ficam_escondidos_abaixo_do_minimo():
    out = gameplay.public_payload({"matches": 199, "devices": 500, "hours": 80, "games": 9}, {"devices": 29, "with_findings": 29})
    assert out == {"matches": None, "hours": None, "games": None, "scanned_pcs": None, "found_percent": None}


def test_numeros_publicos_arredondam_para_baixo():
    out = gameplay.public_payload({"matches": 250, "devices": 40, "hours": 91.9, "games": 7}, {"devices": 30, "with_findings": 29})
    assert out["matches"] == 250 and out["hours"] == 91 and out["games"] == 7
    # 29/30 = 96,67% -> anuncia 96, nunca 97.
    assert out["found_percent"] == 96 and out["scanned_pcs"] == 30


def _s(game, day, fps):
    return {"game_id": game, "started_at": T0 + timedelta(days=day), "avg_fps": fps, "low1_fps": fps / 2, "measured_seconds": 1800}


def test_resumo_so_compara_com_partidas_suficientes_dos_dois_lados():
    first = T0 + timedelta(days=10)
    sessions = [_s("cs2", d, 100) for d in (1, 2, 3)] + [_s("cs2", d, 130) for d in (11, 12, 13)] \
        + [_s("fortnite", 1, 60)] + [_s("fortnite", d, 90) for d in (11, 12, 13)]
    r = gameplay.build_recap({"optimizations": 4, "first_at": first}, sessions)
    assert r["optimizations"] == 4 and r["matches"] == 10 and r["hours"] == 5.0
    # Fortnite tem 1 partida antes: fica de fora em vez de anunciar ganho sem base.
    assert [g["game_id"] for g in r["games"]] == ["cs2"]
    assert r["games"][0]["avg_fps_before"] == 100 and r["games"][0]["avg_fps_after"] == 130


def test_resumo_sem_otimizacao_nao_inventa_antes_e_depois():
    r = gameplay.build_recap({"optimizations": 0, "first_at": None}, [_s("cs2", d, 100) for d in range(6)])
    assert r["games"] == [] and r["matches"] == 6


def test_codigo_de_indicacao_sem_letras_ambiguas():
    for _ in range(200):
        code = referrals.new_code()
        assert len(code) == referrals.CODE_LENGTH
        assert not set(code) & set("01OIL")


class FakeCursor:
    """Cursor de mentira: responde as consultas do premio na ordem em que chegam."""

    def __init__(self, answers):
        self.answers = list(answers)
        self.sql = []

    def execute(self, sql, params=()):
        self.sql.append((" ".join(sql.split()), params))

    def fetchone(self):
        return self.answers.pop(0)


def test_premio_de_indicacao_soma_dias_no_plano_atual(monkeypatch):
    monkeypatch.setattr(referrals.app_settings, "get", lambda k: 7 if k == "referral_days" else "pro")
    lic = {"id": 9, "status": "active"}
    cur = FakeCursor([{"referred_by": 3}, None, lic, {**lic, "expires_at": T0}])
    out = referrals.reward_on_payment(cur, referred_id=5, payment_id=77, at=T0)
    assert out == {"referrer_id": 3, "days": 7, "license_id": 9}
    assert any(s.startswith("UPDATE licenses SET expires_at = expires_at +") and p == (timedelta(days=7), 9) for s, p in cur.sql)
    assert any(s.startswith("INSERT INTO referral_rewards") and p == (3, 5, 77, 9, 7) for s, p in cur.sql)


def test_premio_nao_sai_duas_vezes_nem_para_si_mesmo(monkeypatch):
    monkeypatch.setattr(referrals.app_settings, "get", lambda k: 7)
    assert referrals.reward_on_payment(FakeCursor([{"referred_by": 3}, {"x": 1}]), 5, 77, T0) is None
    assert referrals.reward_on_payment(FakeCursor([{"referred_by": 5}]), 5, 77, T0) is None
    assert referrals.reward_on_payment(FakeCursor([{"referred_by": None}]), 5, 77, T0) is None


def test_premio_desligado_com_zero_dias(monkeypatch):
    monkeypatch.setattr(referrals.app_settings, "get", lambda k: 0)
    cur = FakeCursor([])
    assert referrals.reward_on_payment(cur, 5, 77, T0) is None and cur.sql == []


def test_cadastro_guarda_quem_indicou(monkeypatch):
    got = {}
    monkeypatch.setattr(referrals, "referrer_id", lambda code: 3 if code == "ABC2345" else None)
    monkeypatch.setattr(users, "create", lambda e, n, h, ref=None: got.update(ref=ref) or
                        {"id": 8, "email": e, "name": n, "role": "user", "active": True})
    from app.routers import auth as auth_router
    monkeypatch.setattr(auth_router.licenses, "create_trial", lambda uid: None)
    monkeypatch.setattr(auth_router.emails, "send_later", lambda *a, **k: None)
    monkeypatch.setattr(auth_router.app_settings, "get", lambda k: 7)
    r = TestClient(app).post("/api/auth/register", json={"email": "novo@x.com", "password": "12345678", "ref": "ABC2345"})
    assert r.status_code == 200 and got["ref"] == 3


def test_rotas_novas_da_conta_exigem_login():
    app.dependency_overrides.clear()
    c = TestClient(app)
    assert c.get("/api/account/recap").status_code == 401
    assert c.get("/api/account/referral").status_code == 401


def test_numeros_publicos_na_api(monkeypatch):
    monkeypatch.setattr(gameplay, "public_stats", lambda: {"matches": None})
    assert TestClient(app).get("/api/public/stats").json() == {"matches": None}
    assert auth  # import usado pelas outras suites; mantem o modulo carregado
