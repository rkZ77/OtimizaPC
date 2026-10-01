-- Publica a versão 0.6.1 do app, a pedido do dono (01/10/2026).
-- Mesmo efeito do "Publicar versão" do admin; o app confere o SHA-256 antes de instalar.

INSERT INTO releases (component, version, url, sha256, notes)
VALUES (
    'agent',
    '0.6.1',
    'https://github.com/rkZ77/OtimizaPC/releases/download/v0.6.1/RKZFPS-Setup-0.6.1.exe',
    'ed0a028f866de55935af88e6f77c9c94af5c5e9e71053f29a8a63615a519f43c',
    'O que limitou a partida, com os números que sustentam. Aviso de placa de vídeo errada. Modo Gaming Automático ou Manual. Meta de FPS por jogo, planilha das partidas, painel por cima do jogo (Ctrl+Shift+F) e tema claro.'
)
ON CONFLICT (component, version) DO UPDATE SET
    url = EXCLUDED.url, sha256 = EXCLUDED.sha256, notes = EXCLUDED.notes, active = TRUE;
