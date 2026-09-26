import { useEffect, useState } from 'react'
import PageShell from '../../components/PageShell'
import { Tabs } from '../../components/ui'
import { dateTime } from '../../lib/format'
import AdminUsuarios from './AdminUsuarios'
import AdminFinanceiro from './AdminFinanceiro'
import AdminFunil from './AdminFunil'
import AdminUso from './AdminUso'
import { AdminHead, Catalog, Coupons, Plans, ReadOnly, Releases, Settings } from './AdminConfig'

/*
 * Admin no desenho do Pickia: uma aba por pergunta, e cada numero mora na aba
 * onde se age sobre ele (la', a "Visao geral" foi dissolvida porque vivia um
 * passo atras das abas de acao). A ordem segue a do Pickia: pessoas primeiro,
 * dinheiro depois, operacao do produto por ultimo.
 *
 * O hash da URL manda na aba, entao da' pra abrir e recarregar direto em
 * /admin#financeiro. Hash desconhecido cai em Usuarios.
 */
const ABAS = [
  { key: 'usuarios', label: 'Usuários' },
  { key: 'financeiro', label: 'Financeiro' },
  // Logo depois do Financeiro: "vendeu pouco" quase sempre termina em
  // "onde as pessoas pararam", que e' esta aba.
  { key: 'funil', label: 'Funil e engajamento' },
  { key: 'uso', label: 'Uso do app' },
  { key: 'otimizacoes', label: 'Otimizações' },
  { key: 'planos', label: 'Planos e preços' },
  { key: 'cupons', label: 'Cupons' },
  { key: 'config', label: 'Configurações' },
  { key: 'atualizacoes', label: 'Atualizações' },
  { key: 'erros', label: 'Erros' },
  { key: 'auditoria', label: 'Auditoria' },
] as const
type Aba = (typeof ABAS)[number]['key']

function abaDoHash(): Aba {
  const h = window.location.hash.replace('#', '') as Aba
  return ABAS.some((a) => a.key === h) ? h : 'usuarios'
}

export default function Admin() {
  const [aba, setAba] = useState<Aba>(abaDoHash)

  useEffect(() => {
    // replaceState e nao location.hash: trocar de aba nao deve empilhar
    // entradas no historico nem disparar o scroll para uma ancora.
    window.history.replaceState(null, '', `#${aba}`)
  }, [aba])

  // Link para /admin#financeiro com o admin ja' aberto so' muda o hash, sem
  // remontar a pagina: sem este ouvinte a aba ficava parada na anterior.
  useEffect(() => {
    const onHash = () => setAba(abaDoHash())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  return (
    <PageShell title="Admin" noindex width="full" bar={{ title: 'Admin', sub: 'Toda alteração feita aqui fica registrada na auditoria.' }}
               beforeMain={<div className="border-b border-line"><div className="mx-auto max-w-[1440px] px-4 sm:px-6 lg:px-8"><Tabs<Aba> items={[...ABAS]} value={aba} onChange={setAba} /></div></div>}>
      {aba === 'usuarios' && <AdminUsuarios />}
      {aba === 'financeiro' && <AdminFinanceiro />}
      {aba === 'funil' && <AdminFunil />}
      {aba === 'uso' && <AdminUso />}
      {aba === 'otimizacoes' && <Catalog />}
      {aba === 'planos' && <Plans />}
      {aba === 'cupons' && <Coupons />}
      {aba === 'config' && <Settings />}
      {aba === 'atualizacoes' && <Releases />}
      {aba === 'erros' && (
        <>
          <AdminHead title="Erros reportados pelo app" sub="Falhas de otimização e erros do app, de quem permitiu o envio de dados." />
          <ReadOnly path="/admin/errors" keyName="errors" head={['Data', 'PC', 'Evento', 'Otimização', 'Detalhe', 'Versão']}
            row={(e) => [dateTime(String(e.created_at)), String(e.device_name ?? ''), String(e.event), String(e.optimization_id ?? ''), JSON.stringify(e.detail), String(e.agent_version)]} />
        </>
      )}
      {aba === 'auditoria' && (
        <>
          <AdminHead title="Auditoria" sub="Toda alteração feita por um admin, com quem, quando e o quê." />
          <ReadOnly path="/admin/audit" keyName="entries" head={['Data', 'Admin', 'Ação', 'Alvo', 'Detalhe']}
            row={(a) => [dateTime(String(a.created_at)), String(a.email ?? ''), String(a.action), String(a.target), JSON.stringify(a.detail)]} />
        </>
      )}
    </PageShell>
  )
}
