-- E-mail transacional: codigo de redefinir senha e registro de envios.

-- Codigo de 6 digitos, guardado so' como hash. `attempts` limita o chute:
-- sem ele, 900 mil combinacoes cabem num ataque paciente.
CREATE TABLE password_resets (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    code_hash   TEXT NOT NULL,
    expires_at  TIMESTAMPTZ NOT NULL,
    attempts    INTEGER NOT NULL DEFAULT 0,
    used_at     TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX password_resets_user ON password_resets (user_id, created_at DESC);

-- Sessao emitida antes da troca de senha deixa de valer (celular ou PC
-- roubado que motivou a troca nao continua logado).
ALTER TABLE users ADD COLUMN password_changed_at TIMESTAMPTZ;

-- Todo e-mail que o FPSX tenta mandar. `dedupe_key` e' o que impede o aviso
-- de vencimento sair duas vezes quando ha' mais de um worker, ou quando o
-- staging (mesmo banco) tambem roda o agendador.
CREATE TABLE email_log (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER REFERENCES users(id) ON DELETE SET NULL,
    to_email    TEXT NOT NULL,
    kind        TEXT NOT NULL,
    subject     TEXT NOT NULL DEFAULT '',
    dedupe_key  TEXT UNIQUE,
    status      TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'sent', 'failed', 'skipped')),
    detail      TEXT NOT NULL DEFAULT '',
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    sent_at     TIMESTAMPTZ
);
CREATE INDEX email_log_created ON email_log (created_at DESC);

-- Mesma regra da 0003: tabela nova fechada para a API REST do Supabase.
ALTER TABLE password_resets ENABLE ROW LEVEL SECURITY;
ALTER TABLE email_log ENABLE ROW LEVEL SECURITY;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE ALL ON password_resets, email_log FROM anon';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        EXECUTE 'REVOKE ALL ON password_resets, email_log FROM authenticated';
    END IF;
END $$;
