import { useEffect, useState } from 'react'
import api, { errorMessage } from '../../services/api'
import { AdminHead } from './AdminConfig'
import { Alert, EmptyState, Panel, PanelHead, PanelList, PanelRow, PillGroup, SkeletonRows, StatTile } from '../../components/ui'
import { date, dateTime, TIER_LABEL } from '../../lib/format'

/*
 * Funil e engajamento, no espirito do AdminFunil do Pickia: os cartoes da aba
 * Usuarios contam ESTOQUE; aqui se conta PASSAGEM. De quem criou conta na
 * janela, quantos ativaram o app num PC, usaram o teste e pagaram. Sem essa
 * quebra, "converte pouco" e "pouca gente chega la'" viram a mesma frase.
 *
 * A linha que pede acao e' "criou conta e nunca ativou o PC": sao pessoas
 * que nunca viram o produto funcionando.
 */

interface Funil {
  days: number; downloads: number; signed_up: number; activated_pc: number; used_trial: number; paid: number; never_activated: number
  never_activated_users: { id: number; email: string; name: string; created_at: string }[]
}

interface Engaj {
  active_1d: number; active_7d: number; active_30d: number; gone_30d: number
  expiring: { id: number; email: string; tier: string; eff_status: string; expires_at: string; last_seen_at: string | null }[]
  lapsed_30d: { id: number; email: string; tier: string; expires_at: string; last_seen_at: string | null }[]
}

const pct = (a: number, b: number) => (b > 0 ? `${Math.round((100 * a) / b)}%` : 'sem base')

export default function AdminFunil() {
  const [dias, setDias] = useState<'7' | '30' | '90'>('30')
  const [f, setF] = useState<Funil | null>(null)
  const [e, setE] = useState<Engaj | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    setF(null)
    api.get<Funil>(`/admin/funnel?days=${dias}`).then(({ data }) => setF(data)).catch((err) => setError(errorMessage(err)))
  }, [dias])
  useEffect(() => {
    api.get<Engaj>('/admin/engagement').then(({ data }) => setE(data)).catch((err) => setError(errorMessage(err)))
  }, [])

  return (
    <div className="space-y-8">
      {error && <Alert>{error}</Alert>}

      <div>
        <div className="flex flex-wrap items-end justify-between gap-3">
          <AdminHead title="Funil" sub="Das contas criadas no período, quantas chegaram a cada etapa." className="" />
          <PillGroup<'7' | '30' | '90'> value={dias} onChange={setDias}
            options={[{ value: '7', label: '7 dias' }, { value: '30', label: '30 dias' }, { value: '90', label: '90 dias' }]} />
        </div>
        {!f ? <SkeletonRows rows={2} className="mt-4" /> : (
          <div className="mt-4 grid gap-3 grid-cols-2 lg:grid-cols-5">
            <StatTile label="Baixaram o app" value={f.downloads} hint="cliques em Baixar no site" />
            <StatTile label="Criaram conta" value={f.signed_up} />
            <StatTile label="Ativaram o app no PC" value={f.activated_pc} hint={`${pct(f.activated_pc, f.signed_up)} das contas`} />
            <StatTile label="Usaram o teste" value={f.used_trial} hint={`${pct(f.used_trial, f.activated_pc)} de quem ativou`} />
            <StatTile label="Assinaram" value={f.paid} tone="green" hint={`${pct(f.paid, f.signed_up)} das contas`} />
          </div>
        )}
      </div>

      {f && (
        <Panel>
          <PanelHead label="Criaram conta e nunca ativaram o PC" meta={`${f.never_activated} no período`} />
          <PanelList>
            {f.never_activated_users.length === 0
              ? <PanelRow><span className="text-sm text-ink-3">Todo mundo que criou conta no período já ativou o app.</span></PanelRow>
              : f.never_activated_users.map((u) => (
                <PanelRow key={u.id}>
                  <div className="flex-1 min-w-0 text-sm">
                    <p className="text-ink-1 truncate">{u.email}</p>
                    <p className="text-xs text-ink-3">{u.name || 'sem nome'}, conta de {date(u.created_at)}</p>
                  </div>
                </PanelRow>
              ))}
          </PanelList>
        </Panel>
      )}

      <div>
        <AdminHead title="Engajamento" sub="Última vez que o app de cada pessoa falou com o servidor." />
        {!e ? <SkeletonRows rows={2} /> : (
          <div className="grid gap-3 grid-cols-2 lg:grid-cols-4">
            <StatTile label="Ativos hoje" value={e.active_1d} tone="green" />
            <StatTile label="Ativos em 7 dias" value={e.active_7d} />
            <StatTile label="Ativos em 30 dias" value={e.active_30d} />
            <StatTile label="Sumiram há 30+ dias" value={e.gone_30d} tone={e.gone_30d > 0 ? 'red' : 'muted'} />
          </div>
        )}
      </div>

      {e && (
        <div className="grid gap-6 lg:grid-cols-2">
          <Panel>
            <PanelHead label="Vencem nos próximos 7 dias" meta={e.expiring.length} />
            <PanelList>
              {e.expiring.length === 0 ? <PanelRow><span className="text-sm text-ink-3">Ninguém vence esta semana.</span></PanelRow> : e.expiring.map((u) => (
                <PanelRow key={u.id}>
                  <div className="flex-1 min-w-0 text-sm">
                    <p className="text-ink-1 truncate">{u.email}</p>
                    <p className="text-xs text-ink-3">{TIER_LABEL[u.tier] ?? u.tier} ({u.eff_status === 'trial' ? 'teste' : 'assinante'}), vence {dateTime(u.expires_at)}</p>
                  </div>
                </PanelRow>
              ))}
            </PanelList>
          </Panel>
          <Panel>
            <PanelHead label="Venceram nos últimos 30 dias sem renovar" meta={e.lapsed_30d.length} />
            <PanelList>
              {e.lapsed_30d.length === 0 ? <PanelRow><span className="text-sm text-ink-3">Nenhum vencimento sem renovação.</span></PanelRow> : e.lapsed_30d.map((u) => (
                <PanelRow key={u.id}>
                  <div className="flex-1 min-w-0 text-sm">
                    <p className="text-ink-1 truncate">{u.email}</p>
                    <p className="text-xs text-ink-3">{TIER_LABEL[u.tier] ?? u.tier}, venceu {date(u.expires_at)}{u.last_seen_at ? `, visto ${date(u.last_seen_at)}` : ''}</p>
                  </div>
                </PanelRow>
              ))}
            </PanelList>
          </Panel>
        </div>
      )}
      {e && e.active_30d === 0 && e.expiring.length === 0 && <EmptyState title="Ainda sem uso registrado" description="Os números aparecem conforme os clientes ativam o app." compact />}
    </div>
  )
}
