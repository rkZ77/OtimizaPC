import { useEffect, useState } from 'react'
import { precisaPerguntar } from '../lib/analytics'

// O CookieBanner é fixed bottom-0 e pode sobrepor inputs fixados no rodapé
// de outras páginas (ex: caixa de texto do chat). Paginas com conteudo
// interativo no rodapé usam isso pra reservar espaço enquanto ele estiver visivel.
export function useCookieBannerVisible(): boolean {
  const [visible, setVisible] = useState(precisaPerguntar)

  useEffect(() => {
    const handler = () => setVisible(false)
    window.addEventListener('cookie-consent-accepted', handler)
    return () => window.removeEventListener('cookie-consent-accepted', handler)
  }, [])

  return visible
}
