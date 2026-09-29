"""Assistente do site: tira duvida sobre o RKZFPS e ajuda a escolher o plano.

Chamada direta a API HTTP da OpenAI (httpx ja' e' dependencia), sem SDK.
O que o assistente sabe vem daqui: o texto do produto abaixo e os planos
ATUAIS do banco (preco nunca e' escrito no prompt a mao, igual ao site).

Limites, porque cada pergunta custa: mensagem curta, historico curto,
resposta curta, teto por visitante e teto global por hora. Sem chave
configurada, a rota responde 503 e o site esconde o chat.
"""
import threading
import time

import httpx

from app import settings
from app.services import plans

OPENAI_URL = "https://api.openai.com/v1/chat/completions"
MAX_MESSAGES = 10
MAX_CHARS = 600
MAX_REPLY_TOKENS = 450
GLOBAL_PER_HOUR = 400

_global: list[float] = []
_global_lock = threading.Lock()

PRODUTO = """
O RKZFPS e' um app para Windows 10 e 11 (64 bits) que analisa o PC de quem joga, corrige so' o que faz sentido
para aquele hardware e mede o FPS das partidas reais, antes e depois. Site: rkzfps.com.br.

Como funciona:
- Ao abrir, faz um diagnostico so' de leitura: processador, placa de video, memoria, discos, energia, monitor,
  rede, programas que abrem com o Windows e os jogos instalados. Nada muda nessa etapa.
- Corrige com um clique o que encontrar (plano de energia de economia, monitor abaixo da taxa maxima, Game Mode
  desligado, gravacao em segundo plano, jogo rodando na placa integrada, arquivo de paginacao desligado,
  programas pesando). Toda alteracao guarda o estado anterior e pode ser desfeita, em qualquer plano.
- Aponta o que o Windows nao resolve, com o passo a passo: memoria abaixo da velocidade de fabrica (perfil
  XMP/EXPO desligado na BIOS), memoria com um pente so', cabo do monitor ligado na placa-mae em vez da placa de
  video, jogo instalado em HD comum, driver de video antigo, disco com alerta de saude.
- Ajusta a configuracao de video de CS2, Fortnite e Minecraft conforme o nivel do PC, e reconhece e mede
  tambem Valorant, League of Legends, EA SPORTS FC, Roblox, GTA V, Apex, Call of Duty, PUBG, Rainbow Six,
  Dota 2, Rocket League e Marvel Rivals.
- Mede o FPS sozinho em cada partida (FPS medio, 1% low e travadas), mostra um grafico com as quedas e diz o que
  estava pesando no PC em cada queda. Compara partidas antes e depois de otimizar.
- A medicao usa o registro de quadros do proprio Windows (PresentMon, da Intel) e nao injeta nada no jogo:
  funciona com anti-cheat (Vanguard, Easy Anti-Cheat).

O que o RKZFPS NAO faz, de proposito: desligar antivirus, firewall ou Windows Update; desligar dezenas de
servicos; prioridade tempo real; limpador de RAM; fechar programa a forca; overclock.

Regras comerciais: cada plano vale para 1 PC. O diagnostico e' gratuito. Desfazer funciona em todos os planos,
mesmo depois de cancelar. Pagamento por PIX ou cartao pelo Mercado Pago. 7 dias para desistir e pedir
reembolso (Codigo de Defesa do Consumidor). O app recebe atualizacoes sem custo a mais para quem assina.
"""

REGRAS = """
Voce e' o assistente do site do RKZFPS. Responda em portugues do Brasil, curto (ate 4 frases ou uma lista
curta), direto e simpatico, como alguem que entende de PC e fala com gamer.
- Nunca prometa numero de FPS. O ganho depende do PC; o app mede e mostra o numero real. Pode dizer onde
  costuma ter ganho grande (PC mal configurado) e onde nao (PC ja' bem configurado).
- So' fale do que esta' descrito aqui. Se nao souber, diga que nao sabe e indique o suporte do site.
- Problema com conta, pagamento ou reembolso: oriente a falar com o suporte pelo site.
- Nao peca dados pessoais (senha, CPF, cartao). Nao responda assuntos fora de PC, jogos e do RKZFPS.
- Nao use emoji, nem o travessao (—), nem o caractere ponto do meio (·). Use pontuacao normal, com ponto final nas frases. Use os precos exatamente como estao na lista de planos.
- Quando fizer sentido, lembre que baixar, fazer o diagnostico e medir o FPS das partidas e' gratis: a pessoa
  ve o que esta' pesando no PC dela antes de pagar. Sugira o plano pelo que a pessoa precisa, sem empurrar o mais caro.
"""


class AssistantUnavailable(Exception):
    """Sem chave da OpenAI ou teto global atingido."""


def _money(cents: int) -> str:
    return f"R$ {cents / 100:.2f}".replace(".", ",")


def plans_text() -> str:
    lines = []
    for p in plans.list_active():
        if p["price_cents"] <= 0:
            lines.append(f"- {p['name']}: gratis. {p['description']}")
            continue
        extra = f" (equivale a {_money(p['per_month_cents'])} por mes, {p['savings_percent']}% de economia)" if p.get("savings_percent") else ""
        lines.append(f"- {p['name']}: {_money(p['price_cents'])} por {p['days']} dias{extra}. {p['description']} "
                     f"Inclui: {'; '.join(p['features'])}.")
    return "\n".join(lines)


def system_prompt() -> str:
    return f"{REGRAS}\n\nSOBRE O PRODUTO:\n{PRODUTO}\n\nPLANOS ATUAIS:\n{plans_text()}"


def clean(messages: list[dict]) -> list[dict]:
    """So' papeis user/assistant, texto curto, historico recente, e a ultima tem que ser do visitante."""
    out = [{"role": m["role"], "content": str(m.get("content", ""))[:MAX_CHARS]}
           for m in messages if isinstance(m, dict) and m.get("role") in ("user", "assistant") and str(m.get("content", "")).strip()]
    out = out[-MAX_MESSAGES:]
    if not out or out[-1]["role"] != "user":
        raise ValueError("A conversa precisa terminar com uma pergunta.")
    return out


def _take_global_slot() -> None:
    now = time.monotonic()
    with _global_lock:
        _global[:] = [t for t in _global if now - t < 3600]
        if len(_global) >= GLOBAL_PER_HOUR:
            raise AssistantUnavailable("Muitas perguntas agora. Tente de novo em alguns minutos.")
        _global.append(now)


def ask(messages: list[dict]) -> str:
    if not settings.OPENAI_API_KEY:
        raise AssistantUnavailable("Assistente indisponivel.")
    history = clean(messages)
    _take_global_slot()
    r = httpx.post(
        OPENAI_URL,
        headers={"Authorization": f"Bearer {settings.OPENAI_API_KEY}"},
        json={"model": settings.OPENAI_MODEL, "max_completion_tokens": MAX_REPLY_TOKENS,
              "messages": [{"role": "system", "content": system_prompt()}, *history]},
        timeout=30,
    )
    if r.status_code != 200:
        raise AssistantUnavailable("O assistente nao conseguiu responder agora. Tente de novo.")
    return r.json()["choices"][0]["message"]["content"].strip()


EXPLICAR = """
Voce explica o diagnostico do RKZFPS para quem joga e nao entende de PC. Responda em portugues do Brasil,
em texto simples, sem markdown pesado, em ate 150 palavras:
1. Uma frase dizendo como o PC esta' para jogos, pelo hardware.
2. O que fazer primeiro, em ordem de maior efeito no jogo (no maximo 3 itens), usando SO' os itens do
   diagnostico abaixo. Para cada um, diga em palavras simples por que ajuda.
Regras: nunca prometa numero de FPS (o app mede o real nas partidas). Nunca sugira desligar antivirus,
firewall, Windows Update, servicos em massa, limpador de RAM ou overclock. Nao invente item que nao esta' na
lista. Se a lista estiver vazia, diga que o PC esta' bem configurado e que o proximo passo e' jogar com o app
aberto para medir o FPS. Nao use emoji, nem o travessao (—), nem o caractere ponto do meio (·). Use pontuacao normal, com ponto final nas frases.
"""


def explain(hardware: dict, findings: list[dict]) -> str:
    """Diagnostico do app em palavras simples. Recebe so' titulo, estado e recomendacao de cada item
    (nada de nome de programa, arquivo ou pasta) e o resumo do hardware."""
    if not settings.OPENAI_API_KEY:
        raise AssistantUnavailable("Explicacao com IA indisponivel agora.")
    hw = ", ".join(f"{k}: {str(v)[:80]}" for k, v in list(hardware.items())[:10])
    items = "\n".join(
        f"- [{str(f.get('status', ''))[:12]}] {str(f.get('title', ''))[:160]}. {str(f.get('recommendation') or '')[:300]}"
        f"{' (efeito: ' + str(f['impact'])[:40] + ')' if f.get('impact') else ''}"
        for f in findings[:12])
    _take_global_slot()
    r = httpx.post(
        OPENAI_URL,
        headers={"Authorization": f"Bearer {settings.OPENAI_API_KEY}"},
        json={"model": settings.OPENAI_MODEL, "max_completion_tokens": 500,
              "messages": [{"role": "system", "content": EXPLICAR},
                           {"role": "user", "content": f"HARDWARE: {hw}\n\nDIAGNOSTICO:\n{items or '(nada a corrigir)'}"}]},
        timeout=40,
    )
    if r.status_code != 200:
        raise AssistantUnavailable("A IA nao conseguiu responder agora. Tente de novo.")
    return r.json()["choices"][0]["message"]["content"].strip()


UPGRADE = """
Voce e' o consultor de hardware do RKZFPS, para quem joga no PC. Recebe o resumo do hardware, problemas de montagem
encontrados, os numeros medidos nas partidas e um VEREDITO calculado pelo app a partir dessas medicoes. Responda em
portugues do Brasil, texto simples, em ate 170 palavras, nesta ordem:
1. O que resolver SEM gastar, so' se estiver na lista de montagem (memoria abaixo da velocidade de fabrica, um pente so',
   cabo do monitor na placa-mae, jogo no HD). Uma frase cada.
2. A peca que mais segura o FPS, seguindo o VEREDITO (nunca o contradiga). Diga a faixa de peca que faz sentido com o
   resto deste PC (exemplo: "uma placa de video da faixa da RTX 4060 ou RX 7600") e o que conferir antes de comprar
   (fonte, soquete e chipset da placa-mae, espaco no gabinete).
3. O que NAO vale trocar agora, e por que, em uma frase.
Regras: nunca prometa numero de FPS nem porcentagem de ganho. Nunca cite preco. Se o veredito for "unknown", diga que
falta jogar com o RKZFPS aberto para medir e de' so' orientacao geral pelo hardware. Nao recomende overclock. Nao use
emoji, nem o travessao (—), nem o caractere ponto do meio (·). Use pontuacao normal, com ponto final nas frases.
"""

GAME_TIPS = """
Voce ajusta o menu de video de um jogo para o PC de quem joga, no RKZFPS. Responda em portugues do Brasil, em ate 150
palavras, em lista curta: as 3 a 5 opcoes do menu de video do jogo que mais dao FPS NESTE hardware, da que mais ajuda
para a que menos, cada uma com o valor sugerido e, em poucas palavras, o que muda na imagem. Use os nomes das opcoes
como aparecem no menu do jogo. Se vier medicao: quedas com a placa de video no limite pedem opcoes graficas (sombras,
resolucao, efeitos); quedas com o processador no limite pedem opcoes que pesam na CPU (distancia de visao, jogadores,
fisica); FPS medio bem acima da taxa do monitor permite limitar o FPS perto da taxa para ficar mais estavel.
Regras: nunca prometa numero de FPS. Nao sugira programa externo, mod, editar arquivo a mao nem overclock. Nao use
emoji, nem o travessao (—), nem o caractere ponto do meio (·). Use pontuacao normal.
"""


def _texto(v, n: int) -> str:
    return str(v if v is not None else "")[:n]


def _hardware_txt(hardware: dict) -> str:
    return ", ".join(f"{_texto(k, 40)}: {_texto(v, 80)}" for k, v in list(hardware.items())[:12])


def _jogo_txt(g: dict) -> str:
    campos = ("game", "matches", "avg_fps", "low1_fps", "display_hz", "avg_cpu", "avg_gpu", "drops", "gpu_drops", "cpu_drops", "app_drops")
    return ", ".join(f"{c}={_texto(g.get(c), 60)}" for c in campos if c in g)


def _chat(system: str, user: str, max_tokens: int) -> str:
    if not settings.OPENAI_API_KEY:
        raise AssistantUnavailable("A IA esta' indisponivel agora.")
    _take_global_slot()
    r = httpx.post(
        OPENAI_URL,
        headers={"Authorization": f"Bearer {settings.OPENAI_API_KEY}"},
        json={"model": settings.OPENAI_MODEL, "max_completion_tokens": max_tokens,
              "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]},
        timeout=40,
    )
    if r.status_code != 200:
        raise AssistantUnavailable("A IA nao conseguiu responder agora. Tente de novo.")
    return r.json()["choices"][0]["message"]["content"].strip()


def upgrade(hardware: dict, setup: list, games: list, verdict: str, verdict_text: str) -> str:
    """Qual peca trocar primeiro. O veredito vem calculado do app (medicao), a IA so' escreve."""
    montagem = "\n".join(f"- {_texto(t, 160)}" for t in setup[:8]) or "(nada encontrado)"
    jogos = "\n".join(f"- {_jogo_txt(g)}" for g in games[:6] if isinstance(g, dict)) or "(nenhuma partida medida)"
    user = (f"HARDWARE: {_hardware_txt(hardware)}\n\nMONTAGEM:\n{montagem}\n\nPARTIDAS MEDIDAS:\n{jogos}\n\n"
            f"VEREDITO: {_texto(verdict, 12)}. {_texto(verdict_text, 240)}")
    return _chat(UPGRADE, user, 550)


def game_tips(game: str, hardware: dict, tier: str, measured: dict | None) -> str:
    """O que ajustar primeiro no menu de video do jogo, para este PC."""
    medido = _jogo_txt(measured) if isinstance(measured, dict) else "(sem partida medida deste jogo)"
    user = f"JOGO: {_texto(game, 60)}\nNIVEL DO PC: {_texto(tier, 40)}\nHARDWARE: {_hardware_txt(hardware)}\nMEDICAO: {medido}"
    return _chat(GAME_TIPS, user, 450)
