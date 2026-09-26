import logging
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles

from app import settings
from app.routers import account, admin, agent, auth, payments, public
from app.services import emails

logger = logging.getLogger("fpsx")
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")


@asynccontextmanager
async def lifespan(_app: FastAPI):
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


app = FastAPI(title="FPSX API", version="0.1.0", lifespan=lifespan, docs_url="/api/docs" if not settings.IS_PRODUCTION else None)

app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.CORS_ORIGINS,
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "DELETE"],
    allow_headers=["Authorization", "Content-Type", "X-Device-Token"],
)

for r in (auth.router, account.router, public.router, payments.router, agent.router, admin.router):
    app.include_router(r)


@app.get("/api/health")
def health():
    return {"ok": True}


# ─── site (build do Vite) ────────────────────────────────────────────────
# Mesmo desenho do Pickia: uma imagem so' serve API e SPA. A pasta existe na
# imagem Docker (copiada do estagio de build do frontend).
_dist = Path(__file__).resolve().parent.parent / "dist"
if _dist.exists():
    if (_dist / "assets").exists():
        app.mount("/assets", StaticFiles(directory=_dist / "assets"), name="assets")

    @app.get("/{full_path:path}", include_in_schema=False)
    def spa(full_path: str):
        if full_path.startswith("api/"):
            raise HTTPException(404, "Rota não encontrada.")
        candidate = (_dist / full_path).resolve()
        # resolve() + is_relative_to: bloqueia ../ para fora do dist.
        if full_path and candidate.is_file() and candidate.is_relative_to(_dist):
            return FileResponse(candidate)
        return FileResponse(_dist / "index.html")
