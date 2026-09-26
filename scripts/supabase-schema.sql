-- FPSX: schema completo para colar no SQL Editor do Supabase (uma vez so').
-- Gerado de backend/app/migrations. Registra cada migration em schema_migrations,
-- para a API nao reaplicar quando conectar. Seguro de rodar num banco vazio.

BEGIN;

CREATE TABLE IF NOT EXISTS schema_migrations (
    name TEXT PRIMARY KEY,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ===== 0001_schema.sql =====

-- Schema inicial do FPSX.
-- Nenhuma tabela guarda dado pessoal alem de e-mail e nome da conta: o PC e'
-- identificado por um hash (device_hash), nunca por serial, MAC ou usuario.

CREATE TABLE users (
    id            SERIAL PRIMARY KEY,
    email         TEXT NOT NULL UNIQUE,
    name          TEXT NOT NULL DEFAULT '',
    password_hash TEXT NOT NULL,
    role          TEXT NOT NULL DEFAULT 'user' CHECK (role IN ('user', 'admin')),
    active        BOOLEAN NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Preco em centavos: float para dinheiro ja' deu divergencia de centavo em
-- conciliacao de pagamento. A tela formata.
CREATE TABLE plans (
    key          TEXT PRIMARY KEY,
    name         TEXT NOT NULL,
    tier         TEXT NOT NULL CHECK (tier IN ('free', 'starter', 'pro', 'ultimate', 'custom')),
    description  TEXT NOT NULL DEFAULT '',
    price_cents  INTEGER NOT NULL DEFAULT 0 CHECK (price_cents >= 0),
    days         INTEGER NOT NULL DEFAULT 30 CHECK (days > 0),
    max_devices  INTEGER NOT NULL DEFAULT 1 CHECK (max_devices > 0),
    features     JSONB NOT NULL DEFAULT '[]'::jsonb,
    active       BOOLEAN NOT NULL DEFAULT TRUE,
    sort         INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE licenses (
    id             SERIAL PRIMARY KEY,
    user_id        INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    plan_key       TEXT NOT NULL REFERENCES plans(key),
    tier           TEXT NOT NULL,
    status         TEXT NOT NULL CHECK (status IN ('trial', 'active', 'expired', 'blocked')),
    max_devices    INTEGER NOT NULL,
    starts_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at     TIMESTAMPTZ NOT NULL,
    blocked_reason TEXT,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX licenses_user_idx ON licenses (user_id);

CREATE TABLE devices (
    id             SERIAL PRIMARY KEY,
    user_id        INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    license_id     INTEGER REFERENCES licenses(id) ON DELETE SET NULL,
    device_hash    TEXT NOT NULL,
    name           TEXT NOT NULL DEFAULT '',
    windows_build  TEXT NOT NULL DEFAULT '',
    agent_version  TEXT NOT NULL DEFAULT '',
    activated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_seen_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    deactivated_at TIMESTAMPTZ
);
-- Um PC ativo por conta: reativar o mesmo PC reaproveita a linha.
CREATE UNIQUE INDEX devices_active_uidx ON devices (user_id, device_hash) WHERE deactivated_at IS NULL;

-- Um trial por PC, independente da conta: sem isso, uma conta nova por semana
-- vira PRO de graca para sempre.
CREATE TABLE trial_devices (
    device_hash TEXT PRIMARY KEY,
    user_id     INTEGER REFERENCES users(id) ON DELETE SET NULL,
    used_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE coupons (
    code        TEXT PRIMARY KEY,
    percent_off INTEGER NOT NULL CHECK (percent_off BETWEEN 1 AND 100),
    max_uses    INTEGER,
    used_count  INTEGER NOT NULL DEFAULT 0,
    expires_at  TIMESTAMPTZ,
    active      BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE payments (
    id                  SERIAL PRIMARY KEY,
    user_id             INTEGER REFERENCES users(id) ON DELETE SET NULL,
    plan_key            TEXT NOT NULL,
    provider            TEXT NOT NULL,
    provider_payment_id TEXT NOT NULL,
    status              TEXT NOT NULL,
    amount_cents        INTEGER NOT NULL,
    coupon_code         TEXT,
    license_id          INTEGER REFERENCES licenses(id) ON DELETE SET NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    approved_at         TIMESTAMPTZ,
    UNIQUE (provider, provider_payment_id)
);

-- Trilha de toda notificacao de pagamento, aceita ou recusada. No Pickia a
-- falha real foi silenciosa ("o webhook chegou?"); aqui vira consulta.
CREATE TABLE payment_events (
    id                  BIGSERIAL PRIMARY KEY,
    source              TEXT NOT NULL,
    status              TEXT NOT NULL,
    provider_payment_id TEXT NOT NULL DEFAULT '',
    detail              TEXT NOT NULL DEFAULT '',
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE settings (
    key        TEXT PRIMARY KEY,
    value      JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- O que o admin muda no catalogo. So' campos de metadado: o Agent ignora
-- id que nao tem handler compilado, entao o banco nunca "cria" otimizacao.
CREATE TABLE catalog_overrides (
    kind        TEXT NOT NULL CHECK (kind IN ('optimization', 'game_profile')),
    id          TEXT NOT NULL,
    enabled     BOOLEAN,
    risk        TEXT CHECK (risk IN ('LOW', 'MEDIUM', 'HIGH')),
    description TEXT,
    min_plan    TEXT CHECK (min_plan IN ('free', 'starter', 'pro', 'ultimate')),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (kind, id)
);

CREATE TABLE releases (
    id           SERIAL PRIMARY KEY,
    component    TEXT NOT NULL CHECK (component IN ('agent', 'engine', 'catalog', 'game_profiles')),
    version      TEXT NOT NULL,
    url          TEXT NOT NULL DEFAULT '',
    sha256       TEXT NOT NULL DEFAULT '',
    notes        TEXT NOT NULL DEFAULT '',
    active       BOOLEAN NOT NULL DEFAULT TRUE,
    published_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (component, version)
);

-- Telemetria minima e so' com consentimento no app (secao 40 do spec).
CREATE TABLE telemetry_events (
    id              BIGSERIAL PRIMARY KEY,
    device_id       INTEGER REFERENCES devices(id) ON DELETE CASCADE,
    event           TEXT NOT NULL,
    optimization_id TEXT,
    success         BOOLEAN,
    agent_version   TEXT NOT NULL DEFAULT '',
    windows_build   TEXT NOT NULL DEFAULT '',
    detail          JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX telemetry_event_idx ON telemetry_events (event, created_at DESC);

CREATE TABLE benchmark_results (
    id           BIGSERIAL PRIMARY KEY,
    device_id    INTEGER REFERENCES devices(id) ON DELETE CASCADE,
    game_id      TEXT NOT NULL,
    label        TEXT NOT NULL DEFAULT '',
    session_id   TEXT,
    avg_fps      DOUBLE PRECISION NOT NULL,
    low1_fps     DOUBLE PRECISION NOT NULL,
    low01_fps    DOUBLE PRECISION NOT NULL,
    frametime_ms DOUBLE PRECISION NOT NULL,
    frames       INTEGER NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE audit_log (
    id         BIGSERIAL PRIMARY KEY,
    admin_id   INTEGER REFERENCES users(id) ON DELETE SET NULL,
    action     TEXT NOT NULL,
    target     TEXT NOT NULL DEFAULT '',
    detail     JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO schema_migrations (name) VALUES ('0001_schema.sql') ON CONFLICT DO NOTHING;

-- ===== 0002_seed.sql =====

-- Planos e configuracoes iniciais. Precos sao PONTO DE PARTIDA: o admin
-- altera pelo painel (secao 35 do spec) e o site le sempre da API.

INSERT INTO plans (key, name, tier, description, price_cents, days, max_devices, features, sort) VALUES
  ('free',     'Free',     'free',     'Diagnóstico completo e correções básicas.', 0,    3650, 1,
     '["Scan completo do PC", "Relatório de diagnóstico", "Correções básicas comprovadas"]', 0),
  ('starter',  'Starter',  'starter',  'Otimizações básicas e histórico.',          1490, 30,   1,
     '["Tudo do Free", "Programas de inicialização", "Plano de energia", "Ferramentas de troubleshooting", "Histórico"]', 1),
  ('pro',      'Pro',      'pro',      'Todas as otimizações, benchmark e rollback.', 2490, 30, 2,
     '["Tudo do Starter", "Perfis de jogo (CS2)", "FPSX Benchmark", "Rollback completo", "Relatórios", "2 PCs"]', 2),
  ('ultimate', 'Ultimate', 'ultimate', 'Recursos experimentais e suporte premium.',  3990, 30,   5,
     '["Tudo do Pro", "Otimizações experimentais", "Suporte premium", "5 PCs"]', 3)
ON CONFLICT (key) DO NOTHING;

INSERT INTO settings (key, value) VALUES
  ('trial_days', '7'),
  ('trial_plan', '"pro"'),
  -- Quantos dias o app funciona offline com a ultima licenca assinada.
  ('offline_grace_days', '7'),
  ('telemetry_enabled', 'true')
ON CONFLICT (key) DO NOTHING;

INSERT INTO schema_migrations (name) VALUES ('0002_seed.sql') ON CONFLICT DO NOTHING;

-- ===== 0003_rls.sql =====

-- Fecha as tabelas para a API REST publica do Supabase (PostgREST).
--
-- No Supabase, toda tabela do schema public fica exposta em
-- https://<projeto>.supabase.co/rest/v1/ para quem tiver a chave publishable,
-- que e' publica por definicao. Sem RLS, qualquer um leria users (e-mail e
-- hash de senha), licenses e payments pela internet.
--
-- RLS ligado e NENHUMA policy = ninguem acessa pela API REST. O backend do
-- FPSX conecta como dono das tabelas (usuario postgres), que nao e' afetado
-- por RLS sem FORCE, entao nada muda para a API do FPSX.
--
-- Em Postgres comum (dev local, CI) os papeis anon/authenticated nao existem:
-- o bloco so' revoga quando eles existem, e o ENABLE RLS e' inofensivo.

DO $$
DECLARE
    t text;
BEGIN
    FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t);
    END LOOP;

    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA public FROM anon';
        EXECUTE 'REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM anon';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM anon';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA public FROM authenticated';
        EXECUTE 'REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM authenticated';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM authenticated';
    END IF;
END $$;

INSERT INTO schema_migrations (name) VALUES ('0003_rls.sql') ON CONFLICT DO NOTHING;

COMMIT;
