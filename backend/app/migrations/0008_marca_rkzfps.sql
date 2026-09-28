-- A marca virou RKZFPS (pedido do dono, 28/09/2026). Os textos dos planos
-- ficam no banco (editaveis no admin), entao a troca do nome visivel vem por
-- migracao. Identificadores (chaves de plano, nomes de tabela) nao mudam.
UPDATE plans SET
    features = replace(features::text, 'FPSX', 'RKZFPS')::jsonb,
    description = replace(description, 'FPSX', 'RKZFPS'),
    name = replace(name, 'FPSX', 'RKZFPS')
WHERE features::text LIKE '%FPSX%' OR description LIKE '%FPSX%' OR name LIKE '%FPSX%';
