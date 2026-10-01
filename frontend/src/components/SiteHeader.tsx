import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { Button } from './ui'
import { cn } from '../lib/cn'
import { instalaAqui } from '../lib/dispositivo'
import ThemeToggle from './ThemeToggle'
import Logo from './Logo'
import MenuLateral, { BotaoMenu } from './MenuLateral'

/*
 * Cabeçalho da home (padrão do Pickia).
 *
 * Começa transparente sobre o hero e só ganha fundo, blur e borda depois que a
 * página rola. O gatilho é 8px, não 0: em iOS o scroll elástico devolve valores
 * negativos e um limiar em 0 fazia a barra piscar ao puxar a página pra baixo.
 *
 * O menu completo mora na gaveta lateral (MenuLateral), igual no celular e no
 * PC; a fila de links no topo é só o atalho de quem está no desktop.
 */

const LINKS = [
  { to: '/como-funciona', label: 'Como funciona' },
  { to: '/como-funciona#analisa', label: 'O que analisa' },
  { to: '/planos', label: 'Planos' },
  { to: '/download', label: 'Download' },
  { to: '/#faq', label: 'Dúvidas' },
]

export default function SiteHeader() {
  const [scrolled, setScrolled] = useState(false)
  const [menu, setMenu] = useState(false)
  const fechar = useCallback(() => setMenu(false), [])
  const { user } = useAuth()
  const aqui = instalaAqui()

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  return (
    <header
      className={cn(
        'fixed inset-x-0 top-0 z-50 transition-all duration-2 ease-smooth',
        scrolled
          ? 'bg-surface-0/80 backdrop-blur-xl border-b border-line shadow-elev-sm'
          : 'bg-transparent border-b border-transparent',
      )}
    >
      <div className="max-w-6xl mx-auto px-4 h-16 flex items-center justify-between gap-4">
        <div className="flex items-center gap-2 min-w-0">
          <BotaoMenu onClick={() => setMenu(true)} />
          <Logo />
        </div>

        <nav className="hidden lg:flex items-center gap-1" aria-label="Atalhos">
          {LINKS.map(({ to, label }) => (
            <Link
              key={to}
              to={to}
              className="text-ink-2 hover:text-ink-1 text-sm font-medium px-3 py-2 rounded-md hover:bg-surface-2/60 transition-colors duration-1 ease-smooth"
            >
              {label}
            </Link>
          ))}
        </nav>

        <div className="flex items-center gap-2 shrink-0">
          <ThemeToggle className="-ml-1" />
          {user ? (
            <Button to="/painel" size="sm">Minha conta</Button>
          ) : (
            <>
              <Button to="/entrar" variant="link" size="sm" className="hidden sm:inline-flex">Entrar</Button>
              {/* Fora do Windows o topo leva ao cadastro: o download nao roda
                  ali, e a conta criada no celular ja' guarda o teste. */}
              <Button to={aqui ? '/download' : '/cadastro'} size="sm">
                <span className="hidden sm:inline">{aqui ? 'Analisar meu PC' : 'Criar conta grátis'}</span>
                <span className="sm:hidden">{aqui ? 'Baixar grátis' : 'Criar conta'}</span>
              </Button>
            </>
          )}
        </div>
      </div>

      <MenuLateral open={menu} onClose={fechar} />
    </header>
  )
}
