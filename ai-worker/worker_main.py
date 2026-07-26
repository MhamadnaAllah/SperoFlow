"""Durable AI ingestion worker entrypoint."""

from __future__ import annotations

import asyncio

from speroflow_ai.ai_worker_runtime import run


if __name__ == "__main__":
    asyncio.run(run())
