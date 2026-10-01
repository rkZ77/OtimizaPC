import { useEffect, useState } from 'react'
import {
  BadgeCheck, Download as DownloadIcon, Fingerprint, Gauge, LogIn, MonitorCheck, RefreshCw, ScanSearch,
  ShieldAlert, ShieldCheck, UserRound, Wifi,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import api, { type Release } from '../services/api'
import PageShell from '../components/PageShell'
import { Alert, Button, SectionHead, SkeletonText } from '../components/ui'
import { LogoMark } from '../components/Logo'
import MandarParaPC from '../components/MandarParaPC'
import { date } from '../lib/format'
import { evento } from '../lib/analytics'
import { instalaAqui } from '../lib/dispositivo'

/*
 * Download com cara de loja de app: o icone da marca (o mesmo do app e do
 * instalador), a versao atual com o SHA-256 para quem quer conferir o
 * arquivo, e o historico de versoes publicadas, para mostrar que o produto
 * anda. So' a versao atual tem botao: versao antiga nao recebe correcao.
 */

interface Version { version: string; notes: string; published_at: string }

const REQUISITOS: { Icon: LucideIcon; t: string }[] = [
  { Icon: MonitorCheck, t: 'Windows 11, ou Windows 10 versão 2004 ou mais nova, 64 bits' },
  { Icon: UserRound, t: 'Não precisa ser administrador para o diagnóstico' },
  { Icon: Wifi, t: 'Internet só para entrar na conta e atualizar o catálogo' },
  { Icon: RefreshCw, t: 'Atualiza sozinho, conferindo a assinatura de cada versão nova' },
]

const PASSOS: { Icon: LucideIcon; t: string; d: string }[] = [
  { Icon: ScanSearch, t: 'Instale e abra', d: 'O RKZFPS faz o diagnóstico ao abrir. Nada é alterado nessa etapa.' },
  { Icon: LogIn, t: 'Entre na sua conta', d: 'Na tela Conta do app. Isso ativa este PC no seu plano e libera os recursos.' },
  { Icon: Gauge, t: 'Resolva e meça', d: 'Resolva cada problema encontrado e jogue com o app aberto para medir antes e depois.' },
]

export default function Download() {
  const [release, setRelease] = useState<Release | null | undefined>(undefined)
  const [versions, setVersions] = useState<Version[]>([])
  const aqui = instalaAqui()

  useEffect(() => {
    api.get<{ releases: Release[] }>('/public/releases')
      .then(({ data }) => setRelease(data.releases.find((r) => r.component === 'agent' && r.url) ?? null))
      .catch(() => setRelease(null))
    api.get<{ versions: Version[] }>('/public/changelog').then(({ data }) => setVersions(data.versions)).catch(() => setVersions([]))
  }, [])

  return (
    <PageShell
      title="Baixar o RKZFPS"
      description="Baixe o RKZFPS para Windows 10 e 11. O diagnóstico é gratuito e não altera nada no seu PC."
      width="wide"
      fundo
      bar={{ title: 'Baixar o RKZFPS', sub: 'Instale, abra e o diagnóstico começa sozinho. O scan é gratuito e não altera nada.' }}
    >
      <div className="grid gap-4 lg:grid-cols-3 [&>*]:min-w-0">
        <div className="card p-6 lg:col-span-2">
          <div className="flex items-center gap-4">
            <LogoMark className="h-16 w-16 shrink-0 rounded-2xl shadow-elev" />
            <div className="min-w-0">
              <p className="font-display text-2xl font-bold text-ink-1">RKZFPS {release?.version ?? ''}</p>
              <p className="text-sm text-ink-3">
                {release ? `Publicado em ${date(release.published_at)}. ` : ''}Para Windows 10 e 11, 64 bits.
              </p>
            </div>
          </div>

          {release === undefined && <div className="mt-5"><SkeletonText lines={3} /></div>}
          {release === null && (
            <div className="mt-5">
              <Alert tone="info">O instalador está sendo preparado para lançamento. Crie sua conta agora para ser avisado e garantir o período de teste.</Alert>
            </div>
          )}
          {release && (
            <>
              {/* Passa pelo servidor, que conta o clique (sem dado pessoal) e manda para o instalador.
                  Fora do Windows o .exe nao abre: a acao principal vira mandar o link
                  para o PC, e o download direto fica como opcao discreta. */}
              {aqui ? (
                <Button className="mt-6" size="lg" Icon={DownloadIcon} href="/api/public/download"
                        onClick={() => evento('file_download', { file_name: `RKZFPS-${release.version}.exe` })}>Baixar para Windows</Button>
              ) : (
                <>
                  <MandarParaPC origem="download" className="mt-6" />
                  <Button className="mt-2" variant="link" size="sm" Icon={DownloadIcon} href="/api/public/download"
                          onClick={() => evento('file_download', { file_name: `RKZFPS-${release.version}.exe`, origem: 'fora_do_windows' })}>Baixar o instalador mesmo assim</Button>
                </>
              )}
              {release.notes && <p className="mt-4 whitespace-pre-line text-sm text-ink-2">{release.notes}</p>}
              {release.sha256 && (
                <p className="mt-4 flex items-start gap-2 break-all font-mono text-[11px] text-ink-4">
                  <Fingerprint className="h-3.5 w-3.5 shrink-0" aria-hidden />SHA-256: {release.sha256}
                </p>
              )}
            </>
          )}
          <div className="mt-6 flex flex-wrap gap-3">
            <Button variant="ghost" to="/cadastro">Criar conta grátis</Button>
            <Button variant="link" to="/planos">Ver planos</Button>
          </div>
        </div>

        <div className="card p-6">
          <p className="font-semibold text-ink-1">Requisitos</p>
          <ul className="mt-4 space-y-3 text-sm text-ink-3">
            {REQUISITOS.map(({ Icon, t }) => (
              <li key={t} className="flex gap-3"><Icon className="mt-0.5 h-4 w-4 shrink-0 text-accent-ink" aria-hidden />{t}</li>
            ))}
          </ul>
          <div className="mt-6 flex gap-3 border-t border-line pt-5 text-sm text-ink-3">
            <ShieldCheck className="h-5 w-5 shrink-0 text-accent" aria-hidden />
            <p>O RKZFPS não desativa antivírus, firewall nem atualizações, e toda alteração tem backup e pode ser desfeita.</p>
          </div>
        </div>
      </div>

      <SectionHead className="mt-12" title="Primeiros passos" />
      <ol className="grid gap-4 sm:grid-cols-3">
        {PASSOS.map(({ Icon, t, d }, i) => (
          <li key={t} className="card p-5">
            <div className="flex items-center gap-3">
              <span className="grid h-9 w-9 place-items-center rounded-md bg-accent/15"><Icon className="h-5 w-5 text-accent-ink" aria-hidden /></span>
              <p className="text-xs font-bold uppercase tracking-wide text-accent-ink">Passo {i + 1}</p>
            </div>
            <p className="mt-3 font-semibold text-ink-1">{t}</p>
            <p className="mt-1 text-sm text-ink-3">{d}</p>
          </li>
        ))}
      </ol>

      {/* O instalador ainda nao tem certificado de editor: o SmartScreen do
          Windows avisa em todo app novo assim. Explicar ANTES evita que a
          pessoa desista achando que e' virus. O SHA-256 acima e' a conferencia. */}
      <div className="mt-12 card p-6">
        <div className="flex items-start gap-3">
          <ShieldAlert className="mt-0.5 h-6 w-6 shrink-0 text-accent-ink" aria-hidden />
          <div className="min-w-0">
            <p className="font-semibold text-ink-1">Apareceu "O Windows protegeu o computador"?</p>
            <p className="mt-1 text-sm text-ink-3">
              É o aviso que o Windows mostra para programas novos, que ainda não têm muitos downloads. Não quer dizer que achou vírus.
            </p>
            <ol className="mt-4 grid gap-3 text-sm sm:grid-cols-2">
              <li className="rounded-lg border border-line p-4">
                <p className="text-xs font-bold uppercase tracking-wide text-accent-ink">1</p>
                <p className="mt-1 text-ink-2">Na janela azul, clique em <span className="font-semibold text-ink-1">Mais informações</span>.</p>
              </li>
              <li className="rounded-lg border border-line p-4">
                <p className="text-xs font-bold uppercase tracking-wide text-accent-ink">2</p>
                <p className="mt-1 text-ink-2">Confira o nome RKZFPS e clique em <span className="font-semibold text-ink-1">Executar assim mesmo</span>.</p>
              </li>
            </ol>
            <p className="mt-3 text-xs text-ink-4">Quer conferir o arquivo? Compare o SHA-256 do instalador com o que está nesta página. O app confere a assinatura de cada atualização sozinho.</p>
          </div>
        </div>
      </div>

      {versions.length > 0 && (
        <>
          <SectionHead className="mt-12" title="Versões" sub="Cada versão traz jogos novos, correções e ajustes, sem custo a mais para quem assina." />
          <ol className="relative space-y-3 border-l border-line pl-6">
            {versions.map((v, i) => (
              <li key={v.version} className="relative">
                <span className={`absolute -left-[31px] top-4 h-3 w-3 rounded-full border-2 border-surface-0 ${i === 0 ? 'bg-accent' : 'bg-surface-3'}`} aria-hidden />
                <div className="card p-4">
                  <p className="flex flex-wrap items-center gap-2 font-semibold text-ink-1">
                    <BadgeCheck className="h-4 w-4 text-accent" aria-hidden />Versão {v.version}
                    <span className="text-sm font-normal text-ink-4">{date(v.published_at)}</span>
                    {i === 0 && <span className="rounded-sm bg-accent/15 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide text-accent-ink">Atual</span>}
                  </p>
                  {v.notes && <p className="mt-1.5 text-sm text-ink-3">{v.notes}</p>}
                </div>
              </li>
            ))}
          </ol>
        </>
      )}
    </PageShell>
  )
}
