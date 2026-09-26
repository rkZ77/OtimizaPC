import { useEffect, useState } from 'react'
import { Send } from 'lucide-react'
import api, { errorMessage } from '../../services/api'
import { Alert, Button } from '../../components/ui'
import { dateTime } from '../../lib/format'
import { AdminHead, ReadOnly } from './AdminConfig'

/*
 * E-mails: se o envio esta' configurado, um botao que manda um teste para o
 * proprio admin, e o registro de toda tentativa. A pergunta "o cliente
 * recebeu o codigo?" vira consulta aqui, sem abrir o painel do Resend.
 */

const TIPO: Record<string, string> = {
  welcome: 'Boas-vindas', password_reset: 'Código de senha', password_changed: 'Senha alterada',
  payment_approved: 'Pagamento aprovado', plan_expiring: 'Plano vencendo', plan_expired: 'Plano encerrado',
  admin_test: 'Teste do admin',
}
const STATUS: Record<string, string> = { sent: 'Enviado', failed: 'Falhou', skipped: 'Não enviado', pending: 'Enviando' }

export default function AdminEmails() {
  const [status, setStatus] = useState<{ configured: boolean; from: string } | null>(null)
  const [result, setResult] = useState<{ tone: 'ok' | 'danger'; text: string } | null>(null)
  const [busy, setBusy] = useState(false)
  // Trocar a chave remonta a lista: o envio de teste aparece nela na hora.
  const [version, setVersion] = useState(0)

  useEffect(() => {
    api.get('/admin/emails?limit=1').then(({ data }) => setStatus({ configured: data.configured, from: data.from })).catch(() => setStatus(null))
  }, [])

  const testar = async () => {
    setBusy(true)
    setResult(null)
    try {
      const { data } = await api.post<{ to: string }>('/admin/emails/test')
      setResult({ tone: 'ok', text: `Enviado para ${data.to}. Confira a caixa de entrada e o spam.` })
    } catch (e) {
      setResult({ tone: 'danger', text: errorMessage(e) })
    } finally {
      setBusy(false)
      setVersion((v) => v + 1)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <AdminHead title="Envio de e-mail" sub="Boas-vindas, código de senha, pagamento aprovado e avisos de vencimento saem pelo Resend." />
        {status && (status.configured
          ? <Alert tone="ok">Envio ativo. Remetente: {status.from}</Alert>
          : <Alert tone="warn">Envio desligado. Defina RESEND_API_KEY e RESEND_FROM nas variáveis do Railway. Até lá nada sai, e cada tentativa fica registrada abaixo como "Não enviado".</Alert>)}
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <Button onClick={testar} loading={busy} Icon={Send}>Enviar e-mail de teste para mim</Button>
        </div>
        {result && <Alert tone={result.tone} className="mt-3">{result.text}</Alert>}
      </div>

      <div>
        <AdminHead title="Registro de envios" sub="Toda tentativa, com o motivo quando o envio falha." />
        <ReadOnly key={version} path="/admin/emails" keyName="emails" head={['Data', 'Para', 'Tipo', 'Status', 'Detalhe']}
          row={(e) => [dateTime(String(e.created_at)), String(e.to_email), TIPO[String(e.kind)] ?? String(e.kind),
                       STATUS[String(e.status)] ?? String(e.status), String(e.detail ?? '')]} />
      </div>
    </div>
  )
}
