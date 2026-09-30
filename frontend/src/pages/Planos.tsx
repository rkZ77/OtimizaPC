import { Check, Minus } from 'lucide-react'
import PageShell from '../components/PageShell'
import PlansGrid from '../components/PlansGrid'
import { Faq, Garantias, PERGUNTAS } from '../components/Confianca'
import { SectionHead } from '../components/ui'
import { FEATURES, PLAN_ORDER, includes } from '../lib/features'
import { TIER_LABEL } from '../lib/format'

export default function Planos() {
  return (
    <PageShell
      title="Planos"
      description="Planos do RKZFPS: do diagnóstico gratuito ao pacote completo. Cada plano contém o anterior, e desfazer é liberado em todos."
      width="wide"
      fundo
      bar={{ title: 'Planos', sub: 'Cada plano contém tudo do anterior. Desfazer alterações é liberado em todos, sempre.' }}
    >
      <PlansGrid />

      <SectionHead className="mt-16" title="O que cada plano libera no app" sub="A mesma divisão que o app aplica com a sua licença." />
      <div className="panel overflow-x-auto">
        <table className="w-full min-w-[560px] text-sm">
          <thead className="text-left">
            <tr className="border-b border-line">
              <th className="px-4 py-3 font-semibold text-ink-1">Recurso</th>
              {PLAN_ORDER.map((p) => <th key={p} className="px-3 py-3 text-center font-semibold text-ink-1">{TIER_LABEL[p]}</th>)}
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {FEATURES.map((f) => (
              <tr key={f.label}>
                <td className="px-4 py-3 text-ink-2">{f.label}</td>
                {PLAN_ORDER.map((p) => (
                  <td key={p} className="px-3 py-3 text-center">
                    {includes(p, f.plan)
                      ? <Check className="mx-auto w-4 h-4 text-accent" aria-label="Incluído" />
                      : <Minus className="mx-auto w-4 h-4 text-ink-4" aria-label="Não incluído" />}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="mt-4 text-sm text-ink-3">
        Pagamento por PIX ou cartão pelo Mercado Pago. Precisa de uma oferta para vários PCs? Fale com o suporte.
      </p>

      {/* As duvidas de quem esta' prestes a pagar, respondidas aqui mesmo. */}
      <div className="panel mt-12 p-6"><Garantias /></div>

      <SectionHead className="mt-16" title="Antes de assinar" sub="O que mais perguntam na hora de escolher um plano." />
      <Faq itens={[PERGUNTAS.porqueAssinar, PERGUNTAS.gratis, PERGUNTAS.cancelar, PERGUNTAS.variosPcs, PERGUNTAS.fps, PERGUNTAS.seguro]} />
    </PageShell>
  )
}
