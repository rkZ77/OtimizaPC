"""Partidas medidas pelo app (secao 40 + pedido do dono de 27/09/2026).

O app so' envia com consentimento de telemetria. O servidor aceita so' os
campos da lista, com limite de tamanho: o gráfico tem no máximo 900 pontos e
as causas das quedas sao so' contagens por tipo.

Comparacao com PCs parecidos so' aparece com pelo menos PEER_MIN_DEVICES
PCs diferentes no grupo: com menos, o numero apontaria para uma pessoa.
"""
import json

from app import database

DROP_CAUSES = {"app", "cpu", "gpu", "game", "unknown"}
HARDWARE_KEYS = {"tier": str, "cpu": str, "threads": int, "gpu": str, "vram_gb": float, "ram_gb": float, "windows_build": int}
MAX_POINTS = 900
PEER_MIN_DEVICES = 5


def clean_hardware(hw: dict | None) -> dict:
    out = {}
    for key, kind in HARDWARE_KEYS.items():
        value = (hw or {}).get(key)
        if value is None:
            continue
        try:
            out[key] = kind(value)[:120] if kind is str else kind(value)
        except (TypeError, ValueError):
            continue
    return out


def clean_timeline(points: list) -> list:
    out = []
    for p in points[:MAX_POINTS]:
        if isinstance(p, (list, tuple)) and len(p) == 3 and all(isinstance(v, (int, float)) for v in p):
            t, fps, low = p
            if 0 <= t <= 86400 and 0 <= fps <= 10000 and 0 <= low <= 10000:
                out.append([int(t), round(float(fps), 1), round(float(low), 1)])
    return out


def clean_causes(causes: dict | None) -> dict:
    return {k: int(v) for k, v in (causes or {}).items() if k in DROP_CAUSES and isinstance(v, int) and 0 <= v <= 100000}


def record(device: dict, m: dict) -> None:
    """Grava a partida. Reenvio da mesma partida atualiza em vez de duplicar."""
    database.execute(
        """INSERT INTO gameplay_sessions (device_id, session_key, game_id, started_at, ended_at, measured_seconds,
               avg_fps, low1_fps, low01_fps, p99_frametime_ms, stutters_per_minute, avg_cpu_percent, avg_gpu_percent,
               display_hz, agent_version, hardware, timeline, drops, drop_causes)
           VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
           ON CONFLICT (device_id, session_key) DO UPDATE SET
               hardware = EXCLUDED.hardware, timeline = EXCLUDED.timeline, drops = EXCLUDED.drops,
               drop_causes = EXCLUDED.drop_causes""",
        (device["id"], m["session_key"], m["game_id"], m["started_at"], m["ended_at"], m["measured_seconds"],
         m["avg_fps"], m["low1_fps"], m["low01_fps"], m["p99_frametime_ms"], m["stutters_per_minute"],
         m.get("avg_cpu_percent"), m.get("avg_gpu_percent"), m.get("display_hz"), m.get("agent_version", ""),
         json.dumps(clean_hardware(m.get("hardware"))), json.dumps(clean_timeline(m.get("timeline") or [])),
         m.get("drops", 0), json.dumps(clean_causes(m.get("drop_causes")))))


def peers(device: dict, game_id: str) -> dict:
    """Como PCs parecidos rodam o mesmo jogo: primeiro mesma placa, depois mesmo nivel de PC.

    Usa a partida mais recente de cada OUTRO PC (um PC que joga muito nao pesa mais que os outros)."""
    mine = database.fetch_one(
        """SELECT hardware FROM gameplay_sessions WHERE device_id = %s AND game_id = %s
           ORDER BY started_at DESC LIMIT 1""", (device["id"], game_id))
    hw = (mine or {}).get("hardware") or {}
    for scope, key in (("gpu", hw.get("gpu")), ("tier", hw.get("tier"))):
        if not key:
            continue
        row = database.fetch_one(
            f"""WITH last AS (
                    SELECT DISTINCT ON (device_id) device_id, avg_fps, low1_fps
                    FROM gameplay_sessions
                    WHERE game_id = %s AND device_id <> %s AND hardware->>'{scope}' = %s
                    ORDER BY device_id, started_at DESC)
                SELECT count(*) AS devices,
                       percentile_cont(0.5) WITHIN GROUP (ORDER BY avg_fps) AS avg_fps,
                       percentile_cont(0.5) WITHIN GROUP (ORDER BY low1_fps) AS low1_fps
                FROM last""", (game_id, device["id"], key))
        if row and row["devices"] >= PEER_MIN_DEVICES:
            return {"scope": scope, "label": key, "devices": row["devices"],
                    "avg_fps": round(row["avg_fps"], 1), "low1_fps": round(row["low1_fps"], 1)}
    return {"scope": None, "devices": 0}


def summary() -> dict:
    """Visao do admin: por jogo e por nivel de PC, com mediana (partida real varia muito)."""
    by_game = database.fetch_all(
        """SELECT game_id, count(*) AS matches, count(DISTINCT device_id) AS devices,
                  percentile_cont(0.5) WITHIN GROUP (ORDER BY avg_fps) AS avg_fps,
                  percentile_cont(0.5) WITHIN GROUP (ORDER BY low1_fps) AS low1_fps,
                  percentile_cont(0.5) WITHIN GROUP (ORDER BY stutters_per_minute) AS stutters_per_minute,
                  sum(drops) AS drops, sum(measured_seconds) / 3600 AS hours
           FROM gameplay_sessions GROUP BY game_id ORDER BY matches DESC""")
    by_tier = database.fetch_all(
        """SELECT game_id, coalesce(hardware->>'tier', '?') AS tier, count(*) AS matches,
                  count(DISTINCT device_id) AS devices,
                  percentile_cont(0.5) WITHIN GROUP (ORDER BY avg_fps) AS avg_fps,
                  percentile_cont(0.5) WITHIN GROUP (ORDER BY low1_fps) AS low1_fps
           FROM gameplay_sessions GROUP BY 1, 2 ORDER BY 1, 2""")
    causes = database.fetch_all(
        """SELECT key AS cause, sum(value::int) AS drops
           FROM gameplay_sessions, jsonb_each_text(drop_causes) GROUP BY key ORDER BY 2 DESC""")
    return {"by_game": by_game, "by_tier": by_tier, "drop_causes": causes}


def recent(limit: int = 50, offset: int = 0) -> list[dict]:
    return database.fetch_all(
        """SELECT g.id, g.game_id, g.started_at, g.measured_seconds, g.avg_fps, g.low1_fps, g.low01_fps,
                  g.stutters_per_minute, g.drops, g.drop_causes, g.hardware, g.agent_version, d.name AS device_name,
                  d.user_id
           FROM gameplay_sessions g JOIN devices d ON d.id = g.device_id
           ORDER BY g.started_at DESC LIMIT %s OFFSET %s""", (limit, offset))


def timeline(session_id: int) -> dict | None:
    return database.fetch_one("SELECT id, game_id, avg_fps, timeline FROM gameplay_sessions WHERE id = %s", (session_id,))
