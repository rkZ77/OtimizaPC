"""Cabecalhos de seguranca em toda resposta (site e API).

O que cada um evita:
  * Strict-Transport-Security: o navegador nunca mais fala com o site por
    HTTP puro, nem se alguem digitar http:// ou uma rede publica tentar
    rebaixar a conexao.
  * Content-Security-Policy: script so' do proprio dominio. O unico script
    inline (tema antes do React) entra pelo HASH calculado do index.html do
    build, entao um script injetado por XSS nao roda.
  * frame-ancestors / X-Frame-Options: ninguem embute o site num iframe
    para enganar clique (clickjacking) na tela de pagamento ou de conta.
  * X-Content-Type-Options: arquivo servido como imagem nao vira script.
  * Referrer-Policy e Permissions-Policy: o minimo de informacao sai para
    outros sites, e camera, microfone e localizacao ficam desligados.
"""
import base64
import hashlib
import re
from pathlib import Path

from starlette.middleware.base import BaseHTTPMiddleware

from app import settings

_INLINE_SCRIPT = re.compile(r"<script>(.*?)</script>", re.S)


def inline_script_hashes(index_html: str) -> list[str]:
    """Hash CSP ('sha256-...') de cada <script> inline sem src do index.html."""
    return ["'sha256-" + base64.b64encode(hashlib.sha256(body.encode()).digest()).decode() + "'"
            for body in _INLINE_SCRIPT.findall(index_html)]


def build_csp(script_hashes: list[str]) -> str:
    return "; ".join([
        "default-src 'self'",
        "script-src 'self' " + " ".join(script_hashes),
        # Estilo inline vem das animacoes (style="transform..."), nao de dado do usuario.
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data:",
        "font-src 'self'",
        "connect-src 'self'",
        "frame-ancestors 'none'",
        "base-uri 'self'",
        # Formulario so' posta no proprio site; o checkout abre o Mercado Pago por navegacao.
        "form-action 'self'",
        "object-src 'none'",
    ]).strip()


def _csp_for_dist() -> str:
    index = Path(__file__).resolve().parent.parent / "dist" / "index.html"
    hashes = inline_script_hashes(index.read_text(encoding="utf-8")) if index.exists() else []
    return build_csp(hashes)


class SecurityHeaders(BaseHTTPMiddleware):
    def __init__(self, app):
        super().__init__(app)
        self._csp = _csp_for_dist()

    async def dispatch(self, request, call_next):
        response = await call_next(request)
        h = response.headers
        h.setdefault("Content-Security-Policy", self._csp)
        h.setdefault("X-Content-Type-Options", "nosniff")
        h.setdefault("X-Frame-Options", "DENY")
        h.setdefault("Referrer-Policy", "strict-origin-when-cross-origin")
        h.setdefault("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()")
        h.setdefault("Cross-Origin-Opener-Policy", "same-origin")
        if settings.IS_PRODUCTION:
            h.setdefault("Strict-Transport-Security", "max-age=31536000; includeSubDomains")
        # Resposta da API com dado de conta nunca fica em cache compartilhado.
        if request.url.path.startswith("/api/"):
            h.setdefault("Cache-Control", "no-store")
        return response
