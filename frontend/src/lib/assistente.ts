import api from '../services/api'

/*
 * Ponte entre o menu lateral e o chat do assistente (AssistantChat): o menu
 * so' dispara o evento, e o chat, montado uma vez em App.tsx, abre. A consulta
 * de "esta' ligado?" e' uma so' por visita, dividida entre os dois.
 */
const EVENTO = 'rkz:abrir-assistente'

let ligado: Promise<boolean> | null = null

export function assistenteLigado(): Promise<boolean> {
  ligado ??= api.get<{ enabled: boolean }>('/public/assistant').then(({ data }) => data.enabled).catch(() => false)
  return ligado
}

export function abrirAssistente(): void {
  window.dispatchEvent(new Event(EVENTO))
}

export function aoAbrirAssistente(fn: () => void): () => void {
  window.addEventListener(EVENTO, fn)
  return () => window.removeEventListener(EVENTO, fn)
}
