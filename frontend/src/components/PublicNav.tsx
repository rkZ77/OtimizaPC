import { useCallback, useState } from 'react'
import { Button } from './ui'
import { PAGE_WIDTH, type PageWidth } from '../lib/pageWidth'
import { useAuth } from '../context/AuthContext'
import ThemeToggle from './ThemeToggle'
import Logo from './Logo'
import MenuLateral, { BotaoMenu } from './MenuLateral'
import MenuUsuario from './MenuUsuario'

/*
 * Barra das paginas internas (planos, download, conta, admin), no desenho da
 * PublicNav do Pickia: menu, logo, tema e duas saidas na ordem em que fazem
 * sentido. O menu lateral e' o mesmo da home.
 */
export default function PublicNav({
  width = 'wide',
  acoes,
}: {
  width?: PageWidth
  /** Substitui as acoes padrao (ex.: na propria tela de login). */
  acoes?: React.ReactNode
}) {
  const { user } = useAuth()
  const [menu, setMenu] = useState(false)
  const fechar = useCallback(() => setMenu(false), [])
  return (
    <nav className="border-b border-line/60 bg-surface-0/80 backdrop-blur-sm sticky top-0 z-40">
      <div className={`mx-auto h-14 flex items-center justify-between gap-3 ${PAGE_WIDTH[width]}`}>
        <div className="flex items-center gap-2 min-w-0">
          <BotaoMenu onClick={() => setMenu(true)} />
          <Logo />
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <ThemeToggle className="-ml-1" />
          {acoes ?? (user ? (
            <MenuUsuario />
          ) : (
            <>
              <Button to="/entrar" variant="link" size="sm">Entrar</Button>
              <Button to="/download" size="sm" className="hidden sm:inline-flex">Analisar meu PC</Button>
              {/* No celular a barra so' tinha "Entrar": quem ainda nao tem conta
                  ficava sem saida no topo de planos, download e como funciona. */}
              <Button to="/cadastro" size="sm" className="sm:hidden">Criar conta</Button>
            </>
          ))}
        </div>
      </div>
      <MenuLateral open={menu} onClose={fechar} />
    </nav>
  )
}
