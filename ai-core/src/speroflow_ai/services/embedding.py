"""
Embedding service — supports Cohere Embed v4 (Bedrock) and BAAI/bge-m3 (Local).

Supports Bedrock Cohere embedding models (e.g. cohere.embed-v4:0) and falls back
to local HuggingFace embeddings when running offline.
"""

from __future__ import annotations

import asyncio
import json
import logging
from typing import Any, Optional

from speroflow_ai.config import get_settings

logger = logging.getLogger("speroflow.services.embedding")

_hf_embeddings: Optional[Any] = None


def _get_hf_embedder(model_name: str = "BAAI/bge-m3") -> Any:
    """Lazily initialize and cache the HuggingFace local embedding model."""
    global _hf_embeddings
    if _hf_embeddings is None:
        try:
            from langchain_huggingface import HuggingFaceEmbeddings
        except ImportError as exc:
            raise RuntimeError(
                "langchain-huggingface is required for local embeddings. "
                "Install the ai-api requirements."
            ) from exc

        logger.info("Loading local embedding model: %s", model_name)
        _hf_embeddings = HuggingFaceEmbeddings(
            model_name=model_name,
            model_kwargs={"device": "cpu"},
            encode_kwargs={"normalize_embeddings": True},
        )
        logger.info("Local embedding model loaded.")
    return _hf_embeddings


async def _invoke_bedrock_cohere_embedding(
    texts: list[str],
    model_id: str = "cohere.embed-v4:0",
) -> list[list[float]]:
    """Invoke Bedrock runtime for Cohere Embed models."""
    from speroflow_ai.services.bedrock_client import get_bedrock_client

    settings = get_settings()
    client = get_bedrock_client(settings)

    body = {
        "texts": texts,
        "input_type": "search_document",
        "truncate": "END",
    }

    def _invoke() -> dict:
        response = client.invoke_model(
            body=json.dumps(body),
            modelId=model_id,
            accept="application/json",
            contentType="application/json",
        )
        return json.loads(response["body"].read())

    res_data = await asyncio.to_thread(_invoke)
    embeddings = res_data.get("embeddings", [])
    if isinstance(embeddings, dict) and "float" in embeddings:
        embeddings = embeddings["float"]
    return embeddings


async def generate_embedding(
    text: str,
    model_name: str = "cohere.embed-v4:0",
) -> list[float]:
    """Generate a single embedding vector for the given text."""
    if "cohere" in model_name.lower():
        try:
            vectors = await _invoke_bedrock_cohere_embedding([text], model_id=model_name)
            if vectors and len(vectors) > 0:
                return vectors[0]
        except Exception as exc:
            logger.warning("Bedrock Cohere Embedding unavailable, falling back to local bge-m3: %s", exc)

    embedder = _get_hf_embedder("BAAI/bge-m3")
    embedding = await asyncio.to_thread(embedder.embed_query, text)
    logger.debug("Embedding generated: %d dimensions.", len(embedding))
    return embedding


def generate_embeddings_batch(
    texts: list[str],
    model_name: str = "cohere.embed-v4:0",
) -> list[list[float]]:
    """Generate embeddings for a batch of texts."""
    if "cohere" in model_name.lower():
        try:
            loop = asyncio.get_event_loop()
            if loop.is_running():
                # Synchronous fallback if called inside async loop
                embedder = _get_hf_embedder("BAAI/bge-m3")
                return embedder.embed_documents(texts)
        except Exception:
            pass

    embedder = _get_hf_embedder("BAAI/bge-m3")
    embeddings = embedder.embed_documents(texts)
    logger.debug("Batch embeddings generated: %d vectors.", len(embeddings))
    return embeddings
