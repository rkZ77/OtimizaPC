import type { ReactNode } from 'react'
import {
  Activity, ArrowRight, Check, CircleSlash, Cpu, Gamepad2, Gauge, HardDrive, ListChecks, MemoryStick,
  Monitor, MonitorPlay, Network, Power, RotateCcw, ScanSearch, ShieldCheck,
} from 'lucide-react'
import PageShell from '../components/PageShell'
import SiteHeader from '../components/SiteHeader'
import PlansGrid from '../components/PlansGrid'
import { Button, LiveDot, Marquee, StatusBadge } from '../components/ui'

/*
 * Home no desenho da home do Pickia: cabecalho transparente sobre o hero,
 * selo com ponto vivo, titulo com a linha do meio na cor da marca, card de
 * destaque a direita, fita rolando logo abaixo e secoes centralizadas.
 * O conteudo e' do FPSX e segue a regra do produto: nenhum numero de ganho
 * inventado, e o card de diagnostico e' rotulado como exemplo.
 */

function Section({ id, eyebrow, title, sub, children, alt }: { id?: string; eyebrow?: string; title: string; sub?: string; children: ReactNode; alt?: boolean }) {
  return (
    <section id={id} className={alt ? 'section-alt scroll-mt-16' : 'scroll-mt-16'}>
      <div className="max-w-6xl mx-auto px-4 py-16 sm:py-20">
        <div className="text-center max-w-2xl mx-auto">
          {eyebrow && <p className="text-xs font-bold uppercase tracking-widest text-accent-ink">{eyebrow}</p>}
          <h2 className="mt-2 font-display text-2xl sm:text-3xl font-bold text-ink-1">{title}</h2>
          {sub && <p className="mt-3 text-ink-3">{sub}</p>}
        </div>
        <div className="mt-10">{children}</div>
      </div>
    </section>
  )
}

/** O "Dica do dia" do Pickia, aqui como o relatorio do app. Rotulado como exemplo. */
function DiagnosticoCard() {
  const rows: { area: string; texto: string; status: 'ok' | 'attention' | 'problem' }[] = [
    { area: 'CPU', texto: 'Sem limitação no teste de carga', status: 'ok' },
    { area: 'Monitor', texto: '144 Hz disponível, rodando a 60 Hz', status: 'attention' },
    { area: 'Game Mode', texto: 'Já ativado', status: 'ok' },
    { area: 'RAM', texto: '86% em uso antes de abrir o jogo', status: 'attention' },
    { area: 'Disco', texto: 'Alerta de saúde do SMART', status: 'problem' },
    { area: 'Rede', texto: 'Sem perda de pacotes', status: 'ok' },
  ]
  return (
    <div className="rounded-lg border border-line bg-surface-0/80 backdrop-blur-sm shadow-elev">
      <div className="flex items-center justify-between px-5 py-3.5 border-b border-line">
        <div className="flex items-center gap-2 text-sm font-semibold text-ink-1">
          <LiveDot /> Diagnóstico do PC
        </div>
        <span className="text-[10px] font-bold uppercase tracking-wide px-1.5 py-0.5 rounded-sm border border-green-500/30 text-accent-ink bg-green-500/5">Grátis</span>
      </div>
      <div className="px-5 py-4">
        <p className="text-center text-[11px] font-semibold uppercase tracking-widest text-ink-4">Exemplo de relatório</p>
        <ul className="mt-3 divide-y divide-line/60">
          {rows.map((r) => (
            <li key={r.area} className="flex items-center gap-3 py-2.5 text-sm min-w-0">
              <StatusBadge status={r.status} className="w-[74px] justify-center shrink-0" />
              <span className="w-20 shrink-0 font-semibold text-ink-1">{r.area}</span>
              <span className="min-w-0 truncate text-ink-3">{r.texto}</span>
            </li>
          ))}
        </ul>
        <Button to="/download" block size="md" IconRight={ArrowRight} className="mt-4">Analisar o meu PC</Button>
        <div className="mt-3 flex flex-wrap justify-center gap-x-4 gap-y-1 text-[11px] text-ink-3">
          {['Só leitura na análise', 'Backup em tudo', 'Desfazer em todos os planos'].map((t) => (
            <span key={t} className="inline-flex items-center gap-1"><Check className="w-3 h-3 text-accent" aria-hidden />{t}</span>
          ))}
        </div>
      </div>
    </div>
  )
}

const CHECKS = [
  { icon: Cpu, title: 'CPU', text: 'Uso e limitação térmica medidos com teste de carga.' },
  { icon: MonitorPlay, title: 'GPU e driver', text: 'Modelo, memória de vídeo e idade do driver.' },
  { icon: MemoryStick, title: 'RAM', text: 'Pressão de memória e pagefile desligado por tweak antigo.' },
  { icon: HardDrive, title: 'Armazenamento', text: 'SSD ou HD, espaço livre e saúde do disco.' },
  { icon: Monitor, title: 'Monitor', text: 'Taxa de atualização abaixo do que o monitor suporta.' },
  { icon: Gamepad2, title: 'Game Mode e captura', text: 'Game Mode e gravação contínua da Game Bar.' },
  { icon: Power, title: 'Energia', text: 'Plano de energia avaliado por tipo de PC.' },
  { icon: Network, title: 'Rede', text: 'Latência, jitter e perda, local e provedor.' },
  { icon: ListChecks, title: 'Inicialização', text: 'O que abre com o Windows e o que pesa.' },
  { icon: Activity, title: 'Counter-Strike 2', text: 'V-Sync, Reflex e taxa do jogo.' },
]

function CheckChip({ icon: Icon, title, text }: (typeof CHECKS)[number]) {
  return (
    <div className="w-60 shrink-0 rounded-lg border border-line bg-surface-1 p-4">
      <div className="flex items-center gap-2">
        <Icon className="w-4 h-4 text-accent" aria-hidden />
        <p className="font-semibold text-sm text-ink-1">{title}</p>
      </div>
      <p className="mt-1.5 text-xs text-ink-3 leading-relaxed">{text}</p>
    </div>
  )
}

const NOT_DOING = [
  'Desativar Windows Defender, Firewall ou Windows Update',
  'Desligar dezenas de serviços do Windows de uma vez',
  'Colocar o jogo em prioridade Tempo real',
  'Limpar RAM esvaziando o cache do sistema',
  'Mexer em TCP ou DNS prometendo menos ping',
  'Prometer +50 FPS ou PC 300% mais rápido',
]

const FAQ = [
  ['O FPSX aumenta meu FPS?', 'Depende do seu PC, e é exatamente isso que ele descobre. Em PC com configuração errada (monitor a 60 Hz, plano de economia, pagefile desligado, programas pesando) o ganho pode ser grande. Em PC já bem configurado, o FPSX diz que não há nada a mudar. O benchmark mede antes e depois e mostra o número real.'],
  ['É seguro?', 'Cada alteração tem backup antes e verificação depois, e pode ser desfeita a qualquer momento, em qualquer plano. O FPSX só executa operações de uma lista fechada e nunca toca em segurança do Windows.'],
  ['Funciona em PC fraco?', 'É onde mais faz diferença. O FPSX identifica o perfil do seu hardware, aponta o gargalo (CPU, GPU, RAM ou disco) e ajusta o que dá resultado real nesse tipo de PC.'],
  ['Preciso ser administrador?', 'Não para o diagnóstico. Algumas correções que mexem em configuração do sistema pedem para reabrir o FPSX como administrador, e o app avisa quais.'],
  ['Funciona em notebook?', 'Sim. O FPSX identifica notebook e bateria e ajusta as recomendações: nada que aumente consumo e temperatura é aplicado sem aviso.'],
  ['Que dados vocês coletam?', 'Só com a sua permissão, e só o mínimo: quais otimizações foram aplicadas, se funcionaram, resultado de benchmark e versão do app e do Windows. Nunca arquivos, nomes de programas ou conteúdo pessoal.'],
]

export default function Home() {
  return (
    <PageShell nav={<SiteHeader />} width="wide" mainClassName="!max-w-none !px-0 !py-0" revelacao={false}>
      {/* Hero */}
      {/* isolate: a grade (-z-10) fica atras do conteudo mas na frente do fundo da pagina */}
      <section className="relative isolate overflow-hidden pt-16">
        <div className="absolute inset-0 -z-10 bg-data-grid [background-size:48px_48px] [mask-image:radial-gradient(ellipse_at_top,black_30%,transparent_75%)]" aria-hidden />
        <div className="absolute inset-0 -z-10 bg-[radial-gradient(ellipse_at_top_left,rgb(var(--accent)/0.10),transparent_55%)]" aria-hidden />
        <div className="max-w-6xl mx-auto px-4 py-14 sm:py-20 grid gap-12 lg:grid-cols-[1.1fr_0.9fr] items-center [&>*]:min-w-0">
          <div>
            <span className="entra inline-flex items-center gap-2 rounded-full border border-line bg-surface-1/60 px-3 py-1 text-xs font-medium text-ink-2">
              <LiveDot /> Para Windows 10 e 11
            </span>
            <h1 className="entra entra-1 mt-5 font-display text-4xl sm:text-5xl font-extrabold leading-[1.08] text-ink-1">
              Seu PC pode estar entregando
              <span className="block text-accent-ink">menos desempenho</span>
              do que deveria.
            </h1>
            <p className="entra entra-2 mt-5 text-lg text-ink-2 leading-relaxed max-w-xl">
              O FPSX analisa seu computador e aplica apenas otimizações compatíveis que podem trazer
              benefício real. Com backup, desfazer e medição antes e depois.
            </p>
            <div className="entra entra-3 mt-8 flex flex-col sm:flex-row gap-3">
              <Button to="/download" size="lg" Icon={ScanSearch} IconRight={ArrowRight}>ANALISAR MEU PC</Button>
              <Button to="/planos" size="lg" variant="ghost">Ver planos</Button>
            </div>
            <p className="mt-4 text-sm text-ink-4">O diagnóstico completo é gratuito e não altera nada.</p>
          </div>
          <DiagnosticoCard />
        </div>

        {/* Fita rolando, como o "Na fila da IA" do Pickia */}
        <div className="max-w-6xl mx-auto px-4 pb-6">
          <div className="flex items-center justify-between mb-3">
            <p className="flex items-center gap-2 text-sm font-semibold text-ink-1"><LiveDot /> O que o FPSX verifica</p>
            <p className="text-xs text-ink-4">10 áreas do PC</p>
          </div>
        </div>
        <Marquee items={CHECKS.map((c) => <CheckChip key={c.title} {...c} />)} speed={45} />
      </section>

      {/* Principios */}
      <section className="border-y border-line bg-surface-1/40 mt-10">
        <div className="max-w-6xl mx-auto grid gap-6 px-4 py-10 sm:grid-cols-2 lg:grid-cols-4">
          {[
            { icon: ScanSearch, t: 'Diagnóstico antes de alterar', d: 'Nada muda sem motivo encontrado no seu PC.' },
            { icon: RotateCcw, t: 'Backup e desfazer em tudo', d: 'Cada alteração guarda o valor anterior.' },
            { icon: Gauge, t: 'Medição antes e depois', d: 'FPS médio, 1% low e frametime reais.' },
            { icon: ShieldCheck, t: 'Segurança intocada', d: 'Defender, Firewall e Update ficam como estão.' },
          ].map(({ icon: Icon, t, d }) => (
            <div key={t} className="flex gap-3">
              <Icon className="w-5 h-5 shrink-0 text-accent mt-0.5" aria-hidden />
              <div><p className="font-semibold text-ink-1">{t}</p><p className="text-sm text-ink-3">{d}</p></div>
            </div>
          ))}
        </div>
      </section>

      <Section id="como-funciona" eyebrow="Como funciona" title="Medir, alterar só o necessário, medir de novo."
               sub="O FPSX não é uma coleção de comandos. É um motor que decide, item por item, se uma otimização faz sentido para o seu PC.">
        <ol className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[
            ['Instale e analise', 'O app faz o scan ao abrir: hardware, Windows, energia, monitor, rede, processos e jogos. Só leitura.'],
            ['Entenda o relatório', 'Cada ponto diz o que foi encontrado, por que importa e onde o efeito aparece: FPS, stutter, carregamento ou rede.'],
            ['Resolva com backup', 'Um clique em Resolver. O FPSX mostra o que muda e o risco, guarda o estado anterior e confere se funcionou.'],
            ['Meça o resultado', 'O benchmark compara antes e depois. Se não houve ganho real, o FPSX diz isso e você desfaz.'],
          ].map(([t, d], i) => (
            <li key={t} className="card p-5">
              <span className="flex h-9 w-9 items-center justify-center rounded-full bg-accent/15 font-bold text-accent-ink">{i + 1}</span>
              <p className="mt-4 font-semibold text-ink-1">{t}</p>
              <p className="mt-2 text-sm text-ink-3">{d}</p>
            </li>
          ))}
        </ol>
      </Section>

      <Section id="diagnostico" alt eyebrow="Diagnóstico" title="O que o FPSX analisa no seu PC">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
          {CHECKS.map(({ icon: Icon, title, text }) => (
            <div key={title} className="card p-5">
              <Icon className="w-5 h-5 text-accent" aria-hidden />
              <p className="mt-3 font-semibold text-ink-1">{title}</p>
              <p className="mt-1.5 text-sm text-ink-3">{text}</p>
            </div>
          ))}
        </div>
      </Section>

      <Section eyebrow="O diferencial" title="O FPSX sabe quais otimizações fazem sentido para o seu PC."
               sub="Dois PCs, o mesmo catálogo, resultados diferentes. Isso é proposital: o FPSX prefere não alterar nada quando nada precisa ser alterado.">
        <div className="grid gap-4 sm:grid-cols-2 max-w-4xl mx-auto">
          {[
            { pc: 'PC A', desc: 'Monitor em 60 Hz, plano de economia, captura contínua ligada', n: [15, 5, 3, 7] },
            { pc: 'PC B', desc: 'Já bem configurado', n: [15, 2, 1, 12] },
          ].map(({ pc, desc, n }) => (
            <div key={pc} className="card p-6">
              <p className="font-display text-lg font-bold text-ink-1">{pc}</p>
              <p className="text-sm text-ink-3">{desc}</p>
              <dl className="mt-5 grid grid-cols-2 gap-3 text-sm">
                <div><dt className="text-ink-3">No catálogo</dt><dd className="font-mono text-2xl font-bold text-ink-1">{n[0]}</dd></div>
                <div><dt className="text-ink-3">Aplicáveis</dt><dd className="font-mono text-2xl font-bold text-ink-1">{n[1]}</dd></div>
                <div><dt className="text-ink-3">Recomendadas</dt><dd className="font-mono text-2xl font-bold text-accent-ink">{n[2]}</dd></div>
                <div><dt className="text-ink-3">Desnecessárias</dt><dd className="font-mono text-2xl font-bold text-ink-3">{n[3]}</dd></div>
              </dl>
            </div>
          ))}
        </div>
      </Section>

      <Section alt eyebrow="Medição honesta" title="Quando não há ganho, o FPSX diz."
               sub="A variação normal entre duas partidas iguais passa fácil de 3%. O FPSX Benchmark separa ganho real de ruído, e nunca inventa resultado.">
        <div className="grid gap-4 lg:grid-cols-2 max-w-4xl mx-auto">
          <div className="card p-6">
            <p className="text-sm text-ink-3">Resultado possível 1</p>
            <p className="mt-2 text-lg font-semibold text-accent-ink">Ganho significativo em 1% low</p>
            <p className="mt-2 text-sm text-ink-3">A diferença ficou acima da variação entre rodadas. O número mostrado é o medido no seu PC, no seu cenário.</p>
          </div>
          <div className="card p-6">
            <p className="text-sm text-ink-3">Resultado possível 2</p>
            <p className="mt-2 text-lg font-semibold text-ink-1">Não detectamos ganho significativo.</p>
            <p className="mt-2 text-sm text-ink-3">Sua configuração já estava bem otimizada. Você pode desfazer as alterações com um clique.</p>
          </div>
        </div>
      </Section>

      <Section id="o-que-nao-fazemos" eyebrow="Transparência" title="O que o FPSX não faz, de propósito"
               sub="Tweaks populares sem benefício comprovado, ou que colocam o PC em risco, ficam fora do produto. O app mostra cada um e explica o porquê.">
        <ul className="grid gap-3 sm:grid-cols-2 max-w-4xl mx-auto">
          {NOT_DOING.map((item) => (
            <li key={item} className="flex items-start gap-3 card px-4 py-3">
              <CircleSlash className="mt-0.5 w-5 h-5 shrink-0 text-red-400" aria-hidden />
              <span className="text-ink-2">{item}</span>
            </li>
          ))}
        </ul>
      </Section>

      <Section id="planos" alt eyebrow="Planos" title="Do diagnóstico gratuito ao pacote completo"
               sub="Cada plano contém o anterior. Desfazer alterações é liberado em todos, sempre.">
        <PlansGrid compact />
        <div className="mt-6 text-center"><Button variant="ghost" to="/planos">Comparar planos em detalhe</Button></div>
      </Section>

      <Section id="faq" eyebrow="Dúvidas" title="Perguntas frequentes">
        <div className="panel max-w-3xl mx-auto divide-y divide-line">
          {FAQ.map(([q, a]) => (
            <details key={q} className="group p-5">
              <summary className="cursor-pointer list-none font-semibold text-ink-1 flex justify-between gap-4">
                {q}<span className="text-ink-3 group-open:rotate-45 transition-transform" aria-hidden>+</span>
              </summary>
              <p className="mt-3 text-ink-3 leading-relaxed">{a}</p>
            </details>
          ))}
        </div>
      </Section>

      <section className="max-w-6xl mx-auto px-4 pb-20">
        <div className="relative isolate overflow-hidden rounded-lg border border-green-500/30 bg-green-500/5 p-8 sm:p-12 text-center">
          <div className="absolute inset-0 -z-10 bg-data-grid [background-size:32px_32px] opacity-60" aria-hidden />
          <h2 className="font-display text-2xl sm:text-3xl font-bold text-ink-1">Descubra o que realmente pode melhorar no seu PC.</h2>
          <p className="mt-3 text-ink-3">O diagnóstico é gratuito e não altera nada.</p>
          <div className="mt-6 flex justify-center"><Button to="/download" size="lg" Icon={ScanSearch} IconRight={ArrowRight}>ANALISAR MEU PC</Button></div>
        </div>
      </section>
    </PageShell>
  )
}
