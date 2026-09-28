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

/** Status do pagamento como o Mercado Pago devolve, em palavras de gente. */
export const PAYMENT_STATUS: Record<string, { label: string; tone: 'green' | 'amber' | 'red' | 'neutral' }> = {
  approved: { label: 'Aprovado', tone: 'green' },
  pending: { label: 'Em processamento', tone: 'amber' },
  in_process: { label: 'Em processamento', tone: 'amber' },
  rejected: { label: 'Recusado', tone: 'red' },
  cancelled: { label: 'Cancelado', tone: 'neutral' },
  refunded: { label: 'Reembolsado', tone: 'neutral' },
  charged_back: { label: 'Contestado no cartão', tone: 'red' },
}

export function paymentStatus(status: string) {
  return PAYMENT_STATUS[status] ?? { label: status, tone: 'neutral' as const }
}
