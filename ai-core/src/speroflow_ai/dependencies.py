"""FastAPI dependencies shared by private AI routes."""

from __future__ import annotations

from fastapi import Depends

from speroflow_ai.config import Settings, get_settings


def get_app_settings() -> Settings:
    """Inject the validated Settings singleton into a route."""
    return get_settings()


async def get_neo4j(settings: Settings = Depends(get_app_settings)):
    """Retrieve the async Neo4j driver singleton."""
    from speroflow_ai.db.neo4j_client import get_driver

    return await get_driver(settings)