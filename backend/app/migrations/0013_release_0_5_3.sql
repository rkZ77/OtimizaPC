-- Publica a versão 0.5.3 do app, a pedido do dono (29/09/2026).
-- Mesmo efeito do "Publicar versão" do admin; o app confere o SHA-256 antes de instalar.

INSERT INTO releases (component, version, url, sha256, notes)
VALUES (
    'agent',
    '0.5.3',
    'https://github.com/rkZ77/OtimizaPC/releases/download/v0.5.3/RKZFPS-Setup-0.5.3.exe',
    '228691d2755bea4912f8b79f413c6bb990e6a64a88a5cd6b92f5378dc2b3a2cb',
    'Troca de peça e formatação: o app nota peça nova e diz se precisa formatar. Salve os drivers num pendrive antes de formatar e reinstale todos depois, inclusive o de rede, sem internet. Drivers e reparo: instala driver do Windows Update e repara arquivos do Windows.'
)
ON CONFLICT (component, version) DO UPDATE SET
    url = EXCLUDED.url, sha256 = EXCLUDED.sha256, notes = EXCLUDED.notes, active = TRUE;
