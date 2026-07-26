"""Cross-language contract checks for short-lived knowledge access grants."""

from __future__ import annotations

import json
import tempfile
import time
import unittest
from pathlib import Path
from types import SimpleNamespace
from uuid import uuid4

import jwt
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa

from speroflow_ai.services.knowledge_grants import validate_knowledge_access_grant


class KnowledgeGrantValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        key = rsa.generate_private_key(public_exponent=65_537, key_size=2_048)
        self._private_key = key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        )
        public_key = key.public_key().public_bytes(
            serialization.Encoding.PEM,
            serialization.PublicFormat.SubjectPublicKeyInfo,
        )
        self._temporary_directory = tempfile.TemporaryDirectory()
        self._public_key_path = Path(self._temporary_directory.name) / "knowledge-grant-public.pem"
        self._public_key_path.write_bytes(public_key)
        self._settings = SimpleNamespace(
            knowledge_grant_public_key_path=str(self._public_key_path),
            knowledge_grant_audience="speroflow-ai",
            knowledge_grant_issuer="speroflow-knowledge-api",
            knowledge_grant_clock_skew_seconds=0,
        )

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_accepts_the_bounded_asymmetric_grant_contract(self) -> None:
        dataset_id = str(uuid4())
        token = self._issue_token(
            90,
            [json.dumps(
                {
                    "dataset_id": dataset_id,
                    "release_key": "dataset-release-1",
                    "owner_subject": "owner-subject",
                    "visibility": "published",
                },
                sort_keys=True,
            )],
        )

        grant = validate_knowledge_access_grant(token, self._settings)

        self.assertEqual("grant-1", grant.grant_id)
        self.assertEqual("owner-subject", grant.subject)
        self.assertEqual(dataset_id, grant.datasets[0].dataset_id)
        self.assertEqual("dataset-release-1", grant.datasets[0].release_key)

    def test_rejects_a_grant_whose_lifetime_exceeds_the_platform_bound(self) -> None:
        token = self._issue_token(301, [self._dataset_grant()])

        with self.assertRaisesRegex(ValueError, "required claims"):
            validate_knowledge_access_grant(token, self._settings)

    def _issue_token(self, lifetime_seconds: int, grants: list[str]) -> str:
        now = int(time.time())
        return jwt.encode(
            {
                "iss": "speroflow-knowledge-api",
                "aud": "speroflow-ai",
                "sub": "owner-subject",
                "iat": now,
                "exp": now + lifetime_seconds,
                "jti": "grant-1",
                "scope": "knowledge.query",
                "dataset_grant": grants,
            },
            self._private_key,
            algorithm="RS256",
        )

    @staticmethod
    def _dataset_grant() -> str:
        return json.dumps(
            {
                "dataset_id": str(uuid4()),
                "release_key": "dataset-release-1",
                "owner_subject": "owner-subject",
                "visibility": "private",
            },
            sort_keys=True,
        )


if __name__ == "__main__":
    unittest.main()