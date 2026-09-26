/**
 * Canal de suporte do FPSX, UM lugar so' (padrao do Pickia).
 *
 * Vem de VITE_SUPPORT_URL no build (ex.: link do WhatsApp Business do FPSX).
 * Sem ele, o suporte cai na area do cliente: nunca no canal de outro produto.
 */
export const SUPPORT_URL: string = import.meta.env.VITE_SUPPORT_URL || '/conta'

export const SUPPORT_IS_EXTERNAL = SUPPORT_URL.startsWith('http')
