"""
Embedding service — generates vector embeddings using BAAI/bge-m3.

The model runs locally on CPU (no API key required). It is cached in
memory after the first load to avoid reloading on every request.
"""

from __future__ import annotations

import asyncio
import logging
from typing import Optional

from langchain_huggingface import HuggingFaceEmbeddings

logger = logging.getLogger("knowledge.services.embedding")

_hf_embeddings: Optional[HuggingFaceEmbeddings] = None


def _get_embedder(model_name: str = "BAAI/bge-m3") -> HuggingFaceEmbeddings:
    """Lazily initialize and cache the embedding model."""
    global _hf_embeddings
    if _hf_embeddings is None:
        logger.info("Loading local embedding model: %s", model_name)
        _hf_embeddings = HuggingFaceEmbeddings(
            model_name=model_name,
            model_kwargs={"device": "cpu"},
            encode_kwargs={"normalize_embeddings": True},
        )
        logger.info("Embedding model loaded.")
    return _hf_embeddings


async def generate_embedding(
    text: str,
    model_name: str = "BAAI/bge-m3",
) -> list[float]:
    """Generate a single embedding vector for the given text.

    Args:
        text: Raw text to embed.
        model_name: HuggingFace model name (default: BAAI/bge-m3, 1024-dim).

    Returns:
        A list of floats representing the embedding vector.
    """
    embedder = _get_embedder(model_name)
    embedding = await asyncio.to_thread(embedder.embed_query, text)
    logger.debug("Embedding generated: %d dimensions.", len(embedding))
    return embedding


def generate_embeddings_batch(
    texts: list[str],
    model_name: str = "BAAI/bge-m3",
) -> list[list[float]]:
    """Generate embeddings for a batch of texts.

    Args:
        texts: List of text strings to embed.
        model_name: HuggingFace model name.

    Returns:
        List of embedding vectors (same order as input).
    """
    embedder = _get_embedder(model_name)
    embeddings = embedder.embed_documents(texts)
    logger.debug("Batch embeddings generated: %d vectors.", len(embeddings))
    return embeddings
