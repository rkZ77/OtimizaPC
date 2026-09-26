import axios, { AxiosError } from 'axios'
import { requisicaoIniciou, requisicaoTerminou } from './progressBus'
import { notifyError } from './errorToast'

// Cookie httpOnly de sessao vai e volta sozinho (withCredentials).
const api = axios.create({ baseURL: '/api', withCredentials: true, timeout: 15000 })

/* Mesmo desenho do Pickia: toda requisicao conta para a barra de progresso
   do topo (so' conta, nenhuma decisao de UI acontece aqui), e falha de rede
   ou 5xx em LEITURA vira aviso na tela em vez de sumir num .catch vazio.
   Erro de formulario (4xx em POST/PUT) fica com a propria tela, que mostra a
   mensagem no lugar certo. */
api.interceptors.request.use((config) => {
  requisicaoIniciou()
  return config
})

api.interceptors.response.use(
  (response) => {
    requisicaoTerminou()
    return response
  },
  (error: AxiosError) => {
    requisicaoTerminou()
    const leitura = (error.config?.method ?? 'get').toLowerCase() === 'get'
    const falhaDeServidor = !error.response || error.response.status >= 500
    const sessao = error.config?.url === '/auth/me'
    if (leitura && falhaDeServidor && !sessao) {
      notifyError(errorMessage(error), error.response?.headers?.['x-request-id'] as string | undefined)
    }
    return Promise.reject(error)
  },
)

/**
 * Mensagem amigavel para qualquer erro de API. Erro tecnico (stack, 500,
 * "undefined") nunca chega ao usuario: vira uma frase em pt-BR.
 */
export function errorMessage(err: unknown, fallback = 'Não foi possível concluir. Tente de novo em instantes.'): string {
  if (err instanceof AxiosError) {
    if (!err.response) return 'Sem conexão com o servidor. Verifique sua internet.'
    const detail = (err.response.data as { detail?: unknown } | undefined)?.detail
    if (typeof detail === 'string') return detail
    if (detail && typeof detail === 'object' && 'message' in detail && typeof detail.message === 'string') return detail.message
    // Erro de validacao do FastAPI (422) vem como lista.
    if (Array.isArray(detail)) return 'Confira os campos e tente de novo.'
    if (err.response.status >= 500) return fallback
  }
  return fallback
}

export default api

// ─── tipos das respostas ────────────────────────────────────────────────

export interface User {
  id: number
  email: string
  name: string
  role: 'user' | 'admin'
  active: boolean
  created_at: string
}

export interface Plan {
  key: string
  name: string
  tier: string
  description: string
  price_cents: number
  days: number
  max_devices: number
  features: string[]
  /** none (Free), monthly, quarterly, annual. */
  period: 'none' | 'monthly' | 'quarterly' | 'annual'
  /** Calculados no servidor: o front nao calcula preco. */
  per_month_cents: number
  savings_percent: number
}

export interface PlansResponse {
  currency: string
  trial_days: number
  plans: Plan[]
}

export interface Device {
  id: number
  name: string
  windows_build: string
  agent_version: string
  activated_at: string
  last_seen_at: string
}

export interface LicenseView {
  id?: number
  plan_key: string
  tier: string
  status: string
  expires_at: string | null
  max_devices: number
}

export interface Overview {
  user: User
  license: LicenseView
  plan_name: string
  devices: Device[]
}

export interface Release {
  component: string
  version: string
  url: string
  sha256: string
  notes: string
  published_at: string
}
