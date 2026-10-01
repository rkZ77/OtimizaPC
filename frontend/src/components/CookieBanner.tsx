import { useState, useEffect } from 'react'
import { AnimatePresence, motion } from 'framer-motion'
import { gaId, iniciarAnalytics, precisaPerguntar, responder } from '../lib/analytics'

export default function CookieBanner() {
  const [visible, setVisible] = useState(false)
  // Com o Analytics configurado o aviso vira pergunta (LGPD): aceitar ou so' o essencial.
  const comAnalytics = gaId() !== null

  useEffect(() => {
    iniciarAnalytics()
    if (precisaPerguntar()) setVisible(true)
  }, [])

  const escolher = (valor: 'todos' | 'essenciais') => {
    responder(valor)
    setVisible(false)
  }

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          initial={{ y: '100%' }}
          animate={{ y: 0 }}
          exit={{ y: '100%' }}
          transition={{ type: 'spring', stiffness: 300, damping: 32 }}
          /* A PILHA DE AVISOS PRECISA SABER QUE ESTA BARRA EXISTE.
            *
            * Ela e' uma barra de rodape inteira, e antes os avisos flutuantes
            * simplesmente caiam por cima dela (o toast de erro vivia em
            * `bottom-6`). So' este componente sabe se esta' na tela, entao e'
            * ele que publica a folga · ver PilhaDeAvisos. */
          ref={el => {
            const raiz = document.documentElement
            if (el) raiz.style.setProperty('--aviso-offset', `${el.offsetHeight}px`)
            else raiz.style.removeProperty('--aviso-offset')
          }}
          className="fixed bottom-0 inset-x-0 z-40 bg-surface-1 border-t border-line px-4 py-3"
          style={{ paddingBottom: 'calc(0.75rem + env(safe-area-inset-bottom))' }}
        >
          {/* No celular a barra ocupava um terco da primeira tela, bem em cima
              do botao de baixar. Frase curta e botoes lado a lado. */}
          <div className="max-w-5xl mx-auto flex flex-wrap sm:flex-nowrap items-center gap-x-3 gap-y-2 sm:gap-6">
            <p className="text-xs text-ink-2 basis-full sm:basis-auto flex-1 leading-relaxed">
              {comAnalytics
                ? 'Usamos cookies essenciais para login e, se você permitir, o Google Analytics para saber quais páginas ajudam mais.'
                : 'Usamos só cookies essenciais, para login e funcionamento do site. Ao continuar, você concorda com a Política de Privacidade.'}
            </p>
            {/* A política saiu de dentro da frase e virou botão ao lado do
                "Entendi": ler antes de aceitar é uma escolha, não uma nota de
                rodapé sublinhada. */}
            <a
              href="/privacidade"
              className="shrink-0 inline-flex items-center justify-center text-xs font-bold text-ink-2 hover:text-ink-1 border border-line-strong hover:border-ink-4 px-4 py-2 rounded-lg min-h-[36px] transition-colors"
            >
              Ler a política
            </a>
            {/* "Só essenciais" com o mesmo peso visual de "Ler a política":
                recusar tem que ser tão fácil quanto aceitar. */}
            {comAnalytics && (
              <button
                type="button"
                onClick={() => escolher('essenciais')}
                className="shrink-0 inline-flex items-center justify-center text-xs font-bold text-ink-2 hover:text-ink-1 border border-line-strong hover:border-ink-4 px-4 py-2 rounded-lg min-h-[36px] transition-colors"
              >
                Só essenciais
              </button>
            )}
            <motion.button
              whileTap={{ scale: 0.96 }}
              onClick={() => escolher('todos')}
              className="shrink-0 bg-accent hover:bg-accent-hover active:bg-accent-press text-black text-xs font-black px-5 py-2 rounded-lg transition-colors"
            >
              {comAnalytics ? 'Aceitar todos' : 'Entendi'}
            </motion.button>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  )
}
