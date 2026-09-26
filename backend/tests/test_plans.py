from app.services import plans


def _p(key, tier, period, price):
    return {"key": key, "tier": tier, "period": period, "price_cents": price}


def test_economia_sai_do_servidor_e_nunca_arredonda_para_cima():
    out = {p["key"]: p for p in plans.with_savings([
        _p("free", "free", "none", 0),
        _p("pro", "pro", "monthly", 2990),
        _p("pro-trimestral", "pro", "quarterly", 8070),
        _p("pro-anual", "pro", "annual", 26910),
    ])}
    assert out["pro"]["savings_percent"] == 0
    assert out["pro"]["per_month_cents"] == 2990
    # 3 x 29,90 = 89,70; pagando 80,70 economiza 10,03% -> anuncia 10.
    assert out["pro-trimestral"]["savings_percent"] == 10
    assert out["pro-trimestral"]["per_month_cents"] == 2690
    # 12 x 29,90 = 358,80; pagando 269,10 economiza 25%.
    assert out["pro-anual"]["savings_percent"] == 25
    assert out["pro-anual"]["per_month_cents"] == 2242
    assert out["free"]["savings_percent"] == 0


def test_periodo_sem_mensal_do_mesmo_tier_nao_inventa_economia():
    out = plans.with_savings([_p("custom-anual", "custom", "annual", 50000)])
    assert out[0]["savings_percent"] == 0
