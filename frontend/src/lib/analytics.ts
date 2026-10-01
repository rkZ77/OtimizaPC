/*
 * Google Analytics 4, so' com consentimento (LGPD).
 *
 * O codigo G-XXXX nao fica no build: o servidor poe numa <meta name="rkzfps-ga">
 * quando a variavel GA_MEASUREMENT_ID existe no Railway (ver app/seo.py). Sem
 * a variavel, nada aqui roda e o aviso de cookies continua o de antes.
 *
 * O script do Google so' e' baixado depois de a pessoa tocar em "Aceitar
 * todos". "So' essenciais" ou nenhuma resposta: zero requisicao ao Google.
 * A troca de pagina do React e' medida pelo proprio GA4 (medicao otimizada,
 * que acompanha o historico do navegador), sem codigo a mais.
 */

declare global {
  interface Window {
    dataLayer?: unknown[]
    gtag?: (...args: unknown[]) => void
  }
}

const CHAVE = 'cookie_consent'
/** 'todos' = aceitou Analytics. 'essenciais' = recusou. '1' = resposta antiga, de quando so' havia cookie essencial. */
export type Consentimento = 'todos' | 'essenciais' | '1' | null

let iniciado = false

export function gaId(): string | null {
  const id = document.querySelector<HTMLMetaElement>('meta[name="rkzfps-ga"]')?.content ?? ''
  return /^G-[A-Z0-9]{4,12}$/.test(id) ? id : null
}

export function consentimento(): Consentimento {
  try {
    return localStorage.getItem(CHAVE) as Consentimento
  } catch {
    return null
  }
}

/** O aviso aparece para quem nunca respondeu, e de novo para quem so' viu o aviso antigo (sem a pergunta do Analytics). */
export function precisaPerguntar(): boolean {
  const c = consentimento()
  return !c || (c === '1' && gaId() !== null)
}

export function responder(valor: 'todos' | 'essenciais') {
  try { localStorage.setItem(CHAVE, valor) } catch { /* navegador sem armazenamento: vale so' nesta visita */ }
  window.dispatchEvent(new Event('cookie-consent-accepted'))
  if (valor === 'todos') iniciarAnalytics()
}

export function iniciarAnalytics() {
  const id = gaId()
  if (iniciado || !id || consentimento() !== 'todos') return
  iniciado = true
  window.dataLayer = window.dataLayer || []
  // O gtag precisa receber o objeto `arguments`, nao um array: e' o formato que o script do Google le.
  window.gtag = function gtag() {
    // eslint-disable-next-line prefer-rest-params
    window.dataLayer!.push(arguments)
  }
  window.gtag('js', new Date())
  window.gtag('config', id)
  const s = document.createElement('script')
  s.async = true
  s.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(id)}`
  document.head.appendChild(s)
}

/** Evento de conversao (cadastro, download, checkout). Sem consentimento, nao faz nada. */
export function evento(nome: string, params?: Record<string, string | number>) {
  window.gtag?.('event', nome, params ?? {})
}
