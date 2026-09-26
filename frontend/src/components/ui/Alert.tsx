import { AlertTriangle, CheckCircle2, Info, XCircle } from 'lucide-react'
import { cn } from '../../lib/cn'

/*
 * Aviso em caixa, no mesmo vocabulario do Badge: a cor diz o tipo e o icone
 * repete a informacao (cor nunca e' a unica pista, regra do Pickia).
 */
const TONE = {
  danger: { cls: 'border-red-500/30 bg-red-500/5 text-red-400', Icon: XCircle },
  ok:     { cls: 'border-green-500/30 bg-green-500/5 text-accent-ink', Icon: CheckCircle2 },
  warn:   { cls: 'border-amber-400/30 bg-amber-400/5 text-amber-400', Icon: AlertTriangle },
  info:   { cls: 'border-blue-400/30 bg-blue-400/5 text-blue-400', Icon: Info },
} as const

export default function Alert({
  tone = 'danger',
  children,
  className,
}: {
  tone?: keyof typeof TONE
  children: React.ReactNode
  className?: string
}) {
  const { cls, Icon } = TONE[tone]
  return (
    <div role={tone === 'danger' ? 'alert' : 'status'} className={cn('flex gap-2.5 rounded-md border px-4 py-3 text-sm', cls, className)}>
      <Icon className="w-4 h-4 mt-0.5 shrink-0" aria-hidden />
      <div className="min-w-0 text-ink-2">{children}</div>
    </div>
  )
}
