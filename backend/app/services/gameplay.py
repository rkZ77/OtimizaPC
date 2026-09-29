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


# ---- numeros publicos (prova social do site) ----
# So' contagens do conjunto, nunca de uma pessoa, e so' a partir de um minimo:
# "3 partidas medidas" na home depoe contra, e numero pequeno aponta para alguem.
PUBLIC_MIN_MATCHES = 200
PUBLIC_MIN_DEVICES = 30
_STATS_SECONDS = 600
_stats_cache: tuple[float, dict] | None = None


def public_stats() -> dict:
    import time
    global _stats_cache
    now = time.monotonic()
    if _stats_cache is not None and now - _stats_cache[0] < _STATS_SECONDS:
        return _stats_cache[1]
    row = database.fetch_one(
        """SELECT count(*) AS matches, count(DISTINCT device_id) AS devices,
                  coalesce(sum(measured_seconds), 0) / 3600 AS hours,
                  count(DISTINCT game_id) AS games
           FROM gameplay_sessions""") or {}
    # PRIMEIRO scan de cada PC (antes do RKZFPS mexer): quantos tinham algo para
    # corrigir. O ultimo scan mediria o PC ja' corrigido. Mesmo minimo de PCs.
    scans = database.fetch_one(
        """WITH last AS (
               SELECT DISTINCT ON (device_id) device_id, detail
               FROM telemetry_events WHERE event = 'scan_completed'
               ORDER BY device_id, coalesce(occurred_at, created_at) ASC)
           SELECT count(*) AS devices,
                  count(*) FILTER (WHERE coalesce((detail->>'problems')::int, 0) + coalesce((detail->>'recommended')::int, 0) > 0) AS with_findings
           FROM last""") or {}
    result = public_payload(row, scans)
    _stats_cache = (now, result)
    return result


def public_payload(row: dict, scans: dict) -> dict:
    """Separado da consulta para o teste conferir os minimos sem banco."""
    out: dict = {"matches": None, "hours": None, "games": None, "scanned_pcs": None, "found_percent": None}
    if (row.get("matches") or 0) >= PUBLIC_MIN_MATCHES and (row.get("devices") or 0) >= PUBLIC_MIN_DEVICES:
        out.update(matches=int(row["matches"]), hours=int(row["hours"] or 0), games=int(row["games"] or 0))
    pcs = scans.get("devices") or 0
    if pcs >= PUBLIC_MIN_DEVICES:
        # Arredonda para baixo: nunca anunciar mais do que o medido.
        out.update(scanned_pcs=int(pcs), found_percent=int((scans.get("with_findings") or 0) * 100 // pcs))
    return out


# ---- o que o RKZFPS fez para esta pessoa (pagina Meu plano) ----
#: Partidas de cada lado para comparar antes e depois. Com menos, a variacao
#: normal entre partidas engole a diferenca e o numero mentiria.
RECAP_MIN_SIDE = 3


def recap_for_user(user_id: int) -> dict:
    applied = database.fetch_one(
        """SELECT count(DISTINCT t.optimization_id) AS optimizations,
                  min(coalesce(t.occurred_at, t.created_at)) AS first_at
           FROM telemetry_events t JOIN devices d ON d.id = t.device_id
           WHERE d.user_id = %s AND t.event IN ('optimization_applied', 'fix_applied') AND t.success IS NOT FALSE""",
        (user_id,)) or {}
    sessions = database.fetch_all(
        """SELECT g.game_id, g.started_at, g.avg_fps, g.low1_fps, g.measured_seconds
           FROM gameplay_sessions g JOIN devices d ON d.id = g.device_id
           WHERE d.user_id = %s ORDER BY g.started_at""", (user_id,))
    return build_recap(applied, sessions)


def _median(values: list[float]) -> float:
    s = sorted(values)
    n = len(s)
    return s[n // 2] if n % 2 else (s[n // 2 - 1] + s[n // 2]) / 2


def build_recap(applied: dict, sessions: list[dict]) -> dict:
    first = applied.get("first_at")
    games = []
    by_game: dict[str, list[dict]] = {}
    for s in sessions:
        by_game.setdefault(s["game_id"], []).append(s)
    for game, rows in by_game.items():
        if first is None:
            continue
        before = [r for r in rows if r["started_at"] < first]
        after = [r for r in rows if r["started_at"] >= first]
        if len(before) < RECAP_MIN_SIDE or len(after) < RECAP_MIN_SIDE:
            continue
        b, a = _median([r["avg_fps"] for r in before]), _median([r["avg_fps"] for r in after])
        bl, al = _median([r["low1_fps"] for r in before]), _median([r["low1_fps"] for r in after])
        games.append({"game_id": game, "matches_before": len(before), "matches_after": len(after),
                      "avg_fps_before": round(b, 1), "avg_fps_after": round(a, 1),
                      "low1_fps_before": round(bl, 1), "low1_fps_after": round(al, 1)})
    return {
        "optimizations": int(applied.get("optimizations") or 0),
        "first_optimization_at": first,
        "matches": len(sessions),
        "hours": round(sum(s["measured_seconds"] for s in sessions) / 3600, 1),
        "games": games,
    }
