import logging
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse, JSONResponse, PlainTextResponse, Response
from fastapi.staticfiles import StaticFiles

from app import database, seo, settings
from app.routers import account, admin, agent, auth, payments, public
from app.security_headers import SecurityHeaders
from app.services import emails

logger = logging.getLogger("rkzfps")
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")


@asynccontextmanager
async def lifespan(_app: FastAPI):
    # Endereco publico no log de cada subida: e' dele que saem o retorno do
    # checkout, o aviso do Mercado Pago e os links dos e-mails. Com o dominio
    # trocado, esta linha responde "para onde o pagamento volta?" sem abrir
    # as variaveis (que tem segredo junto).
    logger.info("[CONFIG] PUBLIC_URL=%s CORS=%s", settings.PUBLIC_URL, ",".join(settings.CORS_ORIGINS))
    if settings.AUTO_MIGRATE:
        from app import migrate

        try:
            applied = migrate.run(logger)
            if applied:
                logger.info("[MIGRATION] aplicadas: %s", ", ".join(applied))
        except Exception as e:
            # Sobe mesmo sem banco: /api/health responde e o log mostra o motivo,
            # em vez de o Railway ficar em loop de restart sem pista nenhuma.
            logger.error("[MIGRATION] falhou: %s", e)

    # Avisos de vencimento so' onde o envio esta' configurado. O staging usa
    # o banco de producao e fica sem RESEND_API_KEY: la' o agendador nem sobe.
    scheduler = None
    if emails.configured():
        scheduler = emails.ExpiryScheduler()
        scheduler.start()
    yield
    if scheduler is not None:
        scheduler.stop()


# Sem docs E sem /openapi.json em producao: so' tirar o docs_url deixava o
# esquema inteiro (rotas do admin inclusive) publico em /openapi.json.
app = FastAPI(title="RKZFPS API", version="0.1.0", lifespan=lifespan,
              docs_url="/api/docs" if not settings.IS_PRODUCTION else None,
              redoc_url=None,
              openapi_url="/openapi.json" if not settings.IS_PRODUCTION else None)

app.add_middleware(SecurityHeaders)
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.CORS_ORIGINS,
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "DELETE"],
    allow_headers=["Authorization", "Content-Type", "X-Device-Token"],
)

for r in (auth.router, account.router, public.router, payments.router, agent.router, admin.router):
    app.include_router(r)


@app.exception_handler(database.PoolOcupado)
def _banco_ocupado(_request, _exc):
    # Pico maior que a fila do banco: 503 diz "tente de novo", 500 diria "quebrou".
    return JSONResponse({"detail": "Muitos acessos agora. Tente de novo em instantes."}, status_code=503,
                        headers={"Retry-After": "5"})


@app.get("/api/health")
def health():
    return {"ok": True}


# ─── arquivos para robos de busca e de IA (ver app/seo.py) ──────────────
# Antes caiam no SPA e voltavam o index.html: o Google nao tinha robots nem
# sitemap, e as IAs nao tinham o resumo do produto.
_CACHE_ROBOS = {"Cache-Control": "public, max-age=3600"}


@app.get("/robots.txt", include_in_schema=False)
def robots(request: Request):
    return PlainTextResponse(seo.robots_txt(seo.indexable_host(request.url.hostname)), headers=_CACHE_ROBOS)


@app.get("/sitemap.xml", include_in_schema=False)
def sitemap():
    return Response(seo.sitemap_xml(), media_type="application/xml", headers=_CACHE_ROBOS)


@app.get("/llms.txt", include_in_schema=False)
def llms():
    return PlainTextResponse(seo.llms_txt(), headers=_CACHE_ROBOS)


# ─── site (build do Vite) ────────────────────────────────────────────────
# Mesmo desenho do Pickia: uma imagem so' serve API e SPA. A pasta existe na
# imagem Docker (copiada do estagio de build do frontend).
_dist = Path(__file__).resolve().parent.parent / "dist"
if _dist.exists():
    if (_dist / "assets").exists():
        app.mount("/assets", StaticFiles(directory=_dist / "assets"), name="assets")

    _index_html = (_dist / "index.html").read_text(encoding="utf-8")

    @app.get("/{full_path:path}", include_in_schema=False)
    def spa(full_path: str, request: Request):
        if full_path.startswith("api/"):
            raise HTTPException(404, "Rota não encontrada.")
        candidate = (_dist / full_path).resolve()
        # resolve() + is_relative_to: bloqueia ../ para fora do dist.
        if full_path and candidate.is_file() and candidate.is_relative_to(_dist):
            return FileResponse(candidate)
        # Cada rota sai com o titulo, a descricao e o texto dela, e rota que
        # nao existe responde 404 (o React mostra a tela de nao encontrado).
        path = "/" + full_path.strip("/")
        planos = seo._planos() if path in ("/", "/planos") else []
        page = seo.page_for(path, planos)
        html = seo.render(_index_html, page, seo.indexable_host(request.url.hostname), planos)
        return HTMLResponse(html, status_code=page.status)
