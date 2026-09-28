import { useEffect, useMemo, useState } from 'react'
import api, { errorMessage } from '../../services/api'
import { Alert, ErrorState, SkeletonRows, StatTile, Table } from '../../components/ui'
import { dateTime } from '../../lib/format'
import { AdminHead } from './AdminConfig'

/*
 * Partidas reais medidas pelo app (so' de quem permitiu o envio de dados).
 * E' daqui que sai a calibragem: o preset "equilibrado" ajudou no nivel MID?
 * As quedas vem mais de programa aberto junto ou do proprio jogo? Mediana
 * e nao media, porque partida real varia muito (mapa, modo, jogadores).
 */

type Point = [number, number, number]

interface GameRow { game_id: string; matches: number; devices: number; avg_fps: number; low1_fps: number; stutters_per_minute: number; drops: number; hours: number }
interface TierRow { game_id: string; tier: string; matches: number; devices: number; avg_fps: number; low1_fps: number }
interface Summary { by_game: GameRow[]; by_tier: TierRow[]; drop_causes: { cause: string; drops: number }[] }
interface Match {
  id: number; game_id: string; started_at: string; measured_seconds: number; avg_fps: number; low1_fps: number
  stutters_per_minute: number; drops: number; drop_causes: Record<string, number>
  hardware: { tier?: string; gpu?: string; cpu?: string; threads?: number; ram_gb?: number }; agent_version: string; device_name: string
}

const CAUSA: Record<string, string> = {
  app: 'Programa aberto junto', cpu: 'Processador no limite', gpu: 'Placa de vídeo no limite',
  game: 'Próprio jogo (PC tranquilo)', unknown: 'Sem leitura de uso',
}
const NIVEL: Record<string, string> = { LOW: 'Entrada', MID: 'Intermediário', HIGH: 'Forte', UNKNOWN: '?' }
const n0 = (v: number | null | undefined) => (v == null ? '' : Math.round(v).toLocaleString('pt-BR'))
const clock = (s: number) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`

export default function AdminPartidas() {
  const [s, setS] = useState<Summary | null>(null)
  const [rows, setRows] = useState<Match[]>([])
  const [error, setError] = useState('')
  const [sel, setSel] = useState<Match | null>(null)
  const [points, setPoints] = useState<Point[] | null>(null)

  const load = () => {
    setError('')
    Promise.all([api.get<Summary>('/admin/gameplay/summary'), api.get<{ sessions: Match[] }>('/admin/gameplay?limit=100')])
      .then(([a, b]) => { setS(a.data); setRows(b.data.sessions) })
      .catch((e) => setError(errorMessage(e)))
  }
  useEffect(load, [])

  useEffect(() => {
    if (!sel) return
    setPoints(null)
    api.get<{ timeline: Point[] }>(`/admin/gameplay/${sel.id}/timeline`).then(({ data }) => setPoints(data.timeline)).catch(() => setPoints([]))
  }, [sel])

  if (error) return <ErrorState description={error} onRetry={load} />
  if (!s) return <SkeletonRows rows={5} />

  const total = s.by_game.reduce((a, g) => a + g.matches, 0)
  const horas = s.by_game.reduce((a, g) => a + Number(g.hours), 0)
  const pcs = new Set(rows.map((r) => r.device_name)).size
  const quedas = s.drop_causes.reduce((a, c) => a + Number(c.drops), 0)

  return (
    <div className="space-y-8">
      <div className="grid gap-3 grid-cols-2 lg:grid-cols-4">
        <StatTile label="Partidas medidas" value={total} />
        <StatTile label="Horas de jogo" value={horas.toLocaleString('pt-BR', { maximumFractionDigits: 1 })} />
        <StatTile label="PCs" value={pcs} hint="nas últimas 100 partidas" />
        <StatTile label="Quedas fortes" value={quedas} tone={quedas > 0 ? 'red' : 'muted'} />
      </div>
      {total === 0 && <Alert tone="info">Nenhuma partida ainda. Elas chegam do app 0.4.2 em diante, de quem permitiu o envio de dados, logo depois de cada partida.</Alert>}

      <div>
        <AdminHead title="Por jogo" sub="Mediana das partidas: partida real varia muito, a média seria puxada pelos extremos." />
        <Table<GameRow> rows={s.by_game} rowKey={(r) => r.game_id} minWidth={560}
          columns={[
            { key: 'g', header: 'Jogo', cell: (r) => <span className="text-ink-1">{r.game_id}</span> },
            { key: 'm', header: 'Partidas', align: 'right', cell: (r) => <span className="font-mono">{r.matches}</span> },
            { key: 'd', header: 'PCs', align: 'right', cell: (r) => <span className="font-mono">{r.devices}</span> },
            { key: 'a', header: 'FPS médio', align: 'right', cell: (r) => <span className="font-mono">{n0(r.avg_fps)}</span> },
            { key: 'l', header: '1% low', align: 'right', cell: (r) => <span className="font-mono">{n0(r.low1_fps)}</span> },
            { key: 's', header: 'Travadas/min', align: 'right', cell: (r) => <span className="font-mono">{Number(r.stutters_per_minute).toFixed(1)}</span> },
          ]} />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <div>
          <AdminHead title="Por nível de PC" sub="Onde as regras por hardware estão acertando ou não." />
          <Table<TierRow> rows={s.by_tier} rowKey={(r) => `${r.game_id}-${r.tier}`} minWidth={420}
            columns={[
              { key: 'g', header: 'Jogo', cell: (r) => r.game_id },
              { key: 't', header: 'Nível', cell: (r) => NIVEL[r.tier] ?? r.tier },
              { key: 'd', header: 'PCs', align: 'right', cell: (r) => <span className="font-mono">{r.devices}</span> },
              { key: 'a', header: 'FPS', align: 'right', cell: (r) => <span className="font-mono">{n0(r.avg_fps)}</span> },
              { key: 'l', header: '1% low', align: 'right', cell: (r) => <span className="font-mono">{n0(r.low1_fps)}</span> },
            ]} />
        </div>
        <div>
          <AdminHead title="De onde vêm as quedas" sub="Causa provável no momento de cada queda forte." />
          <Table<{ cause: string; drops: number }> rows={s.drop_causes} rowKey={(r) => r.cause} minWidth={300}
            columns={[
              { key: 'c', header: 'Causa', cell: (r) => CAUSA[r.cause] ?? r.cause },
              { key: 'n', header: 'Quedas', align: 'right', cell: (r) => <span className="font-mono">{r.drops}</span> },
              { key: 'p', header: '%', align: 'right', cell: (r) => <span className="font-mono">{quedas ? Math.round((100 * Number(r.drops)) / quedas) : 0}%</span> },
            ]} />
        </div>
      </div>

      {sel && (
        <div className="rounded-xl border border-line bg-surface-1 p-4 sm:p-5">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h3 className="text-ink-1 font-semibold">{sel.game_id} em {dateTime(sel.started_at)}</h3>
            <span className="text-ink-3 text-sm">{sel.hardware.gpu ?? 'placa não lida'} · {NIVEL[sel.hardware.tier ?? ''] ?? '?'} · app {sel.agent_version}</span>
          </div>
          {points === null ? <SkeletonRows rows={3} /> : points.length < 2
            ? <p className="text-ink-3 text-sm mt-3">Partida sem gráfico (medida antes da versão com linha do tempo).</p>
            : <FpsChart points={points} />}
        </div>
      )}

      <div>
        <AdminHead title="Últimas partidas" sub="Clique numa partida para ver o gráfico." />
        <Table<Match> rows={rows} rowKey={(r) => String(r.id)} minWidth={720} onRowClick={setSel}
          columns={[
            { key: 'd', header: 'Data', cell: (r) => dateTime(r.started_at) },
            { key: 'g', header: 'Jogo', cell: (r) => <span className="text-ink-1">{r.game_id}</span> },
            { key: 'p', header: 'PC', cell: (r) => <span className="text-ink-3">{r.hardware.gpu ?? ''} ({NIVEL[r.hardware.tier ?? ''] ?? '?'})</span> },
            { key: 'min', header: 'Min', align: 'right', cell: (r) => <span className="font-mono">{Math.round(r.measured_seconds / 60)}</span> },
            { key: 'a', header: 'FPS', align: 'right', cell: (r) => <span className="font-mono">{n0(r.avg_fps)}</span> },
            { key: 'l', header: '1% low', align: 'right', cell: (r) => <span className="font-mono">{n0(r.low1_fps)}</span> },
            { key: 'q', header: 'Quedas', align: 'right', cell: (r) => <span className="font-mono">{r.drops}</span> },
          ]} />
      </div>
    </div>
  )
}

/* Duas linhas numa escala so' (FPS): media e pior quadro de cada trecho. */
function FpsChart({ points }: { points: Point[] }) {
  const W = 800, H = 220, L = 40, R = 8, T = 10, B = 22
  const [hover, setHover] = useState<number | null>(null)
  const { max, x, y, path } = useMemo(() => {
    const top = Math.max(...points.map((p) => p[1]))
    const step = [60, 120, 180, 240, 300, 360, 480, 600, 1000].find((v) => v >= top * 1.08) ?? Math.ceil(top / 500) * 500
    const t0 = points[0][0], t1 = points[points.length - 1][0] || 1
    const x = (t: number) => L + ((t - t0) / Math.max(1, t1 - t0)) * (W - L - R)
    const y = (v: number) => T + (H - T - B) * (1 - Math.min(1, v / step))
    const path = (k: 1 | 2) => points.map((p, i) => `${i ? 'L' : 'M'}${x(p[0]).toFixed(1)},${y(p[k]).toFixed(1)}`).join('')
    return { max: step, x, y, path }
  }, [points])

  const onMove = (e: React.MouseEvent<SVGSVGElement>) => {
    const box = e.currentTarget.getBoundingClientRect()
    const px = ((e.clientX - box.left) / box.width) * W
    let best = 0
    points.forEach((p, i) => { if (Math.abs(x(p[0]) - px) < Math.abs(x(points[best][0]) - px)) best = i })
    setHover(best)
  }
  const h = hover != null ? points[hover] : null
  const t0 = points[0][0]

  return (
    <div className="mt-3">
      <div className="flex flex-wrap gap-4 text-xs text-ink-3 mb-2">
        <span className="inline-flex items-center gap-1.5"><span className="h-0.5 w-4 rounded" style={{ background: 'rgb(var(--chart-1))' }} />FPS médio</span>
        <span className="inline-flex items-center gap-1.5"><span className="h-0.5 w-4 rounded" style={{ background: 'rgb(var(--chart-2))' }} />Pior quadro (picos para baixo = travadas)</span>
      </div>
      <div className="relative">
        <svg viewBox={`0 0 ${W} ${H}`} className="w-full h-auto" role="img" aria-label="FPS ao longo da partida"
             onMouseMove={onMove} onMouseLeave={() => setHover(null)}>
          {[0, 0.25, 0.5, 0.75, 1].map((k) => (
            <g key={k}>
              <line x1={L} x2={W - R} y1={y(max * k)} y2={y(max * k)} stroke="rgb(var(--line))" strokeWidth={1} />
              <text x={L - 6} y={y(max * k) + 4} textAnchor="end" fontSize={11} fill="rgb(var(--ink-3))">{Math.round(max * k)}</text>
            </g>
          ))}
          <path d={path(2)} fill="none" stroke="rgb(var(--chart-2))" strokeWidth={1.5} strokeLinejoin="round" />
          <path d={path(1)} fill="none" stroke="rgb(var(--chart-1))" strokeWidth={2} strokeLinejoin="round" />
          {[0, Math.floor(points.length / 2), points.length - 1].map((i) => (
            <text key={i} x={x(points[i][0])} y={H - 6} textAnchor={i === 0 ? 'start' : i === points.length - 1 ? 'end' : 'middle'} fontSize={11} fill="rgb(var(--ink-3))">
              {clock(points[i][0] - t0)}
            </text>
          ))}
          {h && (
            <g>
              <line x1={x(h[0])} x2={x(h[0])} y1={T} y2={H - B} stroke="rgb(var(--ink-4))" strokeWidth={1} />
              <circle cx={x(h[0])} cy={y(h[1])} r={4} fill="rgb(var(--chart-1))" stroke="rgb(var(--surface-1))" strokeWidth={2} />
              <circle cx={x(h[0])} cy={y(h[2])} r={4} fill="rgb(var(--chart-2))" stroke="rgb(var(--surface-1))" strokeWidth={2} />
            </g>
          )}
        </svg>
        {h && (
          <div className="pointer-events-none absolute top-0 rounded-lg border border-line bg-surface-2 px-2.5 py-1.5 text-xs text-ink-2 shadow-elev-sm"
               style={{ left: `${Math.min(75, (x(h[0]) / W) * 100)}%` }}>
            <div className="text-ink-1 font-mono">{clock(h[0] - t0)}</div>
            <div>FPS médio <span className="font-mono text-ink-1">{Math.round(h[1])}</span></div>
            <div>Pior quadro <span className="font-mono text-ink-1">{Math.round(h[2])}</span></div>
          </div>
        )}
      </div>
    </div>
  )
}
