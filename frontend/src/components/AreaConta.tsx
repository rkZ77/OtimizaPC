import { Link, useLocation } from 'react-router-dom'
import { Crown, Home, Laptop, Settings } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useAuth } from '../context/AuthContext'
import { cn } from '../lib/cn'

/*
 * Abas da area logada, iguais nas tres telas da conta.
 *
 * Antes cada tela era uma ilha: Minha conta e Meu plano repetiam o mesmo
 * cartao de plano, e para ir de uma a outra era preciso abrir o menu. Agora
 * ha' um inicio (o painel) e duas telas de detalhe, sempre a um toque.
 */

const ABAS: { to: string; label: string; Icon: LucideIcon }[] = [
  { to: '/painel', label: 'Início', Icon: Home },
  { to: '/meu-plano', label: 'Meu plano', Icon: Crown },
  { to: '/conta', label: 'PCs e medições', Icon: Laptop },
]

export default function AreaConta() {
  const { pathname } = useLocation()
  const { user } = useAuth()
  const abas = user?.role === 'admin' ? [...ABAS, { to: '/admin', label: 'Admin', Icon: Settings }] : ABAS

  return (
    <nav aria-label="Sua conta" className="border-b border-line">
      <div className="mx-auto flex max-w-6xl gap-1 overflow-x-auto px-4 scrollbar-none">
        {abas.map(({ to, label, Icon }) => {
          const ativa = pathname === to
          return (
            <Link key={to} to={to} aria-current={ativa ? 'page' : undefined}
                  className={cn('tab inline-flex min-h-[44px] shrink-0 items-center gap-2 px-3 text-sm font-semibold', ativa && 'tab-active')}>
              <Icon className={cn('h-4 w-4', ativa ? 'text-accent-ink' : 'text-ink-4')} aria-hidden />
              {label}
            </Link>
          )
        })}
      </div>
    </nav>
  )
}
