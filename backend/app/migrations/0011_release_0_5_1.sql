-- Publica a versão 0.5.1 do app, a pedido do dono (29/09/2026).
--
-- Mesmo efeito do "Publicar versão" do admin (catalog.publish_release): o
-- instalador está na release v0.5.1 do GitHub, e o app confere o SHA-256
-- antes de instalar, então uma URL errada nunca vira instalação.
-- Sem linha no audit_log: não houve admin logado; o registro é este arquivo no git.

INSERT INTO releases (component, version, url, sha256, notes)
VALUES (
    'agent',
    '0.5.1',
    'https://github.com/rkZ77/OtimizaPC/releases/download/v0.5.1/RKZFPS-Setup-0.5.1.exe',
    '56866897ee7a02d3019c3af18c9a2fa7f5e74b9092a40b4b80856d7905c026cc',
    'Vigia do PC: avisa quando uma atualização do Windows, um driver novo ou um jogo desfaz uma correção. Resumo da semana com o FPS medido nas suas partidas. Visual mais limpo. Tela de novidades e Instagram @rkzfps.br nas Configurações.'
)
ON CONFLICT (component, version) DO UPDATE SET
    url = EXCLUDED.url, sha256 = EXCLUDED.sha256, notes = EXCLUDED.notes, active = TRUE;
