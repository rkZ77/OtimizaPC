"""Planos: fonte unica de preco.

Licao do Pickia: o preco escrito a mao no front divergiu do cobrado sem
ninguem perceber. Aqui o site e o checkout leem SEMPRE desta tabela.
"""
import json

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


def public_payload(row: dict) -> dict:
    features = row["features"]
    if isinstance(features, str):
        features = json.loads(features)
    return {
        "key": row["key"],
        "name": row["name"],
        "tier": row["tier"],
        "description": row["description"],
        "price_cents": row["price_cents"],
        "days": row["days"],
        "max_devices": row["max_devices"],
        "features": features,
    }


def list_active() -> list[dict]:
    rows = database.fetch_all("SELECT * FROM plans WHERE active ORDER BY sort, price_cents")
    return [public_payload(r) for r in rows]


def list_all() -> list[dict]:
    return database.fetch_all("SELECT * FROM plans ORDER BY sort, price_cents")


def get(key: str) -> dict | None:
    return database.fetch_one("SELECT * FROM plans WHERE key = %s", (key,))


def upsert(plan: dict) -> dict:
    return database.fetch_one(
        """INSERT INTO plans (key, name, tier, description, price_cents, days, max_devices, features, active, sort)
           VALUES (%(key)s, %(name)s, %(tier)s, %(description)s, %(price_cents)s, %(days)s, %(max_devices)s,
                   %(features)s, %(active)s, %(sort)s)
           ON CONFLICT (key) DO UPDATE SET
             name = EXCLUDED.name, tier = EXCLUDED.tier, description = EXCLUDED.description,
             price_cents = EXCLUDED.price_cents, days = EXCLUDED.days, max_devices = EXCLUDED.max_devices,
             features = EXCLUDED.features, active = EXCLUDED.active, sort = EXCLUDED.sort
           RETURNING *""",
        {**plan, "features": json.dumps(plan.get("features", []))},
    )
