"""Regression coverage for durable OCR waiting-message handling."""

from __future__ import annotations

import sys
import types
from unittest import IsolatedAsyncioTestCase
from unittest.mock import AsyncMock, patch

# The local AI-core test environment intentionally does not install Redis; the
# worker image does. Supply just enough type surface to import the coroutine.
try:
    import redis.asyncio  # noqa: F401
except ModuleNotFoundError:
    redis_module = types.ModuleType("redis")
    redis_asyncio = types.ModuleType("redis.asyncio")
    redis_exceptions = types.ModuleType("redis.exceptions")

    class Redis:  # pragma: no cover - import-only fallback
        pass

    class ResponseError(Exception):  # pragma: no cover - import-only fallback
        pass

    redis_asyncio.Redis = Redis
    redis_exceptions.ResponseError = ResponseError
    redis_module.asyncio = redis_asyncio
    sys.modules["redis"] = redis_module
    sys.modules["redis.asyncio"] = redis_asyncio
    sys.modules["redis.exceptions"] = redis_exceptions

from speroflow_ai.ai_worker import AiWorkerSettings, _process_dataset_message
from speroflow_ai.dataset_worker import DatasetJobOutcome


class _Response:
    def __init__(self, payload: dict, status_code: int = 200) -> None:
        self._payload = payload
        self.status_code = status_code

    def json(self) -> dict:
        return self._payload

    def raise_for_status(self) -> None:
        return None


class _Client:
    def __init__(self) -> None:
        self.get_calls: list[tuple] = []
        self.post_calls: list[tuple] = []

    async def get(self, *args, **kwargs):
        self.get_calls.append((args, kwargs))
        return _Response({"jobId": "job-1"})

    async def post(self, *args, **kwargs):
        self.post_calls.append((args, kwargs))
        return _Response({})


class OcrRecoveryMessageTests(IsolatedAsyncioTestCase):
    async def test_waiting_ocr_is_acked_for_durable_reschedule(self) -> None:
        redis = type("Redis", (), {"xack": AsyncMock()})()
        client = _Client()
        settings = AiWorkerSettings()

        with patch(
            "speroflow_ai.ai_worker.process_dataset_job",
            return_value=DatasetJobOutcome("waitingForOcr", "{}", textract_job_id="textract-1"),
        ):
            await _process_dataset_message(
                redis,
                client,
                settings,
                "1-0",
                {"job_id": "job-1", "callback_token": "signed", "type": "dataset.ingestion.requested"},
            )

        redis.xack.assert_awaited_once_with(settings.ai_jobs_stream, settings.ai_jobs_group, "1-0")
        self.assertEqual(client.post_calls[0][1]["json"]["state"], "waitingForOcr")


if __name__ == "__main__":
    import unittest

    unittest.main()
