"""Casca e conteudo dos e-mails transacionais do RKZFPS.

Mesmo desenho do email_templates.py do Pickia, e pela mesma licao de la': a
casca e' UMA funcao. No Pickia ela estava copiada em tres arquivos e cada
copia derivou (uma com logo, outra sem, rodape diferente). Aqui mudar o
rodape e' uma linha que vale para todo e-mail.

Regras de HTML de e-mail seguidas aqui (cliente de e-mail nao e' navegador):
  * layout em <table>, nunca flex/grid: o Outlook ignora e o conteudo desaba;
  * estilo inline, porque o Gmail descarta <style> em boa parte dos casos;
  * cor de fundo em cada celula, senao o modo escuro de alguns clientes
    reescreve o que ficou sem cor;
  * sem emoji; nome e texto vindos do usuario passam por html.escape.

Todo e-mail vai com parte em texto puro junto: e-mail so' com HTML cai em
spam com mais frequencia.
"""
from dataclasses import dataclass
from html import escape

# Paleta espelhando os tokens do site no tema escuro (index.css), fixada em
# hex porque e-mail nao tem variavel CSS.
FUNDO = "#0a0a0c"
CARTAO = "#141418"
CAIXA = "#1f1f24"
BORDA = "#232329"
TEXTO_1 = "#fafafa"
TEXTO_2 = "#c7c7cf"
TEXTO_3 = "#94949e"
TEXTO_4 = "#75757f"
MENTA = "#3ddc97"
ALERTA = "#f5a524"

ASSINATURA = "RKZFPS. Otimização de PC com diagnóstico, backup e desfazer."


@dataclass(frozen=True)
class Email:
    subject: str
    text: str
    html: str


def primeiro_nome(nome: str | None) -> str:
    return (nome or "").strip().split(" ")[0] or "tudo bem"


def _botao(url: str, rotulo: str) -> str:
    # Texto escuro sobre a menta: branco sobre #3ddc97 fica abaixo de 2:1.
    return (f'<a href="{escape(url, quote=True)}" style="display:inline-block;background:{MENTA};color:{FUNDO};'
            f'text-decoration:none;font-weight:800;font-size:15px;padding:14px 36px;border-radius:10px;">{escape(rotulo)}</a>')


def _paragrafos(paragrafos: list[str]) -> str:
    return "".join(
        f'<p style="margin:0 0 14px;color:{TEXTO_2};font-size:15px;line-height:1.6;">{p}</p>' for p in paragrafos)


def casca(conteudo: str, nota_rodape: str = "") -> str:
    nota = (f'<p style="margin:0 0 8px;color:{TEXTO_4};font-size:12px;line-height:1.5;">{nota_rodape}</p>'
            if nota_rodape else "")
    return f"""<!DOCTYPE html>
<html lang="pt-BR">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="dark"></head>
<body style="margin:0;padding:0;background:{FUNDO};font-family:'Helvetica Neue',Helvetica,Arial,sans-serif;">
  <table width="100%" cellpadding="0" cellspacing="0" style="background:{FUNDO};padding:32px 16px;">
    <tr><td align="center">
      <table width="560" cellpadding="0" cellspacing="0" style="background:{CARTAO};border:1px solid {BORDA};border-radius:16px;overflow:hidden;max-width:560px;width:100%;">
        <tr><td style="background:{CARTAO};border-bottom:1px solid {BORDA};padding:22px 36px;">
          <span style="color:{TEXTO_1};font-size:22px;font-weight:900;letter-spacing:-0.5px;">FPS<span style="color:{MENTA};">X</span></span>
        </td></tr>
        <tr><td style="background:{CARTAO};padding:32px 36px;">
{conteudo}
        </td></tr>
        <tr><td style="background:{CARTAO};border-top:1px solid {BORDA};padding:18px 36px;">
          {nota}
          <p style="margin:0;color:{TEXTO_4};font-size:11px;">{ASSINATURA}</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>"""


def _titulo(rotulo: str, titulo: str) -> str:
    return (f'<p style="margin:0 0 6px;color:{TEXTO_3};font-size:12px;text-transform:uppercase;letter-spacing:1px;font-weight:700;">{escape(rotulo)}</p>'
            f'<h1 style="margin:0 0 18px;color:{TEXTO_1};font-size:22px;font-weight:800;line-height:1.3;">{titulo}</h1>')


def _quadro(linhas: list[tuple[str, str]]) -> str:
    """Rotulo e valor num quadro, como o de plano e validade do Pickia."""
    tds = "".join(
        f'<tr><td style="background:{CAIXA};padding:14px 18px;border-bottom:1px solid {BORDA};">'
        f'<div style="color:{TEXTO_3};font-size:11px;text-transform:uppercase;letter-spacing:1px;">{escape(r)}</div>'
        f'<div style="color:{TEXTO_1};font-size:15px;font-weight:700;margin-top:4px;">{escape(v)}</div></td></tr>'
        for r, v in linhas)
    return (f'<table width="100%" cellpadding="0" cellspacing="0" style="background:{CAIXA};border:1px solid {BORDA};'
            f'border-radius:12px;overflow:hidden;margin:0 0 24px;">{tds}</table>')


# ─── 1. boas-vindas ─────────────────────────────────────────────────────

def boas_vindas(nome: str, site_url: str, trial_days: int) -> Email:
    n = primeiro_nome(nome)
    teste = (f"Sua conta já tem <strong style=\"color:{MENTA};\">{trial_days} dias de teste do plano Pro</strong> em 1 PC, sem cartão."
             if trial_days > 0 else "Sua conta está pronta.")
    corpo = [
        teste,
        "Baixe o app, entre com este e-mail e rode o diagnóstico. Ele não muda nada no PC: mostra o que está "
        "atrapalhando e por quê. Toda correção que você aplicar tem backup e pode ser desfeita com um clique.",
    ]
    html = casca(
        _titulo("Conta criada", f"Bem-vindo, {escape(n)}")
        + _paragrafos(corpo)
        + f'<div style="margin-top:10px;">{_botao(f"{site_url}/download", "Baixar o RKZFPS para Windows")}</div>',
        nota_rodape="Você recebeu este e-mail porque este endereço foi usado para criar uma conta no RKZFPS.",
    )
    text = (f"Olá {n},\n\nSua conta no RKZFPS foi criada."
            + (f" Ela já tem {trial_days} dias de teste do plano Pro em 1 PC, sem cartão." if trial_days > 0 else "")
            + f"\n\nBaixe o app: {site_url}/download\n\nO diagnóstico não muda nada no PC, e toda correção tem backup e pode ser desfeita.\n\nEquipe RKZFPS")
    return Email("Sua conta no RKZFPS está pronta", text, html)


# ─── 2. codigo para redefinir a senha ───────────────────────────────────

def codigo_senha(nome: str, codigo: str, site_url: str, email: str, minutos: int) -> Email:
    from urllib.parse import quote

    n = primeiro_nome(nome)
    digitos = "".join(
        f'<td style="padding:0 3px;"><div style="background:{CAIXA};border:1px solid {BORDA};border-radius:10px;'
        f'color:{TEXTO_1};font-family:Consolas,Menlo,monospace;font-size:26px;font-weight:800;padding:12px 0;'
        f'width:42px;text-align:center;">{d}</div></td>' for d in codigo)
    # O e-mail vai no link para a tela abrir direto no passo do codigo (licao
    # do Pickia: sem isso, quem clica pede outro codigo e invalida este).
    destino = f"{site_url}/redefinir-senha?email={quote(email)}"
    html = casca(
        _titulo("Redefinir senha", f"Olá, {escape(n)}")
        + _paragrafos(["Use o código abaixo na tela de redefinição para criar uma senha nova."])
        + f'<table cellpadding="0" cellspacing="0" style="margin:6px 0 12px;"><tr>{digitos}</tr></table>'
        + f'<p style="margin:0 0 24px;color:{TEXTO_4};font-size:12px;">O código vale por {minutos} minutos.</p>'
        + _botao(destino, "Redefinir minha senha")
        + f'<p style="margin:24px 0 0;color:{TEXTO_3};font-size:13px;line-height:1.6;"><strong style="color:{TEXTO_2};">Não foi você?</strong> '
          'Ignore este e-mail. Sua senha atual continua valendo, e ninguém troca nada sem este código.</p>',
        nota_rodape="O RKZFPS nunca pede sua senha por e-mail, WhatsApp ou telefone.",
    )
    text = (f"Olá {n},\n\nSeu código para redefinir a senha do RKZFPS é:\n\n  {codigo}\n\n"
            f"Ele vale por {minutos} minutos. Digite em: {destino}\n\n"
            "Se não foi você, ignore este e-mail. Sua senha atual continua valendo.\n\nEquipe RKZFPS")
    return Email("Seu código para redefinir a senha do RKZFPS", text, html)


def senha_alterada(nome: str, site_url: str) -> Email:
    n = primeiro_nome(nome)
    html = casca(
        _titulo("Segurança", "Sua senha foi alterada")
        + _paragrafos([f"Olá, {escape(n)}. A senha da sua conta no RKZFPS acabou de ser trocada, e as sessões abertas antes disso foram encerradas.",
                       "Se foi você, não precisa fazer nada. Se não foi, peça um código novo agora mesmo."])
        + _botao(f"{site_url}/esqueci-senha", "Recuperar minha conta"),
        nota_rodape="O RKZFPS nunca pede sua senha por e-mail, WhatsApp ou telefone.",
    )
    text = (f"Olá {n},\n\nA senha da sua conta no RKZFPS foi alterada e as sessões antigas foram encerradas.\n"
            f"Se não foi você, recupere a conta em: {site_url}/esqueci-senha\n\nEquipe RKZFPS")
    return Email("A senha da sua conta RKZFPS foi alterada", text, html)


# ─── 3. pagamento aprovado ──────────────────────────────────────────────

def pagamento_aprovado(nome: str, plano: str, vence_em: str, valor: str, site_url: str) -> Email:
    n = primeiro_nome(nome)
    html = casca(
        _titulo("Pagamento confirmado", f"Tudo certo, {escape(n)}")
        + _paragrafos(["O plano já está ativo na sua conta. No app, abra <strong>Conta</strong> e clique em "
                       "<strong>Sincronizar</strong>, ou apenas reabra o RKZFPS."])
        + _quadro([("Plano", plano), ("Valor", valor), ("Válido até", vence_em)])
        + _botao(f"{site_url}/conta", "Ver minha conta"),
        nota_rodape="Guarde este e-mail como comprovante. O desfazer continua liberado em qualquer plano.",
    )
    text = (f"Olá {n},\n\nSeu pagamento foi confirmado.\n\nPlano: {plano}\nValor: {valor}\nVálido até: {vence_em}\n\n"
            f"No app, abra Conta e clique em Sincronizar, ou reabra o RKZFPS.\nSua conta: {site_url}/conta\n\nEquipe RKZFPS")
    return Email(f"Pagamento confirmado: RKZFPS {plano}", text, html)


# ─── 4. avisos de plano (vencendo e encerrado) ──────────────────────────
# Um layout so' para os dois momentos, como no Pickia: o que muda e' a frase
# e o rotulo do botao, nao a estrutura.

def aviso_plano(nome: str, plano: str, vence_em: str, dias: int, trial: bool, site_url: str) -> Email:
    n = primeiro_nome(nome)
    o_que = "O seu teste do RKZFPS" if trial else f"O seu plano RKZFPS {plano}"
    if dias > 0:
        quando = "amanhã" if dias == 1 else f"em {dias} dias"
        titulo = f"{o_que} acaba {quando}"
        frase = (f"{o_que} vale até {vence_em}. Depois disso o app volta para o plano Free: o diagnóstico continua, "
                 "e tudo que foi aplicado pode ser desfeito em qualquer plano.")
        cta = "Assinar agora" if trial else "Renovar agora"
        subject = f"{o_que} acaba {quando}"
    else:
        titulo = f"{o_que} terminou"
        frase = ("O app voltou para o plano Free. O diagnóstico continua funcionando, e o que você aplicou "
                 "segue podendo ser desfeito. Para voltar a corrigir com um clique, escolha um plano.")
        cta = "Ver planos"
        subject = f"{o_que} terminou"
    html = casca(
        _titulo("Seu plano", escape(titulo))
        + _paragrafos([f"Olá, {escape(n)}.", escape(frase)])
        + _botao(f"{site_url}/planos", cta),
        nota_rodape="Você recebeu este aviso porque tem uma conta no RKZFPS.",
    )
    text = f"Olá {n},\n\n{titulo}.\n\n{frase}\n\nPlanos: {site_url}/planos\n\nEquipe RKZFPS"
    return Email(subject, text, html)


# ─── 5. teste do admin ──────────────────────────────────────────────────

def teste(site_url: str) -> Email:
    html = casca(
        _titulo("Teste de envio", "O e-mail do RKZFPS está funcionando")
        + _paragrafos(["Se você está lendo isto, o Resend aceitou o remetente e a mensagem chegou.",
                       "Confira também se ela não caiu no spam: se caiu, falta configurar SPF e DKIM do domínio no Resend."])
        + _botao(f"{site_url}/admin#emails", "Voltar ao admin"),
    )
    return Email("Teste de envio do RKZFPS", "O e-mail do RKZFPS está funcionando.", html)
