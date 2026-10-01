import { useEffect, useState } from 'react'
import { AnimatePresence } from 'framer-motion'
import { Download, Send } from 'lucide-react'
import { Button, Modal } from './ui'
import MandarParaPC from './MandarParaPC'
import { instalaAqui } from '../lib/dispositivo'
import { evento } from '../lib/analytics'

/*
 * Barra de acao fixa no rodape, so' no celular.
 *
 * A Home no celular tem mais de dez telas, e o botao do topo some na primeira
 * rolagem: quem se convenceu no meio da pagina tinha que voltar ao topo para
 * agir. A barra aparece depois do hero e fica a um toque de distancia.
 *
 * Ela publica a propria altura em --barra-fixa para o corpo da pagina e o
 * botao do assistente subirem junto, igual a barra de cookies faz com
 * --aviso-offset.
 */

export default function BarraCelular({ origem, depoisDe = 560 }: { origem: string; depoisDe?: number }) {
  const [visivel, setVisivel] = useState(false)
  const [aberto, setAberto] = useState(false)
  const aqui = instalaAqui()

  useEffect(() => {
    const onScroll = () => setVisivel(window.scrollY > depoisDe)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [depoisDe])

  useEffect(() => () => document.documentElement.style.removeProperty('--barra-fixa'), [])

  return (
    <>
      <div
        ref={(el) => {
          const raiz = document.documentElement
          if (el && visivel) raiz.style.setProperty('--barra-fixa', `${el.offsetHeight}px`)
          else raiz.style.removeProperty('--barra-fixa')
        }}
        aria-hidden={!visivel}
        className={`fixed inset-x-0 z-30 border-t border-line bg-surface-0/95 px-4 pt-3 backdrop-blur-xl transition-transform duration-2 ease-smooth sm:hidden ${visivel ? 'translate-y-0' : 'pointer-events-none translate-y-full'}`}
        style={{ bottom: 'var(--aviso-offset, 0px)', paddingBottom: 'calc(0.75rem + env(safe-area-inset-bottom))' }}
      >
        {aqui ? (
          <Button block size="lg" Icon={Download} to="/download" onClick={() => evento('select_content', { content_type: 'barra_baixar', origem })}>
            Baixar grátis para Windows
          </Button>
        ) : (
          <Button block size="lg" Icon={Send} onClick={() => { setAberto(true); evento('select_content', { content_type: 'barra_link_pc', origem }) }}>
            Mandar o link para o meu PC
          </Button>
        )}
      </div>

      <AnimatePresence>
        {aberto && (
          <Modal onClose={() => setAberto(false)} title="Baixar no PC" width="sm" sheetOnMobile>
            <MandarParaPC origem={`barra-${origem}`} titulo={false} className="border-0 bg-transparent p-0" />
            <p className="mt-4 text-xs text-ink-3">O RKZFPS roda no Windows 10 e 11. O diagnóstico é grátis e não muda nada no PC.</p>
          </Modal>
        )}
      </AnimatePresence>
    </>
  )
}
