import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Check } from 'lucide-react'
import api, { errorMessage, type Plan, type PlansResponse } from '../services/api'
import { useAuth } from '../context/AuthContext'
import { money } from '../lib/format'
import { cn } from '../lib/cn'
import { Alert, Button, Input, SkeletonCard } from './ui'

/**
 * Planos vindos da API, fonte unica de preco (licao do Pickia: o valor
 * escrito a mao na tela divergiu do cobrado). O front nao calcula preco.
 */
export default function PlansGrid({ compact }: { compact?: boolean }) {
  const [data, setData] = useState<PlansResponse | null>(null)
  const [error, setError] = useState('')
  const [buying, setBuying] = useState<string | null>(null)
  const [coupon, setCoupon] = useState('')
  const { user } = useAuth()
  const navigate = useNavigate()

  useEffect(() => {
    api.get<PlansResponse>('/public/plans')
      .then(({ data }) => setData(data))
      .catch((e) => setError(errorMessage(e, 'Não foi possível carregar os planos agora.')))
  }, [])

  const buy = async (plan: Plan) => {
    if (plan.price_cents === 0) {
      navigate('/download')
      return
    }
    if (!user) {
      navigate(`/cadastro?plano=${plan.key}`)
      return
    }
    setBuying(plan.key)
    setError('')
    try {
      const { data } = await api.post<{ checkout_url: string }>('/payments/checkout', { plan_key: plan.key, coupon: coupon.trim() || null })
      window.location.href = data.checkout_url
    } catch (e) {
      setError(errorMessage(e))
      setBuying(null)
    }
  }

  if (error && !data) return <Alert>{error}</Alert>
  if (!data) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[0, 1, 2, 3].map((i) => <SkeletonCard key={i} className="h-72" />)}
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {error && <Alert>{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {data.plans.map((plan) => {
          const destaque = plan.tier === 'pro'
          return (
            <div key={plan.key} className={cn('card relative flex flex-col p-5', destaque && 'border-green-500/50 shadow-elev')}>
              {destaque && (
                <span className="absolute -top-2.5 left-5 rounded-sm bg-accent px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-black">
                  Mais escolhido
                </span>
              )}
              <h3 className="font-display text-lg font-bold text-ink-1">{plan.name}</h3>
              <p className="mt-1 text-sm text-ink-3 min-h-[40px]">{plan.description}</p>
              <p className="mt-4">
                <span className="font-display text-3xl font-extrabold text-ink-1">{plan.price_cents === 0 ? 'Grátis' : money(plan.price_cents)}</span>
                {plan.price_cents > 0 && <span className="text-sm text-ink-3"> por {plan.days} dias</span>}
              </p>
              <p className="mt-1 text-xs text-ink-3">{plan.max_devices === 1 ? '1 PC' : `Até ${plan.max_devices} PCs`}</p>
              {!compact && (
                <ul className="mt-4 flex-1 space-y-2 text-sm text-ink-2">
                  {plan.features.map((f) => (
                    <li key={f} className="flex gap-2"><Check className="mt-0.5 w-4 h-4 shrink-0 text-accent" aria-hidden />{f}</li>
                  ))}
                </ul>
              )}
              <Button className="mt-5" block variant={destaque ? 'primary' : 'ghost'} loading={buying === plan.key} onClick={() => buy(plan)}>
                {plan.price_cents === 0 ? 'Baixar grátis' : 'Assinar'}
              </Button>
            </div>
          )
        })}
      </div>
      {data.trial_days > 0 && (
        <p className="text-center text-sm text-ink-3">
          Conta nova ganha {data.trial_days} dias do plano Pro para testar, em 1 PC, sem cartão.
        </p>
      )}
      {!compact && user && (
        <div className="mx-auto max-w-xs">
          <Input label="Tem cupom?" value={coupon} onChange={(e) => setCoupon(e.target.value.toUpperCase())} placeholder="CÓDIGO" />
        </div>
      )}
    </div>
  )
}
