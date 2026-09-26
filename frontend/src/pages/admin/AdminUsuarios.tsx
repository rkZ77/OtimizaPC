import { useCallback, useEffect, useState } from 'react'
import { AnimatePresence } from 'framer-motion'
import { Laptop, Users } from 'lucide-react'
import api, { errorMessage } from '../../services/api'
import {
  Alert, Badge, Button, Drawer, EmptyState, ErrorState, Pagination, Panel, PanelHead, PanelList, PanelRow,
  PillGroup, SearchInput, SkeletonRows, StatTile, Table, type BadgeTone,
} from '../../components/ui'
import { date, dateTime, money, planName, STATUS_LABEL, TIER_LABEL } from '../../lib/format'

/*
 * Usuarios, no desenho da aba do Pickia: a QUEBRA DA BASE em cartoes em cima
 * (estoque de hoje), filtro por segmento, busca e lista paginada, e a ficha
 * de cada pessoa numa gaveta ao lado com as acoes que existem sobre ela.
 *
 * O segmento vem pronto do backend (mesma regra da licenca vigente que o app
 * usa): o front nao recalcula quem e' assinante.
 */

export type Segment = 'subscriber' | 'trial' | 'free' | 'expired' | 'blocked' | 'admin'

const SEG: Record<Segment, { label: string; tone: BadgeTone }> = {
  subscriber: { label: 'Assinante', tone: 'green' },
  trial:      { label: 'Teste', tone: 'amber' },
  free:       { label: 'Free', tone: 'neutral' },
  expired:    { label: 'Vencido', tone: 'red' },
  blocked:    { label: 'Bloqueado', tone: 'red' },
  admin:      { label: 'Admin', tone: 'purple' },
}

interface Stats {
  total: number; subscribers: number; trial: number; free: number; expired: number; blocked: number; admins: number
  expiring_7d: number; active_7d: number; new_7d: number
  subscribers_by_tier: { tier: string; n: number }[]
}

interface UserRow {
  id: number; email: string; name: string; role: string; active: boolean; created_at: string
  tier: string | null; plan_key: string | null; expires_at: string | null; eff_status: string | null
  devices: number; last_seen_at: string | null; segment: Segment
}

type Filtro = 'todos' | Segment | 'expiring'
const PAGE = 20

/** Aviso de vencimento proximo, como o expiryWarning do Pickia. */
function Vencimento({ iso, segment }: { iso: string | null; segment: Segment }) {
  if (!iso || (segment !== 'subscriber' && segment !== 'trial' && segment !== 'expired')) return <span className="text-ink-4">sem</span>
  const dias = Math.ceil((new Date(iso).getTime() - Date.now()) / 86400000)
  if (dias < 0) return <span className="text-xs font-semibold text-red-400">Venceu {date(iso)}</span>
  if (dias <= 7) return <span className="text-xs font-semibold text-orange-400">{dias}d ({date(iso)})</span>
  return <span>{date(iso)}</span>
}

export function SegmentBadge({ segment }: { segment: Segment }) {
  return <Badge tone={SEG[segment].tone}>{SEG[segment].label}</Badge>
}

export default function AdminUsuarios() {
  const [stats, setStats] = useState<Stats | null>(null)
  const [rows, setRows] = useState<UserRow[] | null>(null)
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [q, setQ] = useState('')
  const [filtro, setFiltro] = useState<Filtro>('todos')
  const [error, setError] = useState('')
  const [aberto, setAberto] = useState<number | null>(null)

  const loadStats = useCallback(() => {
    api.get<Stats>('/admin/users/stats').then(({ data }) => setStats(data)).catch((e) => setError(errorMessage(e)))
  }, [])

  const loadRows = useCallback(() => {
    const params = new URLSearchParams({ q, limit: String(PAGE), offset: String(page * PAGE) })
    if (filtro !== 'todos') params.set('segment', filtro)
    api.get<{ users: UserRow[]; total: number }>(`/admin/users?${params}`)
      .then(({ data }) => { setRows(data.users); setTotal(data.total) })
      .catch((e) => setError(errorMessage(e)))
  }, [q, filtro, page])

  useEffect(loadStats, [loadStats])
  useEffect(() => {
    // Busca com pequena espera: sem ela cada tecla virava uma consulta.
    const t = window.setTimeout(loadRows, 250)
    return () => window.clearTimeout(t)
  }, [loadRows])
  useEffect(() => { setPage(0) }, [q, filtro])

  const recarregar = () => { loadStats(); loadRows() }

  return (
    <div className="space-y-6">
      {error && <Alert>{error}</Alert>}

      {/* Quebra da base */}
      {!stats ? <SkeletonRows rows={2} /> : (
        <>
          <div className="grid gap-3 grid-cols-2 sm:grid-cols-4 lg:grid-cols-8">
            <StatTile label="Total" value={stats.total} hint={`${stats.new_7d} novos em 7 dias`} />
            <StatTile label="Assinantes" value={stats.subscribers} tone="green" />
            <StatTile label="Em teste" value={stats.trial} />
            <StatTile label="Free" value={stats.free} tone="muted" />
            <StatTile label="Vencidos" value={stats.expired} tone={stats.expired > 0 ? 'red' : 'muted'} />
            <StatTile label="Expirando em 7d" value={stats.expiring_7d} tone={stats.expiring_7d > 0 ? 'red' : 'muted'} />
            <StatTile label="Bloqueados" value={stats.blocked} tone="muted" />
            <StatTile label="Admins" value={stats.admins} tone="muted" />
          </div>
          {stats.subscribers_by_tier.length > 0 && (
            <p className="text-sm text-ink-3">
              Assinantes por plano:{' '}
              {stats.subscribers_by_tier.map((t, i) => (
                <span key={t.tier}>{i > 0 && ', '}<span className="font-semibold text-ink-1">{TIER_LABEL[t.tier] ?? t.tier}</span> {t.n}</span>
              ))}
              . Ativos no app nos últimos 7 dias: <span className="font-semibold text-ink-1">{stats.active_7d}</span>.
            </p>
          )}
        </>
      )}

      <div className="flex flex-col lg:flex-row gap-3 lg:items-center lg:justify-between">
        <PillGroup<Filtro>
          value={filtro}
          onChange={setFiltro}
          options={[
            { value: 'todos', label: 'Todos' },
            { value: 'subscriber', label: 'Assinantes' },
            { value: 'trial', label: 'Teste' },
            { value: 'expiring', label: 'Expirando' },
            { value: 'expired', label: 'Vencidos' },
            { value: 'free', label: 'Free' },
            { value: 'blocked', label: 'Bloqueados' },
            { value: 'admin', label: 'Admins' },
          ]}
        />
        <SearchInput value={q} onChange={setQ} placeholder="Buscar por e-mail ou nome" className="lg:w-72" label="Buscar usuário" />
      </div>

      {!rows ? <SkeletonRows rows={6} /> : rows.length === 0 ? (
        <EmptyState Icon={Users} title="Nenhum usuário neste filtro" compact />
      ) : (
        <>
          <Table<UserRow>
            rows={rows}
            rowKey={(u) => String(u.id)}
            onRowClick={(u) => setAberto(u.id)}
            minWidth={760}
            columns={[
              { key: 'conta', header: 'Conta', cell: (u) => (
                <div className="min-w-0">
                  <p className="font-semibold text-ink-1 truncate">{u.email}</p>
                  <p className="text-xs text-ink-3 truncate">{u.name || 'sem nome'}</p>
                </div>
              ) },
              { key: 'seg', header: 'Segmento', cell: (u) => <SegmentBadge segment={u.segment} /> },
              { key: 'plano', header: 'Plano', cell: (u) => u.tier ? (TIER_LABEL[u.tier] ?? u.tier) : 'Free' },
              { key: 'vence', header: 'Vence', cell: (u) => <Vencimento iso={u.expires_at} segment={u.segment} /> },
              { key: 'pcs', header: 'PCs', align: 'center', cell: (u) => <span className="font-mono">{u.devices}</span>, hideOnMobile: true },
              { key: 'visto', header: 'Visto no app', cell: (u) => u.last_seen_at ? dateTime(u.last_seen_at) : <span className="text-ink-4">nunca</span>, hideOnMobile: true },
              { key: 'desde', header: 'Desde', cell: (u) => date(u.created_at), hideOnMobile: true },
            ]}
          />
          <Pagination page={page} pageSize={PAGE} total={total} onChange={setPage} unit="usuários" />
        </>
      )}

      <AnimatePresence>
        {aberto !== null && <FichaUsuario id={aberto} onClose={() => setAberto(null)} onChanged={recarregar} />}
      </AnimatePresence>
    </div>
  )
}

// ─── Ficha do usuario (gaveta) ──────────────────────────────────────────

const EVENTO: Record<string, string> = {
  scan_completed: 'Análise concluída',
  optimization_applied: 'Otimização aplicada',
  optimization_failed: 'Otimização falhou',
  fix_applied: 'Problema resolvido',
  rollback: 'Alteração desfeita',
  benchmark_result: 'Benchmark',
  agent_error: 'Erro no app',
}

interface Ficha {
  user: UserRow
  licenses: { id: number; plan_key: string; tier: string; status: string; eff_status: string; max_devices: number; expires_at: string; blocked_reason: string | null }[]
  devices: { id: number; name: string; windows_build: string; agent_version: string; last_seen_at: string; deactivated_at: string | null }[]
  payments: { id: number; plan_key: string; status: string; amount_cents: number; coupon_code: string | null; created_at: string }[]
  events: { event: string; optimization_id: string | null; success: boolean | null; created_at: string; device_name: string }[]
}

function FichaUsuario({ id, onClose, onChanged }: { id: number; onClose: () => void; onChanged: () => void }) {
  const [f, setF] = useState<Ficha | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [plano, setPlano] = useState('pro')

  const load = useCallback(() => {
    api.get<Ficha>(`/admin/users/${id}`).then(({ data }) => setF(data)).catch((e) => setError(errorMessage(e)))
  }, [id])
  useEffect(load, [load])

  const agir = async (fn: () => Promise<unknown>) => {
    setBusy(true)
    setError('')
    try {
      await fn()
      load()
      onChanged()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const u = f?.user
  const vigente = f?.licenses.find((l) => l.eff_status === 'active' || l.eff_status === 'trial')

  return (
    <Drawer onClose={onClose} title={u?.email ?? 'Usuário'} description={u ? `${u.name || 'sem nome'}, conta criada em ${date(u.created_at)}` : undefined}>
      {error && <Alert className="mb-4">{error}</Alert>}
      {!f || !u ? (error ? <ErrorState onRetry={load} compact /> : <SkeletonRows rows={5} />) : (
        <div className="space-y-6">
          <div className="flex flex-wrap items-center gap-2">
            <SegmentBadge segment={u.segment} />
            {u.tier && <Badge tone="blue">{TIER_LABEL[u.tier] ?? u.tier}</Badge>}
            {!u.active && <Badge tone="red">Conta bloqueada</Badge>}
          </div>

          <div>
            <p className="label-micro mb-2">Ações</p>
            <div className="flex flex-wrap gap-2">
              <select value={plano} onChange={(e) => setPlano(e.target.value)} className="input !w-auto" aria-label="Plano a conceder">
                {['starter', 'pro', 'ultimate'].map((p) => <option key={p} value={p}>{TIER_LABEL[p]}</option>)}
              </select>
              <Button size="sm" loading={busy} onClick={() => agir(() => api.post('/admin/licenses/grant', { user_id: u.id, plan_key: plano }))}>Conceder plano</Button>
              {vigente && <Button size="sm" variant="subtle" disabled={busy} onClick={() => agir(() => api.post(`/admin/licenses/${vigente.id}/extend`, { days: 30 }))}>+30 dias</Button>}
              {vigente && (
                <Button size="sm" variant="danger" disabled={busy} onClick={() => {
                  const reason = window.prompt('Motivo do bloqueio da licença (fica na auditoria):')
                  if (reason !== null) agir(() => api.post(`/admin/licenses/${vigente.id}/block`, { blocked: true, reason }))
                }}>Bloquear licença</Button>
              )}
              <Button size="sm" variant={u.active ? 'danger' : 'ghost'} disabled={busy}
                      onClick={() => agir(() => api.post(`/admin/users/${u.id}/active`, { active: !u.active }))}>
                {u.active ? 'Bloquear conta' : 'Reativar conta'}
              </Button>
              <Button size="sm" variant="ghost" disabled={busy}
                      onClick={() => agir(() => api.post(`/admin/users/${u.id}/role`, { role: u.role === 'admin' ? 'user' : 'admin' }))}>
                {u.role === 'admin' ? 'Remover admin' : 'Tornar admin'}
              </Button>
            </div>
          </div>

          <Panel>
            <PanelHead label="Licenças" meta={f.licenses.length} />
            <PanelList>
              {f.licenses.length === 0 ? <PanelRow><span className="text-sm text-ink-3">Nenhuma licença: conta no plano Free.</span></PanelRow> : f.licenses.map((l) => (
                <PanelRow key={l.id}>
                  <div className="flex-1 min-w-0 text-sm">
                    <p className="text-ink-1">{TIER_LABEL[l.tier] ?? l.plan_key}, até {l.max_devices} PC(s)</p>
                    <p className="text-xs text-ink-3">Vence {dateTime(l.expires_at)}{l.blocked_reason ? `. Bloqueio: ${l.blocked_reason}` : ''}</p>
                  </div>
                  <Badge tone={l.eff_status === 'active' ? 'green' : l.eff_status === 'trial' ? 'amber' : 'red'}>{STATUS_LABEL[l.eff_status] ?? l.eff_status}</Badge>
                  {l.eff_status === 'blocked' && (
                    <Button size="sm" variant="link" disabled={busy} onClick={() => agir(() => api.post(`/admin/licenses/${l.id}/block`, { blocked: false }))}>Desbloquear</Button>
                  )}
                </PanelRow>
              ))}
            </PanelList>
          </Panel>

          <Panel>
            <PanelHead label="PCs" meta={f.devices.filter((d) => !d.deactivated_at).length + ' ativos'} />
            <PanelList>
              {f.devices.length === 0 ? <PanelRow><span className="text-sm text-ink-3">Nunca ativou o app em um PC.</span></PanelRow> : f.devices.map((d) => (
                <PanelRow key={d.id}>
                  <Laptop className="w-4 h-4 shrink-0 text-ink-3" aria-hidden />
                  <div className="flex-1 min-w-0 text-sm">
                    <p className="text-ink-1 truncate">{d.name || 'PC sem nome'}</p>
                    <p className="text-xs text-ink-3">Windows {d.windows_build || '?'}, app {d.agent_version || '?'}, visto {dateTime(d.last_seen_at)}</p>
                  </div>
                  {d.deactivated_at && <Badge>desativado</Badge>}
                </PanelRow>
              ))}
            </PanelList>
          </Panel>

          <Panel>
            <PanelHead label="Pagamentos" meta={f.payments.length} />
            <PanelList>
              {f.payments.length === 0 ? <PanelRow><span className="text-sm text-ink-3">Nenhum pagamento.</span></PanelRow> : f.payments.map((p) => (
                <PanelRow key={p.id}>
                  <div className="flex-1 min-w-0 text-sm">
                    <p className="text-ink-1">{planName(p.plan_key)}{p.coupon_code ? `, cupom ${p.coupon_code}` : ''}</p>
                    <p className="text-xs text-ink-3">{dateTime(p.created_at)}</p>
                  </div>
                  <span className="font-mono text-sm text-ink-1">{money(p.amount_cents)}</span>
                </PanelRow>
              ))}
            </PanelList>
          </Panel>

          <Panel>
            <PanelHead label="Uso recente" meta="telemetria consentida" />
            <PanelList>
              {f.events.length === 0 ? <PanelRow><span className="text-sm text-ink-3">Sem eventos (ou a pessoa não permitiu o envio).</span></PanelRow> : f.events.map((e, i) => (
                <PanelRow key={i}>
                  <div className="flex-1 min-w-0 text-sm">
                    <p className="text-ink-1 truncate">{EVENTO[e.event] ?? e.event}{e.optimization_id ? `: ${e.optimization_id}` : ''}</p>
                    <p className="text-xs text-ink-3">{e.device_name}, {dateTime(e.created_at)}</p>
                  </div>
                  {e.success === false && <Badge tone="red">falhou</Badge>}
                </PanelRow>
              ))}
            </PanelList>
          </Panel>
        </div>
      )}
    </Drawer>
  )
}
