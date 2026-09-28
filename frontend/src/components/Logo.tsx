import { Link } from 'react-router-dom'
import { cn } from '../lib/cn'

/**
 * Marca do RKZFPS: o R anguloso da arte. E' o mesmo arquivo do icone da aba
 * (/favicon.svg): a arte fica num lugar so', sem cor solta no componente.
 */
export function LogoMark({ className }: { className?: string }) {
  return <img src="/favicon.svg" alt="" aria-hidden width={32} height={32} className={cn('w-8 h-8', className)} />
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
