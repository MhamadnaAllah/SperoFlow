"""Redis Streams runtime for isolated knowledge ingestion."""

from __future__ import annotations

import asyncio
import logging
import os
from contextlib import suppress
from uuid import uuid4
from collections.abc import Mapping
from typing import Any

import httpx
from redis.asyncio import Redis
from redis.exceptions import ResponseError

from knowledge_worker.config import Settings, get_settings
from knowledge_worker.dataset_worker import process_dataset_job

logger = logging.getLogger("knowledge.worker")


async def ensure_group(redis: Redis, settings: Settings) -> None:
    try:
        await redis.xgroup_create(settings.knowledge_jobs_stream, settings.knowledge_jobs_group, id="0-0", mkstream=True)
    except ResponseError as exc:
        if "BUSYGROUP" not in str(exc):
            raise


def _decode_fields(fields: Mapping[bytes, bytes]) -> dict[str, str]:
    return {
        key.decode("utf-8") if isinstance(key, bytes) else str(key): value.decode("utf-8") if isinstance(value, bytes) else str(value)
        for key, value in fields.items()
    }


async def _heartbeat(
    client: httpx.AsyncClient,
    settings: Settings,
    job_id: str,
    token_ref: dict[str, str],
    lease_lost: asyncio.Event,
) -> None:
    while True:
        try:
            await asyncio.sleep(settings.knowledge_lease_heartbeat_seconds)
            response = await client.post(
                f"/internal/v1/knowledge/jobs/{job_id}/heartbeat",
                headers={"Authorization": "Bearer " + token_ref["value"]},
            )
            if response.status_code in {401, 404, 409}:
                lease_lost.set()
                logger.error("Knowledge ingestion lease for job %s is no longer active.", job_id)
                return
            response.raise_for_status()
            execution_token = response.json().get("executionToken")
            if not isinstance(execution_token, str) or not execution_token:
                lease_lost.set()
                logger.error("Knowledge API returned no renewed execution token for job %s.", job_id)
                return
            token_ref["value"] = execution_token
        except asyncio.CancelledError:
            raise
        except Exception:
            # A transient renewal failure is retried before the current lease expires.
            logger.exception("Unable to renew the knowledge ingestion lease for job %s.", job_id)


async def _process_delivery(
    redis: Redis,
    client: httpx.AsyncClient,
    settings: Settings,
    message_id: str | bytes,
    fields: Mapping[bytes, bytes],
) -> None:
    delivery_id = message_id.decode("utf-8") if isinstance(message_id, bytes) else str(message_id)
    payload = _decode_fields(fields)
    if payload.get("type") != "knowledge.ingestion.requested":
        logger.warning("Acknowledging unsupported knowledge job message type: %s", payload.get("type"))
        await redis.xack(settings.knowledge_jobs_stream, settings.knowledge_jobs_group, delivery_id)
        return

    job_id = payload.get("job_id")
    delivery_token = payload.get("delivery_token")
    if not job_id or not delivery_token:
        logger.error("Acknowledging malformed knowledge job delivery %s.", delivery_id)
        await redis.xack(settings.knowledge_jobs_stream, settings.knowledge_jobs_group, delivery_id)
        return

    headers = {
        "Authorization": "Bearer " + delivery_token,
        "X-Knowledge-Worker-Lease-Id": str(uuid4()),
    }
    heartbeat: asyncio.Task[None] | None = None
    try:
        response = await client.get(f"/internal/v1/knowledge/jobs/{job_id}", headers=headers)
        if response.status_code in {404, 409}:
            await redis.xack(settings.knowledge_jobs_stream, settings.knowledge_jobs_group, delivery_id)
            return
        response.raise_for_status()
        job = response.json()
        execution_token = job.get("executionToken")
        if not isinstance(execution_token, str) or not execution_token:
            raise RuntimeError("Knowledge API did not issue an execution lease token.")

        token_ref = {"value": execution_token}
        lease_lost = asyncio.Event()
        heartbeat = asyncio.create_task(_heartbeat(client, settings, job_id, token_ref, lease_lost))
        outcome = await asyncio.to_thread(process_dataset_job, get_settings(), job)
        if lease_lost.is_set():
            raise RuntimeError("Knowledge ingestion lease was lost before completion.")

        completion = await client.post(
            f"/internal/v1/knowledge/jobs/{job_id}/complete",
            headers={"Authorization": "Bearer " + token_ref["value"]},
            json=outcome.completion_payload(),
        )
        completion.raise_for_status()
        await redis.xack(settings.knowledge_jobs_stream, settings.knowledge_jobs_group, delivery_id)
        logger.info("Knowledge ingestion job %s completed with state %s.", job_id, outcome.state)
    except Exception:
        # The delivery remains pending. The outbox worker requeues it only after its lease expires.
        logger.exception("Knowledge ingestion job %s failed; leaving its delivery pending for recovery.", job_id)
    finally:
        if heartbeat is not None:
            heartbeat.cancel()
            with suppress(asyncio.CancelledError):
                await heartbeat


async def _reclaim_pending(
    redis: Redis,
    client: httpx.AsyncClient,
    settings: Settings,
    consumer: str,
) -> None:
    claimed = await redis.xautoclaim(
        settings.knowledge_jobs_stream,
        settings.knowledge_jobs_group,
        consumer,
        min_idle_time=settings.knowledge_reclaim_idle_ms,
        start_id="0-0",
        count=10,
    )
    messages = claimed[1] if len(claimed) > 1 else []
    for message_id, fields in messages:
        await _process_delivery(redis, client, settings, message_id, fields)


async def run() -> None:
    settings = get_settings()
    consumer = settings.knowledge_worker_consumer or os.environ.get("HOSTNAME", "knowledge-worker")
    redis = Redis.from_url(settings.knowledge_redis_url, decode_responses=False)
    await ensure_group(redis, settings)
    timeout = httpx.Timeout(connect=5.0, read=180.0, write=30.0, pool=5.0)
    async with httpx.AsyncClient(base_url=settings.knowledge_api_url, timeout=timeout) as client:
        logger.info("Knowledge worker ready: stream=%s group=%s", settings.knowledge_jobs_stream, settings.knowledge_jobs_group)
        while True:
            await _reclaim_pending(redis, client, settings, consumer)
            messages = await redis.xreadgroup(
                settings.knowledge_jobs_group,
                consumer,
                {settings.knowledge_jobs_stream: ">"},
                count=1,
                block=5_000,
            )
            for _, deliveries in messages:
                for message_id, fields in deliveries:
                    await _process_delivery(redis, client, settings, message_id, fields)


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
    asyncio.run(run())