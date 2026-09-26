import { useState, type ReactNode } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { Menu, X } from 'lucide-react'
import { useAuth } from '../context/AuthContext'
import { cn } from '../lib/cn'
import { Button, Spinner } from './ui'

function Logo() {
  return (
    <Link to="/" className="flex items-center gap-2.5 font-bold text-ink-1 text-lg" aria-label="FPSX, página inicial">
      <svg viewBox="0 0 64 64" className="w-8 h-8" aria-hidden>
        <rect width="64" height="64" rx="14" className="fill-surface-2" />
        <polyline points="13,45 27,29 36,37 51,19" fill="none" stroke="currentColor" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" className="text-accent" />
      </svg>
      FPSX
    </Link>
  )
}

const LINKS = [
  { to: '/#como-funciona', label: 'Como funciona' },
  { to: '/planos', label: 'Planos' },
  { to: '/download', label: 'Download' },
  { to: '/#faq', label: 'Dúvidas' },
]

export function SiteHeader() {
  const { user } = useAuth()
  const [open, setOpen] = useState(false)
  const close = () => setOpen(false)

  return (
    <header className="sticky top-0 z-40 border-b border-line bg-surface-0/90 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-page items-center justify-between px-4 sm:px-6">
        <Logo />
        <nav className="hidden md:flex items-center gap-1" aria-label="Principal">
          {LINKS.map((l) => (
            <Link key={l.to} to={l.to} className="rounded-md px-3 py-2 text-sm text-ink-2 hover:bg-surface-2 hover:text-ink-1">{l.label}</Link>
          ))}
        </nav>
        <div className="hidden md:flex items-center gap-2">
          {user ? (
            <>
              {user.role === 'admin' && <Button variant="link" size="sm" to="/admin">Admin</Button>}
              <Button variant="ghost" size="sm" to="/conta">Minha conta</Button>
            </>
          ) : (
            <>
              <Button variant="link" size="sm" to="/entrar">Entrar</Button>
              <Button size="sm" to="/download">Analisar meu PC</Button>
            </>
          )}
        </div>
        <button className="md:hidden rounded-md p-2.5 text-ink-2 hover:bg-surface-2" onClick={() => setOpen(!open)} aria-label={open ? 'Fechar menu' : 'Abrir menu'} aria-expanded={open}>
          {open ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
        </button>
      </div>
      {open && (
        <nav className="md:hidden border-t border-line bg-surface-0 px-4 py-3 space-y-1" aria-label="Menu">
          {LINKS.map((l) => (
            <Link key={l.to} to={l.to} onClick={close} className="block rounded-md px-3 py-3 text-ink-2 hover:bg-surface-2">{l.label}</Link>
          ))}
          <div className="pt-2 grid gap-2">
            {user ? (
              <>
                <Button variant="ghost" to="/conta" onClick={close}>Minha conta</Button>
                {user.role === 'admin' && <Button variant="subtle" to="/admin" onClick={close}>Admin</Button>}
              </>
            ) : (
              <>
                <Button to="/download" onClick={close}>Analisar meu PC</Button>
                <Button variant="ghost" to="/entrar" onClick={close}>Entrar</Button>
              </>
            )}
          </div>
        </nav>
      )}
    </header>
  )
}

export function Footer() {
  return (
    <footer className="border-t border-line mt-20">
      <div className="mx-auto max-w-page px-4 sm:px-6 py-10 grid gap-8 sm:grid-cols-3 text-sm">
        <div className="space-y-3">
          <Logo />
          <p className="text-ink-3">Diagnóstico, otimização compatível, medição e transparência para PCs Windows.</p>
        </div>
        <div className="space-y-2">
          <p className="font-semibold text-ink-1">Produto</p>
          <Link to="/planos" className="block text-ink-3 hover:text-ink-1">Planos</Link>
          <Link to="/download" className="block text-ink-3 hover:text-ink-1">Download</Link>
          <Link to="/#o-que-nao-fazemos" className="block text-ink-3 hover:text-ink-1">O que não fazemos</Link>
        </div>
        <div className="space-y-2">
          <p className="font-semibold text-ink-1">Legal</p>
          <Link to="/privacidade" className="block text-ink-3 hover:text-ink-1">Privacidade</Link>
          <Link to="/termos" className="block text-ink-3 hover:text-ink-1">Termos de uso</Link>
        </div>
      </div>
      <p className="pb-8 text-center text-xs text-ink-4">FPSX {new Date().getFullYear()}. Resultados variam por PC e são sempre medidos, nunca prometidos.</p>
    </footer>
  )
}

export function PageShell({ children, narrow, className }: { children: ReactNode; narrow?: boolean; className?: string }) {
  return (
    <main className={cn('mx-auto px-4 sm:px-6 py-10 sm:py-14', narrow ? 'max-w-md' : 'max-w-page', className)}>
      {children}
    </main>
  )
}

export function PageTitle({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <div className="mb-8">
      <h1 className="text-3xl font-bold text-ink-1">{title}</h1>
      {subtitle && <p className="mt-2 text-ink-3">{subtitle}</p>}
    </div>
  )
}

/** Rota que exige login (e opcionalmente admin). Volta para onde estava depois de entrar. */
export function RequireAuth({ children, admin }: { children: ReactNode; admin?: boolean }) {
  const { user, loading } = useAuth()
  const location = useLocation()
  if (loading) return <div className="flex justify-center py-24"><Spinner className="w-6 h-6" /></div>
  if (!user) return <Navigate to={`/entrar?voltar=${encodeURIComponent(location.pathname + location.search)}`} replace />
  if (admin && user.role !== 'admin') return <Navigate to="/conta" replace />
  return <>{children}</>
}

export function Tabs<T extends string>({ tabs, value, onChange }: { tabs: { id: T; label: string }[]; value: T; onChange: (v: T) => void }) {
  return (
    <div className="mb-6 flex gap-1 overflow-x-auto border-b border-line" role="tablist">
      {tabs.map((t) => (
        <button
          key={t.id}
          role="tab"
          aria-selected={value === t.id}
          onClick={() => onChange(t.id)}
          className={cn('whitespace-nowrap px-4 py-3 text-sm font-medium border-b-2 -mb-px min-h-[44px]',
            value === t.id ? 'border-accent text-ink-1' : 'border-transparent text-ink-3 hover:text-ink-1')}
        >
          {t.label}
        </button>
      ))}
    </div>
  )
}
