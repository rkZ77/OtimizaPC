import { Link } from 'react-router-dom'
import { MessageCircle } from 'lucide-react'
import { SUPPORT_IS_EXTERNAL, SUPPORT_URL } from '../lib/support'
import Logo from './Logo'

/*
 * Rodape do site, no desenho do Pickia: curto de proposito. Rodape nao e' mapa
 * do site, e' onde a pessoa procura o que nao achou em cima.
 *
 * `py-2.5` nos links: o publico e' de celular, e link de rodape com 28px de
 * altura empilhado erra o toque (medido no Pickia).
 */
const LINKS: Array<{ label: string; to: string }> = [
  { label: 'Como funciona', to: '/#como-funciona' },
  { label: 'O que não fazemos', to: '/#o-que-nao-fazemos' },
  { label: 'Quem somos', to: '/quem-somos' },
  { label: 'Planos', to: '/planos' },
  { label: 'Download', to: '/download' },
  { label: 'Minha conta', to: '/conta' },
  { label: 'Termos de uso', to: '/termos' },
  { label: 'Privacidade', to: '/privacidade' },
]

const LINK_CLS = 'py-2.5 text-xs text-ink-3 hover:text-ink-1 transition-colors duration-1 ease-smooth'

export default function Footer() {
  return (
    <footer className="border-t border-line bg-surface-0 mt-auto">
      <div className="max-w-6xl mx-auto px-4 py-8">
        <div className="flex flex-col md:flex-row md:items-start md:justify-between gap-6">
          <div className="min-w-0">
            <Logo className="mb-2 py-1" />
            <p className="text-xs text-ink-3 leading-relaxed max-w-[38ch]">
              Diagnóstico, otimização compatível, medição e transparência para PCs Windows.
              Só muda o que faz sentido para o seu PC.
            </p>
            <div className="flex items-center gap-2 mt-3">
              <a
                href={SUPPORT_URL}
                {...(SUPPORT_IS_EXTERNAL ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
                aria-label="Suporte"
                className="w-9 h-9 rounded-md border border-line flex items-center justify-center text-ink-3 hover:text-ink-1 hover:border-line-strong transition-colors duration-1 ease-smooth"
              >
                <MessageCircle className="w-4 h-4" />
              </a>
            </div>
          </div>

          <nav className="flex flex-wrap gap-x-5 md:justify-end md:max-w-md" aria-label="Rodapé">
            {LINKS.map(l => (
              <Link key={l.label} to={l.to} className={LINK_CLS}>{l.label}</Link>
            ))}
          </nav>
        </div>

        <div className="mt-6 pt-5 border-t border-line flex flex-col sm:flex-row items-center justify-between gap-3">
          <p className="text-[11px] text-ink-4 text-center sm:text-left">
            {new Date().getFullYear()} © FPSX. Otimização de PC com diagnóstico e medição.
          </p>
          <p className="text-[11px] text-ink-4 text-center sm:text-right">
            Resultados variam por PC e são sempre medidos, nunca prometidos.
          </p>
        </div>
      </div>
    </footer>
  )
}
