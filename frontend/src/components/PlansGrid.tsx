import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Check } from 'lucide-react'
import api, { errorMessage, type Plan, type PlansResponse } from '../services/api'
import { useAuth } from '../context/AuthContext'
import { money } from '../lib/format'
import { cn } from '../lib/cn'
import { Alert, Button, Spinner } from './ui'

/**
 * Planos vindos da API (fonte unica de preco). O front nao escreve preco nem
 * calcula desconto: licao do Pickia, onde o valor escrito a mao na tela
 * divergiu do cobrado.
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
  if (!data) return <div className="flex justify-center py-12"><Spinner className="w-6 h-6" /></div>

  return (
    <div className="space-y-6">
      {error && <Alert>{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {data.plans.map((plan) => {
          const highlight = plan.tier === 'pro'
          return (
            <div key={plan.key} className={cn('flex flex-col rounded-xl border bg-surface-1 p-5', highlight ? 'border-accent' : 'border-line')}>
              <div className="flex items-center justify-between">
                <h3 className="text-lg font-bold text-ink-1">{plan.name}</h3>
                {highlight && <span className="rounded-md bg-accent/15 px-2 py-0.5 text-xs font-bold text-accent-ink">MAIS ESCOLHIDO</span>}
              </div>
              <p className="mt-1 text-sm text-ink-3">{plan.description}</p>
              <p className="mt-4">
                <span className="text-3xl font-bold text-ink-1">{plan.price_cents === 0 ? 'Grátis' : money(plan.price_cents)}</span>
                {plan.price_cents > 0 && <span className="text-sm text-ink-3"> por {plan.days} dias</span>}
              </p>
              <p className="mt-1 text-xs text-ink-3">{plan.max_devices === 1 ? '1 PC' : `Até ${plan.max_devices} PCs`}</p>
              {!compact && (
                <ul className="mt-4 flex-1 space-y-2 text-sm">
                  {plan.features.map((f) => (
                    <li key={f} className="flex gap-2"><Check className="mt-0.5 w-4 h-4 shrink-0 text-accent" aria-hidden />{f}</li>
                  ))}
                </ul>
              )}
              <Button className="mt-5" block variant={highlight ? 'primary' : 'ghost'} loading={buying === plan.key} onClick={() => buy(plan)}>
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
          <label className="block text-sm text-ink-3" htmlFor="cupom">Tem cupom?</label>
          <input id="cupom" value={coupon} onChange={(e) => setCoupon(e.target.value.toUpperCase())} placeholder="CÓDIGO"
                 className="mt-1 w-full rounded-md border border-line bg-surface-2 px-3 py-2.5 text-ink-1 min-h-[44px]" />
        </div>
      )}
    </div>
  )
}
