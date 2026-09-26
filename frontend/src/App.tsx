import { useEffect } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import TopProgressBar from './components/TopProgressBar'
import ErrorToast from './components/ErrorToast'
import CookieBanner from './components/CookieBanner'
import { SpinnerBlock } from './components/ui'
import { useAuth } from './context/AuthContext'
import Home from './pages/Home'
import Planos from './pages/Planos'
import Download from './pages/Download'
import Conta from './pages/Conta'
import Admin from './pages/Admin'
import { Cadastro, Entrar } from './pages/Auth'
import { NotFound, Privacidade, Termos } from './pages/Legal'

/** Links como /#faq rolam ate' a secao; troca de pagina volta ao topo. */
function ScrollManager() {
  const { pathname, hash } = useLocation()
  useEffect(() => {
    if (hash) {
      // Espera a secao existir (a Home monta depois da rota trocar).
      const t = window.setTimeout(() => document.getElementById(hash.slice(1))?.scrollIntoView({ behavior: 'smooth' }), 50)
      return () => window.clearTimeout(t)
    }
    window.scrollTo(0, 0)
  }, [pathname, hash])
  return null
}

/** Rota que exige login (e opcionalmente admin). Volta para onde estava depois de entrar. */
function RequireAuth({ children, admin }: { children: React.ReactNode; admin?: boolean }) {
  const { user, loading } = useAuth()
  const location = useLocation()
  if (loading) return <SpinnerBlock />
  if (!user) return <Navigate to={`/entrar?voltar=${encodeURIComponent(location.pathname + location.search)}`} replace />
  if (admin && user.role !== 'admin') return <Navigate to="/conta" replace />
  return <>{children}</>
}

export default function App() {
  return (
    <>
      <TopProgressBar />
      <ScrollManager />
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
      <ErrorToast />
      <CookieBanner />
    </>
  )
}
