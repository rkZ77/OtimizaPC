import { useEffect, useState } from 'react'
import { Copy, Gauge, Gift, Wrench } from 'lucide-react'
import api from '../services/api'
import { Button, StatTile } from './ui'
import { gameName } from './Vitrine'

/*
 * Os dois cartoes que sustentam a renovacao na pagina Meu plano:
 * o que o RKZFPS fez e mediu (so' com telemetria ligada no app, e o antes e
 * depois so' aparece com partidas suficientes dos dois lados) e o link de
 * indicacao, que transforma cliente satisfeito em cliente novo.
 */

interface Recap {
  optimizations: number
  matches: number
  hours: number
  games: { game_id: string; matches_before: number; matches_after: number; avg_fps_before: number; avg_fps_after: number; low1_fps_before: number; low1_fps_after: number }[]
}

interface Referral { code: string; reward_days: number; signups: number; rewarded: number; days_earned: number }

const fps = (n: number) => n.toLocaleString('pt-BR', { maximumFractionDigits: 0 })

export function OQueFoiFeito() {
  const [r, setR] = useState<Recap | null>(null)
  useEffect(() => { api.get<Recap>('/account/recap').then(({ data }) => setR(data)).catch(() => setR(null)) }, [])
  if (!r || (r.optimizations === 0 && r.matches === 0)) return null

  return (
    <div className="card mt-4 p-6">
      <p className="flex items-center gap-2 font-semibold text-ink-1"><Wrench className="h-5 w-5 text-accent-ink" aria-hidden />O que o RKZFPS fez no seu PC</p>
      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
        <StatTile label="Correções aplicadas" value={String(r.optimizations)} />
        <StatTile label="Partidas medidas" value={String(r.matches)} />
        <StatTile label="Horas de jogo medidas" value={r.hours.toLocaleString('pt-BR')} />
      </div>
      {r.games.length > 0 ? (
        <ul className="mt-5 space-y-2">
          {r.games.map((g) => (
            <li key={g.game_id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-line px-4 py-3 text-sm">
              <span className="flex items-center gap-2 font-semibold text-ink-1"><Gauge className="h-4 w-4 text-accent-ink" aria-hidden />{gameName(g.game_id)}</span>
              <span className="text-ink-2">
                FPS médio {fps(g.avg_fps_before)} para <span className="font-semibold text-ink-1">{fps(g.avg_fps_after)}</span>,
                pior 1% {fps(g.low1_fps_before)} para <span className="font-semibold text-ink-1">{fps(g.low1_fps_after)}</span>
              </span>
            </li>
          ))}
        </ul>
      ) : null}
      <p className="mt-4 text-xs text-ink-3">
        Mediana das partidas antes e depois da primeira correção, com pelo menos 3 de cada lado. Só entram PCs com o envio de dados de uso ligado no app.
      </p>
    </div>
  )
}

export function Indique() {
  const [r, setR] = useState<Referral | null>(null)
  const [copiado, setCopiado] = useState(false)
  useEffect(() => { api.get<Referral>('/account/referral').then(({ data }) => setR(data)).catch(() => setR(null)) }, [])
  if (!r || r.reward_days <= 0) return null
  const link = `${window.location.origin}/r/${r.code}`

  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(link)
      setCopiado(true)
      window.setTimeout(() => setCopiado(false), 2000)
    } catch {
      /* sem permissao de area de transferencia: o link segue visivel para copiar na mao */
    }
  }

  return (
    <div className="card mt-4 p-6">
      <p className="flex items-center gap-2 font-semibold text-ink-1"><Gift className="h-5 w-5 text-accent-ink" aria-hidden />Indique e ganhe {r.reward_days} dias</p>
      <p className="mt-1 text-sm text-ink-3">
        Mande o seu link para um amigo. Quando ele fizer a primeira compra, você ganha {r.reward_days} dias no seu plano. Sem plano ativo, os dias vêm no Pro.
      </p>
      <div className="mt-4 flex flex-col gap-2 sm:flex-row">
        <input readOnly value={link} aria-label="Seu link de indicação" className="input min-w-0 flex-1 font-mono text-sm" onFocus={(e) => e.currentTarget.select()} />
        <Button variant="ghost" Icon={Copy} onClick={copiar}>{copiado ? 'Copiado' : 'Copiar link'}</Button>
      </div>
      {(r.signups > 0 || r.days_earned > 0) && (
        <p className="mt-3 text-sm text-ink-2">
          {r.signups} {r.signups === 1 ? 'conta criada' : 'contas criadas'} pelo seu link, {r.rewarded} {r.rewarded === 1 ? 'compra' : 'compras'}, {r.days_earned} dias ganhos.
        </p>
      )}
    </div>
  )
}
