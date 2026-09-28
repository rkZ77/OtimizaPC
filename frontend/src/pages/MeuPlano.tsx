import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ArrowRight, CalendarClock, Check, Crown, Laptop, Lock, Receipt, RotateCcw } from 'lucide-react'
import api, { errorMessage, type Overview, type Plan, type PlansResponse } from '../services/api'
import PageShell from '../components/PageShell'
import {
  Alert, Badge, Button, EmptyState, ErrorState, PlanBadge, SkeletonRows, StatTile, Table,
} from '../components/ui'
import { FEATURES, PLAN_ORDER, includes } from '../lib/features'
import { date, dateTime, money, paymentStatus, planName, STATUS_LABEL, TIER_LABEL } from '../lib/format'

/*
 * Meu plano: a pagina de quem ja' e' cliente. Responde o que a pessoa vem
 * procurar (ate' quando vale, o que libera, quanto pagou) e mostra o proximo
 * passo sem empurrar: renovar o mesmo plano ou ver o que o proximo acrescenta.
 *
 * Nao existe cobranca recorrente: cada compra e' um pagamento unico de N dias
 * (preferencia do Mercado Pago), entao aqui nao ha' "cancelar assinatura".
 */

interface Payment { id: number; plan_key: string; status: string; amount_cents: number; coupon_code: string | null; created_at: string }

const DIA_MS = 86_400_000

function diasRestantes(expires: string | null): number | null {
  if (!expires) return null
  return Math.max(0, Math.ceil((new Date(expires).getTime() - Date.now()) / DIA_MS))
}

export default function MeuPlano() {
  const [params] = useSearchParams()
  const [data, setData] = useState<Overview | null>(null)
  const [plans, setPlans] = useState<Plan[]>([])
  const [payments, setPayments] = useState<Payment[] | null>(null)
  const [error, setError] = useState('')

  const load = () => {
    setError('')
    api.get<Overview>('/account/overview').then(({ data }) => setData(data)).catch((e) => setError(errorMessage(e)))
  }

  useEffect(() => {
    load()
    api.get<PlansResponse>('/public/plans').then(({ data }) => setPlans(data.plans)).catch(() => setPlans([]))
    api.get<{ payments: Payment[] }>('/account/payments').then(({ data }) => setPayments(data.payments)).catch(() => setPayments([]))
  }, [])

  const bar = { title: 'Meu plano', sub: 'Validade, o que seu plano libera e seus pagamentos.' }

  if (!data) {
    return (
      <PageShell title="Meu plano" noindex width="wide" bar={bar}>
        {error ? <ErrorState description={error} onRetry={load} /> : <SkeletonRows rows={4} />}
      </PageShell>
    )
  }

  const lic = data.license
  const tier = lic.tier === 'custom' ? 'ultimate' : lic.tier
  const pago = lic.status === 'active' && tier !== 'free'
  const dias = diasRestantes(lic.expires_at)
  const planoAtual = plans.find((p) => p.key === lic.plan_key)
  // Renovar o mesmo plano: o que a pessoa ja' tem, ou o mensal do mesmo tier.
  const renovar = planoAtual && planoAtual.price_cents > 0 ? planoAtual : plans.find((p) => p.tier === tier && p.period === 'monthly')
  const proximoTier = PLAN_ORDER[PLAN_ORDER.indexOf(tier as (typeof PLAN_ORDER)[number]) + 1]
  // Proximo tier no mesmo periodo que a pessoa ja' usa; sem periodo, o mensal.
  const periodo = planoAtual && planoAtual.period !== 'none' ? planoAtual.period : 'monthly'
  const doProximo = (per: string) => plans.find((p) => p.tier === proximoTier && p.period === per)
  const proximo = proximoTier ? doProximo(periodo) ?? doProximo('monthly') : undefined
  const libera = FEATURES.filter((f) => includes(tier, f.plan))
  const acrescenta = proximoTier ? FEATURES.filter((f) => f.plan === proximoTier) : []
  const pagamento = params.get('pagamento')

  return (
    <PageShell title="Meu plano" noindex width="wide" bar={bar}>
      <div className="space-y-3 mb-6">
        {pagamento === 'aprovado' && <Alert tone="ok">Pagamento aprovado. Seu plano já está ativo: no app, abra Conta e toque em Sincronizar.</Alert>}
        {pagamento === 'pendente' && <Alert tone="warn">Pagamento em processamento. Assim que o Mercado Pago confirmar, seu plano é ativado sozinho e você recebe um e-mail.</Alert>}
        {lic.status === 'trial' && dias !== null && (
          <Alert tone="info">Você está no teste grátis do plano {TIER_LABEL[tier]}. Faltam {dias} {dias === 1 ? 'dia' : 'dias'}.</Alert>
        )}
        {pago && dias !== null && dias <= 7 && (
          <Alert tone="warn">Seu plano vence em {dias} {dias === 1 ? 'dia' : 'dias'}. Não há renovação automática: renove para não perder as correções pagas.</Alert>
        )}
        {lic.status === 'expired' && <Alert tone="warn">Seu plano venceu. O app voltou ao Free, e desfazer continua liberado.</Alert>}
      </div>

      <div className="grid gap-4 lg:grid-cols-[1.3fr_0.7fr] lg:items-start [&>*]:min-w-0">
        <div className="card p-6">
          <div className="flex flex-wrap items-center gap-3">
            <Crown className="h-7 w-7 text-accent-ink" aria-hidden />
            <p className="font-display text-2xl font-bold text-ink-1">Plano {TIER_LABEL[lic.tier] ?? data.plan_name}</p>
            <PlanBadge tier={lic.tier} status={lic.status} />
            {lic.status !== 'trial' && (
              <Badge tone={lic.status === 'active' ? 'green' : lic.status === 'free' ? 'neutral' : 'red'}>{STATUS_LABEL[lic.status] ?? lic.status}</Badge>
            )}
          </div>
          <div className="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-3">
            <StatTile label="Vale até" value={lic.expires_at ? date(lic.expires_at) : 'Sem vencimento'} />
            <StatTile label="Dias restantes" value={dias === null ? 'Sem limite' : String(dias)} />
            <StatTile label="PCs em uso" value={`${data.devices.length} de ${lic.max_devices}`} />
          </div>
          <div className="mt-5 flex flex-wrap gap-3">
            {renovar && (lic.status !== 'free') && (
              <Button to={`/pagamento?plano=${renovar.key}`} Icon={CalendarClock}>{pago ? 'Renovar e somar dias' : lic.status === 'trial' ? `Assinar o ${TIER_LABEL[tier]}` : 'Renovar plano'}</Button>
            )}
            {lic.status === 'free' && <Button to="/planos">Ver planos</Button>}
            <Button variant="ghost" to="/conta" Icon={Laptop}>Gerenciar PCs</Button>
          </div>
          <p className="mt-4 flex items-start gap-2 text-xs text-ink-3">
            <RotateCcw className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
            Pagamento único, sem renovação automática. Renovar antes de vencer soma os dias ao que resta.
          </p>
        </div>

        <div className="card p-6">
          <p className="font-semibold text-ink-1">O que seu plano libera</p>
          <ul className="mt-4 space-y-2 text-sm text-ink-2">
            {libera.map((f) => <li key={f.label} className="flex gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-accent" aria-hidden />{f.label}</li>)}
          </ul>
        </div>
      </div>

      {proximo && acrescenta.length > 0 && (
        <div className="card mt-4 p-6">
          <p className="text-xs font-bold uppercase tracking-wide text-accent-ink">Próximo plano</p>
          <p className="mt-1 font-display text-xl font-bold text-ink-1">O {proximo.name} acrescenta</p>
          <ul className="mt-4 grid gap-2 text-sm text-ink-2 sm:grid-cols-2">
            {acrescenta.map((f) => <li key={f.label} className="flex gap-2"><Lock className="mt-0.5 h-4 w-4 shrink-0 text-ink-3" aria-hidden />{f.label}</li>)}
          </ul>
          <div className="mt-5 flex flex-wrap items-center gap-3">
            <Button to={`/pagamento?plano=${proximo.key}`} IconRight={ArrowRight}>Mudar para o {proximo.name}</Button>
            <span className="text-sm text-ink-3">{money(proximo.per_month_cents)} por mês</span>
          </div>
        </div>
      )}

      <section id="pagamentos" className="mt-10 scroll-mt-20">
        <h2 className="mb-4 font-display text-xl font-bold text-ink-1">Pagamentos</h2>
        {!payments ? <SkeletonRows rows={3} /> : payments.length === 0
          ? <EmptyState Icon={Receipt} title="Nenhum pagamento ainda" action={{ children: 'Ver planos', to: '/planos' }} />
          : (
            <Table<Payment>
              rows={payments}
              rowKey={(p) => String(p.id)}
              columns={[
                { key: 'plano', header: 'Plano', cell: (p) => <span className="text-ink-1">{planName(p.plan_key)}{p.coupon_code ? `, cupom ${p.coupon_code}` : ''}</span> },
                { key: 'data', header: 'Data', cell: (p) => dateTime(p.created_at), hideOnMobile: true },
                { key: 'valor', header: 'Valor', align: 'right', cell: (p) => <span className="font-mono">{money(p.amount_cents)}</span> },
                { key: 'status', header: 'Status', align: 'right', cell: (p) => { const s = paymentStatus(p.status); return <Badge tone={s.tone}>{s.label}</Badge> } },
              ]}
            />
          )}
        <p className="mt-4 text-sm text-ink-3">Quer o reembolso? Você tem 7 dias da compra. Fale com o suporte pelo menu.</p>
      </section>
    </PageShell>
  )
}
