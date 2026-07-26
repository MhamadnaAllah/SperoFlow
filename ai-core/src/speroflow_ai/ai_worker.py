"""Durable Redis Streams worker for legacy roadmap and uploaded dataset ingestion."""

from __future__ import annotations

import asyncio
import logging
import os
from pydantic_settings import BaseSettings, SettingsConfigDict

import httpx
from redis.asyncio import Redis
from redis.exceptions import ResponseError

from speroflow_ai.config import get_settings
from speroflow_ai.dataset_worker import process_dataset_job
from speroflow_ai.worker_ingestion import ingest_content


from speroflow_ai.logging_config import setup_logging

setup_logging("speroflow-ai-worker")
logger = logging.getLogger("speroflow.ai_worker")


class AiWorkerSettings(BaseSettings):
    model_config = SettingsConfigDict(extra="ignore")

    redis_url: str = "redis://redis:6379/0"
    ai_jobs_stream: str = "speroflow:ai:jobs"
    ai_jobs_group: str = "speroflow-ai-workers"
    ai_worker_consumer: str = ""
    primary_api_url: str = "http://api:8080"

    @property
    def consumer_name(self) -> str:
        return self.ai_worker_consumer or os.environ.get("HOSTNAME", "ai-worker")


async def ensure_group(redis: Redis, settings: AiWorkerSettings) -> None:
    try:
        await redis.xgroup_create(settings.ai_jobs_stream, settings.ai_jobs_group, id="0-0", mkstream=True)
    except ResponseError as exc:
        if "BUSYGROUP" not in str(exc):
            raise


async def process_message(
    redis: Redis,
    client: httpx.AsyncClient,
    settings: AiWorkerSettings,
    message_id: str,
    fields: dict[bytes, bytes],
) -> None:
    decoded = {key.decode(): value.decode() for key, value in fields.items()}
    message_type = decoded.get("type")
    if message_type == "document.ingestion.requested":
        await _process_document_message(redis, client, settings, message_id, decoded)
        return
    if message_type == "dataset.ingestion.requested":
        await _process_dataset_message(redis, client, settings, message_id, decoded)
        return

    logger.warning("Acknowledging unknown AI worker message type '%s'.", message_type)
    await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)


async def _process_document_message(
    redis: Redis,
    client: httpx.AsyncClient,
    settings: AiWorkerSettings,
    message_id: str,
    decoded: dict[str, str],
) -> None:
    job_id = decoded.get("job_id")
    callback_token = decoded.get("callback_token")
    if not job_id or not callback_token:
        logger.error("Discarding malformed document ingestion message %s.", message_id)
        await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)
        return

    headers = {"Authorization": "Bearer " + callback_token}
    try:
        response = await client.get("/internal/v1/jobs/" + job_id, headers=headers)
        if response.status_code == 404:
            logger.warning("Document ingestion job %s no longer exists; acknowledging message.", job_id)
            await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)
            return
        response.raise_for_status()
        job = response.json()
        stats = await asyncio.to_thread(
            ingest_content,
            get_settings(),
            job["roadmapName"],
            job["sourceType"],
            job["content"],
        )
        completion = await client.post(
            "/internal/v1/jobs/" + job_id + "/complete",
            headers=headers,
            json={
                "succeeded": True,
                "nodesCreated": stats["nodes_created"],
                "edgesCreated": stats["edges_created"],
                "vectorsEmbedded": stats["vectors_embedded"],
            },
        )
        completion.raise_for_status()
        await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)
        logger.info("Document ingestion job %s completed.", job_id)
    except Exception:
        logger.exception("Document ingestion job %s failed; it remains pending for retry.", job_id)


async def _process_dataset_message(
    redis: Redis,
    client: httpx.AsyncClient,
    settings: AiWorkerSettings,
    message_id: str,
    decoded: dict[str, str],
) -> None:
    job_id = decoded.get("job_id")
    callback_token = decoded.get("callback_token")
    if not job_id or not callback_token:
        logger.error("Discarding malformed dataset ingestion message %s.", message_id)
        await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)
        return

    headers = {"Authorization": "Bearer " + callback_token}
    try:
        response = await client.get("/internal/v1/dataset-jobs/" + job_id, headers=headers)
        if response.status_code == 404:
            logger.warning("Dataset ingestion job %s no longer exists; acknowledging message.", job_id)
            await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)
            return
        if response.status_code == 409:
            logger.info("Dataset ingestion job %s is already complete; acknowledging message.", job_id)
            await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)
            return
        response.raise_for_status()
        outcome = await asyncio.to_thread(process_dataset_job, get_settings(), response.json())
        completion = await client.post(
            "/internal/v1/dataset-jobs/" + job_id + "/complete",
            headers=headers,
            json=outcome.completion_payload(),
        )
        completion.raise_for_status()
        if outcome.state == "waitingForOcr":
            # The .NET worker requeues waiting jobs with a fresh scoped token. Ack this
            # delivery so Redis's `>` consumer loop cannot strand it in pending state.
            await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)
            logger.info("Dataset ingestion job %s is waiting for Textract; durable recovery has been scheduled.", job_id)
            return
        await redis.xack(settings.ai_jobs_stream, settings.ai_jobs_group, message_id)
        logger.info("Dataset ingestion job %s completed with state %s.", job_id, outcome.state)
    except Exception:
        logger.exception("Dataset ingestion job %s failed; it remains pending for retry.", job_id)


async def run() -> None:
    settings = AiWorkerSettings()
    redis = Redis.from_url(settings.redis_url, decode_responses=False)
    await ensure_group(redis, settings)
    timeout = httpx.Timeout(connect=5.0, read=120.0, write=30.0, pool=5.0)
    async with httpx.AsyncClient(base_url=settings.primary_api_url, timeout=timeout) as client:
        logger.info("AI worker ready: stream=%s group=%s", settings.ai_jobs_stream, settings.ai_jobs_group)
        while True:
            messages = await redis.xreadgroup(
                settings.ai_jobs_group,
                settings.consumer_name,
                {settings.ai_jobs_stream: ">"},
                count=1,
                block=5_000,
            )
            for _, stream_messages in messages:
                for message_id, fields in stream_messages:
                    await process_message(redis, client, settings, message_id, fields)


if __name__ == "__main__":
    asyncio.run(run())
