import { Link } from 'react-router-dom'
import { cn } from '../lib/cn'

/** Marca do RKZFPS: o grafico subindo do icone do app, na mesma cor de marca. */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 64 64" className={cn('w-8 h-8', className)} aria-hidden>
      <rect width="64" height="64" rx="14" className="fill-surface-2" />
      <polyline points="13,45 27,29 36,37 51,19" fill="none" stroke="rgb(var(--accent))" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

export default function Logo({ className }: { className?: string }) {
  return (
    <Link to="/" className={cn('flex items-center gap-2.5 shrink-0', className)} aria-label="RKZFPS, início">
      <LogoMark />
      {/* Como na arte da marca: RKZ na cor do texto, FPS no ciano, em italico. */}
      <span className="font-display font-extrabold italic text-lg tracking-tight text-ink-1">
        RKZ<span className="text-accent-ink">FPS</span>
      </span>
    </Link>
  )
}
