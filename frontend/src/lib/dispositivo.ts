/*
 * Onde a pessoa esta' vendo o site: no PC com Windows (instala ali mesmo) ou
 * em qualquer outro lugar (celular, tablet, Mac), onde o app nao roda.
 *
 * A maior parte das visitas vem do celular. Para essas, "Baixar para Windows"
 * baixava um .exe que o telefone nao abre, e a visita acabava ali sem deixar
 * contato. Fora do Windows o site oferece mandar o link para o PC.
 *
 * Pelo user agent e nao por largura de tela: janela estreita no PC continua
 * podendo instalar, e tablet largo continua sem poder.
 */

let cache: boolean | null = null

export function instalaAqui(): boolean {
  if (cache !== null) return cache
  const ua = typeof navigator === 'undefined' ? '' : navigator.userAgent
  // Windows de celular nao existe mais; "Mobile" cobre o modo desktop do Chrome no Android.
  cache = /Windows NT/i.test(ua) && !/Mobile|Android|iPhone|iPad/i.test(ua)
  return cache
}
