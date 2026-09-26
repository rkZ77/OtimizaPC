# FPSX: diretrizes do projeto

## Princípio que manda em tudo

O FPSX é produto de engenharia, não coleção de tweaks. Nenhuma otimização entra sem responder às
dez perguntas da seção 51 do spec (estão no campo `justification` do catálogo). Na dúvida,
**diagnosticar em vez de alterar**. Nunca prometer número de ganho: número só sai de medição.

## Partes

| Pasta | Stack | Checagem |
|---|---|---|
| `agent/` | C# .NET 8 (Core, Windows, Client, App WPF, CLI) | `dotnet test` |
| `backend/` | FastAPI + psycopg2, SQL na mão, sem ORM | `python -m pytest tests -q` |
| `frontend/` | Vite + React 18 + Tailwind (padrão do Pickia) | `npm run build` |

## Regras de código

- Otimização nova: handler em `Fpsx.Core/Optimizations` + entrada no catálogo com justificativa
  completa + teste. Alteração nova só pelos tipos fechados de `Model/Changes.cs`, com regra na `SafetyPolicy`.
- Nunca: desativar Defender, Firewall ou Windows Update; prioridade Realtime; desligar serviços em
  massa; fechar processo à força. A `SafetyPolicy` bloqueia, e o teste garante.
- Desfazer é liberado em todos os planos. Não colocar rollback atrás de plano pago.
- Comentários em português explicando o **porquê**.
- Texto de tela 100% pt-BR, sem emoji, sem travessão e sem ponto do meio; ícones do `lucide-react` no site.
- Cor muda no token (`:root` no site, `Theme.xaml` no app), nunca solta no componente.
- Preço vem sempre de `/api/public/plans`; o front não escreve nem calcula preço.
- **Nenhum teste toca banco**, exceto `test_integration_db.py` com `FPSX_TEST_DATABASE_URL` local.

## Fluxo

- Commit nasce em `dev`. `noprod` é staging. `main` só quando o dono pedir.
- Commit com `git commit -F <arquivo>` no PowerShell 5.1 (aspas quebram o `-m`).
- Editar arquivo com as ferramentas de edição, não com `Get-Content`/`Set-Content` do PowerShell 5.1
  (corrompe UTF-8 sem BOM).
- Mudou tela? Print com Playwright (Edge instalado) em 390px e desktop, conferindo estouro horizontal.
