import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AnimatePresence } from 'framer-motion'
import {
  ArrowRight, Check, Crown, Download, Gamepad2, Headphones, KeyRound, Laptop, LogIn, Pencil, Receipt, Sparkles, UserRound,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import api, { errorMessage, type Overview, type User } from '../services/api'
import { useAuth } from '../context/AuthContext'
import PageShell from '../components/PageShell'
import AreaConta from '../components/AreaConta'
import MandarParaPC from '../components/MandarParaPC'
import { Avatar } from '../components/MenuUsuario'
import { Indique, OQueFoiFeito } from '../components/ResumoPlano'
import { Alert, Badge, Button, ErrorState, Input, Modal, ModalFooter, PlanBadge, SkeletonRows } from '../components/ui'
import { instalaAqui } from '../lib/dispositivo'
import { SUPPORT_IS_EXTERNAL, SUPPORT_URL } from '../lib/support'
import { cn } from '../lib/cn'
import { date, TIER_LABEL } from '../lib/format'
import { libera } from '../lib/features'

/*
 * Inicio da area logada.
 *
 * Quem entrava caia em "Minha conta", uma tela de abas e tabelas, sem dizer o
 * que fazer em seguida. A maior parte das contas nasce pelo celular e nunca
 * chega a instalar o app, e quem nao instala nao assina. O painel responde em
 * ordem: como esta' o plano, qual e' o proximo passo, o que ja' foi feito.
 */

interface Recap { optimizations: number; matches: number }

const DIA_MS = 86_400_000

function diasRestantes(expires: string | null): number | null {
  if (!expires) return null
  return Math.max(0, Math.ceil((new Date(expires).getTime() - Date.now()) / DIA_MS))
}

type Passo = { t: string; d: string; feito: boolean; acao?: React.ReactNode }

export default function Painel() {
  const [params, setParams] = useSearchParams()
  const { atualizar } = useAuth()
  const [data, setData] = useState<Overview | null>(null)
  const [recap, setRecap] = useState<Recap | null>(null)
  const [error, setError] = useState('')
  const [perfil, setPerfil] = useState(false)
  const [nomeEditado, setNomeEditado] = useState('')
  const [salvando, setSalvando] = useState(false)
  const [erroPerfil, setErroPerfil] = useState('')
  const aqui = instalaAqui()

  const load = () => {
    setError('')
    api.get<Overview>('/account/overview').then(({ data }) => setData(data)).catch((e) => setError(errorMessage(e)))
    api.get<Recap>('/account/recap').then(({ data }) => setRecap(data)).catch(() => setRecap(null))
  }
  useEffect(load, [])

  // Editar perfil abre aqui, pelo botao do cartao ou pelo item do menu do topo (?perfil=1).
  useEffect(() => {
    if (params.get('perfil') === '1' && data) { setNomeEditado(data.user.name ?? ''); setPerfil(true) }
  }, [params, data])

  const salvarPerfil = async (e: React.FormEvent) => {
    e.preventDefault()
    setSalvando(true)
    setErroPerfil('')
    try {
      const { data: r } = await api.patch<{ user: User }>('/account/profile', { name: nomeEditado })
      atualizar(r.user)
      setData((d) => (d ? { ...d, user: r.user } : d))
      fecharPerfil()
    } catch (err) {
      setErroPerfil(errorMessage(err))
    } finally {
      setSalvando(false)
    }
  }
  const abrirPerfil = () => { setNomeEditado(data?.user.name ?? ''); setErroPerfil(''); setPerfil(true) }
  const fecharPerfil = () => {
    setPerfil(false)
    if (params.get('perfil')) { params.delete('perfil'); setParams(params, { replace: true }) }
  }

  if (!data) {
    return (
      <PageShell title="Início da conta" noindex width="wide" beforeMain={<AreaConta />}>
        {error ? <ErrorState description={error} onRetry={load} /> : <SkeletonRows rows={4} />}
      </PageShell>
    )
  }

  const lic = data.license
  const primeiroNome = data.user.name?.trim().split(/\s+/)[0] || data.user.email.split('@')[0]
  const tier = lic.tier === 'custom' ? 'ultimate' : lic.tier
  const dias = diasRestantes(lic.expires_at)
  const pago = lic.status === 'active' && tier !== 'free'
  // Cada texto do painel fala so' do que o plano dela libera: no Basico, nada
  // de "antes e depois" (e' do Pro) aparecendo como se fosse dela.
  const comparacao = libera(lic, 'pro')
  const temPc = data.devices.length > 0
  const jogou = (recap?.matches ?? 0) > 0
  const pagamento = params.get('pagamento')

  // O plano em uma frase, e a acao que faz sentido para esse estado.
  const situacao = lic.status === 'trial'
    ? { frase: `Teste grátis do ${TIER_LABEL[tier]}: ${dias === 1 ? 'falta 1 dia' : `faltam ${dias ?? 0} dias`}.`, cta: { to: '/planos', label: 'Assinar para manter as correções' } }
    : lic.status === 'expired'
      ? { frase: 'Seu plano venceu. O diagnóstico e o desfazer continuam liberados.', cta: { to: '/meu-plano', label: 'Renovar plano' } }
      : pago
        ? { frase: dias !== null ? `Ativo até ${date(lic.expires_at!)}, ${dias === 1 ? 'falta 1 dia' : `faltam ${dias} dias`}.` : 'Ativo, sem vencimento.', cta: dias !== null && dias <= 7 ? { to: '/meu-plano', label: 'Renovar e somar dias' } : null }
        : { frase: 'Plano grátis: diagnóstico completo do PC. As correções com um clique são dos planos pagos.', cta: { to: '/planos', label: 'Ver planos' } }

  const passos: Passo[] = [
    { t: 'Criar a conta', d: 'Feito. É com este e-mail que você entra no app.', feito: true },
    {
      t: 'Instalar o app e entrar com esta conta',
      d: temPc ? `${data.devices.length === 1 ? '1 PC ativo' : `${data.devices.length} PCs ativos`} nesta conta.` : 'No app, abra a tela Conta e entre com este e-mail. O diagnóstico começa sozinho.',
      feito: temPc,
      acao: temPc ? undefined : aqui
        ? <Button href="/api/public/download" Icon={Download}>Baixar para Windows</Button>
        : <MandarParaPC origem="painel" />,
    },
    {
      t: 'Jogar uma partida com o app aberto',
      d: jogou
        ? `${recap!.matches} ${recap!.matches === 1 ? 'partida medida' : 'partidas medidas'}.${comparacao ? ' O antes e depois aparece em Meu plano.' : ''}`
        : 'O RKZFPS mede o FPS sozinho, com o jogo aberto, sem mexer no jogo.',
      feito: jogou,
    },
    {
      t: 'Escolher um plano',
      d: pago ? 'Plano ativo. Em Meu plano está a lista do que ele libera.' : 'Para aplicar as correções com um clique. Compare o que cada plano libera.',
      feito: pago,
      acao: pago ? undefined : <Button variant="ghost" to="/planos" IconRight={ArrowRight}>Ver planos</Button>,
    },
  ]
  const feitos = passos.filter((p) => p.feito).length
  // So' o primeiro passo pendente abre a acao: uma coisa de cada vez.
  const proximo = passos.findIndex((p) => !p.feito)

  const atalhos: { to?: string; href?: string; label: string; d: string; Icon: LucideIcon }[] = [
    { to: '/meu-plano', label: 'Meu plano', d: 'Validade e o que libera', Icon: Crown },
    { to: '/conta', label: 'PCs e medições', d: 'Ativar ou trocar de PC', Icon: Laptop },
    { to: '/meu-plano#pagamentos', label: 'Pagamentos', d: 'Comprovantes e reembolso', Icon: Receipt },
    SUPPORT_IS_EXTERNAL ? { href: SUPPORT_URL, label: 'Suporte', d: 'Fale com a gente', Icon: Headphones } : { to: SUPPORT_URL, label: 'Suporte', d: 'Fale com a gente', Icon: Headphones },
  ]

  return (
    <PageShell title="Início da conta" noindex width="wide" beforeMain={<AreaConta />}>
      {/* Quem esta' logado, como na PickIA: avatar, nome, selo e editar perfil.
          ADMIN so' aparece para admin. */}
      <div className="card mb-4 flex flex-wrap items-center gap-4 p-5 sm:p-6">
        <Avatar nome={data.user.name} email={data.user.email} className="h-14 w-14 text-lg" />
        <div className="min-w-0 flex-1">
          <p className="flex flex-wrap items-center gap-2">
            <span className="font-display text-xl font-bold text-ink-1">Olá, {primeiroNome}!</span>
            {data.user.role === 'admin' && <Badge tone="purple">ADMIN</Badge>}
          </p>
          <p className="truncate text-sm text-ink-3">{data.user.email}</p>
        </div>
        <Button variant="ghost" size="sm" Icon={Pencil} onClick={abrirPerfil}>Editar perfil</Button>
      </div>

      <div className="mb-6 space-y-3">
        {pagamento === 'aprovado' && <Alert tone="ok">Pagamento aprovado. Seu plano já está ativo: no app, abra Conta e toque em Sincronizar.</Alert>}
        {pagamento === 'pendente' && <Alert tone="warn">Pagamento em processamento. Assim que o Mercado Pago confirmar, seu plano é ativado sozinho.</Alert>}
        {params.get('bemvindo') && <Alert tone="info">Conta criada. Falta instalar o app no PC e entrar com este e-mail: o teste começa ali.</Alert>}
      </div>

      {/* Situacao do plano */}
      <div className="card flex flex-col gap-4 p-5 sm:flex-row sm:items-center sm:justify-between sm:p-6">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <Crown className="h-5 w-5 text-accent-ink" aria-hidden />
            <p className="font-display text-xl font-bold text-ink-1">Plano {TIER_LABEL[lic.tier] ?? data.plan_name}</p>
            <PlanBadge tier={lic.tier} status={lic.status} />
          </div>
          <p className="mt-1.5 text-sm text-ink-2">{situacao.frase}</p>
        </div>
        {situacao.cta && <Button to={situacao.cta.to} className="shrink-0">{situacao.cta.label}</Button>}
      </div>

      {/* Proximos passos: o caminho ate' o app instalado e medindo */}
      {feitos < passos.length && (
        <section className="card mt-4 p-5 sm:p-6" aria-labelledby="passos-titulo">
          <div className="flex items-center justify-between gap-3">
            <h2 id="passos-titulo" className="font-display text-lg font-bold text-ink-1">Seus próximos passos</h2>
            <span className="text-sm font-semibold text-ink-3">{feitos} de {passos.length}</span>
          </div>
          <div className="mt-3 h-1.5 overflow-hidden rounded-full bg-surface-3" aria-hidden>
            <div className="h-full rounded-full bg-accent transition-all duration-2" style={{ width: `${(feitos / passos.length) * 100}%` }} />
          </div>
          <ol className="mt-5 space-y-4">
            {passos.map((p, i) => (
              <li key={p.t} className="flex gap-3">
                <span aria-hidden className={cn('mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-full border text-xs font-bold',
                  p.feito ? 'border-accent bg-accent text-on-fill' : i === proximo ? 'border-accent/60 text-accent-ink' : 'border-line-strong text-ink-4')}>
                  {p.feito ? <Check className="h-4 w-4" /> : i + 1}
                </span>
                <div className="min-w-0 flex-1">
                  <p className={cn('font-semibold', p.feito ? 'text-ink-3' : 'text-ink-1')}>
                    <span className="sr-only">{p.feito ? 'Feito: ' : 'Pendente: '}</span>{p.t}
                  </p>
                  <p className="mt-0.5 text-sm text-ink-3">{p.d}</p>
                  {i === proximo && p.acao && <div className="mt-3">{p.acao}</div>}
                </div>
              </li>
            ))}
          </ol>
        </section>
      )}

      {/* Atalhos */}
      <div className="mt-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
        {atalhos.map(({ to, href, label, d, Icon }) => {
          const cls = 'card flex min-h-[88px] flex-col gap-1 p-4 transition-colors duration-1 hover:border-line-strong hover:bg-surface-2'
          const corpo = (
            <>
              <Icon className="h-5 w-5 text-accent-ink" aria-hidden />
              <span className="mt-1 font-semibold text-ink-1">{label}</span>
              <span className="text-xs text-ink-3">{d}</span>
            </>
          )
          return href
            ? <a key={label} href={href} target="_blank" rel="noopener noreferrer" className={cls}>{corpo}</a>
            : <Link key={label} to={to!} className={cls}>{corpo}</Link>
        })}
      </div>

      <OQueFoiFeito />
      <Indique />

      {!temPc && (
        <p className="mt-6 flex items-start gap-2 text-sm text-ink-3">
          <LogIn className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
          Já instalou? No app, abra a tela Conta, entre com {data.user.email} e o PC aparece aqui.
        </p>
      )}
      {pago && comparacao && (
        <p className="mt-6 flex items-start gap-2 text-sm text-ink-3">
          <Gamepad2 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
          Dica: deixe o RKZFPS aberto perto do relógio enquanto joga. Cada partida medida deixa o antes e depois mais confiável.
        </p>
      )}
      {lic.status === 'trial' && (
        <p className="mt-6 flex items-start gap-2 text-sm text-ink-3">
          <Sparkles className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
          Se o teste acabar sem assinatura, o app desfaz sozinho as correções do teste e o PC volta como estava.
        </p>
      )}

      <AnimatePresence>
        {perfil && (
          <Modal onClose={fecharPerfil} title="Editar perfil" width="sm">
            <form onSubmit={salvarPerfil} className="space-y-4">
              {erroPerfil && <Alert>{erroPerfil}</Alert>}
              <Input label="Como quer ser chamado" name="name" Icon={UserRound} autoComplete="given-name" maxLength={80}
                     value={nomeEditado} onChange={(e) => setNomeEditado(e.target.value)} />
              <div>
                <p className="text-sm font-semibold text-ink-2">E-mail</p>
                <p className="mt-1 text-sm text-ink-3">{data.user.email}. É com ele que você entra no app, por isso não muda aqui.</p>
              </div>
              <Link to={`/esqueci-senha?email=${encodeURIComponent(data.user.email)}`} className="inline-flex items-center gap-2 text-sm font-semibold text-accent-ink">
                <KeyRound className="h-4 w-4" aria-hidden />Trocar a senha
              </Link>
              <ModalFooter>
                <Button variant="ghost" onClick={fecharPerfil}>Cancelar</Button>
                <Button type="submit" loading={salvando}>Salvar</Button>
              </ModalFooter>
            </form>
          </Modal>
        )}
      </AnimatePresence>
    </PageShell>
  )
}
