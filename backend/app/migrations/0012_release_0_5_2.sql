-- Publica a versão 0.5.2 do app, a pedido do dono (29/09/2026).
-- Mesmo efeito do "Publicar versão" do admin; o app confere o SHA-256 antes de instalar.

INSERT INTO releases (component, version, url, sha256, notes)
VALUES (
    'agent',
    '0.5.2',
    'https://github.com/rkZ77/OtimizaPC/releases/download/v0.5.2/RKZFPS-Setup-0.5.2.exe',
    '12ae9ff04426b69cb78895d6e33eb2473739c811602972c3c0664092f03195c1',
    'Página Drivers e reparo: procura driver novo no Windows Update e instala pelo app, com ponto de restauração antes. Verifica e repara arquivos do Windows corrompidos com as ferramentas da Microsoft. Vigia do PC e resumo da semana.'
)
ON CONFLICT (component, version) DO UPDATE SET
    url = EXCLUDED.url, sha256 = EXCLUDED.sha256, notes = EXCLUDED.notes, active = TRUE;
