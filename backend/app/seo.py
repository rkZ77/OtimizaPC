"""O que o Google e as IAs leem do site, sem precisar rodar JavaScript.

O site e' um SPA: o index.html chega vazio e o React monta tudo depois. O
Google ate' roda o JavaScript (com atraso), mas ChatGPT, Perplexity, Claude e
as previas do WhatsApp/Instagram/Discord leem so' o HTML cru, e viam um site
em branco, com o mesmo titulo em todas as paginas (medido em 01/10/2026: zero
palavras em /, /planos, /como-funciona e /download).

Aqui cada pagina publica sai com titulo, descricao, canonical, previa social,
dados estruturados (JSON-LD) e o texto dela dentro do #root. O React apaga
esse texto ao montar e desenha a pagina de verdade; o conteudo e' o mesmo das
telas (frontend/src/pages), entao robo e pessoa veem a mesma coisa.

Rota que nao existe responde 404 de verdade (antes era 200, o "soft 404" que
faz o Google desconfiar do site). Fora do dominio oficial (staging, dominio do
Railway) tudo sai com noindex: copia do site no indice concorre com o original.

Preco vem sempre do banco (plans.list_active), como no resto do produto.
"""
import json
import re
from dataclasses import dataclass, field
from html import escape

SITE = "https://rkzfps.com.br"
SITE_HOST = "rkzfps.com.br"
NOME = "RKZFPS"
INSTAGRAM = "https://www.instagram.com/rkzfps.br"
#: Print real do app: previa no WhatsApp, Discord e resultado de busca.
OG_IMAGE = f"{SITE}/img/app-dashboard.png"
DESCRICAO_PADRAO = ("O RKZFPS analisa seu PC Windows e aplica só otimizações compatíveis, com backup, "
                    "desfazer e medição antes e depois. Sem tweak placebo, sem promessa de FPS.")

# Mesmo texto das telas do site (Home.tsx, ComoFunciona.tsx, Sobre.tsx,
# Confianca.tsx, Vitrine.tsx). Mudou la', muda aqui.
JOGOS = ["Counter-Strike 2", "EA SPORTS FC", "Fortnite", "Valorant", "League of Legends", "Minecraft", "Roblox",
         "GTA V", "Apex Legends", "Call of Duty", "PUBG", "Rainbow Six Siege", "Dota 2", "Rocket League", "Marvel Rivals"]

NAO_FAZ = [
    "Desligar antivírus, firewall ou Windows Update",
    "Desligar dezenas de serviços do Windows de uma vez",
    'Colocar o jogo em prioridade "Tempo real"',
    'Prometer "+50 FPS" ou "PC 300% mais rápido"',
    "Instalar driver baixado de fonte que não seja o fabricante",
    "Fechar programa à força e perder o que você não salvou",
]

ANALISA = [
    "Processador: modelo, núcleos e se o plano de energia está segurando o desempenho.",
    "Placa de vídeo: qual placa o jogo usa de verdade, driver e se o jogo caiu na integrada.",
    "Memória: quantidade, velocidade de fábrica (XMP/EXPO) e se tem um pente só.",
    "Discos: onde cada jogo está instalado, espaço livre e alerta de saúde do disco.",
    "Energia: plano de economia ligado em PC de mesa, o erro mais comum e mais barato de corrigir.",
    "Monitor: se ele roda na taxa máxima (144 Hz rodando a 60 é comum) e em qual placa está ligado.",
    "Rede: conexão, adaptador e o que costuma atrapalhar o ping em jogo online.",
    "Inicialização: programas que abrem com o Windows e ficam pesando durante a partida.",
    f"Jogos instalados: os {len(JOGOS)} jogos reconhecidos e a configuração de vídeo de cada um.",
]

FORA_DO_WINDOWS = [
    "Memória abaixo da velocidade de fábrica: passo a passo da BIOS da sua placa-mãe",
    "Memória com um pente só, que perde o canal duplo",
    "Cabo do monitor na placa-mãe em vez da placa de vídeo",
    "Jogo instalado em HD comum em vez do SSD",
    "Driver de vídeo antigo, com o link oficial do fabricante",
    "Disco com alerta de saúde, antes de você perder arquivo",
]

SEGURANCA = [
    "Backup antes de cada mudança: o app guarda o estado anterior de tudo o que altera.",
    "Desfazer com um clique, em qualquer plano, mesmo depois de cancelar a assinatura.",
    "Lista fechada de alterações: o app só executa operações revisadas. O servidor não manda o app rodar nada.",
    "Proteção do Windows intocada: antivírus, firewall e Windows Update ficam como estão.",
]

FAQ = [
    ("O RKZFPS aumenta meu FPS?", "Depende do seu PC, e é isso que ele descobre primeiro. Em PC com configuração errada (monitor rodando a 60 Hz, plano de economia de energia, jogo na placa integrada, programas pesando) o ganho costuma ser grande. Em PC já bem configurado, o RKZFPS diz que não há o que mudar. A medição das partidas mostra o número real, antes e depois."),
    ("O que eu consigo fazer no plano grátis?", "Ver tudo: o diagnóstico completo, os problemas encontrados, o que cada otimização resolveria no seu PC e o FPS das suas partidas. Para aplicar as correções, é preciso um plano pago."),
    ("Se as correções ficam no PC, por que assinar?", "Porque o PC não fica parado. Atualização do Windows, driver novo e patch de jogo mudam configuração, religam coisas e criam problemas novos. Com um plano, o RKZFPS analisa de novo a cada abertura e corrige o que aparecer, ajusta seus jogos e compara suas partidas antes e depois. O que ele já fez continua no PC mesmo sem plano, e desfazer segue liberado."),
    ("É seguro? E se der problema?", "Cada alteração guarda o estado anterior e é conferida depois de aplicada. Qualquer uma pode ser desfeita com um clique, em qualquer plano. O RKZFPS só executa operações de uma lista fechada e revisada."),
    ("Funciona com anti-cheat (Vanguard, Easy Anti-Cheat)?", "Sim. A medição de FPS usa o registro de quadros do próprio Windows e não injeta nada no jogo. O RKZFPS também nunca fecha nem mexe em anti-cheat."),
    ("Preciso ser administrador do PC?", "Não para usar. Quando uma correção mexe em configuração do sistema, o Windows pede a sua permissão só para ela, e você vê antes o que vai mudar."),
    ("Posso usar em mais de um PC?", "Cada assinatura vale para 1 PC por vez. Trocou de PC? Desative o antigo em Minha conta e entre no novo."),
    ("Como cancelo?", "Não precisa cancelar: cada compra é um pagamento único, sem renovação automática. O plano vale até o fim do período pago, e o desfazer continua liberado depois. Desistiu em até 7 dias? Peça o reembolso pelo suporte."),
    ("O app recebe atualizações?", "Sim. Novos jogos, novas correções e ajustes para versões novas do Windows e dos jogos entram nas atualizações, sem custo a mais para quem assina."),
]

NAV = [("/", "Início"), ("/como-funciona", "Como funciona"), ("/planos", "Planos"), ("/download", "Baixar"),
       ("/quem-somos", "Quem somos"), ("/privacidade", "Privacidade"), ("/termos", "Termos de uso")]

# Rotas do SPA que existem mas nao devem entrar no indice (tela de conta,
# login, link de indicacao). Respondem 200 com noindex.
PRIVADAS = {"/entrar", "/cadastro", "/esqueci-senha", "/redefinir-senha", "/conta", "/meu-plano", "/pagamento", "/admin"}
_INDICACAO = re.compile(r"^/r/[^/]+$")


@dataclass
class Page:
    path: str
    title: str
    description: str
    #: Blocos de conteudo: ("h1"|"h2"|"p", texto) ou ("ul", [itens]).
    blocks: list = field(default_factory=list)
    status: int = 200
    index: bool = True
    #: Tipos extras de JSON-LD: "app" (produto com os planos) e "faq".
    schema: tuple = ()


# ─── precos (do banco) ──────────────────────────────────────────────────

PERIODO = {"monthly": "por mês", "quarterly": "por trimestre", "annual": "por ano"}


def reais(cents: int) -> str:
    inteiro, centavos = divmod(int(cents), 100)
    return f"R$ {inteiro:,}".replace(",", ".") + f",{centavos:02d}"


def _planos() -> list[dict]:
    """Planos ativos, ou nada se o banco falhar: a pagina sai sem a lista de
    precos, mas sai (o React busca de novo no navegador)."""
    from app.services import plans

    try:
        return plans.list_active()
    except Exception:
        return []


def plano_texto(p: dict) -> str:
    if p["price_cents"] <= 0:
        preco = "grátis"
    else:
        periodo = PERIODO.get(p.get("period")) or f"por {p['days']} dias"
        preco = f"{reais(p['price_cents'])} {periodo}"
    feats = "; ".join(p.get("features") or [])
    return f"{p['name']}: {preco}. {p.get('description') or ''}".strip() + (f" Inclui: {feats}." if feats else "")


# ─── paginas ────────────────────────────────────────────────────────────

def _blocos_planos(planos: list[dict]) -> list:
    if not planos:
        return [("p", "Veja os preços atualizados na página de planos.")]
    return [("ul", [plano_texto(p) for p in planos])]


def _faq_blocos() -> list:
    out: list = [("h2", "Perguntas frequentes")]
    for q, a in FAQ:
        out += [("h3", q), ("p", a)]
    return out


def page_for(path: str, planos: list[dict] | None = None) -> Page:
    path = "/" + path.strip("/") if path.strip("/") else "/"
    if path == "/":
        planos = _planos() if planos is None else planos
        return Page("/", "RKZFPS: otimização de PC para jogos com diagnóstico e medição de FPS", DESCRICAO_PADRAO, [
            ("h1", "Descubra o que está travando seus jogos. E corrija sem medo."),
            ("p", "O RKZFPS analisa o Windows e o hardware do seu PC, corrige só o que encontrar de errado e mede o FPS "
                  "das suas partidas antes e depois. Toda alteração tem backup e pode ser desfeita."),
            ("p", "Windows 10 e 11, 64 bits. O diagnóstico é grátis e não altera nada."),
            ("h2", "Como funciona"),
            ("p", "Analisar, corrigir o que estiver errado e provar o resultado. Nessa ordem."),
            ("h3", "1. Um diagnóstico que não chuta"),
            ("p", "Ao abrir, o RKZFPS lê processador, placa de vídeo, memória, discos, energia, monitor, rede, inicialização "
                  "e os jogos instalados. Só leitura: nada muda nessa etapa. Se o PC já está bem configurado, ele diz isso, "
                  "e não inventa trabalho."),
            ("h3", "2. Ajuste por jogo, no nível do seu PC"),
            ("p", f"O RKZFPS reconhece {len(JOGOS)} jogos e sabe o que pesa em cada um. No CS2, no Fortnite e no Minecraft "
                  "ele corrige o arquivo de vídeo sozinho, com o jogo fechado e com backup."),
            ("h3", "3. Prova de resultado nas suas partidas"),
            ("p", "Com o RKZFPS aberto, cada partida é medida sozinha: FPS médio, 1% low e travadas por minuto. Depois de "
                  "otimizar, o app compara as partidas de antes e de depois. Se a diferença estiver dentro da variação "
                  "normal, ele diz que não houve ganho."),
            ("h2", "Jogos reconhecidos"),
            ("p", "A medição não mexe no jogo e funciona com anti-cheat."),
            ("ul", JOGOS),
            ("h2", "O que o RKZFPS não faz, de propósito"),
            ("ul", NAO_FAZ),
            ("h2", "Planos"),
            ("p", "Comece grátis para ver o diagnóstico do seu PC. Assine quando quiser que o RKZFPS corrija."),
            *_blocos_planos(planos),
            *_faq_blocos(),
        ], schema=("app", "faq"))
    if path == "/como-funciona":
        return Page(path, "Como funciona | RKZFPS",
                    "Como o RKZFPS analisa o PC, corrige só o que faz sentido, guarda backup de cada alteração e mede o FPS das suas partidas antes e depois.", [
            ("h1", "Como funciona"),
            ("p", "Analisar, corrigir o que estiver errado e provar o resultado. Nessa ordem."),
            ("ul", ["Baixe e abra: instalador para Windows 10 e 11. Não pede cadastro para o diagnóstico.",
                    "Diagnóstico: o app lê o PC inteiro em poucos minutos. Só leitura: nada muda nessa etapa.",
                    "Corrige o que faz sentido: um clique por item, com o motivo explicado e backup antes de mudar.",
                    "Mede nas partidas: FPS médio, 1% low e travadas das suas partidas, antes e depois."]),
            ("h2", "Correção com motivo e com volta"),
            ("p", "Cada otimização mostra o que muda, o risco e o efeito esperado naquele PC. Você escolhe o que aplicar. "
                  "Antes de mudar, o app guarda o estado anterior. Depois, confere se a mudança pegou."),
            ("h2", "Prova de resultado nas suas partidas"),
            ("p", "Com o app aberto, cada partida é medida sozinha: FPS médio, 1% low e travadas por minuto, com o que "
                  "estava pesando em cada queda. A medição usa o registro de quadros do próprio Windows e não injeta nada "
                  "no jogo: funciona com anti-cheat."),
            ("h2", "O que ele analisa"),
            ("ul", ANALISA),
            ("h2", "O que o Windows não resolve, ele aponta"),
            ("ul", FORA_DO_WINDOWS),
            ("h2", "Jogos reconhecidos"),
            ("ul", JOGOS),
            ("h2", "Seguro por construção"),
            ("ul", SEGURANCA),
        ])
    if path == "/planos":
        planos = _planos() if planos is None else planos
        return Page(path, "Planos | RKZFPS",
                    "Planos do RKZFPS: do diagnóstico gratuito ao pacote completo. Cada plano contém o anterior, e desfazer é liberado em todos.", [
            ("h1", "Planos do RKZFPS"),
            ("p", "Do diagnóstico gratuito ao pacote completo. Cada plano contém o anterior, e desfazer é liberado em todos. "
                  "Pagamento único por PIX ou cartão pelo Mercado Pago, sem renovação automática, e 7 dias para desistir."),
            *_blocos_planos(planos),
            *_faq_blocos(),
        ], schema=("app", "faq"))
    if path == "/download":
        return Page(path, "Baixar o RKZFPS | RKZFPS",
                    "Baixe o RKZFPS para Windows 10 e 11. O diagnóstico é gratuito e não altera nada no seu PC.", [
            ("h1", "Baixar o RKZFPS"),
            ("p", "Instale, abra e o diagnóstico começa sozinho. O scan é gratuito e não altera nada. Para Windows 10 e 11, 64 bits."),
            ("p", "O RKZFPS não desativa antivírus, firewall nem atualizações, e toda alteração tem backup e pode ser desfeita."),
        ])
    if path == "/quem-somos":
        return Page(path, "Quem somos | RKZFPS",
                    "Por que o RKZFPS existe e como ele decide o que mudar no seu PC: medir em vez de prometer, um porquê para cada ajuste e desfazer sempre liberado.", [
            ("h1", "Quem somos"),
            ("h2", "Por que o RKZFPS existe"),
            ("p", 'Todo mundo que joga no PC já viu o "otimizador milagroso": um botão que promete dobrar o FPS, desliga o '
                  "antivírus, mexe em dezenas de configurações do Windows de uma vez e não diz o que fez. O RKZFPS nasceu "
                  "para fazer o contrário: olhar primeiro, explicar o que encontrou, mudar só o que faz sentido para o seu "
                  "hardware e provar o resultado com medição de verdade, nas suas partidas."),
            ("h2", "No que a gente acredita"),
            ("ul", ["Medir, não prometer: o RKZFPS mede suas partidas antes e depois e mostra o número real, inclusive quando não mudou nada.",
                    "Cada ajuste tem um porquê: nenhuma otimização entra sem dizer que problema resolve, em que PC funciona, qual o risco, como medir e como desfazer.",
                    "Cada PC é um PC: o app lê o hardware antes de mexer e não oferece o que pioraria o seu.",
                    "Desfazer é direito seu: voltar como era funciona em qualquer plano, até no gratuito e depois de cancelar.",
                    "Segurança do Windows fica intacta: antivírus, firewall e Windows Update nunca são desligados."]),
            ("h2", "Como a gente mede"),
            ("p", "A medição usa o registro de quadros do próprio Windows, pela ferramenta aberta PresentMon, da Intel. Ela não "
                  "injeta nada no jogo, por isso funciona com anti-cheat."),
        ])
    if path == "/privacidade":
        return Page(path, "Política de privacidade | RKZFPS",
                    "O que o RKZFPS guarda no seu PC, o que o servidor recebe e o que só sai com a sua permissão.",
                    [("h1", "Política de privacidade"),
                     ("p", "Coletamos o mínimo necessário para o produto funcionar, e nada sem você saber.")])
    if path == "/termos":
        return Page(path, "Termos de uso | RKZFPS", "Termos de uso do RKZFPS.", [("h1", "Termos de uso")])
    if path in PRIVADAS or _INDICACAO.match(path):
        return Page(path, f"{NOME}", DESCRICAO_PADRAO, index=False)
    return Page(path, f"Página não encontrada | {NOME}", DESCRICAO_PADRAO,
                [("h1", "Página não encontrada")], status=404, index=False)


def public_paths() -> list[str]:
    return ["/", "/como-funciona", "/planos", "/download", "/quem-somos", "/privacidade", "/termos"]


# ─── HTML ───────────────────────────────────────────────────────────────

def _bloco(kind: str, value) -> str:
    if kind == "ul":
        return "<ul>" + "".join(f"<li>{escape(str(i))}</li>" for i in value) + "</ul>"
    return f"<{kind}>{escape(str(value))}</{kind}>"


def static_body(page: Page) -> str:
    nav = " ".join(f'<a href="{escape(h)}">{escape(t)}</a>' for h, t in NAV)
    corpo = "".join(_bloco(k, v) for k, v in page.blocks)
    # Aparece so' depois de 2 s: com JavaScript, o React troca isto pela
    # pagina de verdade antes, e ninguem ve o texto cru piscar. Robo le o
    # HTML do mesmo jeito, e quem abre sem JavaScript ve o texto depois.
    return (f'<div class="seo-estatico"><nav>{nav}</nav><main>{corpo}</main>'
            f'<footer><p>{NOME}: otimização de PC para jogos. <a href="{INSTAGRAM}">Instagram</a></p></footer></div>')


ESTILO = ("<style>.seo-estatico{max-width:760px;margin:0 auto;padding:24px 16px;font-family:system-ui,sans-serif;"
          "line-height:1.6;color:#e7e7ea;opacity:0;animation:seo-ver 0s 2s forwards}"
          ".seo-estatico a{color:#5eead4;margin-right:12px}@keyframes seo-ver{to{opacity:1}}</style>")


def json_ld(page: Page, planos: list[dict] | None = None) -> list[dict]:
    org = {"@context": "https://schema.org", "@type": "Organization", "name": NOME, "url": SITE,
           "logo": f"{SITE}/apple-touch-icon.png", "sameAs": [INSTAGRAM]}
    site = {"@context": "https://schema.org", "@type": "WebSite", "name": NOME, "url": SITE, "inLanguage": "pt-BR"}
    out = [org, site] if page.path == "/" else []
    if "app" in page.schema:
        app = {
            "@context": "https://schema.org", "@type": "SoftwareApplication", "name": NOME,
            "applicationCategory": "UtilitiesApplication", "operatingSystem": "Windows 10, Windows 11",
            "description": DESCRICAO_PADRAO, "url": SITE, "downloadUrl": f"{SITE}/download",
            "screenshot": OG_IMAGE, "inLanguage": "pt-BR",
        }
        offers = [{"@type": "Offer", "name": p["name"], "price": f"{p['price_cents'] / 100:.2f}",
                   "priceCurrency": "BRL", "url": f"{SITE}/planos"} for p in (planos or [])]
        if offers:
            app["offers"] = offers
        out.append(app)
    if "faq" in page.schema:
        out.append({"@context": "https://schema.org", "@type": "FAQPage", "mainEntity": [
            {"@type": "Question", "name": q, "acceptedAnswer": {"@type": "Answer", "text": a}} for q, a in FAQ]})
    return out


def _ld_script(data: dict) -> str:
    # "<" escapado: texto do banco nunca fecha a tag <script> antes da hora
    # (o JSON continua o mesmo para quem le).
    return '<script type="application/ld+json">' + json.dumps(data, ensure_ascii=False).replace("<", "\\u003c") + "</script>"


def render(index_html: str, page: Page, indexable: bool = True, planos: list[dict] | None = None) -> str:
    """index.html do build com a cabeca e o corpo desta pagina."""
    index = page.index and indexable
    title, desc = escape(page.title), escape(page.description, quote=True)
    url = SITE + ("" if page.path == "/" else page.path)
    html = re.sub(r"<title>.*?</title>", f"<title>{title}</title>", index_html, count=1, flags=re.S)
    html = re.sub(r'<meta name="description"[^>]*>', f'<meta name="description" content="{desc}" data-rh="true" />',
                  html, count=1)
    # og:title/og:description do index.html sao fixos: saem para os desta pagina.
    html = re.sub(r'\s*<meta property="og:(title|description|type)"[^>]*>', "", html)
    head = [
        f'<meta property="og:title" content="{title}" />',
        f'<meta property="og:description" content="{desc}" />',
        '<meta property="og:type" content="website" />',
        f'<meta property="og:site_name" content="{NOME}" />',
        '<meta property="og:locale" content="pt_BR" />',
        f'<meta property="og:image" content="{OG_IMAGE}" />',
        '<meta name="twitter:card" content="summary_large_image" />',
        ESTILO,
    ]
    from app import settings

    # Analytics so' no dominio oficial: visita ao staging nao entra nos numeros.
    # O script so' carrega depois do "Aceitar todos" (frontend/src/lib/analytics.ts).
    if indexable and settings.GA_MEASUREMENT_ID:
        head.append(f'<meta name="fpsx-ga" content="{escape(settings.GA_MEASUREMENT_ID, quote=True)}" />')
    if indexable and settings.GOOGLE_SITE_VERIFICATION:
        head.append(f'<meta name="google-site-verification" content="{escape(settings.GOOGLE_SITE_VERIFICATION, quote=True)}" />')
    if index:
        head.insert(0, f'<link rel="canonical" href="{escape(url, quote=True)}" data-rh="true" />')
        head.insert(1, f'<meta property="og:url" content="{escape(url, quote=True)}" />')
        head += [_ld_script(d) for d in json_ld(page, planos)]
    else:
        head.insert(0, '<meta name="robots" content="noindex, nofollow" data-rh="true" />')
    html = html.replace("</head>", "    " + "\n    ".join(head) + "\n  </head>", 1)
    if page.blocks:
        html = html.replace('<div id="root"></div>', f'<div id="root">{static_body(page)}</div>', 1)
    return html


# ─── arquivos para robos ────────────────────────────────────────────────

def robots_txt(indexable: bool) -> str:
    if not indexable:
        return "User-agent: *\nDisallow: /\n"
    # /api/public fica liberado: o Google roda o JavaScript do site e precisa
    # buscar planos e versoes para desenhar a pagina como a pessoa ve.
    # Robos de IA (GPTBot, ClaudeBot, PerplexityBot, Google-Extended) entram
    # pela regra geral: o produto quer ser explicado por eles.
    return "\n".join([
        "User-agent: *",
        "Allow: /",
        "Allow: /api/public/",
        "Disallow: /api/",
        # O link do instalador conta cliques: robo seguindo o link inflaria o numero.
        "Disallow: /api/public/download",
        *[f"Disallow: {p}" for p in ("/conta", "/meu-plano", "/pagamento", "/admin", "/r/")],
        "",
        f"Sitemap: {SITE}/sitemap.xml",
        "",
    ])


def sitemap_xml() -> str:
    urls = "".join(f"<url><loc>{SITE}{'' if p == '/' else p}</loc></url>" for p in public_paths())
    return ('<?xml version="1.0" encoding="UTF-8"?>'
            f'<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">{urls}</urlset>')


def llms_txt(planos: list[dict] | None = None) -> str:
    """Resumo em texto puro para IAs (padrao llms.txt): o que e', o que faz,
    o que nao faz, planos com o preco atual e onde ler mais."""
    planos = _planos() if planos is None else planos
    linhas = [
        f"# {NOME}",
        "",
        "> App para Windows 10 e 11 que analisa o PC de quem joga, corrige só o que faz sentido para aquele "
        "hardware, com backup e desfazer, e mede o FPS das partidas reais antes e depois. Site em português do Brasil.",
        "",
        "## O que faz",
        "- Diagnóstico só de leitura: processador, placa de vídeo, memória (XMP/EXPO, canal duplo), discos, energia, "
        "monitor (taxa de atualização e em qual placa está ligado), rede, inicialização e jogos instalados.",
        "- Corrige com um clique, com backup antes e conferência depois. Toda alteração pode ser desfeita, em qualquer plano.",
        "- Aponta o que o Windows não resolve, com passo a passo (BIOS, cabo do monitor, jogo em HD, driver antigo).",
        "- Mede cada partida sozinho (FPS médio, 1% low, travadas por minuto) pelo PresentMon da Intel, sem injetar "
        "nada no jogo: funciona com anti-cheat (Vanguard, Easy Anti-Cheat).",
        "- Ajusta o arquivo de vídeo de CS2, Fortnite e Minecraft conforme o nível do PC.",
        f"- Jogos reconhecidos: {', '.join(JOGOS)}.",
        "- Não promete número de FPS: mostra o ganho medido, inclusive quando não houve ganho.",
        "",
        "## O que não faz, de propósito",
        *[f"- {i}" for i in NAO_FAZ],
        "",
        "## Planos (preços atuais)",
        *([f"- {plano_texto(p)}" for p in planos] or [f"- Veja {SITE}/planos"]),
        "- Pagamento único por PIX ou cartão (Mercado Pago), sem renovação automática. 7 dias para desistir. "
        "Cada plano vale para 1 PC por vez.",
        "",
        "## Perguntas frequentes",
        *[f"- {q} {a}" for q, a in FAQ],
        "",
        "## Páginas",
        f"- [Como funciona]({SITE}/como-funciona)",
        f"- [Planos]({SITE}/planos)",
        f"- [Baixar]({SITE}/download)",
        f"- [Quem somos]({SITE}/quem-somos)",
        f"- [Privacidade]({SITE}/privacidade)",
        "",
    ]
    return "\n".join(linhas)


def indexable_host(host: str | None) -> bool:
    """So' o dominio oficial entra no indice. Staging e o dominio do Railway
    ficam de fora, para nao concorrer com o site de verdade."""
    return (host or "").lower().split(":")[0] == SITE_HOST
