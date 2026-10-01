"""Cache das respostas da IA, no Postgres.

Por que no banco e nao em memoria (como plans._cache): memoria zera a cada
deploy e cada worker teria a sua, e o que interessa aqui e' justamente que a
mesma pergunta de mil PCs iguais custe uma chamada so'.

A chave e' o hash do prompt EXATO (modelo, instrucoes, mensagens, teto de
resposta e CACHE_VERSION). A canonicalizacao (so' campos relevantes, FPS
arredondado) acontece antes, no texto que vai para a IA, em assistant.py:
assim a resposta guardada sempre foi gerada para aquele mesmo texto.

Banco fora do ar nunca derruba a IA: falha de cache vira "nao achou" e a
pergunta segue para a OpenAI como antes.
"""
import hashlib
import json
import logging
import threading

from app import database

logger = logging.getLogger(__name__)

# Sobe para descartar tudo de uma vez (ex.: regra nova que o prompt nao reflete).
CACHE_VERSION = "1"

# Dicas de jogo e diagnostico mudam pouco; o chat do site leva preco no
# prompt (que ja' muda a chave), mas o texto do produto muda com release.
TTL_DAYS = {"explain": 30, "upgrade": 30, "game_tips": 30, "site_chat": 1}
_MAX_TTL_DAYS = max(TTL_DAYS.values())

# Limpeza das linhas vencidas a cada tantos registros novos: sem job agendado
# e sem pesar em toda gravacao.
_PURGE_EVERY = 200
_puts = 0
_puts_lock = threading.Lock()


def key(kind: str, model: str, max_tokens: int, messages: list[dict]) -> str:
    raw = json.dumps({"v": CACHE_VERSION, "kind": kind, "model": model, "max_tokens": max_tokens, "messages": messages},
                     ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def get(kind: str, cache_key: str) -> str | None:
    try:
        row = database.fetch_one(
            """UPDATE ai_cache SET hits = hits + 1, last_hit_at = now()
               WHERE kind = %s AND key = %s AND created_at > now() - make_interval(days => %s)
               RETURNING response""",
            (kind, cache_key, TTL_DAYS[kind]))
    except Exception:  # noqa: BLE001 - cache nunca derruba a IA
        logger.warning("[AI_CACHE] leitura falhou, seguindo sem cache", exc_info=True)
        return None
    return row["response"] if row else None


def put(kind: str, cache_key: str, response: str) -> None:
    global _puts
    if not response.strip():
        return
    try:
        database.execute(
            """INSERT INTO ai_cache (kind, key, response) VALUES (%s, %s, %s)
               ON CONFLICT (kind, key) DO UPDATE SET response = EXCLUDED.response, created_at = now(), hits = 0""",
            (kind, cache_key, response))
        with _puts_lock:
            _puts += 1
            purge = _puts % _PURGE_EVERY == 0
        if purge:
            database.execute("DELETE FROM ai_cache WHERE created_at < now() - make_interval(days => %s)", (_MAX_TTL_DAYS,))
    except Exception:  # noqa: BLE001
        logger.warning("[AI_CACHE] gravacao falhou", exc_info=True)
