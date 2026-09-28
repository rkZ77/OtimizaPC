import os
import sys

import pytest

os.environ.setdefault("APP_ENV", "development")
os.environ["AUTO_MIGRATE"] = "0"
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


@pytest.fixture(autouse=True)
def _sem_banco_no_teste(monkeypatch):
    """Nenhum teste fala com banco. Mesma trava do Pickia, onde uma suite
    solta ja escreveu em producao: aqui o .env pode apontar para o Supabase."""
    def _recusa(*_a, **_kw):
        raise RuntimeError("Teste tentou abrir conexão de banco. Use monkeypatch.")

    from app import auth, database
    from app.services import plans

    monkeypatch.setattr(database, "get_connection", _recusa)
    auth.reset_rate_limits()
    plans.clear_cache()
