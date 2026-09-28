"""Fila do pool de conexoes, sem banco: pool e conexao sao falsos."""
import threading
import time

import pytest

from app import database
from app.database import get_connection as _get_connection_real  # antes da trava do conftest


class _Conn:
    closed = False

    def rollback(self):
        pass


class _Pool:
    def getconn(self):
        return _Conn()

    def putconn(self, conn, close=False):
        pass


@pytest.fixture
def pool_de_uma(monkeypatch):
    monkeypatch.setattr(database, "_obter_pool", lambda: _Pool())
    monkeypatch.setattr(database, "_vagas", threading.BoundedSemaphore(1))
    monkeypatch.setattr(database, "_ESPERA_SEGUNDOS", 0.2)


def test_pool_cheio_espera_a_vez_em_vez_de_dar_erro(pool_de_uma):
    primeira = _get_connection_real()
    threading.Timer(0.05, primeira.close).start()
    inicio = time.monotonic()
    segunda = _get_connection_real()
    assert time.monotonic() - inicio >= 0.04
    segunda.close()


def test_pool_cheio_alem_da_espera_vira_pool_ocupado_e_close_devolve_a_vaga(pool_de_uma):
    primeira = _get_connection_real()
    with pytest.raises(database.PoolOcupado):
        _get_connection_real()
    primeira.close()
    primeira.close()  # fechar duas vezes nao devolve a vaga duas vezes
    _get_connection_real().close()


class _ConnReal:
    closed = False

    class info:
        transaction_status = 0  # TRANSACTION_STATUS_IDLE

    def close(self):
        self.closed = True


def test_pool_guarda_conexoes_devolvidas_ate_o_maximo(monkeypatch):
    abertas = []

    def conectar(self, key=None):
        conn = _ConnReal()
        abertas.append(conn)
        if key is not None:
            self._used[key] = conn
            self._rused[id(conn)] = key
        else:
            self._pool.append(conn)
        return conn

    monkeypatch.setattr(database._PoolQueGuarda, "_connect", conectar)
    pool = database._PoolQueGuarda(1, 5)
    em_uso = [pool.getconn() for _ in range(5)]
    for conn in em_uso:
        pool.putconn(conn)
    assert not any(c.closed for c in abertas)
    [pool.getconn() for _ in range(5)]
    assert len(abertas) == 5  # reusou, nao abriu conexao nova
