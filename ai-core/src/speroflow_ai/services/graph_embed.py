"""
Embedding enrichment service — generates BAAI/bge-m3 vectors for Neo4j nodes.

Key design decisions:
  - IDEMPOTENT: Only processes nodes where embedding IS NULL.
  - BATCHED: Configurable batch size to avoid OOM.
  - ATOMIC WRITES: UNWIND writes an entire batch in one transaction.
  - LOCAL: No API key needed — bge-m3 runs on CPU or CUDA.
"""

from __future__ import annotations

import logging
import time
from typing import Optional

from neo4j import Driver

logger = logging.getLogger("speroflow.services.graph_embed")

EMBEDDING_MODEL = "BAAI/bge-m3"
EMBEDDING_DIMENSIONS = 1024
DEFAULT_BATCH_SIZE = 100
DEFAULT_DEVICE = "cpu"
DEFAULT_TEXT_PROPERTY = "content"
DEFAULT_EMBEDDING_PROPERTY = "embedding"


class EmbeddingPipeline:
    """
    Enriches Neo4j nodes with bge-m3 vector embeddings generated locally.

    Usage:
        pipeline = EmbeddingPipeline(driver, node_label="Topic")
        pipeline.run()
    """

    def __init__(
        self,
        driver: Driver,
        node_label: str = "Topic",
        text_property: str = DEFAULT_TEXT_PROPERTY,
        embedding_property: str = DEFAULT_EMBEDDING_PROPERTY,
        batch_size: int = DEFAULT_BATCH_SIZE,
        device: str = DEFAULT_DEVICE,
    ) -> None:
        self._driver = driver
        self._node_label = node_label
        self._text_property = text_property
        self._embedding_property = embedding_property
        self._batch_size = batch_size
        self._device = device
        self._embedder: Optional[object] = None
        self._total_embedded = 0
        self._total_skipped = 0

    def run(self) -> dict[str, int]:
        """Execute the full embedding enrichment pipeline.

        Returns:
            Dict with keys: embedded, skipped, total.
        """
        start = time.time()
        self._init_embedder()

        nodes = self._fetch_unembedded_nodes()
        if not nodes:
            logger.info(
                "All %s nodes already have embeddings. Nothing to do!",
                self._node_label,
            )
            return {"embedded": 0, "skipped": 0, "total": 0}

        logger.info(
            "Found %d un-embedded %s nodes. Processing in batches of %d...",
            len(nodes), self._node_label, self._batch_size,
        )

        total_batches = (len(nodes) + self._batch_size - 1) // self._batch_size
        for batch_idx in range(0, len(nodes), self._batch_size):
            batch_num = (batch_idx // self._batch_size) + 1
            batch = nodes[batch_idx : batch_idx + self._batch_size]

            texts = [
                (node["text"] or "")[:25000] or "No description available."
                for node in batch
            ]

            try:
                embeddings = self._embedder.embed_documents(texts)
            except Exception as exc:
                logger.error("Embedding failed for batch %d: %s", batch_num, exc)
                self._total_skipped += len(batch)
                continue

            update_data = [
                {"id": node["element_id"], "embedding": emb}
                for node, emb in zip(batch, embeddings)
            ]
            self._write_embeddings_batch(update_data)
            self._total_embedded += len(batch)
            logger.info(
                "Batch %d/%d complete. Total embedded: %d",
                batch_num, total_batches, self._total_embedded,
            )

        self._create_vector_index()

        elapsed = time.time() - start
        logger.info(
            "Embedding complete: %d embedded, %d skipped in %.1fs",
            self._total_embedded, self._total_skipped, elapsed,
        )
        return {
            "embedded": self._total_embedded,
            "skipped": self._total_skipped,
            "total": self._total_embedded + self._total_skipped,
        }

    def _init_embedder(self) -> None:
        from langchain_huggingface import HuggingFaceEmbeddings
        logger.info(
            "Loading embedding model '%s' on device '%s'...",
            EMBEDDING_MODEL, self._device,
        )
        self._embedder = HuggingFaceEmbeddings(
            model_name=EMBEDDING_MODEL,
            model_kwargs={"device": self._device},
            encode_kwargs={"normalize_embeddings": True},
        )
        logger.info("Model loaded.")

    def _fetch_unembedded_nodes(self) -> list[dict]:
        cypher = f"""
            MATCH (n:{self._node_label})
            WHERE n.{self._text_property} IS NOT NULL
              AND n.{self._embedding_property} IS NULL
            RETURN elementId(n) AS element_id,
                   n.{self._text_property} AS text
        """
        with self._driver.session() as session:
            result = session.run(cypher)
            return [{"element_id": r["element_id"], "text": r["text"]} for r in result]

    def _write_embeddings_batch(self, data: list[dict]) -> None:
        cypher = """
            UNWIND $data AS row
            MATCH (n)
            WHERE elementId(n) = row.id
            SET n.embedding = row.embedding
        """
        with self._driver.session() as session:
            session.run(cypher, data=data).consume()

    def _create_vector_index(self) -> None:
        index_name = f"{self._node_label.lower()}_{self._embedding_property}_index"
        cypher = f"""
            CREATE VECTOR INDEX {index_name} IF NOT EXISTS
            FOR (n:{self._node_label})
            ON (n.{self._embedding_property})
            OPTIONS {{
                indexConfig: {{
                    `vector.dimensions`: {EMBEDDING_DIMENSIONS},
                    `vector.similarity_function`: 'cosine'
                }}
            }}
        """
        try:
            with self._driver.session() as session:
                session.run(cypher)
            logger.info(
                "Vector index '%s' ready (%dd, cosine).",
                index_name, EMBEDDING_DIMENSIONS,
            )
        except Exception as exc:
            logger.warning("Could not create vector index: %s", exc)
