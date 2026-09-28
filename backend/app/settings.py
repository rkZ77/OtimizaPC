"""Configuracao lida do ambiente, num lugar so.

Tudo que muda entre dev e producao vem de variavel de ambiente. O que o
ADMIN muda (precos, trial_days, planos) NAO fica aqui: fica no banco, na
tabela `settings`, e e' editado pelo painel.
"""
import os
import warnings

from dotenv import find_dotenv, load_dotenv

load_dotenv(find_dotenv(usecwd=True))

APP_ENV = os.getenv("APP_ENV", "production").lower()
IS_PRODUCTION = APP_ENV in ("production", "prod")

JWT_SECRET = os.getenv("JWT_SECRET", "")
if not JWT_SECRET:
    if IS_PRODUCTION:
        raise RuntimeError("JWT_SECRET nao configurado. Defina antes de iniciar em producao.")
    warnings.warn("JWT_SECRET ausente: usando segredo de desenvolvimento.", stacklevel=1)
    JWT_SECRET = "dev-only-insecure-secret-do-not-use-in-prod"

#: URL publica do site, usada nos links de retorno do checkout.
PUBLIC_URL = os.getenv("PUBLIC_URL", "http://localhost:5173").rstrip("/")

#: Origens aceitas pelo CORS. O app desktop nao passa por CORS (nao e' navegador).
CORS_ORIGINS = [o.strip() for o in os.getenv("CORS_ORIGINS", PUBLIC_URL).split(",") if o.strip()]

MERCADOPAGO_ACCESS_TOKEN = os.getenv("MERCADOPAGO_ACCESS_TOKEN", "")
MERCADOPAGO_WEBHOOK_SECRET = os.getenv("MERCADOPAGO_WEBHOOK_SECRET", "")

#: E-mail pelo Resend, como no Pickia. Sem a chave nada sai, e o registro
#: marca 'skipped': o staging aponta para o banco de producao e, sem chave
#: la', nunca manda e-mail para cliente real por engano.
RESEND_API_KEY = os.getenv("RESEND_API_KEY", "")
#: Remetente de dominio verificado no Resend, ex.: "RKZFPS <nao-responda@fpsx.com.br>".
RESEND_FROM = os.getenv("RESEND_FROM", "")
#: Endereco que recebe as respostas ("Responder" no e-mail). Opcional.
EMAIL_REPLY_TO = os.getenv("EMAIL_REPLY_TO", "")

#: Aplica as migrations no startup. Ligado no deploy, desligado quando
#: alguem quer subir a API sem tocar no schema.
AUTO_MIGRATE = os.getenv("AUTO_MIGRATE", "1") == "1"
