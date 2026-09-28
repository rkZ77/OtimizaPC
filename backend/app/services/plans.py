"""Planos: fonte unica de preco.

Licao do Pickia: o preco escrito a mao no front divergiu do cobrado sem
ninguem perceber. Aqui o site e o checkout leem SEMPRE desta tabela.
"""
import json
import time

from app import database

#: Mesma ordem do Agent (Fpsx.Core.Engine.Plans.Order).
TIER_ORDER = ["free", "starter", "pro", "ultimate"]


def rank(tier: str) -> int:
    # custom fica no topo: e' oferta negociada, com recursos definidos caso a caso.
    if tier == "custom":
        return len(TIER_ORDER)
    return TIER_ORDER.index(tier) if tier in TIER_ORDER else 0


def agent_tier(tier: str) -> str:
    """O Agent so' conhece os quatro tiers; custom libera tudo do ultimate."""
    return "ultimate" if tier == "custom" else tier


#: Meses cobrados em cada periodo, para a economia sair do servidor.
PERIOD_MONTHS = {"monthly": 1, "quarterly": 3, "annual": 12}


def public_payload(row: dict) -> dict:
    features = row["features"]
    if isinstance(features, str):
        features = json.loads(features)
    return {
        "key": row["key"],
        "name": row["name"],
        "tier": row["tier"],
        "period": row.get("period", "monthly"),
        "description": row["description"],
        "price_cents": row["price_cents"],
        "days": row["days"],
        "max_devices": row["max_devices"],
        "features": features,
    }


def with_savings(payloads: list[dict]) -> list[dict]:
    """Preco por mes e economia contra o mensal do mesmo tier.

    Calculado aqui, e nao no site: a regra do projeto e' que o front nao
    calcula preco. Economia arredondada para baixo, para nunca anunciar
    desconto maior que o real.
    """
    monthly = {p["tier"]: p["price_cents"] for p in payloads if p["period"] == "monthly"}
    out = []
    for p in payloads:
        months = PERIOD_MONTHS.get(p["period"])
        per_month = p["price_cents"] // months if months else p["price_cents"]
        base = monthly.get(p["tier"])
        savings = 0
        if months and months > 1 and base:
            savings = max(0, (base * months - p["price_cents"]) * 100 // (base * months))
        out.append({**p, "per_month_cents": per_month, "savings_percent": savings})
    return out


# A home pede os planos em toda visita. Cache curto poupa o banco no pico;
# o checkout le o plano direto (get), entao o valor cobrado nunca vem daqui.
_CACHE_SECONDS = 30
_cache: tuple[float, list[dict]] | None = None


def list_active() -> list[dict]:
    global _cache
    now = time.monotonic()
    if _cache is not None and now - _cache[0] < _CACHE_SECONDS:
        return _cache[1]
    rows = database.fetch_all("SELECT * FROM plans WHERE active ORDER BY sort, price_cents")
    result = with_savings([public_payload(r) for r in rows])
    _cache = (now, result)
    return result


def clear_cache() -> None:
    global _cache
    _cache = None


def list_all() -> list[dict]:
    return database.fetch_all("SELECT * FROM plans ORDER BY sort, price_cents")


def get(key: str) -> dict | None:
    return database.fetch_one("SELECT * FROM plans WHERE key = %s", (key,))


def upsert(plan: dict) -> dict:
    row = database.fetch_one(
        """INSERT INTO plans (key, name, tier, description, price_cents, days, max_devices, features, active, sort, period)
           VALUES (%(key)s, %(name)s, %(tier)s, %(description)s, %(price_cents)s, %(days)s, %(max_devices)s,
                   %(features)s, %(active)s, %(sort)s, %(period)s)
           ON CONFLICT (key) DO UPDATE SET
             name = EXCLUDED.name, tier = EXCLUDED.tier, description = EXCLUDED.description,
             price_cents = EXCLUDED.price_cents, days = EXCLUDED.days, max_devices = EXCLUDED.max_devices,
             features = EXCLUDED.features, active = EXCLUDED.active, sort = EXCLUDED.sort, period = EXCLUDED.period
           RETURNING *""",
        {"period": "monthly", **plan, "features": json.dumps(plan.get("features", []))},
    )
    # Depois do commit: preco alterado no painel aparece no site na hora.
    clear_cache()
    return row
