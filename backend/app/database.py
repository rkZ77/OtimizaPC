"""Acesso ao PostgreSQL com psycopg2 e SQL na mao, sem ORM (padrao do Pickia).

O pool nasce na primeira necessidade, nunca no import: a suite de teste
substitui `get_connection` antes de qualquer chamada, e um pool criado no
import abriria conexao de verdade.
"""
import os
import threading
from contextlib import contextmanager
from urllib.parse import urlparse

import psycopg2
import psycopg2.extras
import psycopg2.pool


def _parametros() -> dict:
    url = os.getenv("DATABASE_URL", "")
    if url:
        p = urlparse(url)
        return dict(
            host=p.hostname, port=p.port or 5432, dbname=p.path.lstrip("/") or "postgres",
            user=p.username, password=p.password,
            sslmode=os.getenv("DB_SSLMODE", "prefer"),
            cursor_factory=psycopg2.extras.RealDictCursor, connect_timeout=10,
            # UTF-8 fixo: pelo pooler do Supabase em modo transacao a conexao
            # chegou a ser lida como latin1, e todo acento do banco virou
            # "DiagnÃ³stico" no site (28/09/2026).
            client_encoding="UTF8",
        )
    return dict(
        host=os.getenv("DB_HOST", "localhost"), port=os.getenv("DB_PORT", "5432"),
        dbname=os.getenv("DB_NAME", "fpsx"), user=os.getenv("DB_USER", "fpsx"),
        password=os.getenv("DB_PASS", ""), sslmode=os.getenv("DB_SSLMODE", "prefer"),
        cursor_factory=psycopg2.extras.RealDictCursor, connect_timeout=10, client_encoding="UTF8",
    )


_POOL_MIN = int(os.getenv("DB_POOL_MIN", "1"))
_POOL_MAX = int(os.getenv("DB_POOL_MAX", "10"))
_pool = None
_pool_lock = threading.Lock()
# O ThreadedConnectionPool nao espera: com as conexoes todas em uso ele lanca
# PoolError na hora, e o visitante leva erro 500 (medido no staging com 10
# acessos simultaneos). O semaforo faz a requisicao esperar a vez na fila.
_vagas = threading.BoundedSemaphore(_POOL_MAX)
_ESPERA_SEGUNDOS = float(os.getenv("DB_POOL_WAIT", "15"))


class PoolOcupado(psycopg2.pool.PoolError):
    """Nenhuma conexao livre dentro do tempo de espera."""


class _PoolQueGuarda(psycopg2.pool.ThreadedConnectionPool):
    """O psycopg2 so' guarda aberto o MINIMO de conexoes: toda conexao alem
    dele e' fechada ao voltar. Com acesso simultaneo, cada requisicao abria
    conexao nova com o Supabase (TLS e login em outra regiao), e o staging
    atendia 3 por segundo com 1 ou 10 simultaneos. Aqui abre o minimo no
    inicio, mas guarda ate' o maximo depois de usadas."""

    def __init__(self, minconn, maxconn, **kwargs):
        super().__init__(minconn, maxconn, **kwargs)
        self.minconn = maxconn


def _obter_pool():
    global _pool
    if _pool is None:
        with _pool_lock:
            if _pool is None:
                _pool = _PoolQueGuarda(_POOL_MIN, _POOL_MAX, **_parametros())
    return _pool


class _ConexaoDoPool:
    """Proxy cujo close() devolve a conexao ao pool em vez de fechar."""

    __slots__ = ("_conn", "_devolvida")

    def __init__(self, conn):
        self._conn = conn
        self._devolvida = False

    def __getattr__(self, nome):
        return getattr(self._conn, nome)

    def close(self):
        # Idempotente: try/finally aninhado costuma fechar duas vezes, e
        # devolver a mesma conexao duas vezes corromperia o pool.
        if self._devolvida:
            return
        self._devolvida = True
        try:
            if not self._conn.closed:
                self._conn.rollback()
            _obter_pool().putconn(self._conn, close=bool(self._conn.closed))
        except psycopg2.Error:
            _obter_pool().putconn(self._conn, close=True)
        finally:
            _vagas.release()


def get_connection():
    if not _vagas.acquire(timeout=_ESPERA_SEGUNDOS):
        raise PoolOcupado("Nenhuma conexão livre com o banco.")
    try:
        conn = _obter_pool().getconn()
        # Confere a cada uso: conexao que voltou do pooler com outra
        # codificacao e' corrigida antes de ler qualquer texto.
        if conn.encoding != "UTF8":
            conn.set_client_encoding("UTF8")
        return _ConexaoDoPool(conn)
    except BaseException:
        _vagas.release()
        raise


@contextmanager
def transaction():
    """Cursor dentro de uma transacao: commit no fim, rollback em qualquer erro."""
    conn = get_connection()
    try:
        cur = conn.cursor()
        yield cur
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()


def fetch_one(sql: str, params=()) -> dict | None:
    with transaction() as cur:
        cur.execute(sql, params)
        row = cur.fetchone()
        return dict(row) if row else None


def fetch_all(sql: str, params=()) -> list[dict]:
    with transaction() as cur:
        cur.execute(sql, params)
        return [dict(r) for r in cur.fetchall()]


def execute(sql: str, params=()) -> int:
    with transaction() as cur:
        cur.execute(sql, params)
        return cur.rowcount
