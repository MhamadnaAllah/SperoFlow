"""Async Neo4j driver singletons for the app graph and isolated knowledge graph."""

from __future__ import annotations

import logging
from typing import Optional

from neo4j import AsyncDriver, AsyncGraphDatabase

from speroflow_ai.config import Settings

logger = logging.getLogger("speroflow.db.neo4j")

_driver: Optional[AsyncDriver] = None
_knowledge_driver: Optional[AsyncDriver] = None


async def init_driver(settings: Settings) -> AsyncDriver:
    """Initialize the primary application graph read driver."""
    global _driver
    if _driver is not None:
        return _driver
    logger.info("Connecting to primary Neo4j graph.")
    _driver = AsyncGraphDatabase.driver(settings.neo4j_uri, auth=(settings.neo4j_user, settings.neo4j_password))
    await _driver.verify_connectivity()
    return _driver


async def get_driver(settings: Settings) -> AsyncDriver:
    if _driver is None:
        return await init_driver(settings)
    return _driver


async def init_knowledge_driver(settings: Settings) -> AsyncDriver:
    """Initialize the dedicated read-only knowledge graph driver lazily."""
    global _knowledge_driver
    if _knowledge_driver is not None:
        return _knowledge_driver
    logger.info("Connecting to isolated knowledge Neo4j graph with the read-only identity.")
    _knowledge_driver = AsyncGraphDatabase.driver(
        settings.knowledge_neo4j_uri,
        auth=(settings.knowledge_neo4j_user, settings.knowledge_neo4j_password),
    )
    await _knowledge_driver.verify_connectivity()
    return _knowledge_driver


async def get_knowledge_driver(settings: Settings) -> AsyncDriver:
    if _knowledge_driver is None:
        return await init_knowledge_driver(settings)
    return _knowledge_driver


async def close_driver() -> None:
    global _driver
    if _driver is not None:
        await _driver.close()
        _driver = None
        logger.info("Primary Neo4j driver closed.")


async def close_knowledge_driver() -> None:
    global _knowledge_driver
    if _knowledge_driver is not None:
        await _knowledge_driver.close()
        _knowledge_driver = None
        logger.info("Knowledge Neo4j driver closed.")