# Arquitetura do FPSX

## Visão geral

```
PC do cliente                                   Nuvem (Railway + Supabase)
┌──────────────────────────────┐               ┌──────────────────────────────┐
│ FPSX.exe (WPF)   fpsx.exe CLI│               │ FastAPI  ──  PostgreSQL       │
│        │               │     │   HTTPS       │   │         (Supabase)        │
│   Fpsx.Client  ◄───────┼─────┼──────────────►│   ├─ licença assinada (ECDSA) │
│        │                     │  só licença,  │   ├─ catálogo assinado        │
│   Fpsx.Core (motor)          │  catálogo e   │   ├─ pagamentos (Mercado Pago)│
│        │                     │  telemetria   │   └─ admin                    │
│   Fpsx.Windows (registro,    │  consentida   │ Site (Vite/React) na mesma    │
│   WMI, energia, vídeo)       │               │ imagem Docker                 │
└──────────────────────────────┘               └──────────────────────────────┘
```

O servidor **nunca manda o PC executar nada** (seção 31 do spec). Ele entrega três coisas,
todas assinadas ou só de leitura: licença, overrides do catálogo e versão disponível.

## Agent (`agent/`)

| Projeto | Papel |
|---|---|
| `Fpsx.Core` | Modelos, catálogo, diagnósticos, otimizações, motor de decisão, safety policy, executor, backup/rollback, benchmark, relatório. Não conhece Windows: é testado com PCs simulados. |
| `Fpsx.Windows` | Única camada que toca o sistema: coleta do snapshot (WMI, registro, contadores, Steam) e `WindowsSystemAccess`. |
| `Fpsx.Client` | Licença assinada, identidade do PC (hash), API, overrides do catálogo, telemetria. Compartilhado por CLI e app. |
| `Fpsx.App` | App WPF do cliente. |
| `Fpsx.Agent` | CLI `fpsx` (suporte, testes, automação). |

### Fluxo de decisão (seção 25)

1. **Detectar**: `SnapshotCollector` monta o `SystemSnapshot`. Campo que não foi lido fica `null` e vira "desconhecido", nunca um palpite.
2. **Validar compatibilidade e necessidade**: cada `IOptimization` devolve `NotApplicable`, `AlreadyOptimal`, `Recommended`, `Optional` ou `Unknown`.
3. **Avaliar risco e permissão**: `DecisionEngine` aplica catálogo (ligada pelo admin?), plano mínimo, admin do Windows e perfil. EXPERIMENTAL e TROUBLESHOOTING nunca são automáticas.
4. **Backup**: `ChangeExecutor.CaptureInverse` lê o estado atual e grava o inverso **antes** de escrever.
5. **Aplicar**: só tipos de alteração fechados (`RegistryValueChange`, `PowerSchemeChange`, `DisplayRefreshChange`, `CacheClearChange`, `NetworkRepairChange`, `ProcessCloseChange`, `GameConfigChange`), todos validados pela `SafetyPolicy` (whitelist de chaves, formatos e processos protegidos).
6. **Verificar**: relê o estado. Falha abre o fail-safe: Restaurar, Continuar ou Parar.
7. **Medir e registrar**: sessão em `%LOCALAPPDATA%\FPSX\sessions`, log em JSON Lines, benchmark com PresentMon.

### Catálogo

`optimization-engine/catalog/optimizations.json` descreve cada otimização e responde às dez perguntas
da seção 51. O código de cada uma vive em `Fpsx.Core/Optimizations`. O `CatalogValidator` recusa o
catálogo inteiro se houver id sem handler, handler sem id ou justificativa incompleta, e o CI quebra.
Itens `NOT_RECOMMENDED` ficam no catálogo para o produto explicar por que **não** aplica, e não podem
ter handler.

### Planos

`PlanFeatures` é a matriz única do que cada plano libera; cada plano contém o anterior. **Desfazer é
liberado em todos.** O `min_plan` de cada otimização no catálogo é conferido contra a matriz por teste.

## Licença

- O backend assina com ECDSA P-256 (`app/signing.py`): `base64url(json).base64url(assinatura DER)`.
- O app verifica com a chave pública embutida (`Fpsx.Client/LicenseToken.cs`), confere o hash do PC e
  a carência offline (`valid_until`). Qualquer falha cai para Free, com o motivo na tela.
- O token fica protegido com DPAPI (amarrado ao usuário do Windows).
- Identidade do PC: SHA-256 do `MachineGuid` com sal. Nenhum serial, MAC ou nome de usuário.

## Backend (`backend/`)

- `app/services/*`: regra de negócio em funções puras no topo (testáveis sem banco) e SQL fino embaixo.
- Pagamento com um único caminho de ativação (`apply_approved_payment`), idempotente pelo id do
  provedor, conferindo o valor pago contra o preço com cupom.
- Migrations em `app/migrations/*.sql`, aplicadas uma vez, em ordem, com advisory lock.
- Testes: a suíte inteira é proibida de tocar banco (trava no `conftest.py`), exceto
  `test_integration_db.py`, que só roda com `FPSX_TEST_DATABASE_URL` apontando para localhost.

## Privacidade

Telemetria só com consentimento explícito (padrão: não enviar), só eventos e campos de uma lista
fechada; o servidor descarta o resto. Detalhes em `frontend/src/pages/Legal.tsx`.
