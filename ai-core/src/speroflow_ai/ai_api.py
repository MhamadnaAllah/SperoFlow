"""Private FastAPI entrypoint for inference and GraphRAG reads.

This application deliberately does not expose browser CORS, direct ingestion,
or mock authentication. Caddy never routes requests here; ASP.NET is the only
caller and presents a short-lived service JWT for every feature route.
"""

from __future__ import annotations

import asyncio
import logging
import time
import uuid
from contextlib import asynccontextmanager
from datetime import datetime, timezone

from fastapi import FastAPI, Request, Response
from fastapi.responses import JSONResponse, PlainTextResponse
from starlette.middleware.base import BaseHTTPMiddleware

from speroflow_ai.config import get_settings
from speroflow_ai.db.neo4j_client import close_driver, close_knowledge_driver, get_driver, init_driver
from speroflow_ai.internal_ai_routes import router as internal_router
from speroflow_ai.logging_config import setup_logging
from speroflow_ai.request_metrics import record as record_http_metric
from speroflow_ai.request_metrics import render_prometheus
from speroflow_ai.routers import cbt, query, roadmap

setup_logging("speroflow-ai-api")
logger = logging.getLogger("speroflow.ai_api")


class RequestObservabilityMiddleware(BaseHTTPMiddleware):
    """Correlation id + timing logs for private AI calls (skips health probes)."""

    async def dispatch(self, request: Request, call_next):
        request_id = (
            request.headers.get("x-request-id")
            or request.headers.get("x-correlation-id")
            or uuid.uuid4().hex
        )
        path = request.url.path
        is_probe = path.startswith("/health/") or path.startswith("/metrics")
        started = time.perf_counter()
        response: Response
        try:
            response = await call_next(request)
        except Exception:
            elapsed_ms = (time.perf_counter() - started) * 1000.0
            if not is_probe:
                logger.exception(
                    "HTTP %s %s failed in %.1fms request_id=%s",
                    request.method,
                    path,
                    elapsed_ms,
                    request_id,
                )
            raise
        response.headers["X-Request-Id"] = request_id
        if not is_probe:
            elapsed_ms = (time.perf_counter() - started) * 1000.0
            record_http_metric(request.method, int(response.status_code), elapsed_ms)
            logger.info(
                "HTTP %s %s => %s in %.1fms request_id=%s",
                request.method,
                path,
                response.status_code,
                elapsed_ms,
                request_id,
            )
        return response


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
app.add_middleware(RequestObservabilityMiddleware)

app.include_router(query.router)
app.include_router(roadmap.router)
app.include_router(cbt.router)
app.include_router(internal_router)


@app.get("/health/live", tags=["System"])
async def live() -> dict[str, str]:
    return {
        "status": "healthy",
        "service": "speroflow-ai-api",
        "utc": datetime.now(tz=timezone.utc).isoformat(),
    }


@app.get("/health/ready", tags=["System"])
async def ready() -> JSONResponse:
    """Return HTTP 503 when Neo4j is unreachable so orchestrators mark the task unhealthy."""
    try:
        driver = await get_driver(settings)
        await asyncio.wait_for(driver.verify_connectivity(), timeout=5.0)
    except Exception as exc:
        logger.warning("AI readiness check failed: %s", exc)
        return JSONResponse(
            status_code=503,
            content={
                "status": "unready",
                "service": "speroflow-ai-api",
                "checks": {"neo4j": "down"},
            },
        )
    return JSONResponse(
        status_code=200,
        content={
            "status": "ready",
            "service": "speroflow-ai-api",
            "checks": {"neo4j": "up"},
        },
    )


@app.get("/metrics", tags=["System"])
async def metrics() -> PlainTextResponse:
    """Prometheus text exposition for private-network scrapers only (not on Caddy edge)."""
    return PlainTextResponse(
        render_prometheus("speroflow-ai-api"),
        media_type="text/plain; version=0.0.4; charset=utf-8",
    )