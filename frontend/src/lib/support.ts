/**
 * Canal de suporte do RKZFPS, UM lugar so' (padrao do Pickia).
 *
 * Vem de VITE_SUPPORT_URL no build (ex.: link do WhatsApp Business do RKZFPS).
 * Sem ele, o suporte cai na area do cliente: nunca no canal de outro produto.
 */
export const SUPPORT_URL: string = import.meta.env.VITE_SUPPORT_URL || '/conta'

export const SUPPORT_IS_EXTERNAL = SUPPORT_URL.startsWith('http')

/** Instagram oficial do RKZFPS: novidades de versão e dicas, no rodapé do site e no app. */
export const INSTAGRAM_URL = 'https://www.instagram.com/rkzfps.br'
