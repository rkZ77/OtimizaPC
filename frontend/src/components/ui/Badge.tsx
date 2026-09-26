import { cn } from '../../lib/cn'

/*
 * Tag em texto com borda (padrao do Pickia). A cor e' o unico eixo que varia,
 * porque e' ela que carrega o significado: menta e' a marca e o OK, ambar e'
 * atencao, vermelho e' problema, azul e' informacao.
 */

const TONE = {
  neutral: 'bg-transparent text-ink-3 border-line-strong',
  green:   'bg-green-500/5 text-accent-ink border-green-500/30',
  red:     'bg-red-500/5 text-red-400 border-red-500/30',
  yellow:  'bg-yellow-400/5 text-yellow-400 border-yellow-400/30',
  amber:   'bg-amber-400/5 text-amber-400 border-amber-400/30',
  blue:    'bg-blue-400/5 text-blue-400 border-blue-400/30',
  purple:  'bg-purple-500/5 text-purple-400 border-purple-500/30',
  sky:     'bg-sky-400/5 text-sky-400 border-sky-400/30',
} as const

export type BadgeTone = keyof typeof TONE

export default function Badge({
  tone = 'neutral',
  children,
  className,
  Icon,
}: {
  tone?: BadgeTone
  children: React.ReactNode
  className?: string
  Icon?: React.ComponentType<{ className?: string }>
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 text-[10px] font-bold tracking-wide uppercase',
        'px-1.5 py-0.5 rounded-sm border',
        TONE[tone],
        className,
      )}
    >
      {Icon && <Icon className="w-3 h-3" />}
      {children}
    </span>
  )
}

/**
 * Como cada plano se chama e se pinta na tela, fonte unica (licao do Pickia:
 * o rotulo reescrito em cinco lugares divergiu). Cada plano contem o anterior,
 * e a cor sobe junto: neutro, azul, menta, roxo.
 */
export const PLANO_META: Record<string, { tone: BadgeTone; label: string }> = {
  free:     { tone: 'neutral', label: 'Free' },
  starter:  { tone: 'blue',    label: 'Starter' },
  pro:      { tone: 'green',   label: 'Pro' },
  ultimate: { tone: 'purple',  label: 'Ultimate' },
  custom:   { tone: 'purple',  label: 'Custom' },
}

export function planoMeta(tier?: string | null, status?: string | null) {
  if (status === 'trial') return { tone: 'amber' as BadgeTone, label: `${PLANO_META[tier ?? 'pro']?.label ?? 'Pro'} (teste)` }
  return PLANO_META[tier ?? 'free'] ?? PLANO_META.free
}

export function PlanBadge({ tier, status, className }: { tier?: string | null; status?: string | null; className?: string }) {
  const { tone, label } = planoMeta(tier, status)
  return <Badge tone={tone} className={className}>{label}</Badge>
}

/** Status do diagnostico, o mesmo vocabulario do app desktop. */
const STATUS_META: Record<string, { tone: BadgeTone; label: string }> = {
  ok:        { tone: 'green',   label: 'OK' },
  info:      { tone: 'blue',    label: 'Info' },
  attention: { tone: 'amber',   label: 'Atenção' },
  problem:   { tone: 'red',     label: 'Problema' },
  unknown:   { tone: 'neutral', label: '?' },
}

export function StatusBadge({ status, className }: { status: keyof typeof STATUS_META; className?: string }) {
  const { tone, label } = STATUS_META[status] ?? STATUS_META.unknown
  return <Badge tone={tone} className={className}>{label}</Badge>
}

/** Ponto pulsante de "ao vivo" / analise rodando. */
export function LiveDot({
  tone = 'green',
  className,
}: {
  tone?: 'green' | 'amber' | 'red'
  className?: string
}) {
  const color = { green: 'bg-accent', amber: 'bg-amber-400', red: 'bg-red-400' }[tone]
  return (
    <span className={cn('relative inline-flex w-2 h-2 shrink-0', className)}>
      <span className={cn('absolute inset-0 rounded-full opacity-60 animate-ping', color)} />
      <span className={cn('relative inline-flex w-2 h-2 rounded-full', color)} />
    </span>
  )
}
