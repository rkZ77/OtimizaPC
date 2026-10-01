-- Cache das respostas da IA (01/10/2026).
--
-- A chave e' o SHA-256 do prompt exato mandado a OpenAI (modelo, instrucoes,
-- mensagens e versao do cache). Resposta reaproveitada sempre foi gerada para
-- aquele mesmo contexto: nunca para um PC "parecido". Mudar o prompt ou o
-- modelo muda a chave, e o que estava guardado deixa de ser lido sozinho.
--
-- Nada de pessoal entra aqui: o prompt ja' sai do app sem nome de programa,
-- arquivo, pasta ou conta, e a tabela guarda so' o hash dele e a resposta.

CREATE TABLE ai_cache (
    kind        TEXT NOT NULL,
    key         TEXT NOT NULL,
    response    TEXT NOT NULL,
    hits        INTEGER NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_hit_at TIMESTAMPTZ,
    PRIMARY KEY (kind, key)
);

CREATE INDEX ai_cache_created_at ON ai_cache (created_at);

-- Mesma trava das outras tabelas (0003): fechada para a API REST do Supabase.
ALTER TABLE ai_cache ENABLE ROW LEVEL SECURITY;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE ALL ON ai_cache FROM anon';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        EXECUTE 'REVOKE ALL ON ai_cache FROM authenticated';
    END IF;
END $$;
