"""Tests for private AI chat provider selection."""

from __future__ import annotations

import sys
import types
import unittest
from unittest.mock import patch

from speroflow_ai.services.chat_model import create_chat_model


class _FakeBedrockChat:
    def __init__(self, **kwargs):
        self.kwargs = kwargs


class ChatModelTests(unittest.TestCase):
    def test_bedrock_provider_uses_converse_model_configuration(self) -> None:
        fake_module = types.SimpleNamespace(ChatBedrockConverse=_FakeBedrockChat)
        with patch.dict(sys.modules, {"langchain_aws": fake_module}):
            model = create_chat_model(
                provider="bedrock",
                model="amazon.nova-lite-v1:0",
                api_base="",
                api_key="",
                temperature=0.2,
                bedrock_region="us-east-1",
                max_tokens=512,
            )

        self.assertIsInstance(model, _FakeBedrockChat)
        self.assertEqual(model.kwargs["model"], "amazon.nova-lite-v1:0")
        self.assertEqual(model.kwargs["region_name"], "us-east-1")
        self.assertEqual(model.kwargs["max_tokens"], 512)

    def test_unknown_provider_fails_closed(self) -> None:
        with self.assertRaisesRegex(ValueError, "LLM_PROVIDER"):
            create_chat_model(
                provider="unsupported",
                model="model",
                api_base="",
                api_key="",
                temperature=0.0,
                bedrock_region="us-east-1",
            )

    def test_empty_model_fails_closed(self) -> None:
        with self.assertRaisesRegex(ValueError, "LLM_MODEL"):
            create_chat_model(
                provider="bedrock",
                model=" ",
                api_base="",
                api_key="",
                temperature=0.0,
                bedrock_region="us-east-1",
            )


if __name__ == "__main__":
    unittest.main()