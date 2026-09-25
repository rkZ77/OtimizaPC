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
