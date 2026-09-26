"""Leituras do painel admin: quebra da base, financeiro, funil, engajamento, uso.

Desenho do admin do Pickia: cartoes contam ESTOQUE (quantos sao assinantes
hoje) e o funil conta PASSAGEM (de quem criou conta na janela, quantos
chegaram a cada etapa). Os dois juntos separam "converte pouco" de "pouca
gente chega la'", que pedem remedios diferentes.

O segmento de cada pessoa sai da MESMA regra de licenses.best_license: a
licenca utilizavel de maior tier, desempate por vencimento mais longe.
"""
from app import database

# Licenca vigente por usuario, com o status efetivo (vencida sem cron vira
# 'expired' aqui tambem). Espelha licenses.best_license/effective_status.
CURRENT_LICENSE = """
    SELECT DISTINCT ON (l.user_id)
        l.user_id, l.id AS license_id, l.tier, l.plan_key, l.expires_at, l.max_devices,
        CASE WHEN l.status = 'blocked' THEN 'blocked'
             WHEN l.expires_at <= now() THEN 'expired'
             ELSE l.status END AS eff_status
    FROM licenses l
    ORDER BY l.user_id,
        (CASE WHEN l.status IN ('active', 'trial') AND l.expires_at > now() THEN 0 ELSE 1 END),
        (CASE l.tier WHEN 'custom' THEN 4 WHEN 'ultimate' THEN 3 WHEN 'pro' THEN 2 WHEN 'starter' THEN 1 ELSE 0 END) DESC,
        l.expires_at DESC
"""

SEGMENT = """
    CASE WHEN u.role = 'admin' THEN 'admin'
         WHEN NOT u.active THEN 'blocked'
         WHEN cur.eff_status = 'active' THEN 'subscriber'
         WHEN cur.eff_status = 'trial' THEN 'trial'
         WHEN cur.eff_status = 'blocked' THEN 'blocked'
         WHEN cur.eff_status = 'expired' THEN 'expired'
         ELSE 'free' END
"""

SEGMENTS = ("subscriber", "trial", "free", "expired", "blocked", "admin")


def _base_users() -> str:
    return f"""
        WITH cur AS ({CURRENT_LICENSE}),
        dev AS (
            SELECT user_id, count(*) FILTER (WHERE deactivated_at IS NULL) AS devices,
                   max(last_seen_at) AS last_seen_at
            FROM devices GROUP BY user_id
        ),
        base AS (
            SELECT u.id, u.email, u.name, u.role, u.active, u.created_at,
                   cur.license_id, cur.tier, cur.plan_key, cur.expires_at, cur.eff_status, cur.max_devices,
                   coalesce(dev.devices, 0) AS devices, dev.last_seen_at,
                   {SEGMENT} AS segment
            FROM users u
            LEFT JOIN cur ON cur.user_id = u.id
            LEFT JOIN dev ON dev.user_id = u.id
        )
    """


def user_stats() -> dict:
    row = database.fetch_one(_base_users() + """
        SELECT count(*) AS total,
               count(*) FILTER (WHERE segment = 'subscriber') AS subscribers,
               count(*) FILTER (WHERE segment = 'trial') AS trial,
               count(*) FILTER (WHERE segment = 'free') AS free,
               count(*) FILTER (WHERE segment = 'expired') AS expired,
               count(*) FILTER (WHERE segment = 'blocked') AS blocked,
               count(*) FILTER (WHERE segment = 'admin') AS admins,
               count(*) FILTER (WHERE segment IN ('subscriber', 'trial')
                                AND expires_at <= now() + interval '7 days') AS expiring_7d,
               count(*) FILTER (WHERE last_seen_at > now() - interval '7 days') AS active_7d,
               count(*) FILTER (WHERE created_at > now() - interval '7 days') AS new_7d
        FROM base
    """)
    by_tier = database.fetch_all(_base_users() + """
        SELECT tier, count(*) AS n FROM base WHERE segment = 'subscriber' GROUP BY tier ORDER BY n DESC
    """)
    return {**(row or {}), "subscribers_by_tier": by_tier}


def list_users(q: str, segment: str | None, limit: int, offset: int) -> dict:
    like = f"%{q.strip().lower()}%"
    params: list = [like, like]
    where = "WHERE (lower(email) LIKE %s OR lower(name) LIKE %s)"
    if segment == "expiring":
        where += " AND segment IN ('subscriber', 'trial') AND expires_at <= now() + interval '7 days'"
    elif segment in SEGMENTS:
        where += " AND segment = %s"
        params.append(segment)
    total = database.fetch_one(_base_users() + f"SELECT count(*) AS n FROM base {where}", params)
    rows = database.fetch_all(
        _base_users() + f"SELECT * FROM base {where} ORDER BY created_at DESC LIMIT %s OFFSET %s",
        [*params, limit, offset])
    return {"users": rows, "total": total["n"] if total else 0}


def user_detail(user_id: int) -> dict | None:
    user = database.fetch_one(_base_users() + "SELECT * FROM base WHERE id = %s", (user_id,))
    if user is None:
        return None
    return {
        "user": user,
        "licenses": database.fetch_all(
            """SELECT id, plan_key, tier, status, max_devices, starts_at, expires_at, blocked_reason, created_at,
                      CASE WHEN status = 'blocked' THEN 'blocked' WHEN expires_at <= now() THEN 'expired' ELSE status END AS eff_status
               FROM licenses WHERE user_id = %s ORDER BY created_at DESC""", (user_id,)),
        "devices": database.fetch_all(
            "SELECT id, name, windows_build, agent_version, activated_at, last_seen_at, deactivated_at FROM devices WHERE user_id = %s ORDER BY last_seen_at DESC",
            (user_id,)),
        "payments": database.fetch_all(
            "SELECT id, plan_key, status, amount_cents, coupon_code, created_at, approved_at FROM payments WHERE user_id = %s ORDER BY id DESC",
            (user_id,)),
        "events": database.fetch_all(
            """SELECT t.event, t.optimization_id, t.success, t.created_at, d.name AS device_name
               FROM telemetry_events t JOIN devices d ON d.id = t.device_id
               WHERE d.user_id = %s ORDER BY t.created_at DESC LIMIT 30""", (user_id,)),
    }


def finance() -> dict:
    totals = database.fetch_one("""
        SELECT coalesce(sum(amount_cents), 0) AS total_cents,
               count(*) AS count,
               coalesce(round(avg(amount_cents)), 0) AS avg_ticket_cents,
               coalesce(sum(amount_cents) FILTER (WHERE approved_at > now() - interval '30 days'), 0) AS last_30d_cents,
               count(*) FILTER (WHERE approved_at > now() - interval '30 days') AS last_30d_count
        FROM payments WHERE status = 'approved'
    """)
    monthly = database.fetch_all("""
        SELECT to_char(date_trunc('month', approved_at), 'YYYY-MM') AS month,
               sum(amount_cents) AS total_cents, count(*) AS count
        FROM payments WHERE status = 'approved' AND approved_at > date_trunc('month', now()) - interval '11 months'
        GROUP BY 1 ORDER BY 1
    """)
    by_plan = database.fetch_all("""
        SELECT plan_key, sum(amount_cents) AS total_cents, count(*) AS count
        FROM payments WHERE status = 'approved' GROUP BY plan_key ORDER BY total_cents DESC
    """)
    coupons = database.fetch_all("""
        SELECT coupon_code, count(*) AS count, sum(amount_cents) AS total_cents
        FROM payments WHERE status = 'approved' AND coupon_code IS NOT NULL GROUP BY coupon_code ORDER BY count DESC
    """)
    return {**(totals or {}), "monthly": monthly, "by_plan": by_plan, "coupons": coupons,
            "subscribers_by_tier": user_stats()["subscribers_by_tier"]}


def funnel(days: int) -> dict:
    """Passagem: das contas criadas na janela, quantas chegaram a cada etapa."""
    row = database.fetch_one("""
        WITH coorte AS (SELECT id FROM users WHERE created_at > now() - make_interval(days => %s) AND role <> 'admin')
        SELECT
          (SELECT count(*) FROM coorte) AS signed_up,
          (SELECT count(DISTINCT d.user_id) FROM devices d JOIN coorte c ON c.id = d.user_id) AS activated_pc,
          (SELECT count(DISTINCT t.user_id) FROM trial_devices t JOIN coorte c ON c.id = t.user_id) AS used_trial,
          (SELECT count(DISTINCT p.user_id) FROM payments p JOIN coorte c ON c.id = p.user_id WHERE p.status = 'approved') AS paid,
          (SELECT count(*) FROM coorte c WHERE NOT EXISTS (SELECT 1 FROM devices d WHERE d.user_id = c.id)) AS never_activated
    """, (days,))
    stuck = database.fetch_all("""
        SELECT u.id, u.email, u.name, u.created_at
        FROM users u
        WHERE u.created_at > now() - make_interval(days => %s) AND u.role <> 'admin'
          AND NOT EXISTS (SELECT 1 FROM devices d WHERE d.user_id = u.id)
        ORDER BY u.created_at DESC LIMIT 50
    """, (days,))
    return {"days": days, **(row or {}), "never_activated_users": stuck}


def engagement() -> dict:
    row = database.fetch_one("""
        WITH ultimo AS (SELECT user_id, max(last_seen_at) AS visto FROM devices GROUP BY user_id)
        SELECT count(*) FILTER (WHERE visto > now() - interval '1 day') AS active_1d,
               count(*) FILTER (WHERE visto > now() - interval '7 days') AS active_7d,
               count(*) FILTER (WHERE visto > now() - interval '30 days') AS active_30d,
               count(*) FILTER (WHERE visto <= now() - interval '30 days') AS gone_30d
        FROM ultimo
    """)
    expiring = database.fetch_all(_base_users() + """
        SELECT id, email, name, tier, eff_status, expires_at, last_seen_at FROM base
        WHERE segment IN ('subscriber', 'trial') AND expires_at <= now() + interval '7 days'
        ORDER BY expires_at LIMIT 100
    """)
    lapsed = database.fetch_all(_base_users() + """
        SELECT id, email, name, tier, expires_at, last_seen_at FROM base
        WHERE segment = 'expired' AND expires_at > now() - interval '30 days'
        ORDER BY expires_at DESC LIMIT 100
    """)
    return {**(row or {}), "expiring": expiring, "lapsed_30d": lapsed}


def usage() -> dict:
    daily = database.fetch_all("""
        SELECT to_char(date_trunc('day', created_at), 'YYYY-MM-DD') AS day,
               count(*) FILTER (WHERE event = 'scan_completed') AS scans,
               count(*) FILTER (WHERE event IN ('optimization_applied', 'fix_applied')) AS applied,
               count(*) FILTER (WHERE event = 'optimization_failed') AS failed,
               count(*) FILTER (WHERE event = 'rollback') AS rollbacks
        FROM telemetry_events WHERE created_at > now() - interval '30 days'
        GROUP BY 1 ORDER BY 1
    """)
    optimizations = database.fetch_all("""
        SELECT optimization_id,
               count(*) FILTER (WHERE event IN ('optimization_applied', 'fix_applied') AND success) AS ok,
               count(*) FILTER (WHERE event = 'optimization_failed') AS failed,
               count(*) FILTER (WHERE event = 'rollback') AS rolled_back
        FROM telemetry_events
        WHERE optimization_id IS NOT NULL AND created_at > now() - interval '30 days'
        GROUP BY optimization_id ORDER BY ok DESC LIMIT 40
    """)
    versions = database.fetch_all("""
        SELECT agent_version, count(*) AS n FROM devices WHERE deactivated_at IS NULL GROUP BY agent_version ORDER BY n DESC
    """)
    builds = database.fetch_all("""
        SELECT windows_build, count(*) AS n FROM devices WHERE deactivated_at IS NULL GROUP BY windows_build ORDER BY n DESC LIMIT 15
    """)
    bench = database.fetch_one("""
        SELECT count(*) AS runs, count(DISTINCT device_id) AS devices FROM benchmark_results
        WHERE created_at > now() - interval '30 days'
    """)
    return {"daily": daily, "optimizations": optimizations, "agent_versions": versions,
            "windows_builds": builds, "benchmarks_30d": bench}


def set_role(user_id: int, role: str) -> int:
    return database.execute("UPDATE users SET role = %s WHERE id = %s", (role, user_id))
