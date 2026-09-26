import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from 'react'
import api, { errorMessage } from '../services/api'
import PageShell from '../components/PageShell'
import { Alert, Badge, Button, Card, EmptyState, Input, Spinner, StatTile, Tabs } from '../components/ui'
import { dateTime, money, STATUS_LABEL, TIER_LABEL } from '../lib/format'

type Row = Record<string, unknown>

/** Carrega uma lista do admin com estado de erro e recarga. */
function useList<T = Row>(path: string, key: string) {
  const [items, setItems] = useState<T[] | null>(null)
  const [error, setError] = useState('')
  const reload = useCallback(() => {
    api.get(path).then(({ data }) => setItems(data[key] as T[])).catch((e) => setError(errorMessage(e)))
  }, [path, key])
  useEffect(reload, [reload])
  return { items, error, reload, setError }
}

function Table({ head, children }: { head: string[]; children: ReactNode }) {
  return (
    <div className="overflow-x-auto rounded-xl border border-line">
      <table className="w-full min-w-[720px] text-sm">
        <thead className="bg-surface-1 text-left text-ink-1">
          <tr>{head.map((h) => <th key={h} className="p-3 font-semibold">{h}</th>)}</tr>
        </thead>
        <tbody className="divide-y divide-line">{children}</tbody>
      </table>
    </div>
  )
}

function Loading<T>({ items, error, children }: { items: T[] | null; error: string; children: (items: T[]) => ReactNode }) {
  if (error) return <Alert>{error}</Alert>
  if (!items) return <Spinner />
  if (items.length === 0) return <EmptyState title="Nada por aqui ainda." />
  return <>{children(items)}</>
}

// ─── Métricas ───────────────────────────────────────────────────────────
function Metrics() {
  const [m, setM] = useState<Row | null>(null)
  const [error, setError] = useState('')
  useEffect(() => { api.get('/admin/metrics').then(({ data }) => setM(data)).catch((e) => setError(errorMessage(e))) }, [])
  if (error) return <Alert>{error}</Alert>
  if (!m) return <Spinner />
  const cards: [string, string][] = [
    ['Contas', String(m.users)],
    ['Licenças ativas', String(m.active_licenses)],
    ['Em teste', String(m.trials)],
    ['PCs ativos', String(m.devices)],
    ['PCs vistos em 7 dias', String(m.devices_7d)],
    ['Receita em 30 dias', money(Number(m.revenue_30d_cents))],
  ]
  const opts = (m.optimizations_30d as Row[]) ?? []
  return (
    <div className="space-y-6">
      <div className="grid gap-4 grid-cols-2 lg:grid-cols-3">
        {cards.map(([label, value]) => <StatTile key={label} label={label} value={value} />)}
      </div>
      <h3 className="font-semibold text-ink-1">Otimizações nos últimos 30 dias (telemetria consentida)</h3>
      {opts.length === 0 ? <EmptyState title="Sem dados de telemetria ainda." /> : (
        <Table head={['Otimização', 'Sucesso', 'Falha']}>
          {opts.map((o) => (
            <tr key={String(o.optimization_id)}><td className="p-3">{String(o.optimization_id)}</td><td className="p-3 text-accent-ink">{String(o.ok)}</td><td className="p-3 text-danger">{String(o.failed)}</td></tr>
          ))}
        </Table>
      )}
    </div>
  )
}

// ─── Usuários ───────────────────────────────────────────────────────────
function Users() {
  const [q, setQ] = useState('')
  const { items, error, reload, setError } = useList(`/admin/users?q=${encodeURIComponent(q)}`, 'users')
  const [grant, setGrant] = useState<{ user_id: number; plan_key: string } | null>(null)
  const toggle = async (u: Row) => {
    try { await api.post(`/admin/users/${u.id}/active`, { active: !u.active }); reload() } catch (e) { setError(errorMessage(e)) }
  }
  const doGrant = async () => {
    if (!grant) return
    try { await api.post('/admin/licenses/grant', grant); setGrant(null); reload() } catch (e) { setError(errorMessage(e)) }
  }
  return (
    <div className="space-y-4">
      <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Buscar por e-mail ou nome" className="w-full max-w-sm rounded-md border border-line bg-surface-2 px-3 py-2.5 min-h-[44px] text-ink-1" />
      {grant && (
        <Card className="flex flex-wrap items-end gap-3">
          <p className="w-full text-sm text-ink-3">Licença cortesia para o usuário {grant.user_id}. Soma os dias do plano ao que ele já tem.</p>
          <select value={grant.plan_key} onChange={(e) => setGrant({ ...grant, plan_key: e.target.value })} className="rounded-md border border-line bg-surface-2 px-3 py-2.5 text-ink-1">
            {['starter', 'pro', 'ultimate'].map((p) => <option key={p} value={p}>{TIER_LABEL[p]}</option>)}
          </select>
          <Button size="sm" onClick={doGrant}>Conceder</Button>
          <Button size="sm" variant="link" onClick={() => setGrant(null)}>Cancelar</Button>
        </Card>
      )}
      <Loading items={items} error={error}>{(rows) => (
        <Table head={['ID', 'E-mail', 'Nome', 'Papel', 'PCs', 'Desde', '']}>
          {rows.map((u) => (
            <tr key={String(u.id)}>
              <td className="p-3">{String(u.id)}</td><td className="p-3 text-ink-1">{String(u.email)}</td><td className="p-3">{String(u.name)}</td>
              <td className="p-3">{u.role === 'admin' ? <Badge tone="purple">admin</Badge> : 'cliente'}</td>
              <td className="p-3">{String(u.devices)}</td><td className="p-3 text-ink-3">{dateTime(String(u.created_at))}</td>
              <td className="p-3 flex gap-2">
                <Button size="sm" variant="subtle" onClick={() => setGrant({ user_id: Number(u.id), plan_key: 'pro' })}>Licença</Button>
                <Button size="sm" variant={u.active ? 'danger' : 'ghost'} onClick={() => toggle(u)}>{u.active ? 'Bloquear' : 'Reativar'}</Button>
              </td>
            </tr>
          ))}
        </Table>
      )}</Loading>
    </div>
  )
}

// ─── Licenças ───────────────────────────────────────────────────────────
function Licenses() {
  const { items, error, reload, setError } = useList('/admin/licenses', 'licenses')
  const act = async (fn: () => Promise<unknown>) => { try { await fn(); reload() } catch (e) { setError(errorMessage(e)) } }
  return (
    <Loading items={items} error={error}>{(rows) => (
      <Table head={['ID', 'Conta', 'Plano', 'Status', 'Vence', 'PCs', '']}>
        {rows.map((l) => (
          <tr key={String(l.id)}>
            <td className="p-3">{String(l.id)}</td><td className="p-3 text-ink-1">{String(l.email)}</td>
            <td className="p-3">{TIER_LABEL[String(l.tier)] ?? String(l.plan_key)}</td>
            <td className="p-3"><Badge tone={l.status === 'active' ? 'green' : l.status === 'trial' ? 'amber' : 'red'}>{STATUS_LABEL[String(l.status)] ?? String(l.status)}</Badge></td>
            <td className="p-3 text-ink-3">{dateTime(String(l.expires_at))}</td><td className="p-3">{String(l.devices)}/{String(l.max_devices)}</td>
            <td className="p-3 flex gap-2">
              <Button size="sm" variant="subtle" onClick={() => act(() => api.post(`/admin/licenses/${l.id}/extend`, { days: 30 }))}>+30 dias</Button>
              {l.status === 'blocked'
                ? <Button size="sm" variant="ghost" onClick={() => act(() => api.post(`/admin/licenses/${l.id}/block`, { blocked: false }))}>Desbloquear</Button>
                : <Button size="sm" variant="danger" onClick={() => {
                    const reason = window.prompt('Motivo do bloqueio (aparece no registro de auditoria):')
                    if (reason !== null) act(() => api.post(`/admin/licenses/${l.id}/block`, { blocked: true, reason }))
                  }}>Bloquear</Button>}
            </td>
          </tr>
        ))}
      </Table>
    )}</Loading>
  )
}

// ─── Planos ─────────────────────────────────────────────────────────────
interface PlanRow { key: string; name: string; tier: string; description: string; price_cents: number; days: number; max_devices: number; features: string[]; active: boolean; sort: number }

function Plans() {
  const { items, error, reload, setError } = useList<PlanRow>('/admin/plans', 'plans')
  const [edit, setEdit] = useState<PlanRow | null>(null)
  const [saved, setSaved] = useState('')
  const save = async (e: FormEvent) => {
    e.preventDefault()
    if (!edit) return
    try {
      await api.put('/admin/plans', edit)
      setSaved(`Plano ${edit.name} salvo. O site e o checkout já usam o valor novo.`)
      setEdit(null)
      reload()
    } catch (err) { setError(errorMessage(err)) }
  }
  const blank: PlanRow = { key: '', name: '', tier: 'custom', description: '', price_cents: 0, days: 30, max_devices: 1, features: [], active: true, sort: 10 }
  return (
    <div className="space-y-4">
      {saved && <Alert tone="ok">{saved}</Alert>}
      <Button size="sm" variant="ghost" onClick={() => setEdit(blank)}>Novo plano (oferta personalizada)</Button>
      {edit && (
        <Card>
          <form onSubmit={save} className="grid gap-4 sm:grid-cols-2">
            <Input label="Chave" value={edit.key} onChange={(e) => setEdit({ ...edit, key: e.target.value })} required pattern="[a-z0-9_-]{2,40}" hint="Minúsculas, sem espaço. Não mude a chave de plano já vendido." />
            <Input label="Nome" value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} required />
            <label className="block"><span className="mb-1.5 block text-sm font-medium text-ink-2">Nível</span>
              <select value={edit.tier} onChange={(e) => setEdit({ ...edit, tier: e.target.value })} className="w-full rounded-md border border-line bg-surface-2 px-3 py-2.5 min-h-[44px] text-ink-1">
                {['free', 'starter', 'pro', 'ultimate', 'custom'].map((t) => <option key={t} value={t}>{TIER_LABEL[t]}</option>)}
              </select>
            </label>
            <Input label="Preço (R$)" type="number" step="0.01" min="0" value={(edit.price_cents / 100).toFixed(2)} onChange={(e) => setEdit({ ...edit, price_cents: Math.round(Number(e.target.value) * 100) })} />
            <Input label="Dias" type="number" min="1" value={edit.days} onChange={(e) => setEdit({ ...edit, days: Number(e.target.value) })} />
            <Input label="PCs" type="number" min="1" value={edit.max_devices} onChange={(e) => setEdit({ ...edit, max_devices: Number(e.target.value) })} />
            <Input label="Descrição" value={edit.description} onChange={(e) => setEdit({ ...edit, description: e.target.value })} />
            <Input label="Ordem" type="number" value={edit.sort} onChange={(e) => setEdit({ ...edit, sort: Number(e.target.value) })} />
            <label className="sm:col-span-2 block"><span className="mb-1.5 block text-sm font-medium text-ink-2">Itens mostrados no site (um por linha)</span>
              <textarea rows={5} value={edit.features.join('\n')} onChange={(e) => setEdit({ ...edit, features: e.target.value.split('\n').map((f) => f.trim()).filter(Boolean) })} className="w-full rounded-md border border-line bg-surface-2 px-3 py-2.5 text-ink-1" />
            </label>
            <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={edit.active} onChange={(e) => setEdit({ ...edit, active: e.target.checked })} /> Ativo à venda</label>
            <div className="flex gap-2 sm:justify-end"><Button type="submit">Salvar</Button><Button variant="link" type="button" onClick={() => setEdit(null)}>Cancelar</Button></div>
          </form>
        </Card>
      )}
      <Loading items={items} error={error}>{(rows) => (
        <Table head={['Chave', 'Nome', 'Nível', 'Preço', 'Dias', 'PCs', 'Ativo', '']}>
          {rows.map((p) => (
            <tr key={p.key}>
              <td className="p-3">{p.key}</td><td className="p-3 text-ink-1">{p.name}</td><td className="p-3">{TIER_LABEL[p.tier]}</td>
              <td className="p-3">{money(p.price_cents)}</td><td className="p-3">{p.days}</td><td className="p-3">{p.max_devices}</td>
              <td className="p-3">{p.active ? <Badge tone="green">sim</Badge> : <Badge>não</Badge>}</td>
              <td className="p-3"><Button size="sm" variant="subtle" onClick={() => setEdit({ ...p, features: typeof p.features === 'string' ? JSON.parse(p.features) : p.features })}>Editar</Button></td>
            </tr>
          ))}
        </Table>
      )}</Loading>
    </div>
  )
}

// ─── Configurações ──────────────────────────────────────────────────────
function Settings() {
  const [s, setS] = useState<Row | null>(null)
  const [msg, setMsg] = useState<{ tone: 'ok' | 'danger'; text: string } | null>(null)
  useEffect(() => { api.get('/admin/settings').then(({ data }) => setS(data.settings)).catch((e) => setMsg({ tone: 'danger', text: errorMessage(e) })) }, [])
  const save = async (key: string, value: unknown) => {
    try { await api.put('/admin/settings', { key, value }); setMsg({ tone: 'ok', text: 'Configuração salva.' }) } catch (e) { setMsg({ tone: 'danger', text: errorMessage(e) }) }
  }
  if (!s) return msg ? <Alert>{msg.text}</Alert> : <Spinner />
  return (
    <div className="max-w-lg space-y-5">
      {msg && <Alert tone={msg.tone}>{msg.text}</Alert>}
      <Card className="space-y-4">
        <Input label="Dias de teste grátis (0 desliga)" type="number" min="0" max="90" defaultValue={Number(s.trial_days)} onBlur={(e) => save('trial_days', Number(e.target.value))} />
        <label className="block"><span className="mb-1.5 block text-sm font-medium text-ink-2">Plano do teste</span>
          <select defaultValue={String(s.trial_plan)} onChange={(e) => save('trial_plan', e.target.value)} className="w-full rounded-md border border-line bg-surface-2 px-3 py-2.5 min-h-[44px] text-ink-1">
            {['starter', 'pro', 'ultimate'].map((p) => <option key={p} value={p}>{TIER_LABEL[p]}</option>)}
          </select>
        </label>
        <Input label="Dias que o app funciona offline" type="number" min="0" max="90" defaultValue={Number(s.offline_grace_days)} onBlur={(e) => save('offline_grace_days', Number(e.target.value))} />
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" defaultChecked={Boolean(s.telemetry_enabled)} onChange={(e) => save('telemetry_enabled', e.target.checked)} /> Aceitar telemetria consentida</label>
      </Card>
    </div>
  )
}

// ─── Cupons ─────────────────────────────────────────────────────────────
function Coupons() {
  const { items, error, reload, setError } = useList('/admin/coupons', 'coupons')
  const [form, setForm] = useState({ code: '', percent_off: 10, max_uses: '' })
  const save = async (e: FormEvent) => {
    e.preventDefault()
    try {
      await api.put('/admin/coupons', { code: form.code, percent_off: form.percent_off, max_uses: form.max_uses ? Number(form.max_uses) : null, active: true })
      setForm({ code: '', percent_off: 10, max_uses: '' })
      reload()
    } catch (err) { setError(errorMessage(err)) }
  }
  return (
    <div className="space-y-4">
      <Card>
        <form onSubmit={save} className="grid gap-3 sm:grid-cols-4 sm:items-end">
          <Input label="Código" value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase() })} required pattern="[A-Z0-9_-]{3,40}" />
          <Input label="Desconto (%)" type="number" min="1" max="100" value={form.percent_off} onChange={(e) => setForm({ ...form, percent_off: Number(e.target.value) })} />
          <Input label="Máximo de usos" type="number" min="1" value={form.max_uses} onChange={(e) => setForm({ ...form, max_uses: e.target.value })} placeholder="sem limite" />
          <Button type="submit">Salvar cupom</Button>
        </form>
      </Card>
      <Loading items={items} error={error}>{(rows) => (
        <Table head={['Código', 'Desconto', 'Usos', 'Ativo']}>
          {rows.map((c) => (
            <tr key={String(c.code)}><td className="p-3 text-ink-1">{String(c.code)}</td><td className="p-3">{String(c.percent_off)}%</td>
              <td className="p-3">{String(c.used_count)}{c.max_uses ? ` de ${c.max_uses}` : ''}</td><td className="p-3">{c.active ? 'sim' : 'não'}</td></tr>
          ))}
        </Table>
      )}</Loading>
    </div>
  )
}

// ─── Catálogo ───────────────────────────────────────────────────────────
interface Def { id: string; name: string; category: string; classification: string; risk: string; min_plan: string; enabled: boolean }
interface Override { kind: string; id: string; enabled: boolean | null; risk: string | null; min_plan: string | null; description: string | null }

function Catalog() {
  const [data, setData] = useState<{ overrides: Override[]; definitions: Def[] } | null>(null)
  const [error, setError] = useState('')
  const load = useCallback(() => { api.get('/admin/catalog').then(({ data }) => setData(data)).catch((e) => setError(errorMessage(e))) }, [])
  useEffect(load, [load])
  const save = async (id: string, patch: Partial<Override>) => {
    const current = data?.overrides.find((o) => o.id === id)
    try {
      await api.put('/admin/catalog', { kind: 'optimization', id, enabled: current?.enabled ?? null, risk: current?.risk ?? null, min_plan: current?.min_plan ?? null, description: current?.description ?? null, ...patch })
      load()
    } catch (e) { setError(errorMessage(e)) }
  }
  if (error) return <Alert>{error}</Alert>
  if (!data) return <Spinner />
  const defs = data.definitions.filter((d) => d.classification !== 'NOT_RECOMMENDED')
  return (
    <div className="space-y-4">
      <Alert tone="info">Mudanças chegam aos apps assinadas, na próxima sincronização. Só é possível desligar, mudar risco ou plano mínimo de otimizações que já existem no app: o painel não cria otimização nova nem muda o que ela faz.</Alert>
      <Table head={['Otimização', 'Classe', 'Ligada', 'Plano mínimo', 'Risco']}>
        {defs.map((d) => {
          const o = data.overrides.find((x) => x.id === d.id)
          const enabled = o?.enabled ?? d.enabled
          return (
            <tr key={d.id}>
              <td className="p-3"><p className="text-ink-1">{d.name}</p><p className="text-xs text-ink-4">{d.id}</p></td>
              <td className="p-3 text-xs">{d.classification}</td>
              <td className="p-3"><input type="checkbox" aria-label={`Ligar ${d.name}`} checked={enabled} onChange={(e) => save(d.id, { enabled: e.target.checked })} /></td>
              <td className="p-3">
                <select value={o?.min_plan ?? d.min_plan} onChange={(e) => save(d.id, { min_plan: e.target.value })} className="rounded-md border border-line bg-surface-2 px-2 py-1.5 text-ink-1">
                  {['free', 'starter', 'pro', 'ultimate'].map((p) => <option key={p} value={p}>{TIER_LABEL[p]}</option>)}
                </select>
              </td>
              <td className="p-3">
                <select value={o?.risk ?? d.risk} onChange={(e) => save(d.id, { risk: e.target.value })} className="rounded-md border border-line bg-surface-2 px-2 py-1.5 text-ink-1">
                  {['LOW', 'MEDIUM', 'HIGH'].map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
              </td>
            </tr>
          )
        })}
      </Table>
    </div>
  )
}

// ─── Releases ───────────────────────────────────────────────────────────
function Releases() {
  const { items, error, reload, setError } = useList('/admin/releases', 'releases')
  const [form, setForm] = useState({ component: 'agent', version: '', url: '', sha256: '', notes: '' })
  const publish = async (e: FormEvent) => {
    e.preventDefault()
    try { await api.post('/admin/releases', form); setForm({ ...form, version: '', url: '', sha256: '', notes: '' }); reload() } catch (err) { setError(errorMessage(err)) }
  }
  return (
    <div className="space-y-4">
      <Card>
        <form onSubmit={publish} className="grid gap-3 sm:grid-cols-2">
          <label className="block"><span className="mb-1.5 block text-sm font-medium text-ink-2">Componente</span>
            <select value={form.component} onChange={(e) => setForm({ ...form, component: e.target.value })} className="w-full rounded-md border border-line bg-surface-2 px-3 py-2.5 min-h-[44px] text-ink-1">
              <option value="agent">App (instalador)</option><option value="catalog">Catálogo</option><option value="game_profiles">Perfis de jogo</option><option value="engine">Motor</option>
            </select>
          </label>
          <Input label="Versão" value={form.version} onChange={(e) => setForm({ ...form, version: e.target.value })} required placeholder="0.2.0" />
          <Input label="URL do download (https)" value={form.url} onChange={(e) => setForm({ ...form, url: e.target.value })} placeholder="https://..." />
          <Input label="SHA-256 do arquivo" value={form.sha256} onChange={(e) => setForm({ ...form, sha256: e.target.value.toLowerCase() })} pattern="[0-9a-f]{64}" />
          <label className="sm:col-span-2 block"><span className="mb-1.5 block text-sm font-medium text-ink-2">Notas da versão</span>
            <textarea rows={3} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} className="w-full rounded-md border border-line bg-surface-2 px-3 py-2.5 text-ink-1" />
          </label>
          <div><Button type="submit">Publicar atualização</Button></div>
        </form>
      </Card>
      <Loading items={items} error={error}>{(rows) => (
        <Table head={['Componente', 'Versão', 'Publicado', 'URL']}>
          {rows.map((r) => (
            <tr key={`${r.component}-${r.version}`}><td className="p-3">{String(r.component)}</td><td className="p-3 text-ink-1">{String(r.version)}</td>
              <td className="p-3 text-ink-3">{dateTime(String(r.published_at))}</td><td className="p-3 break-all text-xs">{String(r.url)}</td></tr>
          ))}
        </Table>
      )}</Loading>
    </div>
  )
}

// ─── Listas somente leitura ─────────────────────────────────────────────
function ReadOnly({ path, keyName, head, row }: { path: string; keyName: string; head: string[]; row: (r: Row) => ReactNode[] }) {
  const { items, error } = useList(path, keyName)
  return (
    <Loading items={items} error={error}>{(rows) => (
      <Table head={head}>
        {rows.map((r, i) => <tr key={i}>{row(r).map((c, j) => <td key={j} className="p-3 align-top">{c}</td>)}</tr>)}
      </Table>
    )}</Loading>
  )
}

type Tab = 'metricas' | 'usuarios' | 'licencas' | 'planos' | 'config' | 'cupons' | 'catalogo' | 'releases' | 'pagamentos' | 'eventos' | 'pcs' | 'erros' | 'auditoria'

export default function Admin() {
  const [tab, setTab] = useState<Tab>('metricas')
  return (
    <PageShell title="Admin" noindex width="full" bar={{ title: 'Admin', sub: 'Toda alteração feita aqui fica registrada na auditoria.' }}>
      <Tabs<Tab> value={tab} onChange={setTab} items={[
        { key: 'metricas', label: 'Métricas' }, { key: 'usuarios', label: 'Usuários' }, { key: 'licencas', label: 'Licenças' },
        { key: 'planos', label: 'Planos' }, { key: 'config', label: 'Configurações' }, { key: 'cupons', label: 'Cupons' },
        { key: 'catalogo', label: 'Otimizações' }, { key: 'releases', label: 'Atualizações' }, { key: 'pagamentos', label: 'Pagamentos' },
        { key: 'eventos', label: 'Webhooks' }, { key: 'pcs', label: 'PCs' }, { key: 'erros', label: 'Erros' }, { key: 'auditoria', label: 'Auditoria' },
      ]} />
      {tab === 'metricas' && <Metrics />}
      {tab === 'usuarios' && <Users />}
      {tab === 'licencas' && <Licenses />}
      {tab === 'planos' && <Plans />}
      {tab === 'config' && <Settings />}
      {tab === 'cupons' && <Coupons />}
      {tab === 'catalogo' && <Catalog />}
      {tab === 'releases' && <Releases />}
      {tab === 'pagamentos' && <ReadOnly path="/admin/payments" keyName="payments" head={['ID', 'Conta', 'Plano', 'Valor', 'Status', 'Data']}
        row={(p) => [String(p.id), String(p.email ?? ''), String(p.plan_key), money(Number(p.amount_cents)), String(p.status), dateTime(String(p.created_at))]} />}
      {tab === 'eventos' && <ReadOnly path="/admin/payment-events" keyName="events" head={['Data', 'Origem', 'Status', 'Pagamento', 'Detalhe']}
        row={(e) => [dateTime(String(e.created_at)), String(e.source), String(e.status), String(e.provider_payment_id), String(e.detail)]} />}
      {tab === 'pcs' && <ReadOnly path="/admin/devices" keyName="devices" head={['Conta', 'PC', 'Windows', 'App', 'Visto', 'Status']}
        row={(d) => [String(d.email), String(d.name), String(d.windows_build), String(d.agent_version), dateTime(String(d.last_seen_at)), d.deactivated_at ? 'desativado' : 'ativo']} />}
      {tab === 'erros' && <ReadOnly path="/admin/errors" keyName="errors" head={['Data', 'PC', 'Evento', 'Otimização', 'Detalhe', 'Versão']}
        row={(e) => [dateTime(String(e.created_at)), String(e.device_name ?? ''), String(e.event), String(e.optimization_id ?? ''), JSON.stringify(e.detail), String(e.agent_version)]} />}
      {tab === 'auditoria' && <ReadOnly path="/admin/audit" keyName="entries" head={['Data', 'Admin', 'Ação', 'Alvo', 'Detalhe']}
        row={(a) => [dateTime(String(a.created_at)), String(a.email ?? ''), String(a.action), String(a.target), JSON.stringify(a.detail)]} />}
    </PageShell>
  )
}
