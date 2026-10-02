-- Publica a versão 0.8.0 do app, a pedido do dono (02/10/2026).
-- Mesmo efeito do "Publicar versão" do admin; o app confere o SHA-256 antes de instalar.
-- Arquivo já conferido no GitHub Releases antes desta migration (hash bate).

INSERT INTO releases (component, version, url, sha256, notes)
VALUES (
    'agent',
    '0.8.0',
    'https://github.com/rkZ77/OtimizaPC/releases/download/v0.8.0/RKZFPS-Setup-0.8.0.exe',
    '856d6e2d47cd0af3f2b0bfc02a205e6504b693ff23a2c6b2b54cb7273f99ebc6',
    'Dicas por jogo na hora, sem esperar a IA, para CS2, Valorant, LoL, Fortnite, Overwatch 2 e Free Fire no emulador. Placa de vídeo reconhecida pelo modelo (Reflex, DLSS e nível do PC mais certeiros). Overwatch 2 e Free Fire pelo emulador com FPS medido.'
)
ON CONFLICT (component, version) DO UPDATE SET
    url = EXCLUDED.url, sha256 = EXCLUDED.sha256, notes = EXCLUDED.notes, active = TRUE;
