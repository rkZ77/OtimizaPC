/*
 * Espelho de agent/src/Rkzfps.Core/Engine/PlanFeatures.cs: o que cada plano
 * libera no app. Se mudar la', muda aqui. O app e' quem aplica a regra (com a
 * licenca assinada); esta tabela so' explica ao visitante.
 */
export const PLAN_ORDER = ['free', 'starter', 'pro', 'ultimate'] as const

export const FEATURES: { label: string; plan: (typeof PLAN_ORDER)[number] }[] = [
  { label: 'Diagnóstico completo do PC', plan: 'free' },
  { label: 'O que cada otimização resolveria no seu PC', plan: 'free' },
  { label: 'FPS medido automaticamente nas suas partidas', plan: 'free' },
  { label: 'Desfazer qualquer alteração', plan: 'free' },
  { label: 'Aplicar as correções comprovadas do Windows', plan: 'starter' },
  { label: 'Resolver problemas: processos, memória, disco e rede', plan: 'starter' },
  { label: 'Driver de vídeo e backup: leva direto à ferramenta oficial', plan: 'starter' },
  { label: 'Programas de inicialização e histórico', plan: 'starter' },
  { label: 'Perfis de jogo: CS2, Fortnite e Minecraft', plan: 'pro' },
  { label: 'Configuração leve do jogo para PC fraco', plan: 'pro' },
  { label: 'Antes e depois do FPS nas suas partidas', plan: 'pro' },
  { label: 'RKZFPS Benchmark e relatórios', plan: 'pro' },
  { label: 'Otimizações experimentais, com medição', plan: 'ultimate' },
  { label: 'Novos jogos e otimizações em acesso antecipado', plan: 'ultimate' },
  { label: 'Suporte prioritário', plan: 'ultimate' },
]

export function includes(plan: string, feature: (typeof PLAN_ORDER)[number]): boolean {
  return PLAN_ORDER.indexOf(plan as (typeof PLAN_ORDER)[number]) >= PLAN_ORDER.indexOf(feature)
}

/**
 * O plano QUE VALE AGORA libera isto? Vencido ou bloqueado vale como Free, e
 * custom como o topo. Para a area logada nao mostrar a quem assinou o Basico
 * algo do Pro como se fosse dele (o servidor tambem nao manda o dado).
 */
export function libera(lic: { tier: string; status: string }, feature: (typeof PLAN_ORDER)[number]): boolean {
  if (lic.status !== 'active' && lic.status !== 'trial') return feature === 'free'
  return includes(lic.tier === 'custom' ? 'ultimate' : lic.tier, feature)
}
