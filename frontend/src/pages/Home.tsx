import { useEffect, useState } from 'react'
import {
  ArrowRight, BadgeCheck, CircleSlash, CreditCard, Download, RotateCcw, ShieldCheck, Undo2,
} from 'lucide-react'
import api from '../services/api'
import PageShell from '../components/PageShell'
import SiteHeader from '../components/SiteHeader'
import PlansGrid from '../components/PlansGrid'
import { Button } from '../components/ui'
import { GAMES, GameGrid, Row, ScreenTour } from '../components/Vitrine'
import { usePlanos } from '../hooks/usePlanos'
import { date } from '../lib/format'

/*
 * Home com cara de produto, nao de template: tela REAL do app no lugar de
 * ilustracao, texto direto, garantias visiveis (pagamento, desistencia,
 * desfazer), historico de versoes real e nada de numero de ganho inventado.
 * Menos efeito (sem grade brilhando, sem ponto pulsando, sem fita rolando):
 * e' o que os sites em que as pessoas confiam fazem.
 */

interface Version { version: string; notes: string; published_at: string }

/** Numeros do conjunto. null = abaixo do minimo, e a faixa nao aparece. */
interface Stats { matches: number | null; hours: number | null; games: number | null; scanned_pcs: number | null; found_percent: number | null }

const inteiro = (n: number) => n.toLocaleString('pt-BR')

const GUARANTEES = [
  { icon: CreditCard, t: 'Pagamento pelo Mercado Pago', d: 'PIX ou cartão. O RKZFPS não vê nem guarda os dados do seu cartão.' },
  { icon: Undo2, t: '7 dias para desistir', d: 'Não gostou? Peça o reembolso em até 7 dias da compra, como manda o Código de Defesa do Consumidor.' },
  { icon: RotateCcw, t: 'Desfazer tudo, sempre', d: 'Toda alteração guarda o estado anterior. Desfazer funciona em qualquer plano, mesmo depois de cancelar.' },
  { icon: ShieldCheck, t: 'Segurança do Windows intocada', d: 'Antivírus, firewall e Windows Update ficam exatamente como estão.' },
]

const NOT_DOING = [
  'Desligar antivírus, firewall ou Windows Update',
  'Desligar dezenas de serviços do Windows de uma vez',
  'Colocar o jogo em prioridade "Tempo real"',
  'Prometer "+50 FPS" ou "PC 300% mais rápido"',
  'Instalar driver baixado de fonte que não seja o fabricante',
  'Fechar programa à força e perder o que você não salvou',
]

const FAQ: [string, string][] = [
  ['O RKZFPS aumenta meu FPS?', 'Depende do seu PC, e é isso que ele descobre primeiro. Em PC com configuração errada (monitor rodando a 60 Hz, plano de economia de energia, jogo na placa integrada, programas pesando) o ganho costuma ser grande. Em PC já bem configurado, o RKZFPS diz que não há o que mudar. A medição das partidas mostra o número real, antes e depois.'],
  ['O que eu consigo fazer no plano grátis?', 'Ver tudo: o diagnóstico completo, os problemas encontrados, o que cada otimização resolveria no seu PC e o FPS das suas partidas. Para aplicar as correções, é preciso um plano pago.'],
  ['Se as correções ficam no PC, por que assinar?', 'Porque o PC não fica parado. Atualização do Windows, driver novo e patch de jogo mudam configuração, religam coisas e criam problemas novos. Com um plano, o RKZFPS analisa de novo a cada abertura e corrige o que aparecer, ajusta seus jogos e compara suas partidas antes e depois. O que ele já fez continua no PC mesmo sem plano, e desfazer segue liberado.'],
  ['É seguro? E se der problema?', 'Cada alteração guarda o estado anterior e é conferida depois de aplicada. Qualquer uma pode ser desfeita com um clique, em qualquer plano. O RKZFPS só executa operações de uma lista fechada e revisada.'],
  ['Funciona com anti-cheat (Vanguard, Easy Anti-Cheat)?', 'Sim. A medição de FPS usa o registro de quadros do próprio Windows e não injeta nada no jogo. O RKZFPS também nunca fecha nem mexe em anti-cheat.'],
  ['Preciso ser administrador do PC?', 'Não para usar. Quando uma correção mexe em configuração do sistema, o Windows pede a sua permissão só para ela, e você vê antes o que vai mudar.'],
  ['Posso usar em mais de um PC?', 'Cada assinatura vale para 1 PC por vez. Trocou de PC? Desative o antigo em Minha conta e entre no novo.'],
  ['Como cancelo?', 'Não precisa cancelar: cada compra é um pagamento único, sem renovação automática. O plano vale até o fim do período pago, e o desfazer continua liberado depois. Desistiu em até 7 dias? Peça o reembolso pelo suporte.'],
  ['O app recebe atualizações?', 'Sim. Novos jogos, novas correções e ajustes para versões novas do Windows e dos jogos entram nas atualizações, sem custo a mais para quem assina. O histórico está nesta página.'],
]

export default function Home() {
  const [versions, setVersions] = useState<Version[]>([])
  const [stats, setStats] = useState<Stats | null>(null)
  const { data: planos } = usePlanos()
  useEffect(() => {
    api.get<{ versions: Version[] }>('/public/changelog').then(({ data }) => setVersions(data.versions)).catch(() => setVersions([]))
    api.get<Stats>('/public/stats').then(({ data }) => setStats(data)).catch(() => setStats(null))
  }, [])
  const latest = versions[0]
  // O teste gratis e' a oferta sem risco: vai no botao principal, nao escondido embaixo da grade.
  const trial = planos?.trial_days ?? 0
  const trialTexto = trial === 1 ? '1 dia' : `${trial} dias`
  const trialFixes = planos?.trial_max_fixes ?? 0
  const provas = [
    stats?.found_percent != null && stats.scanned_pcs != null && { v: `${stats.found_percent}%`, t: `dos ${inteiro(stats.scanned_pcs)} PCs analisados tinham algo para corrigir` },
    stats?.matches != null && { v: inteiro(stats.matches), t: 'partidas medidas com o app aberto' },
    stats?.hours != null && { v: `${inteiro(stats.hours)} h`, t: 'de jogo medidas, antes e depois' },
  ].filter((p): p is { v: string; t: string } => Boolean(p))

  return (
    <PageShell nav={<SiteHeader />} width="wide" mainClassName="!max-w-none !px-0 !py-0" revelacao={false}>
      {/* Hero: o app de verdade, sem efeito de fundo */}
      <section className="pt-16">
        <div className="mx-auto grid max-w-6xl items-center gap-12 px-4 py-14 sm:py-20 lg:grid-cols-[0.95fr_1.05fr] [&>*]:min-w-0">
          <div>
            <h1 className="font-display text-4xl font-extrabold leading-[1.1] text-ink-1 sm:text-5xl">
              Descubra o que está travando seus jogos. E corrija sem medo.
            </h1>
            <p className="mt-5 max-w-xl text-lg leading-relaxed text-ink-2">
              O RKZFPS analisa o Windows e o hardware do seu PC, corrige só o que encontrar de errado e mede o FPS
              das suas partidas antes e depois. Toda alteração tem backup e pode ser desfeita.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row">
              <Button to="/download" size="lg" Icon={Download}>Baixar grátis para Windows</Button>
              <Button to="/planos" size="lg" variant="ghost">Ver planos</Button>
            </div>
            {trial > 0 && (
              <p className="mt-5 inline-flex items-start gap-2 rounded-md border border-accent/40 bg-accent/10 px-3 py-2 text-sm text-ink-1">
                <BadgeCheck className="mt-0.5 h-4 w-4 shrink-0 text-accent-ink" aria-hidden />
                <span>Crie a conta no app e teste o Pro por {trialTexto}, sem cartão: medição das partidas, ajuste dos jogos e até {trialFixes} correções.</span>
              </p>
            )}
            <p className="mt-4 text-sm text-ink-3">
              Windows 10 e 11, 64 bits.{latest ? ` Versão ${latest.version}, de ${date(latest.published_at)}.` : ''} O diagnóstico é grátis e não altera nada.
            </p>
          </div>
          <ScreenTour />
        </div>
      </section>

      {/* Prova social: so' numeros reais do conjunto, e so' acima do minimo */}
      {provas.length > 0 && (
        <section className="border-t border-line">
          <dl className={`mx-auto grid max-w-6xl gap-6 px-4 py-8 text-center ${['', 'sm:grid-cols-1', 'sm:grid-cols-2', 'sm:grid-cols-3'][provas.length]}`}>
            {provas.map((p) => (
              <div key={p.t}>
                <dt className="sr-only">{p.t}</dt>
                <dd className="font-display text-3xl font-extrabold text-ink-1">{p.v}</dd>
                <dd className="mt-1 text-sm text-ink-3">{p.t}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}

      {/* Garantias */}
      <section className="border-y border-line bg-surface-1/50">
        <div className="mx-auto grid max-w-6xl gap-6 px-4 py-10 sm:grid-cols-2 lg:grid-cols-4">
          {GUARANTEES.map(({ icon: Icon, t, d }) => (
            <div key={t} className="flex gap-3">
              <Icon className="mt-0.5 h-5 w-5 shrink-0 text-accent" aria-hidden />
              <div><p className="font-semibold text-ink-1">{t}</p><p className="mt-1 text-sm text-ink-3">{d}</p></div>
            </div>
          ))}
        </div>
      </section>

      {/* Como funciona, com as telas reais */}
      <section id="como-funciona" className="scroll-mt-16">
        <div className="mx-auto max-w-6xl space-y-20 px-4 py-16 sm:py-24">
          <div className="max-w-2xl">
            <h2 className="font-display text-3xl font-bold text-ink-1">Como funciona</h2>
            <p className="mt-3 text-ink-3">Analisar, corrigir o que estiver errado e provar o resultado. Nessa ordem.</p>
          </div>
          <Row title="1. Um diagnóstico que não chuta" img="/img/app-otimizacoes.png" alt="Tela de otimizações do RKZFPS, com o motivo de cada uma">
            <p>Ao abrir, o RKZFPS lê processador, placa de vídeo, memória, discos, energia, monitor, rede, inicialização e os jogos instalados. Só leitura: nada muda nessa etapa.</p>
            <p>Cada ponto diz o que foi encontrado e por que importa. Se o PC já está bem configurado, ele diz isso, e não inventa trabalho.</p>
          </Row>
          <Row flip title="2. Ajuste por jogo, no nível do seu PC" img="/img/app-jogos.png" alt="Tela de jogos do RKZFPS">
            <p>O RKZFPS reconhece {GAMES.length} jogos e sabe o que pesa em cada um. No CS2, no Fortnite e no Minecraft ele corrige o arquivo de vídeo sozinho, com o jogo fechado e com backup.</p>
            <p>Em PC fraco, oferece uma configuração leve que só reduz o que está pesado: nunca deixa pior o que você já tinha ajustado.</p>
          </Row>
          <Row title="3. Prova de resultado nas suas partidas" img="/img/app-partidas.png" alt="Tela de partidas do RKZFPS">
            <p>Com o RKZFPS aberto (pode ser perto do relógio), cada partida é medida sozinha: FPS médio, 1% low e travadas por minuto. Só entram os minutos com o jogo na tela.</p>
            <p>Depois de otimizar, o app compara as partidas de antes e de depois. Se a diferença estiver dentro da variação normal, ele diz que não houve ganho.</p>
          </Row>
          <Button variant="ghost" to="/como-funciona" IconRight={ArrowRight}>Ver tudo o que o RKZFPS analisa e corrige</Button>
        </div>
      </section>

      {/* Jogos */}
      <section id="jogos" className="scroll-mt-16 border-y border-line bg-surface-1/50">
        <div className="mx-auto max-w-6xl px-4 py-14">
          <h2 className="font-display text-2xl font-bold text-ink-1">Jogos reconhecidos</h2>
          <p className="mt-2 text-ink-3">A lista cresce a cada atualização. A medição não mexe no jogo e funciona com anti-cheat. </p>
          <GameGrid />
        </div>
      </section>

      {/* O que nao faz */}
      <section id="o-que-nao-fazemos" className="scroll-mt-16">
        <div className="mx-auto grid max-w-6xl gap-10 px-4 py-16 lg:grid-cols-[0.8fr_1.2fr]">
          <div>
            <h2 className="font-display text-2xl font-bold text-ink-1">O que o RKZFPS não faz, de propósito</h2>
            <p className="mt-3 text-ink-3">Muito "otimizador" ganha fama com truque que não funciona ou que deixa o PC exposto. Isto fica de fora do RKZFPS, e o app mostra o porquê de cada um.</p>
          </div>
          <ul className="grid gap-3 sm:grid-cols-2">
            {NOT_DOING.map((item) => (
              <li key={item} className="flex items-start gap-3 rounded-lg border border-line px-4 py-3">
                <CircleSlash className="mt-0.5 h-5 w-5 shrink-0 text-red-400" aria-hidden />
                <span className="text-ink-2">{item}</span>
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* Sempre atualizado: versoes reais publicadas */}
      {versions.length > 0 && (
        <section className="border-y border-line bg-surface-1/50">
          <div className="mx-auto grid max-w-6xl gap-10 px-4 py-16 lg:grid-cols-[0.8fr_1.2fr]">
            <div>
              <h2 className="font-display text-2xl font-bold text-ink-1">Sempre atualizado</h2>
              <p className="mt-3 text-ink-3">Jogos e o Windows mudam o tempo todo. O RKZFPS acompanha: cada versão traz jogos novos, correções e ajustes, sem custo a mais para quem assina.</p>
            </div>
            <ol className="space-y-4">
              {versions.slice(0, 4).map((v) => (
                <li key={v.version} className="rounded-lg border border-line bg-surface-0 p-4">
                  <p className="flex items-center gap-2 font-semibold text-ink-1"><BadgeCheck className="h-4 w-4 text-accent" aria-hidden />Versão {v.version}<span className="text-sm font-normal text-ink-4">{date(v.published_at)}</span></p>
                  {v.notes && <p className="mt-1.5 text-sm text-ink-3">{v.notes}</p>}
                </li>
              ))}
            </ol>
          </div>
        </section>
      )}

      {/* Planos */}
      <section id="planos" className="scroll-mt-16">
        <div className="mx-auto max-w-6xl px-4 py-16 sm:py-20">
          <div className="max-w-2xl">
            <h2 className="font-display text-3xl font-bold text-ink-1">Planos</h2>
            <p className="mt-3 text-ink-3">Comece grátis para ver o diagnóstico do seu PC. Assine quando quiser que o RKZFPS corrija.</p>
          </div>
          <div className="mt-10"><PlansGrid compact /></div>
          <div className="mt-6"><Button variant="ghost" to="/planos" IconRight={ArrowRight}>Comparar os planos em detalhe</Button></div>
        </div>
      </section>

      {/* Perguntas */}
      <section id="faq" className="scroll-mt-16 border-t border-line">
        <div className="mx-auto grid max-w-6xl gap-10 px-4 py-16 lg:grid-cols-[0.8fr_1.2fr]">
          <div>
            <h2 className="font-display text-2xl font-bold text-ink-1">Perguntas frequentes</h2>
            <p className="mt-3 text-ink-3">Não achou sua dúvida? Pergunte ao assistente ou fale com o suporte pelo menu.</p>
          </div>
          <div className="divide-y divide-line rounded-lg border border-line">
            {FAQ.map(([q, a]) => (
              <details key={q} className="group p-5">
                <summary className="flex cursor-pointer list-none justify-between gap-4 font-semibold text-ink-1">
                  {q}<span className="text-ink-3 transition-transform group-open:rotate-45" aria-hidden>+</span>
                </summary>
                <p className="mt-3 leading-relaxed text-ink-3">{a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>

      <section className="border-t border-line">
        <div className="mx-auto flex max-w-6xl flex-col items-start gap-5 px-4 py-14 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="font-display text-2xl font-bold text-ink-1">Veja o que está pesando no seu PC.</h2>
            <p className="mt-2 text-ink-3">{trial > 0 ? `Diagnóstico grátis e ${trialTexto} do Pro para testar, sem cartão.` : 'Grátis, em poucos minutos, sem mudar nada.'}</p>
          </div>
          <Button to="/download" size="lg" Icon={Download}>Baixar grátis</Button>
        </div>
      </section>
    </PageShell>
  )
}
