import type { InputHTMLAttributes, ReactNode } from 'react'
import { cn } from '../../lib/cn'

export { default as Button } from './Button'
export { default as Spinner } from './Spinner'

export function Card({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={cn('rounded-xl border border-line bg-surface-1 p-5 sm:p-6', className)}>{children}</div>
}

const TONE = {
  ok: 'bg-accent/15 text-accent-ink',
  warn: 'bg-warn/15 text-warn',
  danger: 'bg-danger/15 text-danger',
  info: 'bg-info/15 text-info',
  muted: 'bg-surface-3 text-ink-3',
} as const

export function Badge({ tone = 'muted', children }: { tone?: keyof typeof TONE; children: ReactNode }) {
  return <span className={cn('inline-flex items-center rounded-md px-2 py-0.5 text-xs font-bold uppercase tracking-wide', TONE[tone])}>{children}</span>
}

interface FieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
  hint?: string
}

export function Field({ label, hint, id, className, ...rest }: FieldProps) {
  const inputId = id ?? rest.name
  return (
    <label htmlFor={inputId} className="block">
      <span className="mb-1.5 block text-sm font-medium text-ink-2">{label}</span>
      <input
        id={inputId}
        className={cn('w-full rounded-md border border-line bg-surface-2 px-3 py-2.5 text-ink-1 placeholder:text-ink-4 focus:border-info focus:outline-none min-h-[44px]', className)}
        {...rest}
      />
      {hint && <span className="mt-1 block text-xs text-ink-3">{hint}</span>}
    </label>
  )
}

export function Alert({ tone = 'danger', children }: { tone?: 'danger' | 'ok' | 'warn' | 'info'; children: ReactNode }) {
  const tones = {
    danger: 'border-danger/40 bg-danger/10 text-danger',
    ok: 'border-accent/40 bg-accent/10 text-accent-ink',
    warn: 'border-warn/40 bg-warn/10 text-warn',
    info: 'border-info/40 bg-info/10 text-info',
  }
  return <div role="alert" className={cn('rounded-md border px-4 py-3 text-sm', tones[tone])}>{children}</div>
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="rounded-xl border border-dashed border-line p-8 text-center">
      <p className="font-semibold text-ink-1">{title}</p>
      {children && <div className="mt-2 text-sm text-ink-3">{children}</div>}
    </div>
  )
}
