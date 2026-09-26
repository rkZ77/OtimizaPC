-- Planos novos, a pedido do dono (26/09/2026):
--   * todo plano vale para 1 PC: assinatura nao e' dividida entre amigos;
--   * Free so' enxerga (diagnostico, o que cada otimizacao resolveria, FPS
--     das partidas); aplicar comeca no Starter;
--   * mensal R$ 19,90 / 29,90 / 39,90, e trimestral (10% off) e anual
--     (25% off) como linhas proprias da tabela, cada uma com seus dias.
--
-- Periodo como linha propria (e nao coluna de desconto) mantem o resto do
-- sistema igual: checkout, webhook e licenca continuam falando de UM
-- plan_key com preco e dias. O preco segue so' no banco, editavel no admin.

ALTER TABLE plans ADD COLUMN period TEXT NOT NULL DEFAULT 'monthly'
    CHECK (period IN ('none', 'monthly', 'quarterly', 'annual'));

UPDATE plans SET period = 'none', max_devices = 1,
    description = 'Veja tudo o que está pesando no seu PC, sem pagar nada.',
    features = '["Diagnóstico completo do PC", "O que cada otimização resolveria", "FPS medido nas suas partidas", "Desfazer qualquer alteração"]'
WHERE key = 'free';

UPDATE plans SET price_cents = 1990, max_devices = 1,
    description = 'Corrige o Windows e os problemas que o diagnóstico encontrar.',
    features = '["Tudo do Free", "Aplica as correções comprovadas do Windows", "Resolve problemas: processos, memória, disco e rede", "Leva ao driver certo e ao backup quando precisa", "Programas de inicialização", "Histórico de tudo que foi feito"]'
WHERE key = 'starter';

UPDATE plans SET price_cents = 2990, max_devices = 1,
    description = 'Para quem joga: ajuste dos jogos e prova de resultado.',
    features = '["Tudo do Starter", "Perfis de jogo: CS2, Fortnite e Minecraft", "Configuração leve para PC fraco", "Antes e depois do FPS nas suas partidas", "FPSX Benchmark e relatórios"]'
WHERE key = 'pro';

UPDATE plans SET price_cents = 3990, max_devices = 1,
    description = 'Tudo, antes de todo mundo, com suporte prioritário.',
    features = '["Tudo do Pro", "Otimizações experimentais com medição", "Novos jogos e otimizações em acesso antecipado", "Suporte prioritário"]'
WHERE key = 'ultimate';

-- Trimestral e anual: mesmo tier, mesmos recursos, mais dias por menos.
INSERT INTO plans (key, name, tier, description, price_cents, days, max_devices, features, active, sort, period)
SELECT m.key || v.suffix, m.name, m.tier, m.description, v.price, v.days, 1, m.features, TRUE, m.sort, v.period
FROM plans m
JOIN (VALUES
    ('starter',  '-trimestral', 5370,  90,  'quarterly'),
    ('starter',  '-anual',      17910, 365, 'annual'),
    ('pro',      '-trimestral', 8070,  90,  'quarterly'),
    ('pro',      '-anual',      26910, 365, 'annual'),
    ('ultimate', '-trimestral', 10770, 90,  'quarterly'),
    ('ultimate', '-anual',      35910, 365, 'annual')
) AS v(base, suffix, price, days, period) ON v.base = m.key
ON CONFLICT (key) DO NOTHING;

-- Licencas ativas passam a valer para 1 PC. PC a mais ja' ativado continua
-- ativo ate' a pessoa trocar; a regra vale para a proxima ativacao.
UPDATE licenses SET max_devices = 1 WHERE status IN ('active', 'trial') AND max_devices > 1;
