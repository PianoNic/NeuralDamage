import urllib.parse
from datetime import datetime, timedelta

import httpx
import jwt

from app.config import settings

# Cache JWKS keys
_jwks_cache: dict | None = None


async def get_authorize_url(code_challenge: str, state: str) -> str:
    params = {
        "client_id": settings.OIDC_CLIENT_ID,
        "redirect_uri": settings.OIDC_REDIRECT_URI,
        "response_type": "code",
        "scope": "openid email profile",
        "code_challenge": code_challenge,
        "code_challenge_method": "S256",
        "state": state,
    }
    return f"{settings.OIDC_AUTHORIZE_ENDPOINT}?{urllib.parse.urlencode(params)}"


async def exchange_code(code: str, code_verifier: str) -> dict:
    async with httpx.AsyncClient() as client:
        resp = await client.post(
            settings.OIDC_TOKEN_ENDPOINT,
            data={
                "grant_type": "authorization_code",
                "code": code,
                "redirect_uri": settings.OIDC_REDIRECT_URI,
                "client_id": settings.OIDC_CLIENT_ID,
                "code_verifier": code_verifier,
            },
            headers={"Content-Type": "application/x-www-form-urlencoded"},
            timeout=30.0,
        )
        resp.raise_for_status()
        return resp.json()


async def get_jwks() -> dict:
    global _jwks_cache
    if _jwks_cache is not None:
        return _jwks_cache
    async with httpx.AsyncClient() as client:
        resp = await client.get(settings.OIDC_JWKS_URI, timeout=10.0)
        resp.raise_for_status()
        _jwks_cache = resp.json()
        return _jwks_cache


async def validate_id_token(id_token: str) -> dict:
    jwks_data = await get_jwks()
    jwks_client = jwt.PyJWKClient.__new__(jwt.PyJWKClient)
    # Manually set the JWKS data
    from jwt.api_jwk import PyJWKSet
    jwk_set = PyJWKSet.from_dict(jwks_data)

    # Get the signing key from the token header
    header = jwt.get_unverified_header(id_token)
    kid = header.get("kid")

    signing_key = None
    for key in jwk_set.keys:
        if key.key_id == kid:
            signing_key = key
            break

    if signing_key is None:
        raise ValueError("Unable to find signing key")

    claims = jwt.decode(
        id_token,
        signing_key.key,
        algorithms=["RS256"],
        audience=settings.OIDC_CLIENT_ID,
        issuer=settings.OIDC_ISSUER,
    )
    return claims


async def get_userinfo(access_token: str) -> dict:
    async with httpx.AsyncClient() as client:
        resp = await client.get(
            settings.OIDC_USERINFO_ENDPOINT,
            headers={"Authorization": f"Bearer {access_token}"},
            timeout=10.0,
        )
        resp.raise_for_status()
        return resp.json()


def create_app_token(user_id: str, email: str) -> str:
    payload = {
        "sub": user_id,
        "email": email,
        "exp": datetime.utcnow() + timedelta(hours=settings.JWT_EXPIRY_HOURS),
        "iat": datetime.utcnow(),
    }
    return jwt.encode(payload, settings.JWT_SECRET, algorithm=settings.JWT_ALGORITHM)


def decode_app_token(token: str) -> dict:
    return jwt.decode(token, settings.JWT_SECRET, algorithms=[settings.JWT_ALGORITHM])
