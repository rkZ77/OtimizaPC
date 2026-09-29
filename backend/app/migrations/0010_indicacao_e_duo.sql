-- Indicacao e plano para 2 PCs, a pedido do dono (29/09/2026).
--
-- Indicacao: cada conta ganha um codigo. Quem entra pelo link fica marcado
-- com quem indicou, e na PRIMEIRA compra aprovada dessa pessoa quem indicou
-- ganha dias (referral_days, editavel no admin). So' conta compra de verdade:
-- criar conta falsa nao rende nada, porque conta sem pagamento nao premia.

ALTER TABLE users ADD COLUMN referral_code TEXT UNIQUE;
ALTER TABLE users ADD COLUMN referred_by INTEGER REFERENCES users(id) ON DELETE SET NULL;

CREATE TABLE referral_rewards (
    id           SERIAL PRIMARY KEY,
    referrer_id  INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    -- UNIQUE: cada pessoa indicada premia uma vez so', mesmo comprando de novo.
    referred_id  INTEGER NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    payment_id   INTEGER REFERENCES payments(id) ON DELETE SET NULL,
    license_id   INTEGER REFERENCES licenses(id) ON DELETE SET NULL,
    days         INTEGER NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX referral_rewards_referrer ON referral_rewards (referrer_id);

-- Pro Duo: o mesmo Pro valendo para 2 PCs (PC e notebook, dois irmaos).
-- Nasce DESATIVADO: o preco e' decisao do dono. Ative no admin depois de
-- conferir os valores; o site so' mostra a secao quando houver plano ativo.
-- Proposta: 2 PCs por ~83% do preco de dois Pro, com os mesmos 10% e 25%
-- de desconto do trimestral e do anual.
INSERT INTO plans (key, name, tier, description, price_cents, days, max_devices, features, active, sort, period)
SELECT v.key, 'Pro Duo', 'pro', 'O plano Pro para 2 PCs da mesma casa.', v.price, v.days, 2, p.features, FALSE, p.sort, v.period
FROM plans p
JOIN (VALUES
    ('pro-duo',            4990,  30,  'monthly'),
    ('pro-duo-trimestral', 13470, 90,  'quarterly'),
    ('pro-duo-anual',      44910, 365, 'annual')
) AS v(key, price, days, period) ON p.key = 'pro'
ON CONFLICT (key) DO NOTHING;

-- Mesma trava da 0003: tabela nova fechada para a API REST do Supabase.
-- gameplay_sessions (0007) nasceu sem a linha; entra aqui.
ALTER TABLE referral_rewards ENABLE ROW LEVEL SECURITY;
ALTER TABLE gameplay_sessions ENABLE ROW LEVEL SECURITY;
