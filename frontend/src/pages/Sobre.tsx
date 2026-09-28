import { Link } from 'react-router-dom'
import { Gauge, ListChecks, RotateCcw, ShieldCheck, Ruler, MessageCircle } from 'lucide-react'
import PageShell from '../components/PageShell'
import { Button } from '../components/ui'
import { SUPPORT_IS_EXTERNAL, SUPPORT_URL } from '../lib/support'

/*
 * Quem somos: so' o que e' verdade e da' para conferir. Nada de numero de
 * clientes, depoimento ou "equipe de especialistas": a confianca aqui vem de
 * dizer como o produto decide, o que mede e o que se recusa a fazer.
 */

const PRINCIPIOS = [
  {
    icon: Ruler, t: 'Medir, não prometer',
    d: 'Não existe "até 300% mais FPS" aqui. O RKZFPS mede suas partidas antes e depois e mostra o número real, inclusive quando não mudou nada.',
  },
  {
    icon: ListChecks, t: 'Cada ajuste tem um porquê',
    d: 'Nenhuma otimização entra sem responder dez perguntas: que problema resolve, em que PC funciona, em qual não funciona, qual o risco, como medir e como desfazer.',
  },
  {
    icon: Gauge, t: 'Cada PC é um PC',
    d: 'O app lê o hardware antes de mexer. O que ajuda num notebook com vídeo integrado pode piorar um PC com placa forte, e aí ele simplesmente não oferece.',
  },
  {
    icon: RotateCcw, t: 'Desfazer é direito seu',
    d: 'Toda alteração guarda o estado anterior. Voltar como era funciona em qualquer plano, até no gratuito e depois de cancelar.',
  },
  {
    icon: ShieldCheck, t: 'Segurança do Windows fica intacta',
    d: 'Antivírus, firewall e Windows Update nunca são desligados. Uma trava no próprio app bloqueia isso, e um teste automático confere a trava a cada versão.',
  },
]

export default function Sobre() {
  return (
    <PageShell title="Quem somos" bar={{ title: 'Quem somos', sub: 'Por que o RKZFPS existe e como ele decide o que mudar no seu PC.' }}>
      <div className="mx-auto max-w-3xl space-y-12">
        <section className="space-y-4 leading-relaxed text-ink-2">
          <h2 className="font-display text-2xl font-bold text-ink-1">Por que o RKZFPS existe</h2>
          <p>
            Todo mundo que joga no PC já viu o "otimizador milagroso": um botão que promete dobrar o FPS, desliga o antivírus,
            mexe em dezenas de configurações do Windows de uma vez e não diz o que fez. Às vezes o jogo melhora um pouco. Às vezes
            o PC fica instável e ninguém sabe voltar.
          </p>
          <p>
            O RKZFPS nasceu para fazer o contrário: olhar primeiro, explicar o que encontrou, mudar só o que faz sentido para o seu
            hardware e provar o resultado com medição de verdade, nas suas partidas.
          </p>
        </section>

        <section>
          <h2 className="font-display text-2xl font-bold text-ink-1">No que a gente acredita</h2>
          <ul className="mt-6 grid gap-3 sm:grid-cols-2">
            {PRINCIPIOS.map(({ icon: Icon, t, d }) => (
              <li key={t} className="rounded-xl border border-line bg-surface-1 p-5">
                <Icon className="h-5 w-5 text-accent-ink" aria-hidden />
                <h3 className="mt-3 font-semibold text-ink-1">{t}</h3>
                <p className="mt-1.5 text-sm leading-relaxed text-ink-3">{d}</p>
              </li>
            ))}
          </ul>
        </section>

        <section className="space-y-4 leading-relaxed text-ink-2">
          <h2 className="font-display text-2xl font-bold text-ink-1">Como a gente mede</h2>
          <p>
            A medição usa o registro de quadros do próprio Windows, pela ferramenta aberta PresentMon, da Intel. Ela não injeta
            nada no jogo, por isso funciona com anti-cheat. O RKZFPS descarta o carregamento, os minutos com o jogo minimizado e as
            telas de pausa, e só compara partidas suficientes para a diferença não ser ruído.
          </p>
          <p>
            Quando uma travada acontece, o app mostra o que estava pesando no PC naquele momento: outro programa, o processador no
            limite, a placa de vídeo no limite ou o próprio jogo. Quando o problema é o jogo, ele diz isso, em vez de vender um
            ajuste que não resolve.
          </p>
        </section>

        <section className="space-y-4 leading-relaxed text-ink-2">
          <h2 className="font-display text-2xl font-bold text-ink-1">Seus dados</h2>
          <p>
            O diagnóstico completo, os backups e o histórico ficam no seu PC. Dados de uso só saem com a sua permissão, sem nome de
            programa, de arquivo ou de pasta. O detalhe está na <Link to="/privacidade" className="text-accent-ink underline underline-offset-2">política de privacidade</Link>.
          </p>
        </section>

        <section className="rounded-xl border border-line bg-surface-1 p-6">
          <h2 className="font-display text-xl font-bold text-ink-1">Fale com a gente</h2>
          <p className="mt-2 text-ink-3">Dúvida, sugestão de jogo ou algo que não funcionou no seu PC: a gente responde.</p>
          <div className="mt-4 flex flex-wrap gap-3">
            <Button Icon={MessageCircle} {...(SUPPORT_IS_EXTERNAL ? { href: SUPPORT_URL } : { to: SUPPORT_URL })}>Falar com o suporte</Button>
            <Button variant="ghost" to="/#o-que-nao-fazemos">O que não fazemos</Button>
          </div>
        </section>
      </div>
    </PageShell>
  )
}
