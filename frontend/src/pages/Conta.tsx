import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { AnimatePresence } from 'framer-motion'
import { Download, Gauge, History, Laptop, LogOut, Receipt } from 'lucide-react'
import api, { errorMessage, type Overview } from '../services/api'
import { useAuth } from '../context/AuthContext'
import PageShell from '../components/PageShell'
import {
  Alert, Badge, Button, EmptyState, ErrorState, Modal, ModalFooter, Panel, PanelHead, PanelList, PanelRow,
  PlanBadge, SkeletonRows, StatTile, Table, Tabs,
} from '../components/ui'
import { date, dateTime, money, STATUS_LABEL, TIER_LABEL } from '../lib/format'

interface Payment { id: number; plan_key: string; status: string; amount_cents: number; coupon_code: string | null; created_at: string }
interface Bench { id: number; device_name: string; game_id: string; label: string; avg_fps: number; low1_fps: number; low01_fps: number; frametime_ms: number; created_at: string }
interface Evento { event: string; optimization_id: string | null; success: boolean | null; created_at: string; device_name: string }

const EVENT_LABEL: Record<string, string> = {
  scan_completed: 'Análise concluída',
  optimization_applied: 'Otimização aplicada',
  optimization_failed: 'Otimização falhou',
  rollback: 'Alteração desfeita',
  fix_applied: 'Problema resolvido',
  benchmark_result: 'Benchmark',
  agent_error: 'Erro no app',
}

type Aba = 'pcs' | 'pagamentos' | 'benchmarks' | 'historico'

export default function Conta() {
  const { logout } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [data, setData] = useState<Overview | null>(null)
  const [error, setError] = useState('')
  const [aba, setAba] = useState<Aba>('pcs')
  const [payments, setPayments] = useState<Payment[] | null>(null)
  const [benchmarks, setBenchmarks] = useState<Bench[] | null>(null)
  const [events, setEvents] = useState<Evento[] | null>(null)
  const [confirmar, setConfirmar] = useState<{ id: number; name: string } | null>(null)
  const [removing, setRemoving] = useState(false)

  const load = useCallback(() => {
    setError('')
    api.get<Overview>('/account/overview').then(({ data }) => setData(data)).catch((e) => setError(errorMessage(e)))
  }, [])

  useEffect(load, [load])

  useEffect(() => {
    // Cada aba carrega so' quando aberta: a maioria das visitas e' so' a de PCs.
    if (aba === 'pagamentos' && !payments) api.get<{ payments: Payment[] }>('/account/payments').then(({ data }) => setPayments(data.payments)).catch(() => setPayments([]))
    if (aba === 'benchmarks' && !benchmarks) api.get<{ benchmarks: Bench[] }>('/account/benchmarks').then(({ data }) => setBenchmarks(data.benchmarks)).catch(() => setBenchmarks([]))
    if (aba === 'historico' && !events) api.get<{ events: Evento[] }>('/account/history').then(({ data }) => setEvents(data.events)).catch(() => setEvents([]))
  }, [aba, payments, benchmarks, events])

  const desativar = async () => {
    if (!confirmar) return
    setRemoving(true)
    try {
      await api.post(`/account/devices/${confirmar.id}/deactivate`)
      setConfirmar(null)
      load()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setRemoving(false)
    }
  }

  const sair = async () => {
    await logout()
    navigate('/')
  }

  const bar = {
    title: 'Minha conta',
    sub: data?.user.email,
    actions: <Button variant="ghost" size="sm" Icon={LogOut} onClick={sair}>Sair</Button>,
  }

  if (!data) {
    return (
      <PageShell title="Minha conta" noindex width="wide" bar={bar}>
        {error ? <ErrorState description={error} onRetry={load} /> : <SkeletonRows rows={4} />}
      </PageShell>
    )
  }

  const lic = data.license
  const pagamento = params.get('pagamento')

  return (
    <PageShell title="Minha conta" noindex width="wide" bar={bar}>
      <div className="space-y-3 mb-6">
        {pagamento === 'aprovado' && <Alert tone="ok">Pagamento aprovado. Seu plano já está ativo: no app, abra Conta e toque em Sincronizar.</Alert>}
        {pagamento === 'pendente' && <Alert tone="warn">Pagamento em processamento. Assim que o Mercado Pago confirmar, seu plano é ativado automaticamente.</Alert>}
        {params.get('bemvindo') && <Alert tone="info">Conta criada. Baixe o app, entre com este e-mail e o período de teste começa no seu PC.</Alert>}
        {error && <Alert>{error}</Alert>}
      </div>

      <div className="card p-6">
        <div className="flex flex-wrap items-center gap-3">
          <p className="font-display text-2xl font-bold text-ink-1">Plano {TIER_LABEL[lic.tier] ?? data.plan_name}</p>
          <PlanBadge tier={lic.tier} status={lic.status} />
          {lic.status !== 'trial' && <Badge tone={lic.status === 'active' ? 'green' : lic.status === 'free' ? 'neutral' : 'red'}>{STATUS_LABEL[lic.status] ?? lic.status}</Badge>}
        </div>
        <div className="mt-5 grid gap-3 grid-cols-2 sm:grid-cols-3">
          <StatTile label="Validade" value={lic.expires_at ? date(lic.expires_at) : 'Sem vencimento'} />
          <StatTile label="PCs em uso" value={`${data.devices.length} de ${lic.max_devices}`} />
          <StatTile label="Cliente desde" value={date(data.user.created_at)} tone="muted" />
        </div>
        <div className="mt-5 flex flex-wrap gap-3">
          <Button to="/planos">{lic.status === 'active' ? 'Renovar ou mudar de plano' : 'Ver planos'}</Button>
          <Button variant="ghost" to="/download" Icon={Download}>Baixar o app</Button>
        </div>
      </div>

      <div className="mt-10">
        <Tabs<Aba> value={aba} onChange={setAba} items={[
          { key: 'pcs', label: 'Meus PCs', count: data.devices.length },
          { key: 'pagamentos', label: 'Pagamentos' },
          { key: 'benchmarks', label: 'Benchmarks' },
          { key: 'historico', label: 'Histórico' },
        ]} />

        <div className="mt-5">
          {aba === 'pcs' && (data.devices.length === 0
            ? <EmptyState Icon={Laptop} title="Nenhum PC ativado ainda" description="Instale o app e entre com esta conta na tela Conta." action={{ children: 'Baixar o app', to: '/download' }} />
            : (
              <Panel>
                <PanelHead label="PCs ativos" meta={`${data.devices.length} de ${lic.max_devices}`} />
                <PanelList>
                  {data.devices.map((d) => (
                    <PanelRow key={d.id}>
                      <Laptop className="w-5 h-5 shrink-0 text-ink-3" aria-hidden />
                      <div className="flex-1 min-w-0">
                        <p className="font-semibold text-ink-1 truncate">{d.name || 'PC sem nome'}</p>
                        <p className="text-xs text-ink-3">Visto em {dateTime(d.last_seen_at)}. Windows {d.windows_build || '?'}, app {d.agent_version || '?'}.</p>
                      </div>
                      <Button variant="danger" size="sm" onClick={() => setConfirmar({ id: d.id, name: d.name || 'este PC' })}>Desativar</Button>
                    </PanelRow>
                  ))}
                </PanelList>
              </Panel>
            ))}

          {aba === 'pagamentos' && (!payments ? <SkeletonRows rows={3} /> : payments.length === 0
            ? <EmptyState Icon={Receipt} title="Nenhum pagamento ainda" action={{ children: 'Ver planos', to: '/planos' }} />
            : (
              <Table<Payment>
                rows={payments}
                rowKey={(p) => String(p.id)}
                columns={[
                  { key: 'plano', header: 'Plano', cell: (p) => <span className="text-ink-1">{TIER_LABEL[p.plan_key] ?? p.plan_key}{p.coupon_code ? `, cupom ${p.coupon_code}` : ''}</span> },
                  { key: 'data', header: 'Data', cell: (p) => dateTime(p.created_at), hideOnMobile: true },
                  { key: 'valor', header: 'Valor', align: 'right', cell: (p) => <span className="font-mono">{money(p.amount_cents)}</span> },
                  { key: 'status', header: 'Status', align: 'right', cell: (p) => <Badge tone={p.status === 'approved' ? 'green' : 'amber'}>{p.status === 'approved' ? 'Aprovado' : p.status}</Badge> },
                ]}
              />
            ))}

          {aba === 'benchmarks' && (!benchmarks ? <SkeletonRows rows={3} /> : benchmarks.length === 0
            ? <EmptyState Icon={Gauge} title="Nenhum benchmark enviado" description="Os resultados aparecem aqui quando você mede no app com o envio de dados permitido." />
            : (
              <Table<Bench>
                rows={benchmarks}
                rowKey={(b) => String(b.id)}
                minWidth={560}
                columns={[
                  { key: 'data', header: 'Data', cell: (b) => dateTime(b.created_at) },
                  { key: 'pc', header: 'PC', cell: (b) => b.device_name, hideOnMobile: true },
                  { key: 'rotulo', header: 'Rótulo', cell: (b) => b.label },
                  { key: 'fps', header: 'FPS médio', align: 'right', cell: (b) => <span className="font-mono font-semibold text-ink-1">{b.avg_fps.toFixed(1)}</span> },
                  { key: 'low', header: '1% low', align: 'right', cell: (b) => <span className="font-mono">{b.low1_fps.toFixed(1)}</span> },
                  { key: 'ft', header: 'Frametime', align: 'right', cell: (b) => <span className="font-mono">{b.frametime_ms.toFixed(2)} ms</span>, hideOnMobile: true },
                ]}
              />
            ))}

          {aba === 'historico' && (!events ? <SkeletonRows rows={3} /> : events.length === 0
            ? <EmptyState Icon={History} title="Sem histórico no site" description="O histórico completo, com desfazer, fica no app. Aqui aparecem os eventos enviados com sua permissão." />
            : (
              <Panel>
                <PanelList>
                  {events.map((ev, i) => (
                    <PanelRow key={i}>
                      <div className="flex-1 min-w-0">
                        <p className="text-sm text-ink-1">{EVENT_LABEL[ev.event] ?? ev.event}{ev.optimization_id ? `: ${ev.optimization_id}` : ''}</p>
                        <p className="text-xs text-ink-3">{ev.device_name}, {dateTime(ev.created_at)}</p>
                      </div>
                      {ev.success === false && <Badge tone="red">Falhou</Badge>}
                    </PanelRow>
                  ))}
                </PanelList>
              </Panel>
            ))}
        </div>
      </div>

      <AnimatePresence>
        {confirmar && (
          <Modal onClose={() => setConfirmar(null)} title={`Desativar ${confirmar.name}?`} width="sm">
            <p className="text-sm text-ink-2">
              O app daquele PC volta ao plano Free na próxima verificação, e a vaga fica livre para outro PC.
              As alterações já feitas naquele PC continuam podendo ser desfeitas.
            </p>
            <ModalFooter>
              <Button variant="ghost" onClick={() => setConfirmar(null)}>Cancelar</Button>
              <Button variant="danger" loading={removing} onClick={desativar}>Desativar</Button>
            </ModalFooter>
          </Modal>
        )}
      </AnimatePresence>
    </PageShell>
  )
}
