"""Production runner that retries abandoned Redis Streams ingestion messages."""

from __future__ import annotations

import asyncio
import logging

import httpx
from redis.asyncio import Redis

from speroflow_ai.ai_worker import AiWorkerSettings, ensure_group, process_message


logger = logging.getLogger("speroflow.ai_worker.runtime")


async def claim_abandoned_messages(
    redis: Redis,
    client: httpx.AsyncClient,
    settings: AiWorkerSettings,
) -> int:
    """Claim messages left pending by a terminated worker after a short lease."""
    start_id = "0-0"
    claimed_count = 0
    while True:
        result = await redis.xautoclaim(
            settings.ai_jobs_stream,
            settings.ai_jobs_group,
            settings.consumer_name,
            min_idle_time=60_000,
            start_id=start_id,
            count=10,
        )
        next_start, messages, _ = result
        for message_id, fields in messages:
            await process_message(redis, client, settings, message_id, fields)
            claimed_count += 1
        if not messages or next_start == b"0-0" or next_start == "0-0":
            return claimed_count
        start_id = next_start.decode() if isinstance(next_start, bytes) else next_start


async def run() -> None:
    settings = AiWorkerSettings()
    redis = Redis.from_url(settings.redis_url, decode_responses=False)
    await ensure_group(redis, settings)
    timeout = httpx.Timeout(connect=5.0, read=120.0, write=30.0, pool=5.0)
    async with httpx.AsyncClient(base_url=settings.primary_api_url, timeout=timeout) as client:
        logger.info("AI worker ready: stream=%s group=%s", settings.ai_jobs_stream, settings.ai_jobs_group)
        while True:
            claimed = await claim_abandoned_messages(redis, client, settings)
            if claimed:
                logger.info("Claimed %s abandoned AI jobs.", claimed)
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
