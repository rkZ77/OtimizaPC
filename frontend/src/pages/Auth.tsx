import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { KeyRound, Lock, Mail, User } from 'lucide-react'
import { useAuth } from '../context/AuthContext'
import api, { errorMessage } from '../services/api'
import PageShell from '../components/PageShell'
import PublicNav from '../components/PublicNav'
import { LogoMark, Wordmark } from '../components/Logo'
import { Alert, Button, Card, Input } from '../components/ui'
import { lerIndicacao, limparIndicacao } from '../lib/indicacao'

// So' aceita caminho interno: um ?voltar=https://site-falso nao vira redirect aberto.
// Barra invertida e caractere de controle ficam de fora: o navegador trata
// "/\site-falso.com" como "//site-falso.com" (GHSA-wrjc-x8rr-h8h6 no React Router 6).
export function safeReturn(value: string | null, fallback: string): string {
  if (!value || !value.startsWith('/') || value.startsWith('//')) return fallback
  if (/[\\\u0000-\u001f\u007f]/.test(value)) return fallback
  return value
}

/* Na tela de login, os botoes "Entrar/Criar conta" apontariam para onde a
   pessoa ja' esta' (licao da PublicNav do Pickia): a saida certa e' voltar. */
const navDoLogin = <PublicNav width="wide" acoes={<Button to="/" variant="link" size="sm">Voltar ao site</Button>} />

/*
 * Casca das telas de conta: a arte da marca (logo grande no fundo de grade)
 * ao lado do formulario, como nas artes do Instagram. No celular o logo sobe
 * e encolhe e a frase some: o formulario tem que caber sem rolar.
 */
function TelaDeConta({ title, children }: { title: string; children: ReactNode }) {
  return (
    <PageShell title={title} noindex width="wide" nav={navDoLogin} fundo>
      <div className="grid items-center gap-8 lg:min-h-[68vh] lg:grid-cols-[1.1fr_1fr] lg:gap-16">
        <div className="flex flex-col items-center text-center lg:items-start lg:text-left">
          <div className="flex items-center gap-4 lg:gap-6">
            <LogoMark className="h-14 w-14 lg:h-24 lg:w-24" />
            <Wordmark className="text-[2.6rem] lg:text-7xl" />
          </div>
          <p className="mt-8 hidden max-w-lg font-display text-4xl font-black leading-tight text-ink-1 lg:block">
            Descubra o que está travando seus jogos. <span className="text-accent-ink">E corrija sem medo.</span>
          </p>
          <p className="mt-4 hidden max-w-lg text-lg text-ink-2 lg:block">
            Diagnóstico grátis, correção com um clique e FPS medido antes e depois. Tudo pode ser desfeito.
          </p>
        </div>
        <div className="mx-auto w-full max-w-md lg:mr-0">{children}</div>
      </div>
    </PageShell>
  )
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
    <TelaDeConta title="Entrar">
      <h1 className="font-display text-2xl font-bold text-ink-1">Entrar</h1>
      <p className="mt-1 text-ink-3">Use a mesma conta no site e no app.</p>
      <Card className="mt-6 p-6">
        <form onSubmit={submit} className="space-y-4">
          {params.get('senha') === 'nova' && !error && <Alert tone="ok">Senha nova salva. Entre com ela.</Alert>}
          {error && <Alert>{error}</Alert>}
          <Input label="E-mail" name="email" type="email" Icon={Mail} autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <Input label="Senha" name="password" type="password" Icon={Lock} autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
          <div className="-mt-1 text-right">
            <Link to={email ? `/esqueci-senha?email=${encodeURIComponent(email)}` : '/esqueci-senha'} className="text-sm font-semibold text-accent-ink">Esqueci minha senha</Link>
          </div>
          <Button type="submit" block loading={busy}>Entrar</Button>
        </form>
      </Card>
      <p className="mt-4 text-center text-sm text-ink-3">
        Ainda não tem conta? <Link to="/cadastro" className="font-semibold text-accent-ink">Criar conta grátis</Link>
      </p>
    </TelaDeConta>
  )
}

/*
 * Recuperar senha em dois passos no mesmo componente (desenho do Pickia):
 * /esqueci-senha pede o e-mail; /redefinir-senha?email= abre direto no passo
 * do codigo, que e' para onde o botao do e-mail leva. Sem o e-mail no link,
 * quem clica pediria outro codigo e invalidaria o que acabou de chegar.
 */
export function RecuperarSenha({ etapa }: { etapa: 'pedir' | 'codigo' }) {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [passo, setPasso] = useState<'pedir' | 'codigo'>(etapa === 'codigo' && params.get('email') ? 'codigo' : 'pedir')
  const [email, setEmail] = useState(params.get('email') ?? '')
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const [info, setInfo] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const pedir = async (e?: FormEvent) => {
    e?.preventDefault()
    setBusy(true)
    setError('')
    try {
      const { data } = await api.post<{ minutes: number }>('/auth/forgot-password', { email })
      // Mesma frase exista ou nao a conta: a tela nao revela quem e' cliente.
      setInfo(`Se existe uma conta com ${email}, enviamos um código de 6 dígitos. Ele vale por ${data.minutes} minutos. Confira também o spam.`)
      setPasso('codigo')
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  const redefinir = async (e: FormEvent) => {
    e.preventDefault()
    if (password.length < 8) {
      setError('A senha precisa ter pelo menos 8 caracteres.')
      return
    }
    setBusy(true)
    setError('')
    try {
      await api.post('/auth/reset-password', { email, code, password })
      navigate(`/entrar?senha=nova`)
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <TelaDeConta title="Recuperar senha">
      <h1 className="font-display text-2xl font-bold text-ink-1">{passo === 'pedir' ? 'Recuperar senha' : 'Criar senha nova'}</h1>
      <p className="mt-1 text-ink-3">
        {passo === 'pedir' ? 'Informe o e-mail da conta. Enviamos um código para criar uma senha nova.' : `Digite o código que chegou em ${email}.`}
      </p>
      <Card className="mt-6 p-6">
        {passo === 'pedir' ? (
          <form onSubmit={pedir} className="space-y-4">
            {error && <Alert>{error}</Alert>}
            <Input label="E-mail" name="email" type="email" Icon={Mail} autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
            <Button type="submit" block loading={busy}>Enviar código</Button>
          </form>
        ) : (
          <form onSubmit={redefinir} className="space-y-4">
            {info && <Alert tone="info">{info}</Alert>}
            {error && <Alert>{error}</Alert>}
            <Input label="Código" name="code" Icon={KeyRound} inputMode="numeric" autoComplete="one-time-code" pattern="\d{6}" maxLength={6} required
                   value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} />
            <Input label="Senha nova" name="password" type="password" Icon={Lock} autoComplete="new-password" required hint="Pelo menos 8 caracteres. As sessões abertas em outros aparelhos serão encerradas."
                   value={password} onChange={(e) => setPassword(e.target.value)} />
            <Button type="submit" block loading={busy} disabled={code.length !== 6}>Salvar senha nova</Button>
            <button type="button" className="w-full text-center text-sm font-semibold text-accent-ink disabled:opacity-50" disabled={busy} onClick={() => pedir()}>
              Enviar outro código
            </button>
          </form>
        )}
      </Card>
      <p className="mt-4 text-center text-sm text-ink-3">
        Lembrou a senha? <Link to="/entrar" className="font-semibold text-accent-ink">Entrar</Link>
      </p>
    </TelaDeConta>
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
  const ref = params.get('ref') ?? lerIndicacao()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (password.length < 8) {
      setError('A senha precisa ter pelo menos 8 caracteres.')
      return
    }
    setBusy(true)
    setError('')
    try {
      await register(name, email, password, ref)
      limparIndicacao()
      navigate(plan ? `/pagamento?plano=${encodeURIComponent(plan)}` : '/conta?bemvindo=1')
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <TelaDeConta title="Criar conta">
      <h1 className="font-display text-2xl font-bold text-ink-1">Criar conta</h1>
      <p className="mt-1 text-ink-3">Conta nova ganha um período de teste do plano Pro em 1 PC, sem cartão.</p>
      {ref && <p className="mt-2 text-sm text-accent-ink">Você chegou por indicação de um amigo.</p>}
      <Card className="mt-6 p-6">
        <form onSubmit={submit} className="space-y-4">
          {error && <Alert>{error}</Alert>}
          <Input label="Nome" name="name" Icon={User} autoComplete="name" value={name} onChange={(e) => setName(e.target.value)} />
          <Input label="E-mail" name="email" type="email" Icon={Mail} autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <Input label="Senha" name="password" type="password" Icon={Lock} autoComplete="new-password" required hint="Pelo menos 8 caracteres." value={password} onChange={(e) => setPassword(e.target.value)} />
          <label className="flex items-start gap-3 text-sm text-ink-3">
            <input type="checkbox" className="mt-1 h-4 w-4 accent-[rgb(var(--accent))]" checked={accepted} onChange={(e) => setAccepted(e.target.checked)} required />
            <span>Li e aceito os <Link to="/termos" className="text-accent-ink">termos de uso</Link> e a <Link to="/privacidade" className="text-accent-ink">política de privacidade</Link>.</span>
          </label>
          <Button type="submit" block loading={busy} disabled={!accepted}>Criar conta</Button>
        </form>
      </Card>
      <p className="mt-4 text-center text-sm text-ink-3">
        Já tem conta? <Link to={plan ? `/entrar?voltar=${encodeURIComponent(`/pagamento?plano=${plan}`)}` : '/entrar'} className="font-semibold text-accent-ink">Entrar</Link>
      </p>
    </TelaDeConta>
  )
}
