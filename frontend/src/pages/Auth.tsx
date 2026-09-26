import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { errorMessage } from '../services/api'
import { PageShell } from '../components/Layout'
import { Alert, Button, Card, Field } from '../components/ui'

// So' aceita caminho interno: um ?voltar=https://site-falso nao vira redirect aberto.
function safeReturn(value: string | null, fallback: string): string {
  return value && value.startsWith('/') && !value.startsWith('//') ? value : fallback
}

export function Entrar() {
  const { login } = useAuth()
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      const user = await login(email, password)
      navigate(safeReturn(params.get('voltar'), user.role === 'admin' ? '/admin' : '/conta'))
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <PageShell narrow>
      <h1 className="text-2xl font-bold text-ink-1">Entrar</h1>
      <p className="mt-1 text-ink-3">Use a mesma conta no site e no app.</p>
      <Card className="mt-6">
        <form onSubmit={submit} className="space-y-4">
          {error && <Alert>{error}</Alert>}
          <Field label="E-mail" name="email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <Field label="Senha" name="password" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
          <Button type="submit" block loading={busy}>Entrar</Button>
        </form>
      </Card>
      <p className="mt-4 text-center text-sm text-ink-3">Ainda não tem conta? <Link to="/cadastro" className="text-accent-ink font-semibold">Criar conta grátis</Link></p>
    </PageShell>
  )
}

export function Cadastro() {
  const { register } = useAuth()
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [accepted, setAccepted] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const plan = params.get('plano')

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (password.length < 8) {
      setError('A senha precisa ter pelo menos 8 caracteres.')
      return
    }
    setBusy(true)
    setError('')
    try {
      await register(name, email, password)
      navigate(plan ? '/planos' : '/conta?bemvindo=1')
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <PageShell narrow>
      <h1 className="text-2xl font-bold text-ink-1">Criar conta</h1>
      <p className="mt-1 text-ink-3">Conta nova ganha um período de teste do plano Pro em 1 PC, sem cartão.</p>
      <Card className="mt-6">
        <form onSubmit={submit} className="space-y-4">
          {error && <Alert>{error}</Alert>}
          <Field label="Nome" name="name" autoComplete="name" value={name} onChange={(e) => setName(e.target.value)} />
          <Field label="E-mail" name="email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <Field label="Senha" name="password" type="password" autoComplete="new-password" required hint="Pelo menos 8 caracteres." value={password} onChange={(e) => setPassword(e.target.value)} />
          <label className="flex items-start gap-3 text-sm text-ink-3">
            <input type="checkbox" className="mt-1 h-4 w-4" checked={accepted} onChange={(e) => setAccepted(e.target.checked)} required />
            <span>Li e aceito os <Link to="/termos" className="text-accent-ink">termos de uso</Link> e a <Link to="/privacidade" className="text-accent-ink">política de privacidade</Link>.</span>
          </label>
          <Button type="submit" block loading={busy} disabled={!accepted}>Criar conta</Button>
        </form>
      </Card>
      <p className="mt-4 text-center text-sm text-ink-3">Já tem conta? <Link to="/entrar" className="text-accent-ink font-semibold">Entrar</Link></p>
    </PageShell>
  )
}
