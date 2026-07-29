"""Unit tests for private AI service JWT enforcement."""

from __future__ import annotations

import tempfile
import time
import unittest
from pathlib import Path
import jwt
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from fastapi import HTTPException
from fastapi.security import HTTPAuthorizationCredentials

from speroflow_ai.service_auth import (
    ServiceAuthSettings,
    get_service_principal,
    require_service_scope,
)


class ServiceAuthTests(unittest.TestCase):
    def setUp(self) -> None:
        key = rsa.generate_private_key(public_exponent=65_537, key_size=2_048)
        self._private_pem = key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        )
        public_pem = key.public_key().public_bytes(
            serialization.Encoding.PEM,
            serialization.PublicFormat.SubjectPublicKeyInfo,
        )
        self._tmp = tempfile.TemporaryDirectory()
        self._public_path = Path(self._tmp.name) / "service_jwt_public_key"
        self._public_path.write_bytes(public_pem)
        self._settings = ServiceAuthSettings(
            service_jwt_issuer="speroflow-api",
            service_jwt_audience="speroflow-ai",
            service_jwt_public_key_path=str(self._public_path),
            service_jwt_required=True,
            service_jwt_clock_skew_seconds=5,
        )

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def _token(self, *, sub: str = "service:api", scope: str = "ai.invoke", extra: dict | None = None) -> str:
        now = int(time.time())
        payload = {
            "iss": "speroflow-api",
            "aud": "speroflow-ai",
            "sub": sub,
            "iat": now,
            "exp": now + 60,
            "scope": scope,
            "user_id": "user-1",
        }
        if extra:
            payload.update(extra)
        return jwt.encode(payload, self._private_pem, algorithm="RS256")

    def test_rejects_when_bearer_missing(self) -> None:
        with self.assertRaises(HTTPException) as ctx:
            get_service_principal(credentials=None, settings=self._settings)
        self.assertEqual(401, ctx.exception.status_code)

    def test_rejects_when_jwt_disabled(self) -> None:
        disabled = self._settings.model_copy(update={"service_jwt_required": False})
        creds = HTTPAuthorizationCredentials(scheme="Bearer", credentials=self._token())
        with self.assertRaises(HTTPException) as ctx:
            get_service_principal(credentials=creds, settings=disabled)
        self.assertEqual(503, ctx.exception.status_code)

    def test_accepts_valid_service_token(self) -> None:
        creds = HTTPAuthorizationCredentials(scheme="Bearer", credentials=self._token())
        # Clear lru_cache on key loader between path changes is unnecessary here (fresh path).
        claims = get_service_principal(credentials=creds, settings=self._settings)
        self.assertEqual("service:api", claims["sub"])
        self.assertEqual("user-1", claims["user_id"])

    def test_rejects_wrong_principal(self) -> None:
        creds = HTTPAuthorizationCredentials(scheme="Bearer", credentials=self._token(sub="service:other"))
        with self.assertRaises(HTTPException) as ctx:
            get_service_principal(credentials=creds, settings=self._settings)
        self.assertEqual(403, ctx.exception.status_code)

    def test_require_scope(self) -> None:
        dependency = require_service_scope("ai.invoke")
        principal = {
            "sub": "service:api",
            "scope": "ai.invoke other",
        }
        self.assertEqual(principal, dependency(principal=principal))

        with self.assertRaises(HTTPException) as ctx:
            dependency(principal={"sub": "service:api", "scope": "other"})
        self.assertEqual(403, ctx.exception.status_code)


if __name__ == "__main__":
    unittest.main()
