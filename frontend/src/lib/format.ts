// Formatacao para a tela. Nenhum numero de negocio e' calculado aqui: preco,
// validade e status vem prontos da API (regra do Pickia).

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

export function money(cents: number): string {
  return brl.format(cents / 100)
}

export function date(iso: string | null | undefined): string {
  if (!iso) return 'sem data'
  return new Date(iso).toLocaleDateString('pt-BR')
}

export function dateTime(iso: string | null | undefined): string {
  if (!iso) return 'sem data'
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
}

export const PERIOD_LABEL: Record<string, string> = {
  monthly: 'mensal',
  quarterly: 'trimestral',
  annual: 'anual',
}

/** Nome do plano a partir da chave: "pro-anual" vira "Pro anual". */
export function planName(key: string): string {
  const [tier, suffix] = key.split('-')
  const base = TIER_LABEL[tier] ?? key
  return suffix ? `${base} ${suffix}` : base
}

export const TIER_LABEL: Record<string, string> = {
  free: 'Free',
  starter: 'Starter',
  pro: 'Pro',
  ultimate: 'Ultimate',
  custom: 'Custom',
}

export const STATUS_LABEL: Record<string, string> = {
  free: 'Plano gratuito',
  trial: 'Teste grátis',
  active: 'Ativa',
  expired: 'Vencida',
  blocked: 'Bloqueada',
}
