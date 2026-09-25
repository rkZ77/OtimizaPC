"""Configuracoes editaveis pelo admin (trial_days, carencia offline etc.)."""
import json

from app import database

#: Chaves aceitas e o tipo de cada uma. O painel nao consegue gravar chave
#: desconhecida nem tipo errado: um trial_days "sete" derrubaria o cadastro.
SCHEMA: dict[str, type] = {
    "trial_days": int,
    "trial_plan": str,
    "offline_grace_days": int,
    "telemetry_enabled": bool,
}

DEFAULTS = {"trial_days": 7, "trial_plan": "pro", "offline_grace_days": 7, "telemetry_enabled": True}


def get(key: str):
    row = database.fetch_one("SELECT value FROM settings WHERE key = %s", (key,))
    return row["value"] if row else DEFAULTS.get(key)


def get_all() -> dict:
    values = dict(DEFAULTS)
    for row in database.fetch_all("SELECT key, value FROM settings"):
        if row["key"] in SCHEMA:
            values[row["key"]] = row["value"]
    return values


def validate(key: str, value):
    expected = SCHEMA.get(key)
    if expected is None:
        raise ValueError(f"Configuração desconhecida: {key}")
    # bool e' subclasse de int em Python: sem esta checagem, True passaria como trial_days.
    if expected is int and (isinstance(value, bool) or not isinstance(value, int)):
        raise ValueError(f"{key} precisa ser um número inteiro.")
    if not isinstance(value, expected):
        raise ValueError(f"{key} precisa ser do tipo {expected.__name__}.")
    if key in ("trial_days", "offline_grace_days") and not 0 <= value <= 90:
        raise ValueError(f"{key} precisa estar entre 0 e 90.")
    if key == "trial_plan" and value not in ("starter", "pro", "ultimate"):
        raise ValueError("trial_plan precisa ser starter, pro ou ultimate.")
    return value


def set_value(key: str, value) -> None:
    validate(key, value)
    database.execute(
        """INSERT INTO settings (key, value, updated_at) VALUES (%s, %s, now())
           ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now()""",
        (key, json.dumps(value)),
    )
