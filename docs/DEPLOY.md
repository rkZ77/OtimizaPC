# Deploy: Supabase + Railway

Mesmo desenho do Pickia: uma imagem Docker (API + site) no Railway, banco no Supabase.

## Onde está hoje

| Ambiente | Railway | Branch | URL |
|---|---|---|---|
| Produção | projeto `ample-energy`, serviço `OtimizaPC`, ambiente `production` | `main` | https://otimizapc-production.up.railway.app |
| Staging (`noprod` do Pickia) | serviço `surprising-unity`, ambiente `dev` | `dev` | https://surprising-unity-dev.up.railway.app |

Supabase: projeto `qntwjeeximnztzxwnejj`, região **us-west-2**, Postgres 17. Pooler em
`aws-0-us-west-2.pooler.supabase.com` (6543 transação para a API, 5432 sessão para as migrations),
usuário `postgres.qntwjeeximnztzxwnejj`. Produção e staging usam o mesmo banco, como o `noprod` do Pickia.

As chaves da API REST (publishable/secret) não são usadas pelo FPSX. A migration `0003_rls.sql` liga
RLS em todas as tabelas: pela API REST pública do Supabase, nenhuma tabela é legível (401).
Tabela nova precisa de `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` na própria migration.

## Desenvolvimento local

```powershell
./scripts/dev-db.ps1        # Postgres portátil na porta 54329 (bancos fpsx e fpsx_test), sem Docker
```

O `.env` da raiz (fora do git) já aponta para esse banco, com `APP_ENV=development`.

## 1. Supabase (banco)

1. Crie um projeto em supabase.com (região mais próxima dos clientes, ex.: São Paulo).
2. Em **Project Settings > Database > Connection string**, copie duas URLs do pooler:
   - **Transaction** (porta 6543) → `DATABASE_URL` (a API usa esta)
   - **Session** (porta 5432) → `MIGRATIONS_DATABASE_URL` (só as migrations; o advisory lock precisa de sessão própria)
3. Não é preciso criar tabela: a API aplica `backend/app/migrations/*.sql` no startup.

## 2. Chave de assinatura de licença

A chave privada que já foi gerada fica **fora do repositório**, em `C:\Users\rkZ\.fpsx-keys\license_private.pem`.
A pública correspondente está embutida no app (`agent/src/Fpsx.Client/LicenseToken.cs`).

- Cole o conteúdo do PEM na variável `LICENSE_PRIVATE_KEY_PEM` do Railway.
- Guarde uma cópia desse arquivo num lugar seguro (gerenciador de senhas). Perder a chave obriga a
  publicar uma versão nova do app com a pública nova.
- Para trocar: `python -m app.signing` gera um par; cole a pública em `LicenseToken.cs` e publique o app.

## 3. Railway (API + site)

1. New Project > Deploy from GitHub repo > `rkZ77/OtimizaPC`, branch `dev` (staging) ou `main` (produção).
2. O `railway.json` já aponta para o `Dockerfile` e para o healthcheck `/api/health`.
3. Em **Variables**, preencha tudo que está em `.env.example`:
   `APP_ENV=production`, `PUBLIC_URL`, `CORS_ORIGINS`, `JWT_SECRET`, `DATABASE_URL`,
   `MIGRATIONS_DATABASE_URL`, `DB_SSLMODE=require`, `LICENSE_PRIVATE_KEY_PEM`,
   `MERCADOPAGO_ACCESS_TOKEN`, `MERCADOPAGO_WEBHOOK_SECRET`.
4. **Settings > Networking > Generate Domain** (ou domínio próprio). Atualize `PUBLIC_URL` e
   `CORS_ORIGINS` com ele, e `ClientSettings.ProductionApiUrl` em `agent/src/Fpsx.Client/ClientStorage.cs`.

## 4. Primeiro admin

Crie a conta pelo site e promova pelo terminal (com `DATABASE_URL` apontando para o Supabase):

```powershell
cd backend
.\.venv\Scripts\python -m app.cli make-admin seu@email.com
```

## 5. Mercado Pago

1. Em mercadopago.com.br/developers, crie a aplicação e copie o **Access Token** de produção.
2. Em **Webhooks**, cadastre `https://<seu-dominio>/api/payments/webhook`, evento **Pagamentos**,
   e copie a **assinatura secreta** para `MERCADOPAGO_WEBHOOK_SECRET`.

## 6. Publicar o app

```powershell
./installer/publish.ps1 -Installer
```

Suba o `FPSX-Setup-<versão>.exe` num armazenamento com HTTPS (ex.: GitHub Releases do repositório)
e publique em **Admin > Atualizações** com a URL e o SHA-256 que o script imprime. A página
/download e o aviso de atualização do app passam a apontar para ela.

> Sem certificado de assinatura de código, o Windows SmartScreen mostra "Editor desconhecido" no
> primeiro download. Para produção, assine o instalador e o `FPSX.exe` com um certificado de
> Code Signing (OV ou EV) antes de publicar.
