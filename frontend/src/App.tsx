import { useEffect } from 'react'
import { Route, Routes, useLocation } from 'react-router-dom'
import { Footer, RequireAuth, SiteHeader } from './components/Layout'
import Home from './pages/Home'
import Planos from './pages/Planos'
import Download from './pages/Download'
import Conta from './pages/Conta'
import Admin from './pages/Admin'
import { Cadastro, Entrar } from './pages/Auth'
import { NotFound, Privacidade, Termos } from './pages/Legal'

/** Links como /#faq rolam até a seção; troca de página volta ao topo. */
function ScrollManager() {
  const { pathname, hash } = useLocation()
  useEffect(() => {
    if (hash) {
      document.getElementById(hash.slice(1))?.scrollIntoView({ behavior: 'smooth' })
    } else {
      window.scrollTo(0, 0)
    }
  }, [pathname, hash])
  return null
}

export default function App() {
  return (
    <div className="flex min-h-screen flex-col">
      <ScrollManager />
      <SiteHeader />
      <div className="flex-1">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/planos" element={<Planos />} />
          <Route path="/download" element={<Download />} />
          <Route path="/entrar" element={<Entrar />} />
          <Route path="/cadastro" element={<Cadastro />} />
          <Route path="/privacidade" element={<Privacidade />} />
          <Route path="/termos" element={<Termos />} />
          <Route path="/conta" element={<RequireAuth><Conta /></RequireAuth>} />
          <Route path="/admin" element={<RequireAuth admin><Admin /></RequireAuth>} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </div>
      <Footer />
    </div>
  )
}
