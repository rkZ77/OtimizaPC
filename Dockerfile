# Imagem unica: API FastAPI + site (build do Vite) + catalogo. Mesmo desenho
# do Pickia, que roda assim no Railway.

# ── Estagio 1: build do site ─────────────────────────────────────────────
FROM node:20-slim AS frontend
WORKDIR /frontend
COPY frontend/package*.json ./
RUN npm ci --ignore-scripts
COPY frontend/ .
RUN npm run build

# ── Estagio 2: API + site buildado ───────────────────────────────────────
FROM python:3.12-slim
WORKDIR /app
COPY backend/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY backend/app ./app
COPY --from=frontend /frontend/dist ./dist
# O admin lista as otimizacoes a partir do mesmo catalogo que o Agent embute.
COPY optimization-engine/catalog ./catalog
ENV CATALOG_FILE=/app/catalog/optimizations.json \
    PYTHONUNBUFFERED=1

# Usuario sem privilegio: a API nao precisa de root para nada.
RUN useradd --create-home fpsx && chown -R fpsx /app
USER fpsx

EXPOSE 8000
# --app-dir /app: licao do Pickia com mais de um worker (ver Dockerfile de la').
CMD uvicorn app.main:app --app-dir /app --host 0.0.0.0 --port ${PORT:-8000} --workers ${WEB_CONCURRENCY:-1} --proxy-headers --forwarded-allow-ips=*
