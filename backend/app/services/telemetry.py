"""Telemetria minima (secao 40): so' eventos da lista, so' campos da lista.

O app so' envia com consentimento, e mesmo assim o servidor descarta o que
nao reconhece. Campo livre com caminho de arquivo, nome de usuario ou lista
de processos nunca chega ao banco.
"""
import json
import re
from datetime import datetime, timedelta, timezone

from app import database

EVENTS = {
    "scan_completed", "optimization_applied", "optimization_failed", "rollback",
    "benchmark_result", "agent_error", "fix_applied",
}

#: Chaves aceitas no `detail`. Qualquer outra e' removida.
DETAIL_KEYS = {"profile", "catalog_version", "decision", "error_code", "error", "problems", "recommended", "duration_ms", "plan"}

MAX_TEXT = 300

_VERSION = re.compile(r"^[0-9][0-9A-Za-z.+-]{0,39}$")

#: Evento mais antigo que isso (fila esquecida) ou no futuro (relogio errado)
#: fica sem hora propria: o banco usa a do envio.
MAX_AGE = timedelta(days=180)


def occurred_at(value) -> datetime | None:
    """Hora em que o evento aconteceu no PC, se for plausivel."""
    if not isinstance(value, str):
        return None
    try:
        at = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    if at.tzinfo is None:
        return None
    now = datetime.now(timezone.utc)
    return at if now - MAX_AGE <= at <= now + timedelta(days=1) else None


def sanitize(event: dict) -> dict | None:
    name = str(event.get("event", ""))
    if name not in EVENTS:
        return None
    detail = event.get("detail") or {}
    clean = {}
    if isinstance(detail, dict):
        for k, v in detail.items():
            if k not in DETAIL_KEYS:
                continue
            if isinstance(v, str):
                clean[k] = v[:MAX_TEXT]
            elif isinstance(v, (bool, int, float)):
                clean[k] = v
    opt = event.get("optimization_id")
    version = event.get("agent_version")
    return {
        "occurred_at": occurred_at(event.get("occurred_at")),
        "event_agent_version": version if isinstance(version, str) and _VERSION.match(version) else None,
        "event": name,
        "optimization_id": str(opt)[:80] if opt else None,
        "success": event.get("success") if isinstance(event.get("success"), bool) else None,
        "detail": clean,
    }


def record(device: dict, events: list[dict]) -> int:
    rows = [e for e in (sanitize(e) for e in events[:100]) if e is not None]
    with database.transaction() as cur:
        for e in rows:
            cur.execute(
                """INSERT INTO telemetry_events (device_id, event, optimization_id, success, agent_version, windows_build, detail,
                                                 occurred_at, event_agent_version)
                   VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)""",
                (device["id"], e["event"], e["optimization_id"], e["success"],
                 device["agent_version"], device["windows_build"], json.dumps(e["detail"]),
                 e["occurred_at"], e["event_agent_version"]))
    return len(rows)


def record_benchmark(device: dict, b: dict) -> None:
    database.execute(
        """INSERT INTO benchmark_results (device_id, game_id, label, session_id, avg_fps, low1_fps, low01_fps, frametime_ms, frames)
           VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)""",
        (device["id"], b["game_id"], b.get("label", ""), b.get("session_id"), b["avg_fps"], b["low1_fps"],
         b["low01_fps"], b["frametime_ms"], b["frames"]))


def benchmarks_for_user(user_id: int, limit: int = 50) -> list[dict]:
    return database.fetch_all(
        """SELECT b.*, d.name AS device_name FROM benchmark_results b JOIN devices d ON d.id = b.device_id
           WHERE d.user_id = %s ORDER BY b.created_at DESC LIMIT %s""", (user_id, limit))


def history_for_user(user_id: int, limit: int = 100) -> list[dict]:
    return database.fetch_all(
        """SELECT t.event, t.optimization_id, t.success, t.detail, t.created_at, d.name AS device_name
           FROM telemetry_events t JOIN devices d ON d.id = t.device_id
           WHERE d.user_id = %s ORDER BY t.created_at DESC LIMIT %s""", (user_id, limit))


def errors(limit: int = 100) -> list[dict]:
    return database.fetch_all(
        """SELECT t.*, d.name AS device_name FROM telemetry_events t LEFT JOIN devices d ON d.id = t.device_id
           WHERE t.event IN ('agent_error', 'optimization_failed') ORDER BY t.created_at DESC LIMIT %s""", (limit,))


def metrics() -> dict:
    row = database.fetch_one("""
        SELECT
          (SELECT count(*) FROM users) AS users,
          (SELECT count(*) FROM licenses WHERE status = 'active' AND expires_at > now()) AS active_licenses,
          (SELECT count(*) FROM licenses WHERE status = 'trial' AND expires_at > now()) AS trials,
          (SELECT count(*) FROM devices WHERE deactivated_at IS NULL) AS devices,
          (SELECT count(*) FROM devices WHERE last_seen_at > now() - interval '7 days') AS devices_7d,
          (SELECT coalesce(sum(amount_cents), 0) FROM payments WHERE status = 'approved'
             AND approved_at > now() - interval '30 days') AS revenue_30d_cents
    """)
    by_opt = database.fetch_all("""
        SELECT optimization_id, count(*) FILTER (WHERE success) AS ok, count(*) FILTER (WHERE NOT success) AS failed
        FROM telemetry_events WHERE event IN ('optimization_applied', 'optimization_failed', 'fix_applied')
          AND created_at > now() - interval '30 days' AND optimization_id IS NOT NULL
        GROUP BY optimization_id ORDER BY count(*) DESC LIMIT 30
    """)
    return {**(row or {}), "optimizations_30d": by_opt}
