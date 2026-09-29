-- Alarme falso (28/09/2026): o "DiagnÃ³stico" era o terminal do Windows lendo
-- a resposta da API como cp1252; o banco estava certo e esta migration nao
-- mudou nenhuma linha. Fica por ser inofensiva e ja' constar como aplicada.
-- Conserta texto por texto (nome, descricao e cada item da lista de
-- recursos): so' mexe no que tem o padrao "Ã" e, se um texto nao converte
-- limpo, devolve o original. Uma lista com item certo e item quebrado
-- misturados nao pode impedir o conserto dos quebrados.
CREATE OR REPLACE FUNCTION pg_temp.conserta_acento(t text) RETURNS text AS $$
BEGIN
    IF t LIKE '%Ã%' THEN
        RETURN convert_from(convert_to(t, 'LATIN1'), 'UTF8');
    END IF;
    RETURN t;
EXCEPTION WHEN OTHERS THEN
    RETURN t;
END $$ LANGUAGE plpgsql;

UPDATE plans SET
    name = pg_temp.conserta_acento(name),
    description = pg_temp.conserta_acento(description),
    features = (
        SELECT coalesce(jsonb_agg(to_jsonb(pg_temp.conserta_acento(item)) ORDER BY pos), '[]'::jsonb)
        FROM jsonb_array_elements_text(features) WITH ORDINALITY AS lista(item, pos)
    )
WHERE name LIKE '%Ã%' OR description LIKE '%Ã%' OR features::text LIKE '%Ã%';
