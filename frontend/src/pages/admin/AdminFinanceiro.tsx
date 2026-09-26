import { useEffect, useState } from 'react'
import api, { errorMessage } from '../../services/api'
import { Alert, ErrorState, SkeletonRows, StatTile, Table } from '../../components/ui'
import { dateTime, money, planName, TIER_LABEL } from '../../lib/format'
import { AdminHead, PAY_STATUS, ReadOnly } from './AdminConfig'

/*
 * Financeiro, como a aba do Pickia: receita, ticket medio e assinantes ativos
 * por produto em cima; mes a mes e por plano embaixo; pagamentos e a trilha
 * de webhooks (a pergunta "o Mercado Pago chamou?" vira consulta).
 */

interface Finance {
  total_cents: number; count: number; avg_ticket_cents: number; last_30d_cents: number; last_30d_count: number
  monthly: { month: string; total_cents: number; count: number }[]
  by_plan: { plan_key: string; total_cents: number; count: number }[]
  coupons: { coupon_code: string; count: number; total_cents: number }[]
  subscribers_by_tier: { tier: string; n: number }[]
}

const MESES = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']
const mes = (ym: string) => { const [y, m] = ym.split('-'); return `${MESES[Number(m) - 1]}/${y.slice(2)}` }

export default function AdminFinanceiro() {
  const [f, setF] = useState<Finance | null>(null)
  const [error, setError] = useState('')
  const load = () => { setError(''); api.get<Finance>('/admin/finance').then(({ data }) => setF(data)).catch((e) => setError(errorMessage(e))) }
  useEffect(load, [])

  if (error) return <ErrorState description={error} onRetry={load} />
  if (!f) return <SkeletonRows rows={5} />

  const ativos = f.subscribers_by_tier.reduce((s, t) => s + t.n, 0)
  return (
    <div className="space-y-8">
      <div className="grid gap-3 grid-cols-2 lg:grid-cols-5">
        <StatTile label="Receita total" value={money(f.total_cents)} tone="green" />
        <StatTile label="Receita 30 dias" value={money(f.last_30d_cents)} hint={`${f.last_30d_count} pagamentos`} />
        <StatTile label="Pagamentos" value={f.count} />
        <StatTile label="Ticket médio" value={money(f.avg_ticket_cents)} />
        <StatTile label="Assinantes ativos" value={ativos}
                  hint={f.subscribers_by_tier.map((t) => `${TIER_LABEL[t.tier] ?? t.tier} ${t.n}`).join(', ') || 'nenhum'} />
      </div>

      {f.count === 0 && <Alert tone="info">Nenhum pagamento aprovado ainda. Os números aparecem aqui assim que o Mercado Pago confirmar a primeira venda.</Alert>}

      <div className="grid gap-6 lg:grid-cols-2">
        <div>
          <AdminHead title="Mês a mês" sub="Últimos 12 meses, só pagamentos aprovados." />
          <Table<Finance['monthly'][number]>
            rows={f.monthly}
            rowKey={(r) => r.month}
            minWidth={320}
            columns={[
              { key: 'mes', header: 'Mês', cell: (r) => mes(r.month) },
              { key: 'n', header: 'Vendas', align: 'right', cell: (r) => <span className="font-mono">{r.count}</span> },
              { key: 'v', header: 'Receita', align: 'right', cell: (r) => <span className="font-mono text-ink-1">{money(r.total_cents)}</span> },
            ]}
          />
        </div>
        <div>
          <AdminHead title="Por plano" />
          <Table<Finance['by_plan'][number]>
            rows={f.by_plan}
            rowKey={(r) => r.plan_key}
            minWidth={320}
            columns={[
              { key: 'p', header: 'Plano', cell: (r) => planName(r.plan_key) },
              { key: 'n', header: 'Vendas', align: 'right', cell: (r) => <span className="font-mono">{r.count}</span> },
              { key: 'v', header: 'Receita', align: 'right', cell: (r) => <span className="font-mono text-ink-1">{money(r.total_cents)}</span> },
            ]}
          />
          {f.coupons.length > 0 && (
            <p className="mt-3 text-sm text-ink-3">
              Cupons usados: {f.coupons.map((c) => `${c.coupon_code} (${c.count}, ${money(c.total_cents)})`).join(', ')}.
            </p>
          )}
        </div>
      </div>

      <div>
        <AdminHead title="Pagamentos" />
        <ReadOnly path="/admin/payments" keyName="payments" head={['ID', 'Conta', 'Plano', 'Valor', 'Status', 'Data']}
          row={(p) => [String(p.id), String(p.email ?? ''), planName(String(p.plan_key)), money(Number(p.amount_cents)), PAY_STATUS[String(p.status)] ?? String(p.status), dateTime(String(p.created_at))]} />
      </div>
      <div>
        <AdminHead title="Webhooks e eventos de pagamento" sub="Toda notificação do Mercado Pago, aceita ou recusada." />
        <ReadOnly path="/admin/payment-events" keyName="events" head={['Data', 'Origem', 'Status', 'Pagamento', 'Detalhe']}
          row={(e) => [dateTime(String(e.created_at)), String(e.source), String(e.status), String(e.provider_payment_id), String(e.detail)]} />
      </div>
    </div>
  )
}
