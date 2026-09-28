import { Link, useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Home } from 'lucide-react'

const BOTAO = 'flex items-center justify-center w-11 h-11 rounded-full border border-line text-ink-3 hover:border-line-strong hover:text-ink-1 transition-colors shrink-0'

export default function BackButton({ to, className = '' }: { to?: string; className?: string }) {
  const navigate = useNavigate()
  /* Quem chega por link direto (Instagram, anuncio, e-mail) nao tem pagina
     anterior no site: o -1 tiraria a pessoa do RKZFPS. O React Router guarda a
     posicao no historico em `idx`; na primeira pagina ela e' 0, e ai' o voltar
     leva para o inicio. */
  const voltar = () => {
    if (to) return navigate(to)
    const idx = (window.history.state as { idx?: number } | null)?.idx ?? 0
    return idx > 0 ? navigate(-1) : navigate('/')
  }
  return (
    <motion.button
      whileTap={{ scale: 0.9 }}
      onClick={voltar}
      aria-label="Voltar"
      title="Voltar"
      className={`${BOTAO} ${className}`}
    >
      <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M15 18l-6-6 6-6" />
      </svg>
    </motion.button>
  )
}

export function HomeButton({ className = '' }: { className?: string }) {
  return (
    <Link to="/" aria-label="Página inicial" title="Página inicial" className={`${BOTAO} ${className}`}>
      <Home className="h-4 w-4" strokeWidth={2.25} />
    </Link>
  )
}

/** Voltar e ir ao inicio, lado a lado. Toda pagina fora da home tem os dois. */
export function NavVoltar({ to, className = '' }: { to?: string; className?: string }) {
  return (
    <div className={`flex items-center gap-2 shrink-0 ${className}`}>
      <BackButton to={to} />
      <HomeButton />
    </div>
  )
}
