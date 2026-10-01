import {
  ArrowRight, CircuitBoard, Cable, Cpu, Download, Gamepad2, Gauge, HardDrive, History, ListChecks, Lock,
  MemoryStick, Monitor, Rocket, ScanSearch, ShieldCheck, TriangleAlert, Undo2, Wifi, Wrench, Zap,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import PageShell from '../components/PageShell'
import { Button } from '../components/ui'
import { GAMES, GameGrid, Row } from '../components/Vitrine'

/*
 * Como funciona, em pagina propria: a home conta a historia curta, e aqui fica
 * o detalhe para quem quer saber exatamente o que o app le, o que muda e como
 * volta atras antes de instalar. Nenhum numero de ganho: so' o que o app faz.
 */

const PASSOS: { Icon: LucideIcon; t: string; d: string }[] = [
  { Icon: Download, t: 'Baixe e abra', d: 'Instalador para Windows 10 e 11. Não pede cadastro para o diagnóstico.' },
  { Icon: ScanSearch, t: 'Diagnóstico', d: 'O app lê o PC inteiro em poucos minutos. Só leitura: nada muda nessa etapa.' },
  { Icon: Wrench, t: 'Corrige o que faz sentido', d: 'Um clique por item, com o motivo explicado e backup antes de mudar.' },
  { Icon: Gauge, t: 'Mede nas partidas', d: 'FPS médio, 1% low e travadas das suas partidas, antes e depois.' },
]

const ANALISA: { Icon: LucideIcon; t: string; d: string }[] = [
  { Icon: Cpu, t: 'Processador', d: 'Modelo, núcleos e se o plano de energia está segurando o desempenho.' },
  { Icon: CircuitBoard, t: 'Placa de vídeo', d: 'Qual placa o jogo usa de verdade, driver e se o jogo caiu na integrada.' },
  { Icon: MemoryStick, t: 'Memória', d: 'Quantidade, velocidade de fábrica (XMP/EXPO) e se tem um pente só.' },
  { Icon: HardDrive, t: 'Discos', d: 'Onde cada jogo está instalado, espaço livre e alerta de saúde do disco.' },
  { Icon: Zap, t: 'Energia', d: 'Plano de economia ligado em PC de mesa, o erro mais comum e mais barato de corrigir.' },
  { Icon: Monitor, t: 'Monitor', d: 'Se ele roda na taxa máxima (144 Hz rodando a 60 é comum) e em qual placa está ligado.' },
  { Icon: Wifi, t: 'Rede', d: 'Conexão, adaptador e o que costuma atrapalhar o ping em jogo online.' },
  { Icon: Rocket, t: 'Inicialização', d: 'Programas que abrem com o Windows e ficam pesando durante a partida.' },
  { Icon: Gamepad2, t: 'Jogos instalados', d: `Os ${GAMES.length} jogos reconhecidos e a configuração de vídeo de cada um.` },
]

const FORA_DO_WINDOWS: { Icon: LucideIcon; t: string }[] = [
  { Icon: MemoryStick, t: 'Memória abaixo da velocidade de fábrica: passo a passo da BIOS da sua placa-mãe' },
  { Icon: MemoryStick, t: 'Memória com um pente só, que perde o canal duplo' },
  { Icon: Cable, t: 'Cabo do monitor na placa-mãe em vez da placa de vídeo' },
  { Icon: HardDrive, t: 'Jogo instalado em HD comum em vez do SSD' },
  { Icon: CircuitBoard, t: 'Driver de vídeo antigo, com o link oficial do fabricante' },
  { Icon: TriangleAlert, t: 'Disco com alerta de saúde, antes de você perder arquivo' },
]

const SEGURANCA: { Icon: LucideIcon; t: string; d: string }[] = [
  { Icon: History, t: 'Backup antes de cada mudança', d: 'O app guarda o estado anterior de tudo o que altera.' },
  { Icon: Undo2, t: 'Desfazer com um clique', d: 'Em qualquer plano, mesmo depois de cancelar a assinatura.' },
  { Icon: ListChecks, t: 'Lista fechada de alterações', d: 'O app só executa operações revisadas. O servidor não manda o app rodar nada.' },
  { Icon: Lock, t: 'Proteção do Windows intocada', d: 'Antivírus, firewall e Windows Update ficam como estão.' },
]

function Grade({ itens }: { itens: { Icon: LucideIcon; t: string; d: string }[] }) {
  return (
    <ul className="mt-8 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {itens.map(({ Icon, t, d }) => (
        <li key={t} className="card flex gap-3 p-5">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-md border border-line bg-surface-2">
            <Icon className="h-5 w-5 text-accent-ink" aria-hidden />
          </span>
          <div className="min-w-0">
            <p className="font-semibold text-ink-1">{t}</p>
            <p className="mt-1 text-sm text-ink-3">{d}</p>
          </div>
        </li>
      ))}
    </ul>
  )
}

export default function ComoFunciona() {
  return (
    <PageShell
      title="Como funciona"
      description="Como o RKZFPS analisa o PC, corrige só o que faz sentido, guarda backup de cada alteração e mede o FPS das suas partidas antes e depois."
      width="wide"
      fundo
      bar={{ title: 'Como funciona', sub: 'Analisar, corrigir o que estiver errado e provar o resultado. Nessa ordem.' }}
    >
      <ol className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {PASSOS.map(({ Icon, t, d }, i) => (
          <li key={t} className="card p-5">
            <div className="flex items-center gap-3">
              <span className="grid h-9 w-9 place-items-center rounded-md bg-accent/15"><Icon className="h-5 w-5 text-accent-ink" aria-hidden /></span>
              <span className="text-xs font-bold uppercase tracking-wide text-accent-ink">Passo {i + 1}</span>
            </div>
            <p className="mt-3 font-semibold text-ink-1">{t}</p>
            <p className="mt-1 text-sm text-ink-3">{d}</p>
          </li>
        ))}
      </ol>

      <div className="mt-20 space-y-20">
        <Row title="Um diagnóstico que não chuta" img="/img/app-dashboard-v2.png" alt="Tela inicial do RKZFPS com o diagnóstico do PC">
          <p>Ao abrir, o RKZFPS lê o hardware e o Windows e mostra o que encontrou, com o efeito esperado no jogo.</p>
          <p>Cada ponto diz o que foi encontrado e por que importa. Se o PC já está bem configurado, ele diz isso, e não inventa trabalho.</p>
        </Row>
        <Row flip title="Correção com motivo e com volta" img="/img/app-otimizacoes-v2.png" alt="Tela de otimizações do RKZFPS">
          <p>Cada otimização mostra o que muda, o risco e o efeito esperado naquele PC. Você escolhe o que aplicar.</p>
          <p>Antes de mudar, o app guarda o estado anterior. Depois, confere se a mudança pegou.</p>
        </Row>
        <Row title="Ajuste por jogo, no nível do seu PC" img="/img/app-jogos-v2.png" alt="Tela de jogos do RKZFPS">
          <p>No CS2, no Fortnite e no Minecraft o RKZFPS ajusta o arquivo de vídeo sozinho, com o jogo fechado e com backup.</p>
          <p>Em PC fraco, oferece uma configuração leve que só reduz o que está pesado.</p>
        </Row>
        <Row flip title="Prova de resultado nas suas partidas" img="/img/app-partidas-v2.png" alt="Tela de partidas do RKZFPS">
          <p>Com o app aberto, cada partida é medida sozinha: FPS médio, 1% low e travadas por minuto, com o que estava pesando em cada queda.</p>
          <p>A medição usa o registro de quadros do próprio Windows e não injeta nada no jogo: funciona com anti-cheat.</p>
        </Row>
      </div>

      <section id="analisa" className="mt-24 scroll-mt-20">
        <h2 className="font-display text-3xl font-bold text-ink-1">O que ele analisa</h2>
        <p className="mt-3 max-w-2xl text-ink-3">Tudo o que pesa no jogo, lido direto do PC. Nada sai do seu computador sem a sua permissão.</p>
        <Grade itens={ANALISA} />
      </section>

      <section className="mt-20">
        <h2 className="font-display text-2xl font-bold text-ink-1">O que o Windows não resolve, ele aponta</h2>
        <p className="mt-3 max-w-2xl text-ink-3">Tem problema que nenhum programa corrige sozinho. Nesses, o app explica o que fazer, passo a passo.</p>
        <ul className="mt-8 grid gap-3 sm:grid-cols-2">
          {FORA_DO_WINDOWS.map(({ Icon, t }) => (
            <li key={t} className="flex items-start gap-3 rounded-lg border border-line px-4 py-3">
              <Icon className="mt-0.5 h-5 w-5 shrink-0 text-accent-ink" aria-hidden />
              <span className="text-ink-2">{t}</span>
            </li>
          ))}
        </ul>
      </section>

      <section id="jogos" className="mt-20 scroll-mt-20">
        <h2 className="font-display text-2xl font-bold text-ink-1">Jogos reconhecidos</h2>
        <p className="mt-2 text-ink-3">A lista cresce a cada atualização.</p>
        <GameGrid />
      </section>

      <section className="mt-20">
        <div className="flex items-center gap-3">
          <ShieldCheck className="h-7 w-7 text-accent-ink" aria-hidden />
          <h2 className="font-display text-2xl font-bold text-ink-1">Seguro por construção</h2>
        </div>
        <Grade itens={SEGURANCA} />
      </section>

      <section className="card mt-20 flex flex-col items-start gap-5 p-6 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h2 className="font-display text-2xl font-bold text-ink-1">Veja o que está pesando no seu PC.</h2>
          <p className="mt-2 text-ink-3">O diagnóstico é grátis e não muda nada.</p>
        </div>
        <div className="flex flex-wrap gap-3">
          <Button to="/download" size="lg" Icon={Download}>Baixar grátis</Button>
          <Button to="/planos" size="lg" variant="ghost" IconRight={ArrowRight}>Ver planos</Button>
        </div>
      </section>
    </PageShell>
  )
}
