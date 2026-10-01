from fastapi import APIRouter, Depends, HTTPException, Request
from pydantic import BaseModel, Field

from app import auth
from app.services import payments

router = APIRouter(prefix="/api/payments", tags=["payments"])


class CheckoutIn(BaseModel):
    plan_key: str = Field(max_length=40)
    coupon: str | None = Field(default=None, max_length=40)


@router.post("/quote")
def quote(body: CheckoutIn, user: dict = Depends(auth.current_user)):
    # A cotacao diz se um cupom existe: sem teto, uma conta testaria codigos
    # curtos sem parar ate' achar um de 100%. 60 em 10 minutos sobra para a tela.
    auth.rate_limit("quote", str(user["id"]), limit=60, window_seconds=600)
    try:
        q = payments.quote(body.plan_key, body.coupon)
    except ValueError as e:
        raise HTTPException(400, str(e))
    return {"plan_key": q["plan"]["key"], "amount_cents": q["amount_cents"], "percent_off": q["percent_off"], "coupon": q["coupon"]}


@router.post("/checkout")
def checkout(body: CheckoutIn, user: dict = Depends(auth.current_user)):
    auth.rate_limit("checkout", str(user["id"]), limit=10, window_seconds=600)
    try:
        return payments.create_checkout(user, body.plan_key, body.coupon)
    except ValueError as e:
        raise HTTPException(400, str(e))
    except RuntimeError:
        raise HTTPException(503, "Pagamentos temporariamente indisponíveis. Tente de novo em instantes.")


@router.post("/webhook")
async def webhook(request: Request):
    # data.id vem da QUERY STRING: e' sobre ela que o Mercado Pago assina.
    data_id = request.query_params.get("data.id") or request.query_params.get("id") or ""
    if not data_id:
        return {"ok": True, "ignored": "sem data.id"}
    result = payments.handle_webhook(
        data_id, request.headers.get("x-signature", ""), request.headers.get("x-request-id", ""))
    if result.get("reason") == "bad_signature":
        raise HTTPException(403, "Assinatura inválida.")
    return {"ok": True, **result}
