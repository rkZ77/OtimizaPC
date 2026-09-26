import {
  Activity, Cpu, Gauge, HardDrive, MemoryStick, Monitor, Network, Power, Rocket, RotateCcw, ScanSearch,
  ShieldCheck, Gamepad2, ListChecks, MonitorPlay, CircleSlash,
} from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '../components/ui'
import PlansGrid from '../components/PlansGrid'

function Section({ id, eyebrow, title, subtitle, children }: { id?: string; eyebrow?: string; title: string; subtitle?: string; children: ReactNode }) {
  return (
    <section id={id} className="mx-auto max-w-page px-4 sm:px-6 py-16 sm:py-20 scroll-mt-16">
      {eyebrow && <p className="text-sm font-semibold uppercase tracking-wider text-accent-ink">{eyebrow}</p>}
      <h2 className="mt-2 text-2xl sm:text-3xl font-bold text-ink-1 max-w-2xl">{title}</h2>
      {subtitle && <p className="mt-3 max-w-2xl text-ink-3">{subtitle}</p>}
      <div className="mt-10">{children}</div>
    </section>
  )
}

/** Ilustração do relatório do app. Rotulada como exemplo, sem número de ganho inventado. */
function ReportPreview() {
  const rows: [string, string, 'ok' | 'warn' | 'danger'][] = [
    ['CPU', 'Sem limitação durante o teste', 'ok'],
    ['Monitor', '144 Hz disponível, rodando a 60 Hz', 'warn'],
    ['Game Mode', 'Já ativado', 'ok'],
    ['RAM', '86% em uso antes de abrir o jogo', 'warn'],
    ['Armazenamento', 'Disco com alerta de saúde', 'danger'],
    ['Rede', 'Sem perda de pacotes', 'ok'],
  ]
  const tone = { ok: 'bg-accent/15 text-accent-ink', warn: 'bg-warn/15 text-warn', danger: 'bg-danger/15 text-danger' }
  const label = { ok: 'OK', warn: 'ATENÇÃO', danger: 'PROBLEMA' }
  return (
    <div className="min-w-0 rounded-2xl border border-line bg-surface-1 p-4 sm:p-5 shadow-2xl shadow-black/40" aria-label="Exemplo de relatório do FPSX">
      <div className="flex items-center justify-between">
        <p className="font-semibold text-ink-1">Performance Readiness</p>
        <span className="text-xs text-ink-4">exemplo</span>
      </div>
      <ul className="mt-4 space-y-2.5">
        {rows.map(([area, text, t]) => (
          <li key={area} className="flex items-center gap-2 sm:gap-3 text-sm min-w-0">
            <span className={`w-[72px] shrink-0 rounded-md px-1.5 py-0.5 text-center text-[11px] font-bold ${tone[t]}`}>{label[t]}</span>
            <span className="w-24 sm:w-28 shrink-0 font-medium text-ink-1 truncate">{area}</span>
            <span className="min-w-0 flex-1 text-ink-3 truncate">{text}</span>
          </li>
        ))}
      </ul>
      <div className="mt-5 grid grid-cols-3 gap-2 text-center">
        <div className="rounded-lg bg-surface-2 p-3"><p className="text-xl font-bold text-warn">3</p><p className="text-[11px] text-ink-3">atenção</p></div>
        <div className="rounded-lg bg-surface-2 p-3"><p className="text-xl font-bold text-info">2</p><p className="text-[11px] text-ink-3">recomendadas</p></div>
        <div className="rounded-lg bg-surface-2 p-3"><p className="text-xl font-bold text-accent-ink">14</p><p className="text-[11px] text-ink-3">já otimizado</p></div>
      </div>
    </div>
  )
}

const CHECKS = [
  { icon: Cpu, title: 'CPU', text: 'Uso, limitação térmica e de energia medidas com teste de carga. Problema físico é chamado de problema físico.' },
  { icon: MonitorPlay, title: 'GPU e driver', text: 'Modelo, VRAM e idade do driver, com o link oficial do fabricante. O FPSX nunca baixa executável.' },
  { icon: MemoryStick, title: 'RAM e paginação', text: 'Pressão de memória, quem está consumindo, e pagefile desativado por tweak antigo.' },
  { icon: HardDrive, title: 'Armazenamento', text: 'SSD ou HD, espaço livre e saúde do disco. Jogo em HD afeta carregamento, não FPS médio, e dizemos isso.' },
  { icon: Monitor, title: 'Monitor', text: 'Monitor de 144 Hz rodando a 60 Hz é mais comum do que parece. O FPSX detecta e corrige.' },
  { icon: Gamepad2, title: 'Game Mode e captura', text: 'Game Mode desligado e gravação contínua da Game Bar ocupando o encoder da GPU.' },
  { icon: Power, title: 'Energia', text: 'Plano de energia avaliado por tipo de PC. Notebook na bateria não recebe Alto desempenho.' },
  { icon: Network, title: 'Rede', text: 'Latência, jitter e perda separados entre rede local e provedor. Sem promessa de ping com DNS.' },
  { icon: ListChecks, title: 'Inicialização e processos', text: 'O que abre com o Windows e o que pesa em segundo plano. Você escolhe item a item.' },
  { icon: Activity, title: 'Counter-Strike 2', text: 'V-Sync, Reflex e taxa de atualização do jogo lidos do arquivo de vídeo e corrigidos com o jogo fechado.' },
]

const NOT_DOING = [
  'Desativar Windows Defender, Firewall ou Windows Update',
  'Desligar dezenas de serviços do Windows de uma vez',
  'Colocar o jogo em prioridade Tempo real',
  '"Limpar RAM" esvaziando o cache do sistema',
  'Mexer em TCP ou DNS prometendo menos ping',
  'Prometer "+50 FPS" ou "PC 300% mais rápido"',
]

const FAQ = [
  ['O FPSX aumenta meu FPS?', 'Depende do seu PC, e é exatamente isso que ele descobre. Em PC com configuração errada (monitor a 60 Hz, plano de economia, pagefile desligado, programas pesando) o ganho pode ser grande. Em PC já bem configurado, o FPSX diz que não há nada a mudar. O benchmark mede antes e depois e mostra o número real.'],
  ['É seguro?', 'Cada alteração tem backup antes e verificação depois, e pode ser desfeita a qualquer momento, em qualquer plano. O FPSX só executa operações de uma lista fechada e nunca toca em segurança do Windows.'],
  ['Preciso ser administrador?', 'Não para o diagnóstico. Algumas correções que mexem em configuração do sistema pedem para reabrir o FPSX como administrador, e o app avisa quais.'],
  ['Funciona em notebook?', 'Sim. O FPSX identifica notebook e bateria e ajusta as recomendações: nada que aumente consumo e temperatura é aplicado sem aviso.'],
  ['Quais jogos são suportados?', 'O diagnóstico do PC vale para qualquer jogo. O perfil de jogo com correção de configuração começa pelo Counter-Strike 2, e novos jogos entram sem reinstalar o app.'],
  ['Que dados vocês coletam?', 'Só com a sua permissão, e só o mínimo: quais otimizações foram aplicadas, se funcionaram, resultado de benchmark e versão do app e do Windows. Nunca arquivos, nomes de programas ou conteúdo pessoal.'],
]

export default function Home() {
  return (
    <>
      {/* Hero, seção 33 do spec */}
      <section className="relative overflow-hidden">
        <div className="absolute inset-0 -z-10 bg-[radial-gradient(ellipse_at_top,rgb(var(--accent)/0.12),transparent_60%)]" aria-hidden />
        <div className="mx-auto grid max-w-page items-center gap-12 px-4 sm:px-6 py-16 sm:py-24 lg:grid-cols-2 [&>*]:min-w-0">
          <div>
            <p className="inline-flex rounded-full border border-line px-3 py-1 text-xs font-medium text-ink-3">Para Windows 10 e 11</p>
            <h1 className="mt-5 text-4xl sm:text-5xl font-bold leading-tight text-ink-1">
              Seu PC pode estar entregando menos desempenho do que deveria.
            </h1>
            <p className="mt-5 text-lg text-ink-3">
              O FPSX analisa seu computador e aplica apenas otimizações compatíveis que podem trazer benefício real.
              Com backup, desfazer e medição antes e depois.
            </p>
            <div className="mt-8 flex flex-col sm:flex-row gap-3">
              <Button size="lg" to="/download" icon={ScanSearch}>ANALISAR MEU PC</Button>
              <Button size="lg" variant="ghost" to="/planos">Ver planos</Button>
            </div>
            <p className="mt-4 text-sm text-ink-4">O diagnóstico completo é gratuito.</p>
          </div>
          <ReportPreview />
        </div>
      </section>

      {/* Princípios */}
      <section className="border-y border-line bg-surface-1/50">
        <div className="mx-auto grid max-w-page gap-6 px-4 sm:px-6 py-10 sm:grid-cols-2 lg:grid-cols-4">
          {[
            { icon: ScanSearch, t: 'Diagnóstico antes de alterar', d: 'Nada muda sem motivo encontrado no seu PC.' },
            { icon: RotateCcw, t: 'Backup e desfazer em tudo', d: 'Cada alteração guarda o valor anterior.' },
            { icon: Gauge, t: 'Medição antes e depois', d: 'FPS médio, 1% low e frametime reais.' },
            { icon: ShieldCheck, t: 'Segurança intocada', d: 'Defender, Firewall e Update ficam como estão.' },
          ].map(({ icon: Icon, t, d }) => (
            <div key={t} className="flex gap-3">
              <Icon className="w-6 h-6 shrink-0 text-accent" aria-hidden />
              <div><p className="font-semibold text-ink-1">{t}</p><p className="text-sm text-ink-3">{d}</p></div>
            </div>
          ))}
        </div>
      </section>

      <Section id="como-funciona" eyebrow="Como funciona" title="Medir, alterar só o necessário, medir de novo."
               subtitle="O FPSX não é uma coleção de comandos. É um motor que decide, item por item, se uma otimização faz sentido para o seu PC.">
        <ol className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[
            ['Instale e analise', 'O app faz o scan ao abrir: hardware, Windows, energia, monitor, rede, processos e jogos. Só leitura.'],
            ['Entenda o relatório', 'Cada ponto diz o que foi encontrado, por que importa e onde o efeito aparece: FPS, stutter, carregamento ou rede.'],
            ['Resolva com backup', 'Um clique em Resolver. O FPSX mostra o que muda e o risco, guarda o estado anterior e confere se funcionou.'],
            ['Meça o resultado', 'O benchmark compara antes e depois. Se não houve ganho real, o FPSX diz isso e você desfaz.'],
          ].map(([t, d], i) => (
            <li key={t} className="rounded-xl border border-line bg-surface-1 p-5">
              <span className="flex h-9 w-9 items-center justify-center rounded-full bg-accent/15 font-bold text-accent-ink">{i + 1}</span>
              <p className="mt-4 font-semibold text-ink-1">{t}</p>
              <p className="mt-2 text-sm text-ink-3">{d}</p>
            </li>
          ))}
        </ol>
      </Section>

      <Section eyebrow="Diagnóstico" title="O que o FPSX analisa no seu PC">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {CHECKS.map(({ icon: Icon, title, text }) => (
            <div key={title} className="rounded-xl border border-line bg-surface-1 p-5">
              <Icon className="w-6 h-6 text-accent" aria-hidden />
              <p className="mt-3 font-semibold text-ink-1">{title}</p>
              <p className="mt-1.5 text-sm text-ink-3">{text}</p>
            </div>
          ))}
        </div>
      </Section>

      <Section eyebrow="O diferencial" title="O FPSX sabe quais otimizações fazem sentido para o seu PC."
               subtitle="Dois PCs, o mesmo catálogo, resultados diferentes. Isso é proposital: o FPSX prefere não alterar nada quando nada precisa ser alterado.">
        <div className="grid gap-4 sm:grid-cols-2">
          {[
            { pc: 'PC A', desc: 'Monitor em 60 Hz, plano de economia, captura contínua ligada', n: [15, 5, 3, 7] },
            { pc: 'PC B', desc: 'Já bem configurado', n: [15, 2, 1, 12] },
          ].map(({ pc, desc, n }) => (
            <div key={pc} className="rounded-xl border border-line bg-surface-1 p-6">
              <p className="text-lg font-bold text-ink-1">{pc}</p>
              <p className="text-sm text-ink-3">{desc}</p>
              <dl className="mt-5 grid grid-cols-2 gap-3 text-sm">
                <div><dt className="text-ink-3">Otimizações no catálogo</dt><dd className="text-2xl font-bold text-ink-1">{n[0]}</dd></div>
                <div><dt className="text-ink-3">Aplicáveis</dt><dd className="text-2xl font-bold text-ink-1">{n[1]}</dd></div>
                <div><dt className="text-ink-3">Recomendadas</dt><dd className="text-2xl font-bold text-accent-ink">{n[2]}</dd></div>
                <div><dt className="text-ink-3">Desnecessárias</dt><dd className="text-2xl font-bold text-ink-3">{n[3]}</dd></div>
              </dl>
            </div>
          ))}
        </div>
      </Section>

      <Section eyebrow="Medição honesta" title="Quando não há ganho, o FPSX diz."
               subtitle="A variação normal entre duas partidas iguais passa fácil de 3%. O FPSX Benchmark separa ganho real de ruído, e nunca inventa resultado.">
        <div className="grid gap-4 lg:grid-cols-2">
          <div className="rounded-xl border border-line bg-surface-1 p-6">
            <p className="text-sm text-ink-3">Resultado possível 1</p>
            <p className="mt-2 text-lg font-semibold text-accent-ink">Ganho significativo em 1% low</p>
            <p className="mt-2 text-sm text-ink-3">A diferença ficou acima da variação entre rodadas. O número mostrado é o medido no seu PC, no seu cenário.</p>
          </div>
          <div className="rounded-xl border border-line bg-surface-1 p-6">
            <p className="text-sm text-ink-3">Resultado possível 2</p>
            <p className="mt-2 text-lg font-semibold text-ink-1">Não detectamos ganho significativo.</p>
            <p className="mt-2 text-sm text-ink-3">Sua configuração já estava bem otimizada. Você pode desfazer as alterações com um clique.</p>
          </div>
        </div>
      </Section>

      <Section id="o-que-nao-fazemos" eyebrow="Transparência" title="O que o FPSX não faz, de propósito"
               subtitle="Tweaks populares que não têm benefício comprovado ou colocam o PC em risco ficam fora do produto. O app mostra cada um e explica o porquê.">
        <ul className="grid gap-3 sm:grid-cols-2">
          {NOT_DOING.map((item) => (
            <li key={item} className="flex items-start gap-3 rounded-lg border border-line bg-surface-1 px-4 py-3">
              <CircleSlash className="mt-0.5 w-5 h-5 shrink-0 text-danger" aria-hidden />
              <span className="text-ink-2">{item}</span>
            </li>
          ))}
        </ul>
      </Section>

      <Section id="planos" eyebrow="Planos" title="Do diagnóstico gratuito ao pacote completo"
               subtitle="Cada plano contém o anterior. Desfazer alterações é liberado em todos, sempre.">
        <PlansGrid compact />
        <div className="mt-6 text-center"><Button variant="ghost" to="/planos">Comparar planos em detalhe</Button></div>
      </Section>

      <Section id="faq" eyebrow="Dúvidas" title="Perguntas frequentes">
        <div className="divide-y divide-line rounded-xl border border-line bg-surface-1">
          {FAQ.map(([q, a]) => (
            <details key={q} className="group p-5">
              <summary className="cursor-pointer list-none font-semibold text-ink-1 flex justify-between gap-4">
                {q}<span className="text-ink-3 group-open:rotate-45 transition-transform" aria-hidden>+</span>
              </summary>
              <p className="mt-3 text-ink-3">{a}</p>
            </details>
          ))}
        </div>
      </Section>

      <section className="mx-auto max-w-page px-4 sm:px-6">
        <div className="rounded-2xl border border-accent/40 bg-accent/10 p-8 sm:p-12 text-center">
          <Rocket className="mx-auto w-8 h-8 text-accent" aria-hidden />
          <h2 className="mt-4 text-2xl sm:text-3xl font-bold text-ink-1">Descubra o que realmente pode melhorar no seu PC.</h2>
          <p className="mt-3 text-ink-3">O diagnóstico é gratuito e não altera nada.</p>
          <div className="mt-6"><Button size="lg" to="/download" icon={ScanSearch}>ANALISAR MEU PC</Button></div>
        </div>
      </section>
    </>
  )
}
