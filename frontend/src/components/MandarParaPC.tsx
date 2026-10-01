import { useState } from 'react'
import { Check, Copy, Mail, MessageCircle, MonitorSmartphone, Send } from 'lucide-react'
import api, { errorMessage } from '../services/api'
import { Button } from './ui'
import { evento } from '../lib/analytics'
import { cn } from '../lib/cn'

/*
 * "Mandar o link para o PC": a acao principal de quem esta' no celular.
 *
 * O app so' roda no Windows, e a maioria chega pelo telefone (Instagram,
 * WhatsApp, busca no celular). Em vez de um .exe que o celular nao abre, a
 * pessoa recebe o link no e-mail e baixa quando sentar no PC. O e-mail fica
 * registrado no envio, e o lembrete chega na caixa de entrada dela.
 *
 * Quem nao quer dar o e-mail tem duas saidas sem cadastro: mandar o link
 * para si mesma no WhatsApp ou copiar.
 */

const LINK_PC = 'https://rkzfps.com.br/download'

export default function MandarParaPC({ origem, className, titulo = true }: { origem: string; className?: string; titulo?: boolean }) {
  const [email, setEmail] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [enviado, setEnviado] = useState(false)
  const [erro, setErro] = useState('')
  const [copiado, setCopiado] = useState(false)

  const enviar = async (e: React.FormEvent) => {
    e.preventDefault()
    setErro('')
    setEnviando(true)
    try {
      await api.post('/public/download-link', { email, origem })
      setEnviado(true)
      evento('generate_lead', { method: 'email_link_pc', origem })
    } catch (err) {
      setErro(errorMessage(err, 'Não deu para enviar agora. Tente de novo ou mande pelo WhatsApp.'))
    } finally {
      setEnviando(false)
    }
  }

  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(LINK_PC)
      setCopiado(true)
      window.setTimeout(() => setCopiado(false), 2000)
    } catch {
      /* sem area de transferencia: o link segue escrito abaixo */
    }
  }

  const whatsapp = `https://wa.me/?text=${encodeURIComponent(`Baixar o RKZFPS no PC: ${LINK_PC}`)}`

  return (
    <div className={cn('rounded-lg border border-accent/40 bg-accent/5 p-4', className)}>
      {titulo && (
        <p className="flex items-center gap-2 font-semibold text-ink-1">
          <MonitorSmartphone className="h-5 w-5 shrink-0 text-accent-ink" aria-hidden />
          O RKZFPS é para PC com Windows
        </p>
      )}
      {enviado ? (
        <div className={cn(titulo && 'mt-3')} role="status">
          <p className="flex items-start gap-2 text-sm text-ink-1">
            <Check className="mt-0.5 h-4 w-4 shrink-0 text-accent-ink" aria-hidden />
            <span>Pronto. Abra o e-mail no PC e clique em <span className="font-semibold">Baixar o RKZFPS no PC</span>. Não chegou? Olhe no spam.</span>
          </p>
        </div>
      ) : (
        <>
          {titulo && <p className="mt-1 text-sm text-ink-3">Mande o link para o seu e-mail e baixe quando estiver no computador.</p>}
          <form onSubmit={enviar} className={cn('flex flex-col gap-2 sm:flex-row', titulo && 'mt-3')}>
            <label className="sr-only" htmlFor={`email-pc-${origem}`}>Seu e-mail</label>
            <div className="relative min-w-0 flex-1">
              <Mail className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-4" aria-hidden />
              <input id={`email-pc-${origem}`} type="email" required autoComplete="email" inputMode="email"
                     placeholder="Seu e-mail" value={email} onChange={(e) => setEmail(e.target.value)}
                     className="input !py-2.5 pl-9" />
            </div>
            <Button type="submit" Icon={Send} loading={enviando}>Mandar o link</Button>
          </form>
          {erro && <p className="mt-2 text-sm text-red-400" role="alert">{erro}</p>}
        </>
      )}
      <div className="mt-3 flex flex-wrap gap-2">
        <Button variant="ghost" size="sm" Icon={MessageCircle} href={whatsapp}
                onClick={() => evento('share', { method: 'whatsapp', origem })}>Mandar no WhatsApp</Button>
        <Button variant="ghost" size="sm" Icon={copiado ? Check : Copy} onClick={copiar}>{copiado ? 'Copiado' : 'Copiar link'}</Button>
      </div>
    </div>
  )
}
