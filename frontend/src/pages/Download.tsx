import { useEffect, useState } from 'react'
import { Download as DownloadIcon, ShieldCheck } from 'lucide-react'
import api, { type Release } from '../services/api'
import { PageShell, PageTitle } from '../components/Layout'
import { Alert, Button, Card, Spinner } from '../components/ui'
import { date } from '../lib/format'

export default function Download() {
  const [release, setRelease] = useState<Release | null | undefined>(undefined)

  useEffect(() => {
    api.get<{ releases: Release[] }>('/public/releases')
      .then(({ data }) => setRelease(data.releases.find((r) => r.component === 'agent' && r.url) ?? null))
      .catch(() => setRelease(null))
  }, [])

  return (
    <PageShell>
      <PageTitle title="Baixar o FPSX" subtitle="Instale, abra e o diagnóstico começa sozinho. O scan é gratuito e não altera nada no seu PC." />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          {release === undefined && <Spinner />}
          {release === null && (
            <Alert tone="info">O instalador está sendo preparado para lançamento. Crie sua conta agora para ser avisado e garantir o período de teste.</Alert>
          )}
          {release && (
            <>
              <p className="text-lg font-semibold text-ink-1">FPSX {release.version}</p>
              <p className="text-sm text-ink-3">Publicado em {date(release.published_at)}</p>
              <Button className="mt-5" size="lg" icon={DownloadIcon} href={release.url}>Baixar para Windows</Button>
              {release.sha256 && (
                <p className="mt-4 break-all text-xs text-ink-4">SHA-256: {release.sha256}</p>
              )}
              {release.notes && <p className="mt-4 whitespace-pre-line text-sm text-ink-3">{release.notes}</p>}
            </>
          )}
          <div className="mt-6 flex flex-wrap gap-3">
            <Button variant="ghost" to="/cadastro">Criar conta grátis</Button>
            <Button variant="link" to="/planos">Ver planos</Button>
          </div>
        </Card>

        <Card>
          <p className="font-semibold text-ink-1">Requisitos</p>
          <ul className="mt-3 space-y-2 text-sm text-ink-3">
            <li>Windows 11, ou Windows 10 versão 2004 ou mais nova, 64 bits</li>
            <li>Não precisa ser administrador para o diagnóstico</li>
            <li>Internet só para entrar na conta e atualizar o catálogo</li>
          </ul>
          <div className="mt-6 flex gap-3 text-sm text-ink-3">
            <ShieldCheck className="w-5 h-5 shrink-0 text-accent" aria-hidden />
            <p>O FPSX não desativa antivírus, firewall nem atualizações, e toda alteração tem backup e pode ser desfeita.</p>
          </div>
        </Card>
      </div>

      <h2 className="mt-12 mb-4 text-xl font-bold text-ink-1">Primeiros passos</h2>
      <ol className="grid gap-4 sm:grid-cols-3">
        {[
          ['Instale e abra', 'O FPSX faz o diagnóstico ao abrir. Nada é alterado nessa etapa.'],
          ['Entre na sua conta', 'Na tela Conta do app. Isso ativa este PC no seu plano e libera os recursos.'],
          ['Resolva e meça', 'Use Resolver em cada problema encontrado e o Benchmark para medir antes e depois.'],
        ].map(([t, d], i) => (
          <li key={t} className="rounded-xl border border-line bg-surface-1 p-5">
            <p className="text-sm font-bold text-accent-ink">Passo {i + 1}</p>
            <p className="mt-1 font-semibold text-ink-1">{t}</p>
            <p className="mt-1 text-sm text-ink-3">{d}</p>
          </li>
        ))}
      </ol>
    </PageShell>
  )
}
