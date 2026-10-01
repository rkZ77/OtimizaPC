"""Assinatura ECDSA P-256 de licencas e do catalogo publicado.

Formato do token: base64url(json) + "." + base64url(assinatura DER).
O Agent verifica com a chave PUBLICA embutida no executavel. Isso e' o que
impede tanto um cliente de editar a propria licenca quanto um servidor falso
(DNS envenenado, proxy) de mandar catalogo que libera o que o admin desligou.
"""
import base64
import json
import os
import warnings

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec

from app import settings

_private_key: ec.EllipticCurvePrivateKey | None = None


def _load_key() -> ec.EllipticCurvePrivateKey:
    global _private_key
    if _private_key is not None:
        return _private_key

    pem = os.getenv("LICENSE_PRIVATE_KEY_PEM", "").replace("\\n", "\n").strip()
    path = os.getenv("LICENSE_PRIVATE_KEY_FILE", "")
    if not pem and path and os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            pem = f.read()

    if pem:
        key = serialization.load_pem_private_key(pem.encode(), password=None)
        if not isinstance(key, ec.EllipticCurvePrivateKey):
            raise RuntimeError("LICENSE_PRIVATE_KEY precisa ser uma chave EC P-256.")
        _private_key = key
    elif settings.IS_PRODUCTION:
        raise RuntimeError("LICENSE_PRIVATE_KEY_PEM nao configurada. Gere com: python -m app.signing")
    else:
        warnings.warn("Chave de licenca ausente: gerando chave efemera de desenvolvimento.", stacklevel=1)
        _private_key = ec.generate_private_key(ec.SECP256R1())
    return _private_key


def _b64(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def _unb64(text: str) -> bytes:
    return base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))


def sign(payload: dict) -> str:
    body = json.dumps(payload, separators=(",", ":"), sort_keys=True, ensure_ascii=False).encode()
    signature = _load_key().sign(body, ec.ECDSA(hashes.SHA256()))
    return f"{_b64(body)}.{_b64(signature)}"


def verify(token: str) -> dict | None:
    """Usado pelo backend para aceitar o token que o proprio Agent devolve."""
    try:
        body_b64, sig_b64 = token.split(".", 1)
        body = _unb64(body_b64)
        _load_key().public_key().verify(_unb64(sig_b64), body, ec.ECDSA(hashes.SHA256()))
        return json.loads(body)
    except (ValueError, InvalidSignature):
        return None


def public_key_pem() -> str:
    return _load_key().public_key().public_bytes(
        serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo
    ).decode()


if __name__ == "__main__":
    # Gera um par novo. A privada vai para o secret do deploy; a publica e'
    # embutida no Agent (agent/src/Rkzfps.Client/LicenseKeys.cs).
    key = ec.generate_private_key(ec.SECP256R1())
    print(key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8,
                            serialization.NoEncryption()).decode())
    print(key.public_key().public_bytes(serialization.Encoding.PEM,
                                        serialization.PublicFormat.SubjectPublicKeyInfo).decode())
