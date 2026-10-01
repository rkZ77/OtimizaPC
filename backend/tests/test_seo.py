"""O que o Google e as IAs leem do site sem rodar JavaScript (app/seo.py)."""
import json
import re

import pytest
from fastapi.testclient import TestClient

from app import seo
from app.main import app

# Mesmo esqueleto do index.html que o Vite gera.
INDEX = """<!doctype html>
<html lang="pt-BR">
  <head>
    <meta charset="UTF-8" />
    <title>RKZFPS: otimização de PC com diagnóstico e medição</title>
    <meta name="description" content="padrao" data-rh="true" />
    <meta property="og:title" content="fixo" />
    <meta property="og:description" content="fixo" />
    <meta property="og:type" content="website" />
    <script>(function () { tema() })()</script>
    <script type="module" crossorigin src="/assets/index-abc.js"></script>
  </head>
  <body>
    <div id="root"></div>
  </body>
</html>"""

PLANOS = [
    {"key": "free", "name": "Free", "price_cents": 0, "days": 3650, "period": "none", "description": "Diagnóstico.", "features": []},
    {"key": "pro", "name": "Pro", "price_cents": 2990, "days": 30, "period": "monthly", "description": "Tudo.",
     "features": ["Correções", "Medição"]},
]


def _texto(html: str) -> str:
    corpo = re.search(r'<div id="root">(.*)</div>\s*</body>', html, re.S).group(1)
    return re.sub(r"<[^>]+>", " ", corpo)


def test_pagina_publica_chega_com_texto_titulo_e_canonical_sem_javascript():
    html = seo.render(INDEX, seo.page_for("/como-funciona"), indexable=True)
    assert "<title>Como funciona | RKZFPS</title>" in html
    assert '<link rel="canonical" href="https://rkzfps.com.br/como-funciona" data-rh="true" />' in html
    assert "XMP/EXPO" in _texto(html) and len(_texto(html).split()) > 200
    assert 'og:image' in html and "noindex" not in html
    # og fixo do index.html sai; fica so' o desta pagina.
    assert html.count('property="og:title"') == 1 and 'content="fixo"' not in html


def test_home_traz_preco_do_banco_e_dados_estruturados():
    html = seo.render(INDEX, seo.page_for("/", PLANOS), indexable=True, planos=PLANOS)
    assert "R$ 29,90 por mês" in _texto(html)
    blocos = [json.loads(b) for b in re.findall(r'<script type="application/ld\+json">(.*?)</script>', html, re.S)]
    tipos = {b["@type"] for b in blocos}
    assert {"Organization", "WebSite", "SoftwareApplication", "FAQPage"} <= tipos
    app_ld = next(b for b in blocos if b["@type"] == "SoftwareApplication")
    assert {"price": "29.90", "priceCurrency": "BRL"}.items() <= app_ld["offers"][1].items()


def test_csp_continua_valendo_com_o_json_ld():
    # O hash do CSP cobre so' o <script> inline sem atributo (o do tema); o
    # JSON-LD e' dado, nao roda, e nao pode mudar a lista de hashes.
    from app.security_headers import inline_script_hashes
    html = seo.render(INDEX, seo.page_for("/", PLANOS), indexable=True, planos=PLANOS)
    assert inline_script_hashes(html) == inline_script_hashes(INDEX)


def test_texto_do_banco_nao_quebra_o_html():
    mal = [{**PLANOS[1], "name": "</script><script>alert(1)</script>", "description": "<img src=x onerror=alert(1)>"}]
    html = seo.render(INDEX, seo.page_for("/planos", mal), indexable=True, planos=mal)
    assert "<script>alert(1)" not in html and "<img src=x" not in html


def test_rota_inexistente_e_404_e_tela_privada_fica_fora_do_indice():
    assert seo.page_for("/nao-existe").status == 404
    for p in ("/conta", "/entrar", "/r/ABC1234", "/admin"):
        page = seo.page_for(p)
        assert page.status == 200 and not page.index, p
        assert 'name="robots" content="noindex' in seo.render(INDEX, page)


def test_fora_do_dominio_oficial_nada_e_indexado():
    assert seo.indexable_host("rkzfps.com.br") and not seo.indexable_host("rkzfps-noprod.up.railway.app")
    html = seo.render(INDEX, seo.page_for("/planos", PLANOS), indexable=False, planos=PLANOS)
    assert "noindex" in html and "canonical" not in html
    assert seo.robots_txt(False) == "User-agent: *\nDisallow: /\n"


@pytest.fixture
def client(monkeypatch):
    from app.services import plans
    monkeypatch.setattr(plans, "list_active", lambda: PLANOS)
    return TestClient(app)


def test_robots_sitemap_e_llms(client):
    r = client.get("/robots.txt")
    # O TestClient fala com "testserver": fora do dominio oficial.
    assert r.status_code == 200 and r.text.startswith("User-agent: *\nDisallow: /")
    assert "Sitemap: https://rkzfps.com.br/sitemap.xml" in seo.robots_txt(True)
    assert "Allow: /api/public/" in seo.robots_txt(True)

    s = client.get("/sitemap.xml")
    assert s.headers["content-type"].startswith("application/xml")
    assert "<loc>https://rkzfps.com.br/planos</loc>" in s.text and "/conta" not in s.text

    l = client.get("/llms.txt")
    assert l.status_code == 200 and l.text.startswith("# RKZFPS") and "R$ 29,90 por mês" in l.text


def test_analytics_so_no_dominio_oficial_e_csp_so_abre_com_ele(monkeypatch):
    from app import settings
    from app.security_headers import build_csp
    monkeypatch.setattr(settings, "GA_MEASUREMENT_ID", "G-ABC1234")
    monkeypatch.setattr(settings, "GOOGLE_SITE_VERIFICATION", "codigo-de-verificacao-123")
    oficial = seo.render(INDEX, seo.page_for("/download"), indexable=True)
    staging = seo.render(INDEX, seo.page_for("/download"), indexable=False)
    assert '<meta name="rkzfps-ga" content="G-ABC1234" />' in oficial and "rkzfps-ga" not in staging
    assert 'name="google-site-verification" content="codigo-de-verificacao-123"' in oficial
    assert "googletagmanager" not in build_csp([]) and "googletagmanager" in build_csp([], analytics=True)
    # O Google entra no script-src por dominio, nunca liberando script inline.
    assert "unsafe-inline" not in build_csp([], analytics=True).split("script-src")[1].split(";")[0]


def test_texto_para_robos_segue_as_regras_de_texto_do_site():
    textos = seo.llms_txt(PLANOS) + "".join(_texto(seo.render(INDEX, seo.page_for(p, PLANOS), planos=PLANOS))
                                           for p in seo.public_paths())
    assert "—" not in textos and "·" not in textos
