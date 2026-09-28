import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Check, Monitor, RotateCcw } from 'lucide-react'
import api, { errorMessage, type Plan, type PlansResponse } from '../services/api'
import { useAuth } from '../context/AuthContext'
import { money } from '../lib/format'
import { cn } from '../lib/cn'
import { Alert, Button, SkeletonCard } from './ui'

type Period = 'monthly' | 'quarterly' | 'annual'

const PERIODS: { key: Period; label: string; every: string }[] = [
  { key: 'monthly', label: 'Mensal', every: 'por mês' },
  { key: 'quarterly', label: 'Trimestral', every: 'a cada 3 meses' },
  { key: 'annual', label: 'Anual', every: 'por ano' },
]

const TIERS = ['free', 'starter', 'pro', 'ultimate']

/**
 * Planos vindos da API, fonte unica de preco (licao do Pickia: o valor
 * escrito a mao na tela divergiu do cobrado). O front nao calcula preco:
 * valor por mes e economia chegam prontos do servidor.
 */
export default function PlansGrid({ compact }: { compact?: boolean }) {
  const [data, setData] = useState<PlansResponse | null>(null)
  const [error, setError] = useState('')
  const [period, setPeriod] = useState<Period>('annual')
  const { user } = useAuth()
  const navigate = useNavigate()

  useEffect(() => {
    api.get<PlansResponse>('/public/plans')
      .then(({ data }) => setData(data))
      .catch((e) => setError(errorMessage(e, 'Não foi possível carregar os planos agora.')))
  }, [])

  // Um cartão por tier: o Free sempre, os pagos no período escolhido.
  const cards = useMemo(() => {
    if (!data) return []
    return TIERS.map((tier) => data.plans.find((p) => p.tier === tier && (tier === 'free' ? p.period === 'none' : p.period === period)))
      .filter((p): p is Plan => Boolean(p))
  }, [data, period])

  const savings = (p: Period) => Math.max(0, ...(data?.plans.filter((x) => x.period === p).map((x) => x.savings_percent) ?? [0]))

  const buy = (plan: Plan) => {
    if (plan.price_cents === 0) {
      navigate('/download')
      return
    }
    // Logado vai para a pagina de pagamento (resumo, periodo e cupom); sem
    // conta, cria a conta e cai direto nela.
    navigate(user ? `/pagamento?plano=${plan.key}` : `/cadastro?plano=${plan.key}`)
  }

  if (error && !data) return <Alert>{error}</Alert>
  if (!data) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[0, 1, 2, 3].map((i) => <SkeletonCard key={i} className="h-72" />)}
      </div>
    )
  }

  const every = PERIODS.find((p) => p.key === period)!.every

  return (
    <div className="space-y-6">
      {error && <Alert>{error}</Alert>}

      <div className="flex justify-center">
        <div role="tablist" aria-label="Período da assinatura" className="inline-flex rounded-lg border border-line bg-surface-1 p-1">
          {PERIODS.map((p) => {
            const off = savings(p.key)
            return (
              <button
                key={p.key}
                role="tab"
                aria-selected={period === p.key}
                onClick={() => setPeriod(p.key)}
                className={cn('rounded-md px-3 py-2 text-sm font-semibold transition-colors sm:px-4',
                  period === p.key ? 'bg-surface-3 text-ink-1' : 'text-ink-3 hover:text-ink-1')}
              >
                {p.label}
                {off > 0 && <span className="ml-1.5 text-xs font-bold text-accent-ink">-{off}%</span>}
              </button>
            )
          })}
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {cards.map((plan) => {
          const destaque = plan.tier === 'pro'
          const free = plan.price_cents === 0
          return (
            <div key={plan.key} className={cn('card relative flex flex-col p-5', destaque && 'border-accent/50 shadow-elev')}>
              {destaque && (
                <span className="absolute -top-2.5 left-5 rounded-sm bg-accent px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-black">
                  Mais escolhido
                </span>
              )}
              <h3 className="font-display text-lg font-bold text-ink-1">{plan.name}</h3>
              <p className="mt-1 min-h-[40px] text-sm text-ink-3">{plan.description}</p>
              <p className="mt-4">
                <span className="font-display text-3xl font-extrabold text-ink-1">{free ? 'Grátis' : money(plan.per_month_cents)}</span>
                {!free && <span className="text-sm text-ink-3"> /mês</span>}
              </p>
              <p className="mt-1 min-h-[20px] text-xs text-ink-3">
                {free ? 'Para sempre' : period === 'monthly' ? `Pagamento único de ${plan.days} dias, sem renovação automática` : `${money(plan.price_cents)} ${every}`}
                {plan.savings_percent > 0 && <span className="ml-1 font-semibold text-accent-ink">(economia de {plan.savings_percent}%)</span>}
              </p>
              <p className="mt-2 inline-flex items-center gap-1.5 text-xs text-ink-3"><Monitor className="h-3.5 w-3.5" aria-hidden />1 PC por assinatura</p>
              {!compact && (
                <ul className="mt-4 flex-1 space-y-2 text-sm text-ink-2">
                  {plan.features.map((f) => (
                    <li key={f} className="flex gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-accent" aria-hidden />{f}</li>
                  ))}
                </ul>
              )}
              <Button className="mt-5" block variant={destaque ? 'primary' : 'ghost'} onClick={() => buy(plan)}>
                {free ? 'Baixar grátis' : 'Assinar'}
              </Button>
            </div>
          )
        })}
      </div>

      <div className="flex flex-col items-center gap-1.5 text-center text-sm text-ink-3">
        {data.trial_days > 0 && <p>Conta nova ganha {data.trial_days} dias do plano Pro para testar, sem cartão.</p>}
        <p className="inline-flex items-center gap-1.5"><RotateCcw className="h-3.5 w-3.5" aria-hidden />Desfazer qualquer alteração continua liberado em todos os planos, mesmo depois de cancelar.</p>
      </div>
    </div>
  )
}
