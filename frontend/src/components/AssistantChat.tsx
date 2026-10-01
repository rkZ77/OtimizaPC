import { useEffect, useRef, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { MessageCircle, Send, X } from 'lucide-react'
import api, { errorMessage } from '../services/api'
import { cn } from '../lib/cn'
import { aoAbrirAssistente, assistenteLigado } from '../lib/assistente'

/*
 * Assistente do site: tira duvida sobre o RKZFPS e ajuda a escolher o plano.
 * O servidor monta o que ele sabe (texto do produto e planos do banco); aqui
 * so' vai a conversa. Some quando o servidor nao tem chave, e fora do admin.
 */

type Msg = { role: 'user' | 'assistant'; content: string }

const HELLO: Msg = {
  role: 'assistant',
  content: 'Oi! Sou o assistente do RKZFPS. Pergunte o que quiser sobre o app, os jogos, os planos ou se ele serve para o seu PC.',
}

const SUGESTOES = ['Vai aumentar meu FPS?', 'Qual plano escolher?', 'Funciona com anti-cheat?']

export default function AssistantChat() {
  const { pathname } = useLocation()
  const [enabled, setEnabled] = useState(false)
  const [open, setOpen] = useState(false)
  const [msgs, setMsgs] = useState<Msg[]>([HELLO])
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const endRef = useRef<HTMLDivElement>(null)

  useEffect(() => { assistenteLigado().then(setEnabled) }, [])
  // O item "Assistente com IA" do menu lateral abre o chat por aqui.
  useEffect(() => aoAbrirAssistente(() => setOpen(true)), [])

  useEffect(() => { endRef.current?.scrollIntoView({ block: 'end' }) }, [msgs, busy, open])

  if (!enabled || pathname.startsWith('/admin')) return null

  const send = async (question: string) => {
    const q = question.trim()
    if (!q || busy) return
    const next: Msg[] = [...msgs, { role: 'user', content: q.slice(0, 600) }]
    setMsgs(next)
    setText('')
    setError('')
    setBusy(true)
    try {
      // A saudacao e' so' da tela: nao vai para o servidor.
      const { data } = await api.post<{ reply: string }>('/public/assistant', { messages: next.slice(1) }, { timeout: 40000 })
      setMsgs([...next, { role: 'assistant', content: data.reply }])
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {!open && (
        // Sobe junto com a barra de cookies (--aviso-offset) e com a barra de
        // acao do celular (--barra-fixa): antes o botao ficava por cima delas.
        <button type="button" onClick={() => setOpen(true)} aria-label="Tirar dúvidas com o assistente"
                style={{ bottom: 'calc(1rem + var(--aviso-offset, 0px) + var(--barra-fixa, 0px))' }}
                className="fixed right-4 z-40 flex items-center gap-2 rounded-full bg-accent px-4 py-3 text-sm font-bold text-black shadow-elev hover:bg-accent-hover active:bg-accent-press transition-colors">
          <MessageCircle className="h-5 w-5" aria-hidden />
          <span className="hidden sm:inline">Tirar dúvidas</span>
        </button>
      )}

      {open && (
        <div role="dialog" aria-label="Assistente do RKZFPS"
             className="fixed inset-0 z-50 flex flex-col bg-surface-0 sm:inset-auto sm:bottom-4 sm:right-4 sm:h-[560px] sm:w-[380px] sm:rounded-xl sm:border sm:border-line sm:shadow-elev">
          <div className="flex items-center justify-between border-b border-line px-4 py-3">
            <div>
              <p className="font-display font-bold text-ink-1">Assistente RKZFPS</p>
              <p className="text-xs text-ink-3">Respostas automáticas. Para conta e pagamento, fale com o suporte.</p>
            </div>
            <button type="button" onClick={() => setOpen(false)} aria-label="Fechar" className="rounded-md p-2 text-ink-3 hover:text-ink-1">
              <X className="h-5 w-5" />
            </button>
          </div>

          <div className="flex-1 space-y-3 overflow-y-auto px-4 py-4" aria-live="polite">
            {msgs.map((m, i) => (
              <div key={i} className={cn('max-w-[85%] whitespace-pre-line rounded-lg px-3 py-2 text-sm leading-relaxed',
                m.role === 'user' ? 'ml-auto bg-accent/15 text-ink-1' : 'bg-surface-2 text-ink-2')}>
                {m.content}
              </div>
            ))}
            {msgs.length === 1 && (
              <div className="flex flex-wrap gap-2 pt-1">
                {SUGESTOES.map((s) => (
                  <button key={s} type="button" onClick={() => send(s)} className="pill">{s}</button>
                ))}
              </div>
            )}
            {busy && <div className="w-16 rounded-lg bg-surface-2 px-3 py-2 text-sm text-ink-3">...</div>}
            {error && <p className="text-sm text-red-400">{error}</p>}
            <div ref={endRef} />
          </div>

          <form className="flex gap-2 border-t border-line p-3" onSubmit={(e) => { e.preventDefault(); send(text) }}>
            <input value={text} onChange={(e) => setText(e.target.value)} maxLength={600} placeholder="Escreva sua dúvida"
                   aria-label="Sua dúvida" className="input flex-1" />
            <button type="submit" disabled={busy || !text.trim()} aria-label="Enviar"
                    className="rounded-md bg-accent px-3 text-black disabled:opacity-40 hover:bg-accent-hover">
              <Send className="h-4 w-4" />
            </button>
          </form>
        </div>
      )}
    </>
  )
}
