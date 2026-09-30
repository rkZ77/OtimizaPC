-- Publica a versão 0.6.0 do app, a pedido do dono (30/09/2026).
-- Mesmo efeito do "Publicar versão" do admin; o app confere o SHA-256 antes de instalar.

INSERT INTO releases (component, version, url, sha256, notes)
VALUES (
    'agent',
    '0.6.0',
    'https://github.com/rkZ77/OtimizaPC/releases/download/v0.6.0/RKZFPS-Setup-0.6.0.exe',
    '14aed1e1800446c0b31802800ef51888098d164fadca983533ebbb6f22141c3c',
    'O que limitou a partida: o app lê uso por núcleo, clocks, temperatura e memória durante o jogo e diz o que segurou o FPS, com os números que sustentam. Avisa quando o jogo rodou na placa de vídeo errada. Modo Gaming Automático ou Manual. Histórico por jogo.'
)
ON CONFLICT (component, version) DO UPDATE SET
    url = EXCLUDED.url, sha256 = EXCLUDED.sha256, notes = EXCLUDED.notes, active = TRUE;
