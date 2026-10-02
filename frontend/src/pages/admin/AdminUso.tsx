import { useEffect, useState } from 'react'
import api, { errorMessage } from '../../services/api'
import { Alert, ErrorState, SkeletonRows, StatTile, Table } from '../../components/ui'
import { dateTime } from '../../lib/format'
import { AdminHead, ReadOnly } from './AdminConfig'

/*
 * Uso do app (telemetria CONSENTIDA): o que os clientes aplicam, o que falha e
 * o que desfazem. Otimizacao muito desfeita e' sinal de que o motor
 * recomendou algo que nao ajudou: e' daqui que sai a decisao de desligar ou
 * mudar o risco na aba Otimizacoes.
 */

interface Uso {
  daily: { day: string; scans: number; applied: number; failed: number; rollbacks: number }[]
  optimizations: { optimization_id: string; ok: number; failed: number; rolled_back: number }[]
  agent_versions: { agent_version: string; n: number }[]
  windows_builds: { windows_build: string; n: number }[]
  benchmarks_30d: { runs: number; devices: number }
}

// Cache da IA: cada chamada paga vira uma linha; cada resposta reaproveitada
// e' uma chamada que nao precisou acontecer. E' o numero que mostra se o custo
// de IA cresce com os usuarios ou so' com as combinacoes novas de PC e jogo.
interface CacheIa {
  days: number
  calls: number
  hits: number
  reuse_percent: number
  kinds: { kind: string; label: string; calls: number; hits: number; reuse_percent: number }[]
}

const soma = (xs: Uso['daily'], k: 'scans' | 'applied' | 'failed' | 'rollbacks') => xs.reduce((s, d) => s + d[k], 0)

export default function AdminUso() {
  const [u, setU] = useState<Uso | null>(null)
  const [ia, setIa] = useState<CacheIa | null>(null)
  const [error, setError] = useState('')
  const load = () => {
    setError('')
    api.get<Uso>('/admin/usage').then(({ data }) => setU(data)).catch((e) => setError(errorMessage(e)))
    // Falha aqui nao derruba a pagina: o cache e' um quadro a mais.
    api.get<CacheIa>('/admin/ai-cache').then(({ data }) => setIa(data)).catch(() => setIa(null))
  }
  useEffect(load, [])

  if (error) return <ErrorState description={error} onRetry={load} />
  if (!u) return <SkeletonRows rows={5} />

  const falhas = soma(u.daily, 'failed')
  return (
    <div className="space-y-8">
      <div className="grid gap-3 grid-cols-2 lg:grid-cols-5">
        <StatTile label="Análises em 30 dias" value={soma(u.daily, 'scans')} />
        <StatTile label="Otimizações aplicadas" value={soma(u.daily, 'applied')} tone="green" />
        <StatTile label="Falhas" value={falhas} tone={falhas > 0 ? 'red' : 'muted'} />
        <StatTile label="Desfeitas" value={soma(u.daily, 'rollbacks')} />
        <StatTile label="Benchmarks" value={u.benchmarks_30d.runs} hint={`${u.benchmarks_30d.devices} PCs`} />
      </div>
      {u.daily.length === 0 && <Alert tone="info">Sem telemetria nos últimos 30 dias. Ela só chega de quem permitiu o envio de dados no app.</Alert>}

      {ia && (
        <div>
          <AdminHead title="Cache da IA" sub={`Últimos ${ia.days} dias. Resposta reaproveitada não gera chamada paga.`} />
          <div className="grid gap-3 grid-cols-1 sm:grid-cols-3 mb-4">
            <StatTile label="Chamadas pagas" value={ia.calls} />
            <StatTile label="Respostas reaproveitadas" value={ia.hits} tone="green" />
            <StatTile label="Reaproveitamento" value={`${ia.reuse_percent}%`} />
          </div>
          <Table<CacheIa['kinds'][number]>
            rows={ia.kinds}
            rowKey={(r) => r.kind}
            minWidth={420}
            columns={[
              { key: 'k', header: 'Uso', cell: (r) => <span className="text-ink-1">{r.label}</span> },
              { key: 'c', header: 'Pagas', align: 'right', cell: (r) => <span className="font-mono">{r.calls}</span> },
              { key: 'h', header: 'Reaproveitadas', align: 'right', cell: (r) => <span className="font-mono text-accent-ink">{r.hits}</span> },
              { key: 'p', header: '%', align: 'right', cell: (r) => <span className="font-mono">{r.reuse_percent}%</span> },
            ]}
          />
        </div>
      )}

      <div>
        <AdminHead title="Por otimização" sub="Muitas desfeitas indicam recomendação que não ajudou." />
        <Table<Uso['optimizations'][number]>
          rows={u.optimizations}
          rowKey={(r) => r.optimization_id}
          minWidth={480}
          columns={[
            { key: 'id', header: 'Otimização', cell: (r) => <span className="text-ink-1">{r.optimization_id}</span> },
            { key: 'ok', header: 'Aplicadas', align: 'right', cell: (r) => <span className="font-mono text-accent-ink">{r.ok}</span> },
            { key: 'f', header: 'Falharam', align: 'right', cell: (r) => <span className={`font-mono ${r.failed > 0 ? 'text-red-400' : ''}`}>{r.failed}</span> },
            { key: 'rb', header: 'Desfeitas', align: 'right', cell: (r) => <span className="font-mono">{r.rolled_back}</span> },
          ]}
        />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <div>
          <AdminHead title="Versões do app" sub="PCs ativos por versão." />
          <Table<Uso['agent_versions'][number]> rows={u.agent_versions} rowKey={(r) => r.agent_version || '?'} minWidth={240}
            columns={[{ key: 'v', header: 'Versão', cell: (r) => r.agent_version || 'desconhecida' }, { key: 'n', header: 'PCs', align: 'right', cell: (r) => <span className="font-mono">{r.n}</span> }]} />
        </div>
        <div>
          <AdminHead title="Versões do Windows" sub="Build do Windows dos PCs ativos." />
          <Table<Uso['windows_builds'][number]> rows={u.windows_builds} rowKey={(r) => r.windows_build || '?'} minWidth={240}
            columns={[{ key: 'b', header: 'Build', cell: (r) => r.windows_build || 'desconhecido' }, { key: 'n', header: 'PCs', align: 'right', cell: (r) => <span className="font-mono">{r.n}</span> }]} />
        </div>
      </div>

      <div>
        <AdminHead title="Todos os PCs" />
        <ReadOnly path="/admin/devices" keyName="devices" head={['Conta', 'PC', 'Windows', 'App', 'Visto', 'Status']}
          row={(d) => [String(d.email), String(d.name), String(d.windows_build), String(d.agent_version), dateTime(String(d.last_seen_at)), d.deactivated_at ? 'desativado' : 'ativo']} />
      </div>
    </div>
  )
}
