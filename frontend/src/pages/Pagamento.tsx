import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { CalendarClock, Check, CreditCard, Lock, Monitor, QrCode, RotateCcw, Tag, Undo2 } from 'lucide-react'
import api, { errorMessage, type Overview, type Plan, type PlansResponse } from '../services/api'
import PageShell from '../components/PageShell'
import { Alert, Button, EmptyState, SkeletonCard } from '../components/ui'
import { cn } from '../lib/cn'
import { money, PERIOD_LABEL, TIER_LABEL } from '../lib/format'

/*
 * Pagamento: um passo antes do Mercado Pago, para a pessoa ver o que esta'
 * comprando, trocar o periodo, aplicar cupom e so' entao sair do site.
 *
 * O valor final e' sempre o que o servidor devolve em /payments/quote: o front
 * nao calcula preco nem desconto (regra do projeto). O checkout confere de
 * novo no servidor, e o webhook confere o valor pago.
 */

interface Quote { plan_key: string; amount_cents: number; percent_off: number; coupon: string | null }

const PERIOD_ORDER = ['monthly', 'quarterly', 'annual'] as const

export default function Pagamento() {
  const [params, setParams] = useSearchParams()
  const planKey = params.get('plano') ?? ''
  const [plans, setPlans] = useState<Plan[] | null>(null)
  const [overview, setOverview] = useState<Overview | null>(null)
  const [quote, setQuote] = useState<Quote | null>(null)
  const [coupon, setCoupon] = useState('')
  const [couponMsg, setCouponMsg] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.get<PlansResponse>('/public/plans').then(({ data }) => setPlans(data.plans)).catch((e) => setError(errorMessage(e)))
    api.get<Overview>('/account/overview').then(({ data }) => setOverview(data)).catch(() => setOverview(null))
  }, [])

  const plan = plans?.find((p) => p.key === planKey && p.price_cents > 0)
  // Outros periodos do mesmo plano, para trocar sem voltar a tela de planos.
  const periodos = useMemo(
    () => (plan && plans ? PERIOD_ORDER.map((per) => plans.find((p) => p.tier === plan.tier && p.max_devices === plan.max_devices && p.period === per)).filter((p): p is Plan => Boolean(p)) : []),
    [plan, plans],
  )

  const cotar = (key: string, code: string | null) =>
    api.post<Quote>('/payments/quote', { plan_key: key, coupon: code })

  // Trocou de plano ou de periodo: preco sem cupom, e o cupom e' reaplicado se houver.
  useEffect(() => {
    if (!plan) return
    setQuote(null)
    cotar(plan.key, null).then(({ data }) => setQuote(data)).catch((e) => setError(errorMessage(e)))
    setCouponMsg('')
  }, [plan?.key]) // eslint-disable-line react-hooks/exhaustive-deps

  const aplicarCupom = async (e: FormEvent) => {
    e.preventDefault()
    if (!plan || !coupon.trim()) return
    setCouponMsg('')
    try {
      const { data } = await cotar(plan.key, coupon.trim())
      setQuote(data)
    } catch (err) {
      setCouponMsg(errorMessage(err))
    }
  }

  const pagar = async () => {
    if (!plan) return
    setBusy(true)
    setError('')
    try {
      const { data } = await api.post<{ checkout_url: string }>('/payments/checkout', { plan_key: plan.key, coupon: quote?.coupon ?? null })
      window.location.href = data.checkout_url
    } catch (e) {
      setError(errorMessage(e))
      setBusy(false)
    }
  }

  const bar = { title: 'Pagamento', sub: 'Confira o plano, aplique seu cupom e pague com PIX ou cartão.', back: '/planos' }

  if (!plans && !error) {
    return <PageShell title="Pagamento" noindex bar={bar}><div className="grid gap-4 lg:grid-cols-[1.2fr_0.8fr]"><SkeletonCard className="h-80" /><SkeletonCard className="h-80" /></div></PageShell>
  }
  if (!plan) {
    return (
      <PageShell title="Pagamento" noindex bar={bar}>
        {error ? <Alert>{error}</Alert> : <EmptyState Icon={Tag} title="Escolha um plano para continuar" action={{ children: 'Ver planos', to: '/planos' }} />}
      </PageShell>
    )
  }

  // Veio do app, com o diagnostico feito: lembrar o que esta' esperando no PC
  // e' o que faz a pessoa terminar a compra. So' o numero que o app mandou.
  const achados = Math.max(0, Math.min(99, Number.parseInt(params.get('achados') ?? '', 10) || 0))
  const doApp = params.get('origem') === 'app'

  const atual = overview?.license
  const mesmoTierAtivo = atual && atual.tier === plan.tier && atual.status === 'active'

  return (
    <PageShell title="Pagamento" noindex bar={bar}>
      <div className="space-y-3 mb-6">
        {params.get('pagamento') === 'recusado' && (
          <Alert tone="warn">O pagamento não foi concluído e nada foi cobrado. Confira os dados e tente de novo, ou escolha outra forma de pagamento.</Alert>
        )}
        {error && <Alert>{error}</Alert>}
        {doApp && achados > 0 && (
          <Alert tone="info">
            {achados === 1 ? 'O diagnóstico achou 1 correção esperando no seu PC.' : `O diagnóstico achou ${achados} correções esperando no seu PC.`}{' '}
            Depois do pagamento, abra Conta no app e toque em Sincronizar para liberar.
          </Alert>
        )}
      </div>

      {/* No celular o resumo (total e botao de pagar) vem logo depois do
          plano, nao no fim da pagina; no desktop fica fixo na coluna direita. */}
      <div className="grid gap-4 lg:grid-cols-[1.2fr_0.8fr] lg:items-start [&>*]:min-w-0">
        <div className="card p-6 lg:col-start-1">
          <p className="text-xs font-bold uppercase tracking-wide text-accent-ink">Seu plano</p>
          <h2 className="mt-1 font-display text-2xl font-bold text-ink-1">RKZFPS {plan.name}</h2>
          <p className="mt-1 text-sm text-ink-3">{plan.description}</p>

          {periodos.length > 1 && (
            <div role="radiogroup" aria-label="Período" className="mt-5 grid gap-2 sm:grid-cols-3">
              {periodos.map((p) => (
                <button
                  key={p.key} type="button" role="radio" aria-checked={p.key === plan.key}
                  onClick={() => setParams((atual) => { atual.set('plano', p.key); return atual }, { replace: true })}
                  className={cn('rounded-lg border px-4 py-3 text-left transition-colors',
                    p.key === plan.key ? 'border-accent bg-accent/10' : 'border-line hover:border-line-strong')}
                >
                  <p className="text-sm font-semibold capitalize text-ink-1">{PERIOD_LABEL[p.period] ?? p.period}</p>
                  <p className="text-xs text-ink-3">{money(p.price_cents)} por {p.days} dias</p>
                  {p.savings_percent > 0 && <p className="mt-0.5 text-xs font-semibold text-accent-ink">Economia de {p.savings_percent}%</p>}
                </button>
              ))}
            </div>
          )}

          <ul className="mt-5 space-y-2 text-sm text-ink-2">
            {plan.features.map((f) => <li key={f} className="flex gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-accent" aria-hidden />{f}</li>)}
          </ul>
        </div>

        <aside className="card h-fit p-6 lg:col-start-2 lg:row-span-2 lg:row-start-1 lg:sticky lg:top-20">
          <p className="font-semibold text-ink-1">Resumo</p>
          <dl className="mt-4 space-y-2 text-sm">
            <div className="flex justify-between gap-3"><dt className="text-ink-3">Plano</dt><dd className="text-ink-1">{TIER_LABEL[plan.tier] ?? plan.name} {PERIOD_LABEL[plan.period] ?? ''}</dd></div>
            <div className="flex justify-between gap-3"><dt className="text-ink-3">Duração</dt><dd className="text-ink-1">{plan.days} dias</dd></div>
            <div className="flex justify-between gap-3"><dt className="text-ink-3">PCs</dt><dd className="text-ink-1">{plan.max_devices === 1 ? '1 PC' : `${plan.max_devices} PCs`}</dd></div>
            {quote && quote.percent_off > 0 && (
              <div className="flex justify-between gap-3"><dt className="text-ink-3">Cupom {quote.coupon}</dt><dd className="font-semibold text-accent-ink">-{quote.percent_off}%</dd></div>
            )}
          </dl>

          <form onSubmit={aplicarCupom} className="mt-5 flex gap-2">
            <input
              value={coupon} onChange={(e) => setCoupon(e.target.value.toUpperCase())} maxLength={40}
              placeholder="Cupom" aria-label="Cupom de desconto" className="input min-w-0 flex-1"
            />
            <Button type="submit" variant="ghost" size="sm" disabled={!coupon.trim()}>Aplicar</Button>
          </form>
          {couponMsg && <p className="mt-2 text-xs text-red-400">{couponMsg}</p>}

          <div className="mt-5 border-t border-line pt-4">
            <div className="flex items-baseline justify-between gap-3">
              <span className="text-sm text-ink-3">Total</span>
              <span className="font-display text-3xl font-extrabold text-ink-1">{quote ? money(quote.amount_cents) : '...'}</span>
            </div>
            <p className="mt-1 text-right text-xs text-ink-3">Pagamento único, sem renovação automática</p>
          </div>

          <Button className="mt-5" block size="lg" loading={busy} disabled={!quote} onClick={pagar} Icon={Lock}>
            Ir para o pagamento
          </Button>

          <ul className="mt-5 space-y-2.5 text-xs text-ink-3">
            {mesmoTierAtivo && (
              <li className="flex gap-2"><CalendarClock className="h-4 w-4 shrink-0 text-accent-ink" aria-hidden />Você já tem o {plan.name} ativo: os dias desta compra somam aos que restam.</li>
            )}
            <li className="flex gap-2"><Monitor className="h-4 w-4 shrink-0" aria-hidden />O plano ativa na hora da confirmação. No app, abra Conta e toque em Sincronizar.</li>
            <li className="flex gap-2"><Undo2 className="h-4 w-4 shrink-0" aria-hidden />7 dias para desistir e pedir o reembolso, como manda o Código de Defesa do Consumidor.</li>
            <li className="flex gap-2"><RotateCcw className="h-4 w-4 shrink-0" aria-hidden />Desfazer alterações continua liberado mesmo depois que o plano vence.</li>
          </ul>
        </aside>

        <div className="card p-6 lg:col-start-1">
          <p className="font-semibold text-ink-1">Formas de pagamento</p>
          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            <div className="flex items-center gap-3 rounded-lg border border-line p-4">
              <QrCode className="h-6 w-6 text-accent-ink" aria-hidden />
              <div><p className="text-sm font-semibold text-ink-1">PIX</p><p className="text-xs text-ink-3">Aprovado na hora</p></div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border border-line p-4">
              <CreditCard className="h-6 w-6 text-accent-ink" aria-hidden />
              <div><p className="text-sm font-semibold text-ink-1">Cartão de crédito</p><p className="text-xs text-ink-3">Na fatura aparece RKZFPS</p></div>
            </div>
          </div>
          <p className="mt-4 flex items-start gap-2 text-xs text-ink-3">
            <Lock className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
            Você escolhe a forma de pagamento na página do Mercado Pago. O RKZFPS não vê nem guarda os dados do seu cartão.
          </p>
        </div>
      </div>
    </PageShell>
  )
}
