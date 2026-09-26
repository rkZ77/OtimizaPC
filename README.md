# FPSX

Otimização de PC Windows com **diagnóstico, otimização compatível, medição e transparência**.
O FPSX analisa o PC, aplica só o que faz sentido para aquele PC (com backup e desfazer),
e mede antes e depois. Quando nada precisa mudar, ele diz isso.

## Partes

| Pasta | O que é | Stack |
|---|---|---|
| `agent/` | Motor, app desktop do cliente (WPF) e CLI | C# / .NET 8 |
| `backend/` | API: contas, licenças assinadas, PCs, pagamentos, admin | FastAPI + psycopg2 (SQL na mão) |
| `frontend/` | Site: divulgação, área do cliente e admin | Vite + React 18 + Tailwind |
| `optimization-engine/catalog/` | Catálogo de otimizações e perfis de uso | JSON (com a justificativa de cada item) |
| `game-profiles/` | Perfis de jogo (CS2, Fortnite, Minecraft Java) | JSON |
| `installer/` | Publicação do app e instalador | PowerShell + Inno Setup |
| `docs/` | Arquitetura, deploy e decisões | Markdown |

## Rodar localmente

```powershell
# Agent: testes e app
cd agent
dotnet test
dotnet run --project src/Fpsx.App          # o app desktop
dotnet run --project src/Fpsx.Agent -- scan # o CLI

# API (precisa de um Postgres; ver docs/DEPLOY.md)
cd backend
python -m venv .venv; .\.venv\Scripts\pip install -r requirements-dev.txt
.\.venv\Scripts\python -m pytest tests -q
.\.venv\Scripts\python run_dev.py            # http://127.0.0.1:8000

# Site
cd frontend
npm install
npm run dev                                  # http://localhost:5173 (proxy /api -> 8000)
```

## Gerar o instalador

```powershell
./installer/publish.ps1 -Installer   # installer/out/FPSX-Setup-<versão>.exe + SHA-256
```

## Fluxo de branches

Como no Pickia: o commit nasce em `dev`, `noprod` é staging, `main` só quando o dono pedir.
O CI (`.github/workflows/ci.yml`) roda Agent, API e site a cada push.

Mais detalhes: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) e [docs/DEPLOY.md](docs/DEPLOY.md).
