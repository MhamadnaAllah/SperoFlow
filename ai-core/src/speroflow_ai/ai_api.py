"""Private FastAPI entrypoint for inference and GraphRAG reads.

This application deliberately does not expose browser CORS, direct ingestion,
or mock authentication. Caddy never routes requests here; ASP.NET is the only
caller and presents a short-lived service JWT for every feature route.
"""

from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI

from speroflow_ai.config import get_settings
from speroflow_ai.db.neo4j_client import close_driver, close_knowledge_driver, get_driver, init_driver
from speroflow_ai.internal_ai_routes import router as internal_router
from speroflow_ai.routers import cbt, query, roadmap


from speroflow_ai.logging_config import setup_logging

setup_logging("speroflow-ai-api")
logger = logging.getLogger("speroflow.ai_api")


@asynccontextmanager
async def lifespan(_: FastAPI):
    """Initialize only the read/query driver; ingestion belongs to ai-worker."""
    settings = get_settings()
    logger.info("Starting private SperoFlow AI API.")
    await init_driver(settings)
    try:
        yield
    finally:
        await close_knowledge_driver()
        await close_driver()
        logger.info("Private SperoFlow AI API stopped.")


settings = get_settings()
app = FastAPI(
    title="SperoFlow Private AI Service",
    version="3.0.0",
    docs_url="/docs" if settings.app_env == "development" else None,
    redoc_url=None,
    openapi_url="/openapi.json" if settings.app_env == "development" else None,
    lifespan=lifespan,
)

app.include_router(query.router)
app.include_router(roadmap.router)
app.include_router(cbt.router)
app.include_router(internal_router)


@app.get("/health/live", tags=["System"])
async def live() -> dict[str, str]:
    return {"status": "healthy", "service": "speroflow-ai-api"}


@app.get("/health/ready", tags=["System"])
async def ready() -> dict[str, str]:
    try:
        driver = await get_driver(settings)
        await asyncio.wait_for(driver.verify_connectivity(), timeout=5.0)
    except Exception as exc:
        logger.warning("AI readiness check failed: %s", exc)
        return {"status": "unready", "service": "speroflow-ai-api"}
    return {"status": "ready", "service": "speroflow-ai-api"}
