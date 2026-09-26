import pytest

from app import migrate
from app.services import app_settings, catalog, telemetry


def test_telemetria_descarta_evento_e_campo_desconhecidos():
    assert telemetry.sanitize({"event": "keylogger"}) is None
    e = telemetry.sanitize({
        "event": "optimization_applied", "optimization_id": "game-mode-enable", "success": True,
        "detail": {"profile": "gaming", "caminho": "C:\\Users\\joao\\Desktop", "processos": ["chrome"], "error": "x" * 999},
    })
    assert e["detail"] == {"profile": "gaming", "error": "x" * telemetry.MAX_TEXT}
    assert e["success"] is True


def test_configuracoes_validadas():
    assert app_settings.validate("trial_days", 7) == 7
    for key, value in [("trial_days", True), ("trial_days", "7"), ("trial_days", 400), ("trial_plan", "free"), ("desconhecida", 1)]:
        with pytest.raises(ValueError):
            app_settings.validate(key, value)


def test_override_de_catalogo_validado():
    catalog.validate_override({"kind": "optimization", "id": "game-mode-enable", "risk": "LOW"})
    with pytest.raises(ValueError):
        catalog.validate_override({"kind": "optimization", "id": "x", "risk": "BAIXO"})
    with pytest.raises(ValueError):
        catalog.validate_override({"kind": "script", "id": "x"})


def test_admin_le_o_catalogo_real_com_comentarios():
    defs = catalog.definitions()
    ids = {d["id"] for d in defs}
    assert {"game-mode-enable", "game-settings-fix", "disable-windows-defender"} <= ids
    assert next(d for d in defs if d["id"] == "hags-enable")["min_plan"] == "ultimate"


def test_release_so_por_https():
    with pytest.raises(ValueError):
        catalog.publish_release({"component": "agent", "version": "1.0", "url": "http://x/fpsx.exe"})


def test_migrations_em_ordem_e_sem_drop():
    files = migrate.pending(set())
    assert [f.name for f in files] == sorted(f.name for f in files)
    assert files[0].name == "0001_schema.sql"
    assert migrate.pending({f.name for f in files}) == []
    for f in files:
        assert "DROP " not in f.read_text(encoding="utf-8").upper()
