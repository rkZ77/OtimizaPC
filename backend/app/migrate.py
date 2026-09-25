"""Migrations versionadas em SQL puro.

O Pickia acumulou ALTER TABLE soltos no startup e o proprio comentario de la
pede para migrar para arquivos versionados. Aqui ja nasce assim: cada arquivo
em migrations/ roda uma vez, em ordem, dentro de uma transacao, registrado em
schema_migrations.
"""
import logging
from pathlib import Path

from app import database

MIGRATIONS_DIR = Path(__file__).parent / "migrations"

# Chave fixa de advisory lock: com mais de um worker subindo ao mesmo tempo,
# so' um aplica migrations e os outros esperam.
_LOCK_KEY = 718_2026


def pending(applied: set[str]) -> list[Path]:
    return [p for p in sorted(MIGRATIONS_DIR.glob("*.sql")) if p.name not in applied]


def run(logger: logging.Logger) -> list[str]:
    conn = database.get_connection()
    done = []
    try:
        cur = conn.cursor()
        cur.execute("SELECT pg_advisory_lock(%s)", (_LOCK_KEY,))
        cur.execute("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                name TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
            )
        """)
        conn.commit()
        cur.execute("SELECT name FROM schema_migrations")
        applied = {r["name"] for r in cur.fetchall()}
        for path in pending(applied):
            logger.info("[MIGRATION] aplicando %s", path.name)
            cur.execute(path.read_text(encoding="utf-8"))
            cur.execute("INSERT INTO schema_migrations (name) VALUES (%s)", (path.name,))
            conn.commit()
            done.append(path.name)
        cur.execute("SELECT pg_advisory_unlock(%s)", (_LOCK_KEY,))
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()
    return done


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    print(run(logging.getLogger("migrate")))
