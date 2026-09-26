/*
 * Secoes de configuracao do admin: planos, configuracoes, cupons, catalogo de
 * otimizacoes, atualizacoes e listas somente leitura. Usadas por Admin.tsx.
 */
import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from 'react'
import api, { errorMessage } from '../../services/api'
import { Alert, Badge, Button, Card, EmptyState, Input, Spinner } from '../../components/ui'
import { dateTime, money, TIER_LABEL } from '../../lib/format'

export type Row = Record<string, unknown>

/**
 * Cabecalho de secao do admin: compacto e alinhado a' esquerda. O SectionHead
 * do Pickia e' de vitrine (grande e centralizado) e, entre tabelas, deixava o
 * titulo solto longe do conteudo que ele nomeia.
 */
export function AdminHead({ title, sub, className }: { title: string; sub?: string; className?: string }) {
  return (
    <div className={className ?? 'mb-3'}>
      <h3 className="font-display text-base font-bold text-ink-1">{title}</h3>
      {sub && <p className="text-xs text-ink-3 mt-0.5">{sub}</p>}
    </div>
  )
}

/** Status de pagamento em portugues (o provedor manda em ingles). */
export const PAY_STATUS: Record<string, string> = {
  approved: 'Aprovado', pending: 'Pendente', rejected: 'Recusado', refunded: 'Reembolsado', cancelled: 'Cancelado',
}

/** Carrega uma lista do admin com estado de erro e recarga. */
export function useList<T = Row>(path: string, key: string) {
  const [items, setItems] = useState<T[] | null>(null)
  const [error, setError] = useState('')
  const reload = useCallback(() => {
    api.get(path).then(({ data }) => setItems(data[key] as T[])).catch((e) => setError(errorMessage(e)))
  }, [path, key])
  useEffect(reload, [reload])
  return { items, error, reload, setError }
}

export function Table({ head, children }: { head: string[]; children: ReactNode }) {
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

export function Loading<T>({ items, error, children }: { items: T[] | null; error: string; children: (items: T[]) => ReactNode }) {
  if (error) return <Alert>{error}</Alert>
  if (!items) return <Spinner />
  if (items.length === 0) return <EmptyState title="Nada por aqui ainda." />
  return <>{children(items)}</>
}

// ─── Planos ─────────────────────────────────────────────────────────────
interface PlanRow { key: string; name: string; tier: string; period: string; description: string; price_cents: number; days: number; max_devices: number; features: string[]; active: boolean; sort: number }

const PERIOD_OPTIONS: Record<string, string> = { none: 'Sem cobrança (Free)', monthly: 'Mensal', quarterly: 'Trimestral', annual: 'Anual' }

export function Plans() {
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
  const blank: PlanRow = { key: '', name: '', tier: 'custom', period: 'monthly', description: '', price_cents: 0, days: 30, max_devices: 1, features: [], active: true, sort: 10 }
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
            <label className="block"><span className="mb-1.5 block text-sm font-medium text-ink-2">Período</span>
              <select value={edit.period} onChange={(e) => setEdit({ ...edit, period: e.target.value })} className="w-full rounded-md border border-line bg-surface-2 px-3 py-2.5 min-h-[44px] text-ink-1">
                {Object.entries(PERIOD_OPTIONS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
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
        <Table head={['Chave', 'Nome', 'Nível', 'Período', 'Preço', 'Dias', 'PCs', 'Ativo', '']}>
          {rows.map((p) => (
            <tr key={p.key}>
              <td className="p-3">{p.key}</td><td className="p-3 text-ink-1">{p.name}</td><td className="p-3">{TIER_LABEL[p.tier]}</td>
              <td className="p-3">{PERIOD_OPTIONS[p.period] ?? p.period}</td>
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
export function Settings() {
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
export function Coupons() {
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

export function Catalog() {
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
export function Releases() {
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
export function ReadOnly({ path, keyName, head, row }: { path: string; keyName: string; head: string[]; row: (r: Row) => ReactNode[] }) {
  const { items, error } = useList(path, keyName)
  return (
    <Loading items={items} error={error}>{(rows) => (
      <Table head={head}>
        {rows.map((r, i) => <tr key={i}>{row(r).map((c, j) => <td key={j} className="p-3 align-top">{c}</td>)}</tr>)}
      </Table>
    )}</Loading>
  )
}

