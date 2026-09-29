import { Helmet } from 'react-helmet-async'
import { useLocation } from 'react-router-dom'
import { cn } from '../lib/cn'
import PublicNav from './PublicNav'
import Footer from './Footer'
import { NavVoltar } from './BackButton'
import FundoMarca from './FundoMarca'
import { PAGE_WIDTH, type PageWidth } from '../lib/pageWidth'
import { useRevelacao, classesRevelacao, FADE_REVELACAO_MS } from '../hooks/useRevelacao'

/*
 * Casca de página.
 *
 * Antes cada tela montava a sua: das 23 páginas, 5 usavam .shell, o resto
 * escolhia entre max-w-7xl, 6xl, 5xl, 3xl e 2xl a esmo; o Footer aparecia em 8
 * delas; e só 8 declaravam <Helmet>, então metade do site compartilhava o
 * mesmo título de aba e a mesma description no resultado de busca.
 *
 * A escala de largura e o porquê de cada degrau vivem em lib/pageWidth.
 */

/** Origem do site. Fixa, e não `window.location.origin`: o canonical precisa
 *  apontar pro endereço público mesmo quando a página é aberta por
 *  localhost:5173 ou pelo domínio do Railway. */
const ORIGEM = 'https://otimizapc-production.up.railway.app'

/** Description de fallback · a mesma do index.html. Serve as cinco páginas que
 *  não passam uma própria (Login, VerifyEmail, ForgotPassword, NotFound e o
 *  link público de pick), que hoje não competem na busca. */
const DESCRICAO_PADRAO =
  'O RKZFPS analisa seu PC Windows e aplica só otimizações compatíveis, com backup, ' +
  'desfazer e medição antes e depois. Sem tweak placebo, sem promessa de FPS.'

export interface PageBar {
  title: React.ReactNode
  /** Linha de apoio. Some abaixo de sm, onde o espaço é do título. */
  sub?: React.ReactNode
  /**
   * Mantém o `sub` visível no celular.
   *
   * O padrão de esconder existe porque na maioria das páginas o `sub` é
   * legenda, e legenda pode esperar. Em Picks ele não é: carrega o navegador
   * de dia (setas + "voltar pra Hoje"), e o site é mobile-first, então o
   * controle estava invisível justamente para a maior parte dos usuários.
   */
  subMobile?: boolean
  /** Botões à direita. */
  actions?: React.ReactNode
  /** Para onde o voltar leva. Sem valor, volta uma no histórico (ou vai ao início). */
  back?: string
}

export default function PageShell({
  children,
  title,
  description,
  canonical,
  /** Fora do índice de busca. Todo conteúdo atrás de login usa isto. */
  noindex,
  width = 'default',
  /** Navbar do app. Desligar em página que monta a própria (Home, Login). */
  nav = true,
  footer = true,
  bar,
  /** Renderizado entre a barra e o conteúdo, na largura toda (abas, banner). */
  beforeMain,
  className,
  mainClassName,
  /**
   * Desliga o portão de revelação · o conteúdo aparece no primeiro quadro.
   *
   * Só para tela sem carga inicial nenhuma (Termos, Privacidade). Nas outras o
   * portão é o padrão de propósito: ver hooks/useRevelacao.
   */
  revelacao = true,
  /** Grade ciano com brilho no canto, a das artes da marca. Para pagina de
   *  apresentacao e de conta (login, planos, download), nao para tela de uso. */
  fundo = false,
}: {
  children: React.ReactNode
  /** Vira "<title> | RKZFPS". Passar já com o sufixo desliga o automático. */
  title?: string
  description?: string
  canonical?: string
  noindex?: boolean
  width?: PageWidth
  /**
   * true usa a Navbar do app, false não põe nenhuma, e um nó monta a sua.
   * O nó existe para página pública que também abre logada (Resultados, pick
   * compartilhado): deslogado, a Navbar do app só oferece links privados que
   * jogam o visitante direto no login.
   */
  nav?: boolean | React.ReactNode
  footer?: boolean
  bar?: PageBar
  beforeMain?: React.ReactNode
  className?: string
  mainClassName?: string
  revelacao?: boolean
  fundo?: boolean
}) {
  const revelado = useRevelacao(!revelacao)
  const { pathname } = useLocation()
  const fullTitle = title
    ? (title.includes('RKZFPS') ? title : `${title} | RKZFPS`)
    : undefined

  /*
   * O <Helmet> daqui é INCONDICIONAL, e as duas tags abaixo saem sempre.
   *
   * O index.html marca a description e o canonical dele com `data-rh`, ou
   * seja, entrega os dois pro Helmet gerenciar (ver a nota lá). A partir daí
   * quem não declarar fica sem nenhum -- e por isso não dá pra deixar a
   * emissão condicionada a a página ter passado o texto.
   *
   * `canonical` continua aceitando um valor explícito porque há caso em que a
   * URL canônica não é a que o usuário está vendo (a página de liga monta a
   * dela a partir do slug). Quando ninguém passa, o próprio caminho responde,
   * sem query string: `?aba=vip` não é outra página.
   *
   * Página com noindex não declara canonical de propósito. Ela não deve entrar
   * no índice, e apontar uma URL canônica é justamente pedir para entrar.
   */
  /* Voltar e casinha em toda pagina menos a home: quem cai direto no login ou
     nos planos (link do Instagram, anuncio) precisa de uma saida visivel. */
  const temVoltar = pathname !== '/'

  const canonicalFinal = noindex ? null : (canonical ?? `${ORIGEM}${pathname}`)

  return (
    <div className={cn('min-h-screen bg-surface-0 text-ink-1 flex flex-col', fundo && 'relative isolate overflow-x-clip', className)}>
      {fundo && <FundoMarca />}
      <Helmet>
        {fullTitle && <title>{fullTitle}</title>}
        <meta name="description" content={description ?? DESCRICAO_PADRAO} />
        {canonicalFinal && <link rel="canonical" href={canonicalFinal} />}
        {/* Tela de usuário não deve indexar: o robô só veria a casca vazia,
            e essas URLs competiam com as páginas públicas na busca. */}
        {noindex && <meta name="robots" content="noindex, nofollow" />}
      </Helmet>

      {nav === true ? <PublicNav width={width} /> : nav || null}

      {bar && (
        <div className="border-b border-line">
          {/* Quebra em duas linhas antes de espremer o título.
              A fila de ações é `shrink-0`, então ela nunca cede largura · sem
              `flex-wrap` aqui, quem cedia era o título, que tem `min-w-0` e
              truncava. Numa tela de 360px com três botões (a Banca tem
              Sacar, Configurar e Zerar mês) "Minha Banca" virava "Minha B...".
              Com a quebra, as ações descem inteiras para a linha de baixo e o
              título fica legível, que é a ordem certa de prioridade. */}
          <div className={cn('mx-auto py-4 flex flex-wrap items-center justify-between gap-x-3 gap-y-2', PAGE_WIDTH[width])}>
            <div className="flex items-center gap-3 min-w-0">
              {temVoltar && <NavVoltar to={bar.back} />}
              <div className="min-w-0">
                <h1 className="font-display text-base font-semibold text-ink-1 leading-tight truncate">
                  {bar.title}
                </h1>
                {bar.sub != null && (
                  <p className={`text-ink-3 text-[11px] mt-0.5 ${bar.subMobile ? '' : 'hidden sm:block'}`}>{bar.sub}</p>
                )}
              </div>
            </div>
            {/* `ml-auto` mantém as ações à direita também quando elas descem
                para a própria linha · `justify-between` do pai só alinha itens
                que dividem a MESMA linha. */}
            {bar.actions && (
              <div className="flex items-center gap-2 flex-wrap justify-end shrink-0 ml-auto">
                {bar.actions}
              </div>
            )}
          </div>
        </div>
      )}

      {/* A faixa de plano morava aqui e saiu em 21/08, a pedido do usuário.
          Ela custava uma linha inteira do topo em toda tela do app e empurrava
          para baixo justamente o conteúdo que a pessoa abriu a página para ver.
          Virou aviso de rodapé, montado uma vez em App.tsx, mais um item na
          Central de Notificações · ver components/PlanUpsellToast.tsx. */}

      {beforeMain}

      {/*
        O portão vive NO <main>, e não num <div> em volta dele.
        Sete telas passam layout por `mainClassName` · `space-y-5`, `grid
        lg:grid-cols-2`, `flex items-center` · e todas dependem de o conteúdo
        ser filho direto deste elemento. Um invólucro extra viraria o único
        filho do grid e achataria a página inteira numa coluna.
      */}
      <main
        className={cn(
          'flex-1 w-full mx-auto py-6 md:py-8',
          PAGE_WIDTH[width],
          classesRevelacao(revelado),
          mainClassName,
        )}
        style={{ transitionDuration: `${FADE_REVELACAO_MS}ms` }}
        aria-busy={!revelado}
      >
        {!bar && temVoltar && <NavVoltar className="mb-5" />}
        {children}
      </main>

      {footer && <Footer />}
    </div>
  )
}
