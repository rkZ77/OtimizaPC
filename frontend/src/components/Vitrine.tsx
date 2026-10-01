import { useEffect, useState, type CSSProperties, type ReactNode } from 'react'

/*
 * Pecas da vitrine usadas na home e na pagina Como funciona: print real do
 * app, linha de texto com print ao lado e a grade de jogos reconhecidos.
 * Num lugar so' para as duas paginas nao contarem historias diferentes.
 */

export function Screenshot({ src, alt }: { src: string; alt: string }) {
  return (
    <figure className="min-w-0">
      <div className="tela-app">
        <img src={src} alt={alt} width={1180} height={780} loading="lazy" className="tela-app-img" />
      </div>
      <figcaption className="mt-3 text-xs text-ink-4">Tela real do app, sem edição.</figcaption>
    </figure>
  )
}

/* `step` vira o numero em destaque ao lado do titulo: o "1." escrito dentro do
 * titulo se perdia no meio do texto e nao dava para ver a sequencia de longe. */
export function Row({ title, children, img, alt, flip, step }: { title: string; children: ReactNode; img: string; alt: string; flip?: boolean; step?: number }) {
  return (
    <div className="grid items-center gap-8 lg:grid-cols-2 lg:gap-14">
      <div className={flip ? 'lg:order-2' : undefined}>
        {step != null && (
          <span aria-hidden className="mb-4 grid h-9 w-9 place-items-center rounded-full border border-accent/40 bg-accent/10 font-display text-sm font-bold text-accent-ink">
            {step}
          </span>
        )}
        <h3 className="font-display text-2xl font-bold text-ink-1">{step != null && <span className="sr-only">{step}. </span>}{title}</h3>
        <div className="mt-4 space-y-3 text-ink-2 leading-relaxed">{children}</div>
      </div>
      <Screenshot src={img} alt={alt} />
    </div>
  )
}

/* Icone de cada jogo em /img/games (baixado uma vez e servido pelo proprio
 * site). Sem icone, o quadro mostra as iniciais.
 * 128 px para um quadro de 36: em tela de celular (2x a 3x) o icone de 32 px
 * da Steam ficava esticado e borrado. Os da Steam sao recorte da capa oficial
 * (600x900) em volta do logo; os outros vem do icone do app do jogo. */
export const GAMES: Array<[name: string, icon?: string]> = [
  ['Counter-Strike 2', 'cs2.webp'], ['EA SPORTS FC', 'eafc.webp'], ['Fortnite', 'fortnite.webp'], ['Valorant', 'valorant.webp'],
  ['League of Legends', 'lol.svg'], ['Minecraft', 'minecraft.webp'], ['Roblox', 'roblox.webp'], ['GTA V', 'gta5.webp'],
  ['Apex Legends', 'apex.webp'], ['Call of Duty', 'warzone.webp'], ['PUBG', 'pubg.webp'], ['Rainbow Six Siege', 'r6.webp'],
  ['Dota 2', 'dota2.webp'], ['Rocket League', 'rocketleague.webp'], ['Marvel Rivals', 'marvelrivals.webp'],
]

/* Duas letras do nome: "Counter-Strike 2" vira CS, "GTA V" vira GV. */
function initials(name: string) {
  const words = name.split(/[\s:-]+/).filter((w) => /^[\p{L}\d]/u.test(w))
  return words.length === 1 ? words[0].slice(0, 2).toUpperCase() : (words[0][0] + words[1][0]).toUpperCase()
}

export function GameGrid() {
  return (
    <>
      <ul className="mt-6 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
        {GAMES.map(([g, icon]) => (
          <li key={g} className="flex items-center gap-3 rounded-lg border border-line bg-surface-0 px-3 py-2.5 transition-colors duration-1 hover:border-line-strong hover:bg-surface-1">
            {icon ? (
              <img src={`/img/games/${icon}`} alt="" width={36} height={36} loading="lazy"
                   className="h-9 w-9 shrink-0 rounded-md border border-line bg-surface-2 object-cover" />
            ) : (
              <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-md border border-line bg-surface-2 font-display text-sm font-bold text-accent-ink">
                {initials(g)}
              </span>
            )}
            <span className="min-w-0 text-sm leading-tight text-ink-2">{g}</span>
          </li>
        ))}
      </ul>
      <p className="mt-4 text-xs text-ink-4">Os nomes dos jogos são marcas dos seus donos. O RKZFPS é independente e não tem parceria com os estúdios.</p>
    </>
  )
}

/*
 * Hero em movimento com as telas REAIS: diagnostico, otimizacoes, jogos e
 * partidas, na ordem em que a pessoa usa o app. Troca sozinha e para quando
 * o mouse esta' em cima ou quando o sistema pede menos movimento.
 */
const TOUR: { src: string; t: string; alt: string }[] = [
  { src: '/img/app-dashboard.png', t: 'Diagnóstico', alt: 'Tela inicial do RKZFPS com o diagnóstico do PC' },
  { src: '/img/app-otimizacoes.png', t: 'Correções', alt: 'Tela de otimizações do RKZFPS, com o motivo de cada uma' },
  { src: '/img/app-jogos.png', t: 'Jogos', alt: 'Tela de jogos do RKZFPS' },
  { src: '/img/app-partidas.png', t: 'Partidas', alt: 'Tela de partidas do RKZFPS com o FPS medido' },
]

const TOUR_MS = 4500

export function ScreenTour() {
  const [i, setI] = useState(0)
  const [parado, setParado] = useState(false)
  const [calmo] = useState(() => Boolean(window.matchMedia?.('(prefers-reduced-motion: reduce)').matches))
  useEffect(() => {
    if (parado || calmo) return
    const t = window.setInterval(() => setI((n) => (n + 1) % TOUR.length), TOUR_MS)
    return () => window.clearInterval(t)
  }, [parado, calmo])

  return (
    <figure className="min-w-0" onMouseEnter={() => setParado(true)} onMouseLeave={() => setParado(false)}>
      <div className="tela-app">
        <div className="relative aspect-[1180/780] overflow-hidden rounded-lg border border-line-strong bg-surface-1 shadow-elev">
          {TOUR.map((s, n) => (
            <img key={s.src} src={s.src} alt={s.alt} width={1180} height={780} loading={n === 0 ? 'eager' : 'lazy'}
                 aria-hidden={n !== i}
                 className={`absolute inset-0 h-full w-full object-cover object-top transition-opacity duration-700 ${n === i ? 'opacity-100' : 'opacity-0'}`} />
          ))}
        </div>
      </div>
      {/* Abas em grade de 4 colunas: no celular as quatro cabem numa linha so',
          e a barrinha embaixo da ativa mostra quando a tela vai trocar. */}
      <div role="tablist" aria-label="Telas do app" className="mt-4 grid grid-cols-4 gap-2">
        {TOUR.map((s, n) => {
          const ativa = n === i
          return (
            <button key={s.src} type="button" role="tab" aria-selected={ativa} onClick={() => { setI(n); setParado(true) }}
                    className={`relative overflow-hidden rounded-md border px-1 py-2 text-xs font-semibold transition-colors ${ativa ? 'border-accent/60 bg-accent/10 text-ink-1' : 'border-line text-ink-3 hover:border-line-strong hover:text-ink-1'}`}>
              {s.t}
              {ativa && (
                <span aria-hidden className="absolute inset-x-0 bottom-0 h-0.5 bg-accent/30">
                  {!parado && !calmo
                    ? <span key={i} className="tour-progresso block h-full bg-accent" style={{ '--tour-dur': `${TOUR_MS}ms` } as CSSProperties} />
                    : <span className="block h-full bg-accent" />}
                </span>
              )}
            </button>
          )
        })}
      </div>
      <figcaption className="mt-3 text-xs text-ink-4">Telas reais do app, sem edição.</figcaption>
    </figure>
  )
}

/** Nome do jogo pelo id do perfil do app (o icone tem o mesmo nome do perfil). */
export function gameName(id: string) {
  return GAMES.find(([, icon]) => icon?.replace(/\.\w+$/, '') === id)?.[0] ?? id
}
