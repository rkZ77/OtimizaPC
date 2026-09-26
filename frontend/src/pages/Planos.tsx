import { Check, Minus } from 'lucide-react'
import { PageShell, PageTitle } from '../components/Layout'
import PlansGrid from '../components/PlansGrid'
import { FEATURES, PLAN_ORDER, includes } from '../lib/features'
import { TIER_LABEL } from '../lib/format'

export default function Planos() {
  return (
    <PageShell>
      <PageTitle title="Planos" subtitle="Cada plano contém tudo do anterior. Desfazer alterações é liberado em todos os planos, sempre." />
      <PlansGrid />

      <h2 className="mt-16 mb-4 text-xl font-bold text-ink-1">O que cada plano libera no app</h2>
      <div className="overflow-x-auto rounded-xl border border-line">
        <table className="w-full min-w-[560px] text-sm">
          <thead className="bg-surface-1 text-left">
            <tr>
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
        Pagamento por PIX ou cartão pelo Mercado Pago. Precisa de uma oferta para vários PCs ou para sua lan house? Fale com a gente pelo suporte.
      </p>
    </PageShell>
  )
}
