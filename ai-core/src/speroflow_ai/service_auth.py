"""Service-to-service JWT verification for the private AI boundary."""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path
from typing import Any

import jwt
from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


_bearer = HTTPBearer(auto_error=False)


class ServiceAuthSettings(BaseSettings):
    """Settings shared by the ASP.NET API and private AI service."""

    model_config = SettingsConfigDict(extra="ignore")

    service_jwt_issuer: str = "speroflow-api"
    service_jwt_audience: str = "speroflow-ai"
    service_jwt_public_key_path: str = "/run/secrets/service_jwt_public_key"
    service_jwt_key_id: str = "speroflow-service-1"
    service_jwt_required: bool = True
    service_jwt_clock_skew_seconds: int = Field(default=30, ge=0, le=120)


@lru_cache
def get_service_auth_settings() -> ServiceAuthSettings:
    return ServiceAuthSettings()


@lru_cache
def _load_public_key(path: str) -> str:
    try:
        return Path(path).read_text(encoding="utf-8")
    except OSError as exc:
        raise RuntimeError("The AI service JWT public key is not mounted.") from exc


def get_service_principal(
    credentials: HTTPAuthorizationCredentials | None = Depends(_bearer),
    settings: ServiceAuthSettings = Depends(get_service_auth_settings),
) -> dict[str, Any]:
    """Require an ASP.NET-issued, short-lived service token."""
    if not settings.service_jwt_required:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Service JWT verification must be enabled outside local development.",
        )
    if credentials is None or credentials.scheme.lower() != "bearer":
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Internal service authentication is required.")

    try:
        claims = jwt.decode(
            credentials.credentials,
            _load_public_key(settings.service_jwt_public_key_path),
            algorithms=["RS256"],
            audience=settings.service_jwt_audience,
            issuer=settings.service_jwt_issuer,
            options={"require": ["exp", "iat", "sub", "scope"]},
            leeway=settings.service_jwt_clock_skew_seconds,
        )
    except jwt.PyJWTError as exc:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid internal service token.") from exc

    if claims.get("sub") != "service:api":
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Unexpected service principal.")
    return claims


def require_service_scope(required_scope: str):
    """Create a dependency that checks a signed scope claim."""

    def dependency(principal: dict[str, Any] = Depends(get_service_principal)) -> dict[str, Any]:
        scopes = set(str(principal.get("scope", "")).split())
        if required_scope not in scopes:
            raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Required service scope is missing.")
        return principal

    return dependency


def require_verified_user(principal: dict[str, Any] = Depends(require_service_scope("ai.invoke"))) -> dict[str, Any]:
    """Return the verified owner context carried by an ASP.NET service token."""
    user_id = principal.get("user_id")
    if not isinstance(user_id, str) or not user_id:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Verified user context is required.")
    return {
        "sub": user_id,
        "role": "authenticated",
        "auth_verified": True,
        "service": principal.get("sub"),
    }
