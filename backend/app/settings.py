"""Configuracao lida do ambiente, num lugar so.

Tudo que muda entre dev e producao vem de variavel de ambiente. O que o
ADMIN muda (precos, trial_days, planos) NAO fica aqui: fica no banco, na
tabela `settings`, e e' editado pelo painel.
"""
import os
import re
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
#: Remetente de dominio verificado no Resend, ex.: "RKZFPS <nao-responda@rkzfps.com.br>".
RESEND_FROM = os.getenv("RESEND_FROM", "")

#: Assistente do site (OpenAI). Sem chave, o chat some do site.
OPENAI_API_KEY = os.getenv("OPENAI_API_KEY", "")
OPENAI_MODEL = os.getenv("OPENAI_MODEL", "gpt-4.1-mini")
#: Endereco que recebe as respostas ("Responder" no e-mail). Opcional.
EMAIL_REPLY_TO = os.getenv("EMAIL_REPLY_TO", "")

#: Google Analytics 4 do site. O ID nao e' segredo (fica publico em todo site
#: com Analytics), por isso o da conta do dono e' o padrao; a variavel troca,
#: e GA_MEASUREMENT_ID=off desliga. So' o dominio oficial recebe a tag
#: (seo.render), e o script so' carrega depois do "Aceitar todos".
GA_MEASUREMENT_ID = os.getenv("GA_MEASUREMENT_ID", "G-W8VDSCZRG7").strip()
if not re.fullmatch(r"G-[A-Z0-9]{4,12}", GA_MEASUREMENT_ID):
    GA_MEASUREMENT_ID = ""

#: Codigo da meta "google-site-verification" do Search Console. Opcional:
#: verificar pelo DNS (registro TXT) tambem funciona e cobre www e http.
GOOGLE_SITE_VERIFICATION = os.getenv("GOOGLE_SITE_VERIFICATION", "").strip()
if not re.fullmatch(r"[A-Za-z0-9_-]{10,100}", GOOGLE_SITE_VERIFICATION):
    GOOGLE_SITE_VERIFICATION = ""

#: Aplica as migrations no startup. Ligado no deploy, desligado quando
#: alguem quer subir a API sem tocar no schema.
AUTO_MIGRATE = os.getenv("AUTO_MIGRATE", "1") == "1"
