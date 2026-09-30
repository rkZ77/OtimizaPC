import { CreditCard, RotateCcw, ShieldCheck, Undo2 } from 'lucide-react'

/*
 * Garantias e perguntas frequentes, num lugar so' para a home e a pagina de
 * planos. Na pagina de planos e' onde a pessoa decide pagar: as mesmas
 * respostas precisam estar la', sem ela ter que voltar para a home.
 */

const GARANTIAS = [
  { icon: CreditCard, t: 'Pagamento pelo Mercado Pago', d: 'PIX ou cartão. O RKZFPS não vê nem guarda os dados do seu cartão.' },
  { icon: Undo2, t: '7 dias para desistir', d: 'Não gostou? Peça o reembolso em até 7 dias da compra, como manda o Código de Defesa do Consumidor.' },
  { icon: RotateCcw, t: 'Desfazer tudo, sempre', d: 'Toda alteração guarda o estado anterior. Desfazer funciona em qualquer plano, mesmo depois de cancelar.' },
  { icon: ShieldCheck, t: 'Segurança do Windows intocada', d: 'Antivírus, firewall e Windows Update ficam exatamente como estão.' },
]

export function Garantias({ className = '' }: { className?: string }) {
  return (
    <div className={`grid gap-6 sm:grid-cols-2 lg:grid-cols-4 ${className}`}>
      {GARANTIAS.map(({ icon: Icon, t, d }) => (
        <div key={t} className="flex gap-3">
          <Icon className="mt-0.5 h-5 w-5 shrink-0 text-accent" aria-hidden />
          <div><p className="font-semibold text-ink-1">{t}</p><p className="mt-1 text-sm text-ink-3">{d}</p></div>
        </div>
      ))}
    </div>
  )
}

export type Pergunta = [pergunta: string, resposta: string]

export const PERGUNTAS: Record<string, Pergunta> = {
  fps: ['O RKZFPS aumenta meu FPS?', 'Depende do seu PC, e é isso que ele descobre primeiro. Em PC com configuração errada (monitor rodando a 60 Hz, plano de economia de energia, jogo na placa integrada, programas pesando) o ganho costuma ser grande. Em PC já bem configurado, o RKZFPS diz que não há o que mudar. A medição das partidas mostra o número real, antes e depois.'],
  gratis: ['O que eu consigo fazer no plano grátis?', 'Ver tudo: o diagnóstico completo, os problemas encontrados, o que cada otimização resolveria no seu PC e o FPS das suas partidas. Para aplicar as correções, é preciso um plano pago.'],
  porqueAssinar: ['Se as correções ficam no PC, por que assinar?', 'Porque o PC não fica parado. Atualização do Windows, driver novo e patch de jogo mudam configuração, religam coisas e criam problemas novos. Com um plano, o RKZFPS analisa de novo a cada abertura e corrige o que aparecer, ajusta seus jogos e compara suas partidas antes e depois. O que ele já fez continua no PC mesmo sem plano, e desfazer segue liberado.'],
  seguro: ['É seguro? E se der problema?', 'Cada alteração guarda o estado anterior e é conferida depois de aplicada. Qualquer uma pode ser desfeita com um clique, em qualquer plano. O RKZFPS só executa operações de uma lista fechada e revisada.'],
  antiCheat: ['Funciona com anti-cheat (Vanguard, Easy Anti-Cheat)?', 'Sim. A medição de FPS usa o registro de quadros do próprio Windows e não injeta nada no jogo. O RKZFPS também nunca fecha nem mexe em anti-cheat.'],
  admin: ['Preciso ser administrador do PC?', 'Não para usar. Quando uma correção mexe em configuração do sistema, o Windows pede a sua permissão só para ela, e você vê antes o que vai mudar.'],
  variosPcs: ['Posso usar em mais de um PC?', 'Cada assinatura vale para 1 PC por vez. Trocou de PC? Desative o antigo em Minha conta e entre no novo.'],
  cancelar: ['Como cancelo?', 'Não precisa cancelar: cada compra é um pagamento único, sem renovação automática. O plano vale até o fim do período pago, e o desfazer continua liberado depois. Desistiu em até 7 dias? Peça o reembolso pelo suporte.'],
  atualizacoes: ['O app recebe atualizações?', 'Sim. Novos jogos, novas correções e ajustes para versões novas do Windows e dos jogos entram nas atualizações, sem custo a mais para quem assina. O histórico está nesta página.'],
}

export function Faq({ itens }: { itens: Pergunta[] }) {
  return (
    <div className="divide-y divide-line rounded-lg border border-line">
      {itens.map(([q, a]) => (
        <details key={q} className="group p-5">
          <summary className="flex cursor-pointer list-none justify-between gap-4 font-semibold text-ink-1">
            {q}<span className="text-ink-3 transition-transform group-open:rotate-45" aria-hidden>+</span>
          </summary>
          <p className="mt-3 leading-relaxed text-ink-3">{a}</p>
        </details>
      ))}
    </div>
  )
}
