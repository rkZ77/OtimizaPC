import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { AnimatePresence } from 'framer-motion'
import { Download, Gauge, History, Laptop, LogOut } from 'lucide-react'
import api, { errorMessage, type Overview } from '../services/api'
import { useAuth } from '../context/AuthContext'
import PageShell from '../components/PageShell'
import AreaConta from '../components/AreaConta'
import {
  Alert, Badge, Button, EmptyState, ErrorState, Modal, ModalFooter, Panel, PanelHead, PanelList, PanelRow,
  SkeletonRows, Table, Tabs,
} from '../components/ui'
import { date, dateTime } from '../lib/format'
import { libera } from '../lib/features'

/*
 * PCs e medicoes: o detalhe tecnico da conta.
 *
 * O cartao do plano e os pagamentos sairam daqui: o plano esta' no Inicio
 * (painel) e em Meu plano, e os pagamentos em Meu plano. Repetidos em duas
 * telas, a pessoa nao sabia qual era a certa. A rota continua /conta porque
 * o app abre ela para desativar PC.
 */

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

type Aba = 'pcs' | 'benchmarks' | 'historico'

export default function Conta() {
  const { logout } = useAuth()
  const navigate = useNavigate()
  const [data, setData] = useState<Overview | null>(null)
  const [error, setError] = useState('')
  const [aba, setAba] = useState<Aba>('pcs')
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
    if (aba === 'benchmarks' && !benchmarks) api.get<{ benchmarks: Bench[] }>('/account/benchmarks').then(({ data }) => setBenchmarks(data.benchmarks)).catch(() => setBenchmarks([]))
    if (aba === 'historico' && !events) api.get<{ events: Evento[] }>('/account/history').then(({ data }) => setEvents(data.events)).catch(() => setEvents([]))
  }, [aba, benchmarks, events])

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
    title: 'PCs e medições',
    sub: data ? `${data.user.email}, cliente desde ${date(data.user.created_at)}` : undefined,
    actions: <Button variant="ghost" size="sm" Icon={LogOut} onClick={sair}>Sair</Button>,
  }

  if (!data) {
    return (
      <PageShell title="PCs e medições" noindex width="wide" bar={bar} beforeMain={<AreaConta />}>
        {error ? <ErrorState description={error} onRetry={load} /> : <SkeletonRows rows={4} />}
      </PageShell>
    )
  }

  const lic = data.license
  // Benchmark e relatorios sao do Pro: abaixo dele a aba nem aparece (e o
  // servidor tambem nao devolve os resultados).
  const benchLiberado = libera(lic, 'pro')
  const abaAtual: Aba = aba === 'benchmarks' && !benchLiberado ? 'pcs' : aba

  return (
    <PageShell title="PCs e medições" noindex width="wide" bar={bar} beforeMain={<AreaConta />}>
      {error && <div className="mb-6"><Alert>{error}</Alert></div>}

      <Tabs<Aba> value={abaAtual} onChange={setAba} items={[
        { key: 'pcs', label: 'Meus PCs', count: data.devices.length },
        ...(benchLiberado ? [{ key: 'benchmarks' as const, label: 'Benchmarks' }] : []),
        { key: 'historico', label: 'Histórico' },
      ]} />

      <div className="mt-5">
        {abaAtual === 'pcs' && (data.devices.length === 0
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
        {abaAtual === 'pcs' && data.devices.length > 0 && (
          <div className="mt-4"><Button variant="ghost" size="sm" to="/download" Icon={Download}>Baixar o app em outro PC</Button></div>
        )}

        {abaAtual === 'benchmarks' && (!benchmarks ? <SkeletonRows rows={3} /> : benchmarks.length === 0
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

        {abaAtual === 'historico' && (!events ? <SkeletonRows rows={3} /> : events.length === 0
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
