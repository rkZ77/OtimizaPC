"""Overrides do catalogo (admin) e releases (secao 41).

O que sai daqui para o Agent vai ASSINADO. O Agent aplica so' os campos
permitidos (enabled, risk, description, min_plan) e so' em ids que ele ja
conhece: o banco nao consegue criar otimizacao nova nem trocar o que ela faz.
"""
import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path

from app import database, signing

RISKS = {"LOW", "MEDIUM", "HIGH"}
MIN_PLANS = {"free", "starter", "pro", "ultimate"}


def validate_override(o: dict) -> dict:
    if o.get("kind") not in ("optimization", "game_profile"):
        raise ValueError("kind precisa ser optimization ou game_profile.")
    if not o.get("id") or len(o["id"]) > 80:
        raise ValueError("id inválido.")
    if o.get("risk") is not None and o["risk"] not in RISKS:
        raise ValueError("risk precisa ser LOW, MEDIUM ou HIGH.")
    if o.get("min_plan") is not None and o["min_plan"] not in MIN_PLANS:
        raise ValueError("min_plan inválido.")
    if o.get("description") is not None and len(o["description"]) > 500:
        raise ValueError("description passa de 500 caracteres.")
    return o


def _catalog_file() -> Path:
    # No Docker o catalogo e' copiado para /app/catalog; no repo, fica em
    # optimization-engine/catalog. O mesmo arquivo que o Agent embute.
    env = os.getenv("CATALOG_FILE")
    if env:
        return Path(env)
    return Path(__file__).resolve().parents[3] / "optimization-engine" / "catalog" / "optimizations.json"


def definitions() -> list[dict]:
    """Otimizacoes do catalogo publicado, para o admin escolher o que ajustar."""
    path = _catalog_file()
    if not path.exists():
        return []
    # O arquivo aceita comentarios de linha (// ...), como o Agent le.
    text = "\n".join(line for line in path.read_text(encoding="utf-8").splitlines() if not line.lstrip().startswith("//"))
    data = json.loads(re.sub(r",(\s*[}\]])", r"\1", text))
    return [
        {k: o.get(k) for k in ("id", "name", "category", "classification", "risk", "min_plan", "description")}
        | {"enabled": o.get("enabled", True), "min_plan": o.get("min_plan", "free")}
        for o in data.get("optimizations", [])
    ]


def list_overrides() -> list[dict]:
    return database.fetch_all("SELECT kind, id, enabled, risk, description, min_plan, updated_at FROM catalog_overrides ORDER BY kind, id")


def upsert_override(o: dict) -> dict:
    validate_override(o)
    return database.fetch_one(
        """INSERT INTO catalog_overrides (kind, id, enabled, risk, description, min_plan, updated_at)
           VALUES (%(kind)s, %(id)s, %(enabled)s, %(risk)s, %(description)s, %(min_plan)s, now())
           ON CONFLICT (kind, id) DO UPDATE SET enabled = EXCLUDED.enabled, risk = EXCLUDED.risk,
             description = EXCLUDED.description, min_plan = EXCLUDED.min_plan, updated_at = now()
           RETURNING kind, id, enabled, risk, description, min_plan, updated_at""",
        {"enabled": None, "risk": None, "description": None, "min_plan": None, **o})


def delete_override(kind: str, oid: str) -> int:
    return database.execute("DELETE FROM catalog_overrides WHERE kind = %s AND id = %s", (kind, oid))


def signed_overrides() -> str:
    items = [
        {k: v for k, v in o.items() if k != "updated_at" and v is not None}
        for o in list_overrides()
    ]
    return signing.sign({"v": 1, "issued_at": datetime.now(timezone.utc).isoformat(), "overrides": items})


def latest_releases() -> list[dict]:
    return database.fetch_all(
        """SELECT DISTINCT ON (component) component, version, url, sha256, notes, published_at
           FROM releases WHERE active ORDER BY component, published_at DESC""")


def changelog(limit: int = 10) -> list[dict]:
    """Versoes do app, da mais nova para a mais antiga, para o site mostrar
    que o produto e' atualizado. So' o que o admin publicou: versao, data e notas."""
    return database.fetch_all(
        """SELECT version, notes, published_at FROM releases
           WHERE active AND component = 'agent' ORDER BY published_at DESC LIMIT %s""", (limit,))


def publish_release(r: dict) -> dict:
    if r.get("component") not in ("agent", "engine", "catalog", "game_profiles"):
        raise ValueError("component inválido.")
    if not r.get("version"):
        raise ValueError("version é obrigatória.")
    if r.get("url") and not r["url"].startswith("https://"):
        # Download so' por HTTPS, e o app confere o sha256 antes de instalar.
        raise ValueError("url precisa ser https.")
    return database.fetch_one(
        """INSERT INTO releases (component, version, url, sha256, notes) VALUES (%(component)s, %(version)s, %(url)s, %(sha256)s, %(notes)s)
           ON CONFLICT (component, version) DO UPDATE SET url = EXCLUDED.url, sha256 = EXCLUDED.sha256, notes = EXCLUDED.notes, active = TRUE
           RETURNING *""",
        {"url": "", "sha256": "", "notes": "", **r})


def audit(admin_id: int, action: str, target: str, detail: dict | None = None) -> None:
    import json

    database.execute("INSERT INTO audit_log (admin_id, action, target, detail) VALUES (%s, %s, %s, %s)",
                     (admin_id, action, target, json.dumps(detail or {}, default=str)))
