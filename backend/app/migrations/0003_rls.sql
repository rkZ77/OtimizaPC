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
