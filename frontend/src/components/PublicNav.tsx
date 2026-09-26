import { Button } from './ui'
import { PAGE_WIDTH, type PageWidth } from '../lib/pageWidth'
import { useAuth } from '../context/AuthContext'
import ThemeToggle from './ThemeToggle'
import Logo from './Logo'

/*
 * Barra das paginas internas (planos, download, conta, admin), no desenho da
 * PublicNav do Pickia: logo, tema e duas saidas na ordem em que fazem sentido.
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
  return (
    <nav className="border-b border-line/60 bg-surface-0/80 backdrop-blur-sm sticky top-0 z-40">
      <div className={`mx-auto h-14 flex items-center justify-between gap-3 ${PAGE_WIDTH[width]}`}>
        <Logo />
        <div className="flex items-center gap-2 shrink-0">
          <ThemeToggle className="-ml-1" />
          {acoes ?? (user ? (
            <>
              {user.role === 'admin' && <Button to="/admin" variant="link" size="sm" className="hidden sm:inline-flex">Admin</Button>}
              <Button to="/conta" size="sm">Minha conta</Button>
            </>
          ) : (
            <>
              <Button to="/entrar" variant="link" size="sm">Entrar</Button>
              <Button to="/download" size="sm" className="hidden sm:inline-flex">Analisar meu PC</Button>
            </>
          ))}
        </div>
      </div>
    </nav>
  )
}
