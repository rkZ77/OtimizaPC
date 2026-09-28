import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { Link, useLocation } from 'react-router-dom'
import { AnimatePresence, motion } from 'framer-motion'
import {
  BookOpen, Crown, Download, FileText, Gamepad2, Headphones, HelpCircle, Home, Info, LogIn,
  Menu as MenuIcon, Receipt, Settings, ShieldCheck, Sparkles, User, X,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useAuth } from '../context/AuthContext'
import { SUPPORT_IS_EXTERNAL, SUPPORT_URL } from '../lib/support'
import { abrirAssistente, assistenteLigado } from '../lib/assistente'
import { cn } from '../lib/cn'
import { LogoMark } from './Logo'

/*
 * Menu lateral do site, no desenho do MenuLateral do Pickia: gaveta pela
 * esquerda, icone em cada item e itens agrupados pelo que a pessoa procura
 * (o produto, a conta, a ajuda) em vez de uma fila so'. O mesmo menu no
 * celular e no PC, em todas as paginas.
 *
 * A entrada na conta fica destacada com borda porque e' o unico item que muda
 * o estado da pessoa; o resto so' navega.
 */

type Item = { label: string; Icon: LucideIcon; to?: string; href?: string; onClick?: () => void }

const MENU: Item[] = [
  { label: 'Início', Icon: Home, to: '/' },
  { label: 'Como funciona', Icon: BookOpen, to: '/como-funciona' },
  { label: 'Jogos reconhecidos', Icon: Gamepad2, to: '/como-funciona#jogos' },
  { label: 'Planos', Icon: Crown, to: '/planos' },
  { label: 'Download', Icon: Download, to: '/download' },
]

const ITEM = 'relative w-full flex items-center gap-3 px-3 min-h-[44px] rounded-md text-sm font-medium text-left transition-colors duration-1 ease-smooth'

function ItemDaGaveta({ label, Icon, to, href, onClick, ativo = false }: Item & { ativo?: boolean }) {
  const cls = cn(ITEM, ativo ? 'text-ink-1 bg-surface-2/60' : 'text-ink-2 hover:text-ink-1 hover:bg-surface-2/60')
  const conteudo = (
    <>
      <Icon className={cn('w-[18px] h-[18px] shrink-0', ativo ? 'text-accent-ink' : 'text-ink-3')} aria-hidden />
      <span>{label}</span>
    </>
  )
  if (href) {
    const externo = href.startsWith('http')
    return <a href={href} onClick={onClick} className={cls} {...(externo ? { target: '_blank', rel: 'noopener noreferrer' } : {})}>{conteudo}</a>
  }
  if (to) return <Link to={to} onClick={onClick} aria-current={ativo ? 'page' : undefined} className={cls}>{conteudo}</Link>
  return <button type="button" onClick={onClick} className={cls}>{conteudo}</button>
}

function Secao({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div className="px-3 pt-4">
      <p className="px-3 pb-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-ink-4">{titulo}</p>
      <div className="space-y-0.5">{children}</div>
    </div>
  )
}

export default function MenuLateral({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { user } = useAuth()
  const { pathname } = useLocation()
  const [assistente, setAssistente] = useState(false)

  useEffect(() => { assistenteLigado().then(setAssistente) }, [])
  useEffect(() => { onClose() }, [pathname]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!open) return
    document.body.style.overflow = 'hidden'
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => {
      document.body.style.overflow = ''
      window.removeEventListener('keydown', onKey)
    }
  }, [open, onClose])

  const ajuda: Item[] = [
    ...(assistente ? [{ label: 'Assistente com IA', Icon: Sparkles, onClick: () => { onClose(); abrirAssistente() } }] : []),
    { label: 'Suporte', Icon: Headphones, ...(SUPPORT_IS_EXTERNAL ? { href: SUPPORT_URL } : { to: SUPPORT_URL }) },
    { label: 'Perguntas frequentes', Icon: HelpCircle, to: '/#faq' },
    { label: 'Quem somos', Icon: Info, to: '/quem-somos' },
    { label: 'Termos de uso', Icon: FileText, to: '/termos' },
    { label: 'Privacidade', Icon: ShieldCheck, to: '/privacidade' },
  ]

  // Portal no body: o cabecalho tem backdrop-blur, e blur vira referencia de
  // posicao para `fixed`. Sem o portal a gaveta ficava presa nos 64px do topo.
  return createPortal(
    <AnimatePresence>
      {open && (
        <div className="fixed inset-0 z-[60]">
          <motion.button
            type="button" aria-label="Fechar menu" onClick={onClose}
            initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} transition={{ duration: 0.2 }}
            className="absolute inset-0 bg-black/50 backdrop-blur-md cursor-default"
          />
          <motion.aside
            role="dialog" aria-modal="true" aria-label="Menu"
            initial={{ x: '-100%' }} animate={{ x: 0 }} exit={{ x: '-100%' }}
            transition={{ duration: 0.25, ease: [0.2, 0, 0, 1] }}
            className="absolute inset-y-0 left-0 w-[min(300px,85vw)] flex flex-col bg-surface-0 border-r border-line shadow-elev"
          >
            <div className="h-16 shrink-0 flex items-center justify-between px-5 border-b border-line">
              <Link to="/" onClick={onClose} className="flex items-center gap-2.5" aria-label="RKZFPS, início">
                <LogoMark className="w-7 h-7" />
                <span className="font-display font-extrabold italic text-base tracking-tight text-ink-1">
                  RKZ<span className="text-accent-ink">FPS</span>
                </span>
              </Link>
              <button
                type="button" onClick={onClose} aria-label="Fechar menu"
                className="w-9 h-9 rounded-full border border-line flex items-center justify-center text-ink-3 hover:text-ink-1 hover:border-line-strong transition-colors duration-1 ease-smooth"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <nav className="flex-1 overflow-y-auto pb-6" aria-label="Navegação principal">
              <Secao titulo="Menu">
                {MENU.map((item) => <ItemDaGaveta key={item.label} {...item} ativo={pathname === item.to} onClick={onClose} />)}
                <Link
                  to={user ? '/conta' : '/entrar'} onClick={onClose}
                  className="mt-2 flex items-center gap-3 px-3 min-h-[44px] rounded-md border border-line-strong bg-surface-1 text-sm font-semibold text-ink-1 hover:bg-surface-2 transition-colors duration-1 ease-smooth"
                >
                  {user ? <User className="w-[18px] h-[18px] shrink-0 text-ink-2" aria-hidden /> : <LogIn className="w-[18px] h-[18px] shrink-0 text-ink-2" aria-hidden />}
                  <span>{user ? 'Minha conta' : 'Entrar'}</span>
                </Link>
              </Secao>

              {user && (
                <Secao titulo="Sua conta">
                  <ItemDaGaveta label="Meu plano" Icon={Crown} to="/meu-plano" ativo={pathname === '/meu-plano'} onClick={onClose} />
                  <ItemDaGaveta label="Pagamentos" Icon={Receipt} to="/meu-plano#pagamentos" onClick={onClose} />
                  {user.role === 'admin' && <ItemDaGaveta label="Admin" Icon={Settings} to="/admin" onClick={onClose} />}
                </Secao>
              )}

              <Secao titulo="Ajuda">
                {ajuda.map((item) => <ItemDaGaveta key={item.label} {...item} ativo={pathname === item.to} onClick={item.onClick ?? onClose} />)}
              </Secao>
            </nav>

            {!user && (
              <div className="shrink-0 p-4 border-t border-line">
                <Link
                  to="/download" onClick={onClose}
                  className="flex items-center justify-center gap-2 min-h-[44px] rounded-md bg-accent hover:bg-accent-hover text-on-fill text-sm font-bold transition-colors duration-1 ease-smooth"
                >
                  <Download className="w-4 h-4" aria-hidden /> Baixar grátis
                </Link>
              </div>
            )}
          </motion.aside>
        </div>
      )}
    </AnimatePresence>,
    document.body,
  )
}

/** Botao que abre o menu, a esquerda do logo (como no Pickia). */
export function BotaoMenu({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button" onClick={onClick} aria-label="Abrir menu"
      className="-ml-2 p-2 rounded-md text-ink-2 hover:text-ink-1 hover:bg-surface-2/60 transition-colors duration-1 ease-smooth"
    >
      <MenuIcon className="w-5 h-5" aria-hidden />
    </button>
  )
}
