"""Validation for signed, short-lived knowledge graph access grants."""

from __future__ import annotations

import json
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from typing import Any
from uuid import UUID

import jwt

from speroflow_ai.config import Settings


@dataclass(frozen=True)
class DatasetGrant:
    dataset_id: str
    release_key: str
    owner_subject: str
    visibility: str


@dataclass(frozen=True)
class KnowledgeAccessGrant:
    grant_id: str
    subject: str
    datasets: tuple[DatasetGrant, ...]


@lru_cache
def _load_public_key(path: str) -> str:
    try:
        return Path(path).read_text(encoding="utf-8")
    except OSError as exc:
        raise ValueError("Knowledge grant public key is not mounted.") from exc


def validate_knowledge_access_grant(token: str | None, settings: Settings) -> KnowledgeAccessGrant:
    if not token or len(token) > 16_000:
        raise ValueError("A bounded knowledge access grant is required.")
    try:
        claims: dict[str, Any] = jwt.decode(
            token,
            _load_public_key(settings.knowledge_grant_public_key_path),
            algorithms=["RS256"],
            audience=settings.knowledge_grant_audience,
            issuer=settings.knowledge_grant_issuer,
            options={"require": ["exp", "iat", "jti", "sub", "scope"]},
            leeway=settings.knowledge_grant_clock_skew_seconds,
        )
    except jwt.PyJWTError as exc:
        raise ValueError("Knowledge access grant is invalid or expired.") from exc

    scopes = set(str(claims.get("scope", "")).split())
    subject = claims.get("sub")
    grant_id = claims.get("jti")
    issued_at = claims.get("iat")
    expires_at = claims.get("exp")
    if (
        "knowledge.query" not in scopes
        or not isinstance(subject, str)
        or not subject.strip()
        or not isinstance(grant_id, str)
        or not 1 <= len(grant_id) <= 128
        or isinstance(issued_at, bool)
        or not isinstance(issued_at, int)
        or isinstance(expires_at, bool)
        or not isinstance(expires_at, int)
        or expires_at <= issued_at
        or expires_at - issued_at > 300
    ):
        raise ValueError("Knowledge access grant is missing required claims.")

    values = claims.get("dataset_grant", [])
    if isinstance(values, str):
        values = [values]
    if not isinstance(values, list) or not 1 <= len(values) <= 20:
        raise ValueError("Knowledge access grant has an invalid dataset selection.")

    grants: list[DatasetGrant] = []
    seen: set[str] = set()
    for value in values:
        if not isinstance(value, str):
            raise ValueError("Knowledge access grant contains a malformed dataset selection.")
        try:
            payload = json.loads(value)
            dataset_id = str(UUID(str(payload["dataset_id"])))
            release_key = str(payload["release_key"]).strip()
            owner_subject = str(payload["owner_subject"]).strip()
            visibility = str(payload["visibility"]).strip().lower()
        except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
            raise ValueError("Knowledge access grant contains a malformed dataset selection.") from exc
        if dataset_id in seen or not 1 <= len(release_key) <= 200 or not 1 <= len(owner_subject) <= 256 or visibility not in {"private", "pendingreview", "published"}:
            raise ValueError("Knowledge access grant contains invalid dataset constraints.")
        seen.add(dataset_id)
        grants.append(DatasetGrant(dataset_id, release_key, owner_subject, visibility))

    return KnowledgeAccessGrant(grant_id=grant_id, subject=subject.strip(), datasets=tuple(grants))