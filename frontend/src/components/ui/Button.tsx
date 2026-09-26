import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { LucideIcon } from 'lucide-react'
import { cn } from '../../lib/cn'
import Spinner from './Spinner'

// Variante e tamanho sao eixos separados (licao do Pickia): trocar de
// variante nao muda a altura, e dois botoes lado a lado sempre alinham.
const VARIANT = {
  primary: 'bg-accent hover:bg-accent-hover text-black font-bold',
  ghost: 'border border-line-strong hover:border-ink-4 text-ink-2 hover:text-ink-1 font-medium',
  subtle: 'bg-surface-2 hover:bg-surface-3 text-ink-2 hover:text-ink-1 font-medium',
  danger: 'border border-danger/40 hover:border-danger/70 hover:bg-danger/10 text-danger font-semibold',
  link: 'text-ink-2 hover:text-ink-1 hover:bg-surface-2 font-semibold',
} as const

// Altura minima pensando em dedo: 44px e' o alvo para acao principal.
const SIZE = {
  sm: 'text-xs px-3 py-2 gap-1.5 rounded-md min-h-[36px]',
  md: 'text-sm px-4 py-2.5 gap-2 rounded-md min-h-[44px]',
  lg: 'text-base px-7 py-3.5 gap-2 rounded-lg min-h-[52px]',
} as const

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: keyof typeof VARIANT
  size?: keyof typeof SIZE
  block?: boolean
  loading?: boolean
  icon?: LucideIcon
  to?: string
  href?: string
  children?: ReactNode
}

const Button = forwardRef<HTMLButtonElement, Props>(function Button(
  { variant = 'primary', size = 'md', block, loading, icon: Icon, to, href, className, children, disabled, ...rest },
  ref,
) {
  const classes = cn(
    'inline-flex items-center justify-center transition-colors disabled:opacity-50 disabled:cursor-not-allowed',
    VARIANT[variant], SIZE[size], block && 'w-full', className,
  )
  const content = (
    <>
      {loading ? <Spinner /> : Icon ? <Icon className="w-4 h-4" aria-hidden /> : null}
      {children}
    </>
  )
  if (to) return <Link to={to} className={classes}>{content}</Link>
  if (href) return <a href={href} className={classes} target="_blank" rel="noreferrer">{content}</a>
  return (
    <button ref={ref} className={classes} disabled={disabled || loading} {...rest}>
      {content}
    </button>
  )
})

export default Button
