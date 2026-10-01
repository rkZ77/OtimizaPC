import { useEffect, useRef, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { ChevronDown, Crown, Home, Laptop, LogOut, Pencil, Settings } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useAuth } from '../context/AuthContext'
import { Badge } from './ui'
import { cn } from '../lib/cn'

/*
 * Quem esta' logado no topo do site, como na PickIA: avatar, primeiro nome e o
 * selo ADMIN, e um menu com a conta. Antes era so' um botao "Minha conta", e
 * a pessoa nao tinha certeza de que estava logada nem com qual e-mail.
 *
 * O selo e o item Admin so' existem para admin: usuario comum nao ve nem o
 * caminho (e a rota /admin recusa do mesmo jeito).
 */

export function iniciais(nome: string | undefined, email: string): string {
  const base = (nome || '').trim() || email.split('@')[0]
  const partes = base.split(/\s+/).filter(Boolean)
  return (partes.length > 1 ? partes[0][0] + partes[1][0] : base.slice(0, 2)).toUpperCase()
}

export function Avatar({ nome, email, className }: { nome?: string; email: string; className?: string }) {
  return (
    <span aria-hidden className={cn('grid shrink-0 place-items-center rounded-full border border-accent/40 bg-accent/10 font-display font-bold text-accent-ink', className)}>
      {iniciais(nome, email)}
    </span>
  )
}

export default function MenuUsuario() {
  const { user, logout } = useAuth()
  const [aberto, setAberto] = useState(false)
  const caixa = useRef<HTMLDivElement>(null)
  const { pathname } = useLocation()
  const navigate = useNavigate()

  useEffect(() => { setAberto(false) }, [pathname])
  useEffect(() => {
    if (!aberto) return
    const fora = (e: MouseEvent) => { if (!caixa.current?.contains(e.target as Node)) setAberto(false) }
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setAberto(false) }
    document.addEventListener('mousedown', fora)
    window.addEventListener('keydown', esc)
    return () => { document.removeEventListener('mousedown', fora); window.removeEventListener('keydown', esc) }
  }, [aberto])

  if (!user) return null
  const admin = user.role === 'admin'
  const primeiro = (user.name || '').trim().split(/\s+/)[0] || user.email.split('@')[0]

  const itens: { to: string; label: string; Icon: LucideIcon }[] = [
    { to: '/painel', label: 'Minha conta', Icon: Home },
    { to: '/meu-plano', label: 'Meu plano', Icon: Crown },
    { to: '/conta', label: 'PCs e medições', Icon: Laptop },
    { to: '/painel?perfil=1', label: 'Editar perfil', Icon: Pencil },
    ...(admin ? [{ to: '/admin', label: 'Admin', Icon: Settings }] : []),
  ]

  const sair = async () => {
    setAberto(false)
    await logout()
    navigate('/')
  }

  return (
    <div ref={caixa} className="relative">
      <button type="button" onClick={() => setAberto((v) => !v)} aria-expanded={aberto} aria-haspopup="menu"
              aria-label={`Conta de ${primeiro}`}
              className="flex min-h-[40px] items-center gap-2 rounded-full py-1 pl-1 pr-2 transition-colors duration-1 hover:bg-surface-2/60">
        <Avatar nome={user.name} email={user.email} className="h-8 w-8 text-xs" />
        <span className="hidden max-w-[120px] truncate text-sm font-semibold text-ink-1 sm:inline">{primeiro}</span>
        {admin && <Badge tone="purple" className="hidden sm:inline-flex">ADMIN</Badge>}
        <ChevronDown className={cn('hidden h-4 w-4 text-ink-3 transition-transform sm:block', aberto && 'rotate-180')} aria-hidden />
      </button>

      {aberto && (
        <div role="menu" className="absolute right-0 top-full z-50 mt-2 w-60 overflow-hidden rounded-lg border border-line bg-surface-1 shadow-elev">
          <div className="border-b border-line px-4 py-3">
            <p className="flex items-center gap-2 truncate font-semibold text-ink-1">
              <span className="truncate">{user.name?.trim() || primeiro}</span>
              {admin && <Badge tone="purple">ADMIN</Badge>}
            </p>
            <p className="truncate text-xs text-ink-3">{user.email}</p>
          </div>
          <div className="py-1">
            {itens.map(({ to, label, Icon }) => (
              <Link key={to} to={to} role="menuitem" onClick={() => setAberto(false)}
                    className="flex min-h-[40px] items-center gap-3 px-4 text-sm text-ink-2 transition-colors hover:bg-surface-2 hover:text-ink-1">
                <Icon className="h-4 w-4 text-ink-3" aria-hidden />{label}
              </Link>
            ))}
          </div>
          <button type="button" role="menuitem" onClick={sair}
                  className="flex min-h-[40px] w-full items-center gap-3 border-t border-line px-4 text-sm text-ink-2 transition-colors hover:bg-surface-2 hover:text-ink-1">
            <LogOut className="h-4 w-4 text-ink-3" aria-hidden />Sair
          </button>
        </div>
      )}
    </div>
  )
}
