import type { ReactNode } from 'react'

/*
 * Pecas da vitrine usadas na home e na pagina Como funciona: print real do
 * app, linha de texto com print ao lado e a grade de jogos reconhecidos.
 * Num lugar so' para as duas paginas nao contarem historias diferentes.
 */

export function Screenshot({ src, alt }: { src: string; alt: string }) {
  return (
    <figure className="min-w-0">
      <img src={src} alt={alt} width={1180} height={780} loading="lazy"
           className="w-full h-auto rounded-lg border border-line shadow-elev" />
      <figcaption className="mt-2 text-xs text-ink-4">Tela real do app, sem edição.</figcaption>
    </figure>
  )
}

export function Row({ title, children, img, alt, flip }: { title: string; children: ReactNode; img: string; alt: string; flip?: boolean }) {
  return (
    <div className="grid items-center gap-8 lg:grid-cols-2 lg:gap-14">
      <div className={flip ? 'lg:order-2' : undefined}>
        <h3 className="font-display text-2xl font-bold text-ink-1">{title}</h3>
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
          <li key={g} className="flex items-center gap-3 rounded-lg border border-line bg-surface-0 px-3 py-2.5">
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
