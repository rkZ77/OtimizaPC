-- Publica a versão 0.7.0 do app, a pedido do dono (02/10/2026).
-- Mesmo efeito do "Publicar versão" do admin; o app confere o SHA-256 antes de instalar.
-- Arquivo já conferido no GitHub Releases antes desta migration (hash bate).

INSERT INTO releases (component, version, url, sha256, notes)
VALUES (
    'agent',
    '0.7.0',
    'https://github.com/rkZ77/OtimizaPC/releases/download/v0.7.0/RKZFPS-Setup-0.7.0.exe',
    '5a508f58c3a23bdb3a37e5e8b563999bdd50afd41a76eda9a3fde1c82ef0bbe3',
    'Perfis novos (Automático, Desempenho, Equilibrado, Qualidade e Personalizado) que juntam a sua escolha com o hardware. Telas mais limpas, com a explicação no ícone de informação. Dicas da IA por jogo levam em conta o seu perfil.'
)
ON CONFLICT (component, version) DO UPDATE SET
    url = EXCLUDED.url, sha256 = EXCLUDED.sha256, notes = EXCLUDED.notes, active = TRUE;
