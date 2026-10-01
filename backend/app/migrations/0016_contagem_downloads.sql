-- Quantas vezes o botao "Baixar para Windows" do site foi usado, por dia e
-- por versao (pedido do dono, 01/10/2026).
--
-- So' o numero: sem IP, sem conta, sem navegador. Por isso nao depende do
-- aviso de cookies e conta todo mundo, nao so' quem aceitou o Analytics.
-- A contagem do GitHub nao serve para isto: ela soma tambem os downloads
-- da atualizacao automatica do app, que baixa o mesmo instalador.

CREATE TABLE download_clicks (
    day     DATE NOT NULL,
    version TEXT NOT NULL,
    n       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (day, version)
);

-- Mesma trava das outras tabelas (0003): fechada para a API REST do Supabase.
ALTER TABLE download_clicks ENABLE ROW LEVEL SECURITY;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE ALL ON download_clicks FROM anon';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        EXECUTE 'REVOKE ALL ON download_clicks FROM authenticated';
    END IF;
END $$;
