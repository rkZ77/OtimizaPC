-- Texto do plano Pro com os jogos novos (Fortnite, Minecraft) e a
-- configuracao leve para PC fraco. So' troca o item se ele ainda estiver
-- exatamente como o seed deixou: texto que o admin ja' editou pelo painel
-- fica como ele escreveu.
UPDATE plans
SET features = (
    SELECT jsonb_agg(
        CASE WHEN f = 'Perfis de jogo (CS2)'
             THEN to_jsonb('Perfis de jogo (CS2, Fortnite, Minecraft) e configuração leve para PC fraco'::text)
             ELSE to_jsonb(f) END
        ORDER BY ord)
    FROM jsonb_array_elements_text(features) WITH ORDINALITY AS t(f, ord)
)
WHERE key = 'pro' AND features ? 'Perfis de jogo (CS2)';
