/*
 * Espelho de agent/src/Fpsx.Core/Engine/PlanFeatures.cs: o que cada plano
 * libera no app. Se mudar la', muda aqui. O app e' quem aplica a regra (com a
 * licenca assinada); esta tabela so' explica ao visitante.
 */
export const PLAN_ORDER = ['free', 'starter', 'pro', 'ultimate'] as const

export const FEATURES: { label: string; plan: (typeof PLAN_ORDER)[number] }[] = [
  { label: 'Scan completo do PC', plan: 'free' },
  { label: 'Correções básicas comprovadas (Game Mode, energia, monitor, jogos em janela)', plan: 'free' },
  { label: 'Desfazer qualquer alteração', plan: 'free' },
  { label: 'Resolver problemas encontrados (processos, pagefile, espaço)', plan: 'starter' },
  { label: 'Jogo na placa de vídeo dedicada em notebook com duas GPUs', plan: 'starter' },
  { label: 'Programas de inicialização', plan: 'starter' },
  { label: 'Ferramentas de troubleshooting (shader cache, rede)', plan: 'starter' },
  { label: 'Histórico de otimizações', plan: 'starter' },
  { label: 'Perfis de jogo com correção automática (CS2, Fortnite, Minecraft)', plan: 'pro' },
  { label: 'Configuração leve do jogo para PC fraco', plan: 'pro' },
  { label: 'FPSX Benchmark antes e depois', plan: 'pro' },
  { label: 'Relatórios', plan: 'pro' },
  { label: 'Otimizações experimentais, com medição', plan: 'ultimate' },
]

export function includes(plan: string, feature: (typeof PLAN_ORDER)[number]): boolean {
  return PLAN_ORDER.indexOf(plan as (typeof PLAN_ORDER)[number]) >= PLAN_ORDER.indexOf(feature)
}
