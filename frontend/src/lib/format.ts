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
