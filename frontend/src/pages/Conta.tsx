import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Laptop, LogOut } from 'lucide-react'
import api, { errorMessage, type Overview } from '../services/api'
import { useAuth } from '../context/AuthContext'
import { PageShell, PageTitle, Tabs } from '../components/Layout'
import { Alert, Badge, Button, Card, EmptyState, Spinner } from '../components/ui'
import { date, dateTime, money, STATUS_LABEL, TIER_LABEL } from '../lib/format'

interface Payment { id: number; plan_key: string; status: string; amount_cents: number; coupon_code: string | null; created_at: string }
interface Bench { id: number; device_name: string; game_id: string; label: string; avg_fps: number; low1_fps: number; low01_fps: number; frametime_ms: number; created_at: string }
interface Event { event: string; optimization_id: string | null; success: boolean | null; created_at: string; device_name: string }

const EVENT_LABEL: Record<string, string> = {
  scan_completed: 'Análise concluída',
  optimization_applied: 'Otimização aplicada',
  optimization_failed: 'Otimização falhou',
  rollback: 'Alteração desfeita',
  fix_applied: 'Problema resolvido',
  benchmark_result: 'Benchmark',
  agent_error: 'Erro no app',
}

type Tab = 'pcs' | 'pagamentos' | 'benchmarks' | 'historico'

export default function Conta() {
  const { logout } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [data, setData] = useState<Overview | null>(null)
  const [error, setError] = useState('')
  const [tab, setTab] = useState<Tab>('pcs')
  const [payments, setPayments] = useState<Payment[] | null>(null)
  const [benchmarks, setBenchmarks] = useState<Bench[] | null>(null)
  const [events, setEvents] = useState<Event[] | null>(null)
  const [removing, setRemoving] = useState<number | null>(null)

  const load = useCallback(() => {
    api.get<Overview>('/account/overview').then(({ data }) => setData(data)).catch((e) => setError(errorMessage(e)))
  }, [])

  useEffect(load, [load])

  useEffect(() => {
    // Carrega cada aba so' quando aberta: a maioria das visitas e' so' a de PCs.
    if (tab === 'pagamentos' && !payments) api.get<{ payments: Payment[] }>('/account/payments').then(({ data }) => setPayments(data.payments)).catch(() => setPayments([]))
    if (tab === 'benchmarks' && !benchmarks) api.get<{ benchmarks: Bench[] }>('/account/benchmarks').then(({ data }) => setBenchmarks(data.benchmarks)).catch(() => setBenchmarks([]))
    if (tab === 'historico' && !events) api.get<{ events: Event[] }>('/account/history').then(({ data }) => setEvents(data.events)).catch(() => setEvents([]))
  }, [tab, payments, benchmarks, events])

  const deactivate = async (id: number, name: string) => {
    if (!window.confirm(`Desativar ${name}? O app daquele PC volta ao plano Free na próxima verificação, e a vaga fica livre para outro PC.`)) return
    setRemoving(id)
    try {
      await api.post(`/account/devices/${id}/deactivate`)
      load()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setRemoving(null)
    }
  }

  const sair = async () => {
    await logout()
    navigate('/')
  }

  if (!data) return <PageShell>{error ? <Alert>{error}</Alert> : <div className="flex justify-center py-24"><Spinner className="w-6 h-6" /></div>}</PageShell>

  const lic = data.license
  const status = lic.status
  const payment = params.get('pagamento')

  return (
    <PageShell>
      <PageTitle title="Minha conta" subtitle={data.user.email} />
      {payment === 'aprovado' && <div className="mb-6"><Alert tone="ok">Pagamento aprovado. Seu plano já está ativo: no app, abra Conta e toque em Sincronizar.</Alert></div>}
      {payment === 'pendente' && <div className="mb-6"><Alert tone="warn">Pagamento em processamento. Assim que o Mercado Pago confirmar, seu plano é ativado automaticamente.</Alert></div>}
      {params.get('bemvindo') && <div className="mb-6"><Alert tone="info">Conta criada. Baixe o app, entre com este e-mail e o período de teste começa no seu PC.</Alert></div>}
      {error && <div className="mb-6"><Alert>{error}</Alert></div>}

      <div className="grid gap-4 md:grid-cols-3">
        <Card className="md:col-span-2">
          <div className="flex flex-wrap items-center gap-3">
            <p className="text-2xl font-bold text-ink-1">Plano {TIER_LABEL[lic.tier] ?? data.plan_name}</p>
            <Badge tone={status === 'active' ? 'ok' : status === 'trial' ? 'info' : status === 'free' ? 'muted' : 'danger'}>{STATUS_LABEL[status] ?? status}</Badge>
          </div>
          <p className="mt-2 text-ink-3">
            {lic.expires_at ? `Válido até ${date(lic.expires_at)}.` : 'Sem vencimento.'} {data.devices.length} de {lic.max_devices} PC(s) em uso.
          </p>
          <div className="mt-5 flex flex-wrap gap-3">
            <Button to="/planos">{status === 'active' ? 'Renovar ou mudar de plano' : 'Ver planos'}</Button>
            <Button variant="ghost" to="/download">Baixar o app</Button>
          </div>
        </Card>
        <Card>
          <p className="font-semibold text-ink-1">{data.user.name || 'Sua conta'}</p>
          <p className="text-sm text-ink-3">Cliente desde {date(data.user.created_at)}</p>
          <Button className="mt-5" variant="ghost" icon={LogOut} onClick={sair}>Sair</Button>
        </Card>
      </div>

      <div className="mt-10">
        <Tabs<Tab> value={tab} onChange={setTab} tabs={[
          { id: 'pcs', label: 'Meus PCs' },
          { id: 'pagamentos', label: 'Pagamentos' },
          { id: 'benchmarks', label: 'Benchmarks' },
          { id: 'historico', label: 'Histórico' },
        ]} />

        {tab === 'pcs' && (data.devices.length === 0
          ? <EmptyState title="Nenhum PC ativado ainda.">Instale o app e entre com esta conta na tela Conta.</EmptyState>
          : (
            <ul className="space-y-3">
              {data.devices.map((d) => (
                <li key={d.id} className="flex flex-col sm:flex-row sm:items-center gap-3 rounded-xl border border-line bg-surface-1 p-4">
                  <Laptop className="w-6 h-6 text-ink-3" aria-hidden />
                  <div className="flex-1">
                    <p className="font-semibold text-ink-1">{d.name || 'PC sem nome'}</p>
                    <p className="text-sm text-ink-3">Visto por último em {dateTime(d.last_seen_at)}. Windows build {d.windows_build || '?'}, app {d.agent_version || '?'}.</p>
                  </div>
                  <Button variant="danger" size="sm" loading={removing === d.id} onClick={() => deactivate(d.id, d.name || 'este PC')}>Desativar</Button>
                </li>
              ))}
            </ul>
          ))}

        {tab === 'pagamentos' && (!payments ? <Spinner /> : payments.length === 0
          ? <EmptyState title="Nenhum pagamento ainda." />
          : (
            <ul className="divide-y divide-line rounded-xl border border-line bg-surface-1">
              {payments.map((p) => (
                <li key={p.id} className="flex flex-wrap justify-between gap-2 p-4 text-sm">
                  <span className="text-ink-1">Plano {TIER_LABEL[p.plan_key] ?? p.plan_key}{p.coupon_code ? `, cupom ${p.coupon_code}` : ''}</span>
                  <span className="text-ink-3">{dateTime(p.created_at)}</span>
                  <span className="font-semibold text-ink-1">{money(p.amount_cents)}</span>
                  <Badge tone={p.status === 'approved' ? 'ok' : 'warn'}>{p.status === 'approved' ? 'Aprovado' : p.status}</Badge>
                </li>
              ))}
            </ul>
          ))}

        {tab === 'benchmarks' && (!benchmarks ? <Spinner /> : benchmarks.length === 0
          ? <EmptyState title="Nenhum benchmark enviado.">Os resultados aparecem aqui quando você mede no app com o envio de dados permitido.</EmptyState>
          : (
            <div className="overflow-x-auto rounded-xl border border-line">
              <table className="w-full min-w-[560px] text-sm">
                <thead className="bg-surface-1 text-left text-ink-1">
                  <tr><th className="p-3">Data</th><th className="p-3">PC</th><th className="p-3">Rótulo</th><th className="p-3">FPS médio</th><th className="p-3">1% low</th><th className="p-3">Frametime</th></tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {benchmarks.map((b) => (
                    <tr key={b.id}>
                      <td className="p-3 text-ink-3">{dateTime(b.created_at)}</td>
                      <td className="p-3">{b.device_name}</td>
                      <td className="p-3">{b.label}</td>
                      <td className="p-3 font-semibold text-ink-1">{b.avg_fps.toFixed(1)}</td>
                      <td className="p-3">{b.low1_fps.toFixed(1)}</td>
                      <td className="p-3">{b.frametime_ms.toFixed(2)} ms</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}

        {tab === 'historico' && (!events ? <Spinner /> : events.length === 0
          ? <EmptyState title="Sem histórico no site.">O histórico completo, com desfazer, fica no app. Aqui aparecem os eventos enviados com sua permissão.</EmptyState>
          : (
            <ul className="divide-y divide-line rounded-xl border border-line bg-surface-1">
              {events.map((ev, i) => (
                <li key={i} className="flex flex-wrap justify-between gap-2 p-4 text-sm">
                  <span className="text-ink-1">{EVENT_LABEL[ev.event] ?? ev.event}{ev.optimization_id ? `: ${ev.optimization_id}` : ''}</span>
                  <span className="text-ink-3">{ev.device_name}, {dateTime(ev.created_at)}</span>
                </li>
              ))}
            </ul>
          ))}
      </div>
    </PageShell>
  )
}
