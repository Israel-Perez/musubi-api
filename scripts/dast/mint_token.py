"""Access token de prueba para el DAST (02-10, SCRUM-123).

Uso: python3 scripts/dast/mint_token.py   (lee DAST_JWT_KEY: el Jwt:SigningKey en base64 de la API del job)

Firma un JWT HS256 con los mismos claims que AccessTokenIssuer (iss, aud, sub, community, role, jti, iat, nbf,
exp, iat_ms) para que ZAP tambien recorra las rutas con sesion. La llave es aleatoria y solo existe en el job: el
token no sirve en ningun otro lado. Sin dependencias (solo la biblioteca estandar).
"""

import base64
import hashlib
import hmac
import json
import os
import secrets
import time


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def main() -> None:
    key = base64.b64decode(os.environ["DAST_JWT_KEY"])
    now = time.time()
    claims = {
        "iss": "musubi-api",
        "aud": "musubi-app",
        "sub": secrets.token_hex(12),
        "community": "000000000000000000000001",
        "role": ["student"],
        "jti": secrets.token_hex(16),
        "iat": int(now),
        "nbf": int(now),
        "exp": int(now) + 3600,
        "iat_ms": int(now * 1000),
    }
    header = b64url(json.dumps({"alg": "HS256", "typ": "JWT"}, separators=(",", ":")).encode())
    payload = b64url(json.dumps(claims, separators=(",", ":")).encode())
    signature = hmac.new(key, f"{header}.{payload}".encode(), hashlib.sha256).digest()
    print(f"{header}.{payload}.{b64url(signature)}")


if __name__ == "__main__":
    main()
