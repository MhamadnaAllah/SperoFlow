"""Bounded-memory execution path for worker-only dataset graph ingestion.

The profiling helpers deliberately yield CSV and JSON records. This module keeps
that property through embeddings, semantic extraction, and Neo4j writes instead
of turning a 100 MB source into a single in-memory list. It is intentionally an
extension of the provenance graph contract, not a browser-facing graph writer.
"""

from __future__ import annotations

from itertools import chain
from typing import Any, Iterable, Iterator

from knowledge_worker.dataset_ingestion import (
    ContentUnit,
    DatasetGraphIngester,
    DatasetGraphRecord,
    DatasetIngestStats,
    DatasetIngestionError,
    SourceFileGraphRecord,
    _semantic_graph_rows,
    extract_semantics_safely,
)


_MAX_REPORTED_WARNINGS = 50


class StreamingDatasetGraphIngester(DatasetGraphIngester):
    """Idempotently ingest an iterable in small, recoverable batches.

    ``run_marker`` is normally the durable ingestion job ID. Content units are
    marked with it as they are upserted; only a fully completed run inactivates
    earlier units from the same source. A failed run therefore never performs a
    destructive cleanup and can be safely resumed with the same job ID.
    """

    def ingest_stream(
        self,
        dataset: DatasetGraphRecord,
        source: SourceFileGraphRecord,
        units: Iterable[ContentUnit],
        *,
        extractor: Any | None,
        run_marker: str,
    ) -> DatasetIngestStats:
        if dataset.dataset_id != source.dataset_id or dataset.owner_id != source.owner_id:
            raise DatasetIngestionError("Dataset and source ownership must match.")
        marker = run_marker.strip()
        if not marker or len(marker) > 200:
            raise DatasetIngestionError("A bounded durable ingestion run marker is required.")

        batches = _bounded_batches(units, self._batch_size)
        try:
            first_batch = next(batches)
        except StopIteration as exc:
            raise DatasetIngestionError("No extractable content units were produced from the source.") from exc

        # Fetching the first batch before schema/source writes ensures a scanned PDF
        # raises OcrRequired before the graph is touched.
        self.ensure_schema()
        stats = DatasetIngestStats(sources=1)
        semantic_disabled_reported = False

        with self._driver.session(database=self._database) as session:
            session.execute_write(self._merge_dataset, dataset)
            session.execute_write(self._merge_source, source)

            for batch in chain((first_batch,), batches):
                if any(
                    unit.dataset_id != dataset.dataset_id
                    or unit.owner_id != dataset.owner_id
                    or unit.source_file_id != source.source_file_id
                    or unit.release_key != source.release_key
                    for unit in batch
                ):
                    raise DatasetIngestionError("All content units must belong to the dataset source and owner.")

                if extractor is None:
                    semantic_by_content_unit: dict[str, Any] = {}
                    if not semantic_disabled_reported:
                        _append_warnings(
                            stats,
                            ["Semantic extraction was not configured; source content was ingested without entities or facts."],
                        )
                        semantic_disabled_reported = True
                else:
                    semantic = extract_semantics_safely(batch, extractor)
                    semantic_by_content_unit = semantic.by_content_unit
                    _append_warnings(stats, semantic.warnings)

                rows, vector_count, vector_warning = self._content_rows(batch)
                for row in rows:
                    row["ingestion_run_id"] = marker
                if vector_warning:
                    _append_warnings(stats, [vector_warning])
                stats.content_units += len(batch)
                stats.vectors += vector_count
                session.execute_write(self._merge_content_units_with_marker, rows)

                entities, mentions, facts = _semantic_graph_rows(batch, semantic_by_content_unit)
                for entity_batch in _bounded_batches(entities, self._batch_size):
                    session.execute_write(self._merge_entities, entity_batch)
                for mention_batch in _bounded_batches(mentions, self._batch_size):
                    session.execute_write(self._merge_mentions, mention_batch)
                for fact_batch in _bounded_batches(facts, self._batch_size):
                    session.execute_write(self._merge_facts, fact_batch)
                # These are processed-row counts. They remain bounded without retaining
                # every entity ID in memory, while graph MERGE preserves unique nodes.
                stats.entities += len(entities)
                stats.facts += len(facts)

            stats.inactive_units = session.execute_write(
                self._mark_units_not_seen_in_run_inactive,
                source.source_file_id,
                marker,
            )
            session.execute_write(
                self._synchronize_derived_owner,
                source.source_file_id,
                dataset.dataset_id,
                dataset.owner_id,
            )

        return stats

    @staticmethod
    def _merge_content_units_with_marker(tx: Any, rows: list[dict[str, Any]]) -> None:
        tx.run(
            "UNWIND $rows AS row "
            "MATCH (source:SourceFile {source_file_id: row.source_file_id, active: true}) "
            "MERGE (unit:ContentUnit {content_unit_id: row.content_unit_id}) "
            "SET unit.dataset_id = row.dataset_id, unit.owner_id = row.owner_id, unit.release_key = row.release_key, unit.source_file_id = row.source_file_id, "
            "unit.source_hash = row.source_hash, unit.chunk_index = row.chunk_index, unit.page = row.page, "
            "unit.text = row.text, unit.citation = row.citation, unit.active = row.active, "
            "unit.ingestion_run_id = row.ingestion_run_id, unit.updated_at = datetime(), "
            "unit.embedding = CASE WHEN row.embedding IS NULL THEN unit.embedding ELSE row.embedding END "
            "ON CREATE SET unit.created_at = datetime() "
            "MERGE (source)-[:HAS_CONTENT]->(unit)",
            rows=rows,
        ).consume()

    @staticmethod
    def _mark_units_not_seen_in_run_inactive(tx: Any, source_file_id: str, run_marker: str) -> int:
        result = tx.run(
            "MATCH (unit:ContentUnit {source_file_id: $source_file_id}) "
            "WHERE unit.active = true AND coalesce(unit.ingestion_run_id, '') <> $run_marker "
            "SET unit.active = false, unit.updated_at = datetime() "
            "RETURN count(unit) AS count",
            source_file_id=source_file_id,
            run_marker=run_marker,
        ).single()
        return int(result["count"]) if result else 0

    @staticmethod
    def _synchronize_derived_owner(tx: Any, source_file_id: str, dataset_id: str, owner_id: str) -> None:
        tx.run(
            "MATCH (:SourceFile {source_file_id: $source_file_id})-[:HAS_CONTENT]->(unit:ContentUnit {active: true}) "
            "OPTIONAL MATCH (unit)-[:MENTIONS]->(entity:Entity) "
            "OPTIONAL MATCH (unit)-[:ASSERTS]->(fact:Fact) "
            "WITH collect(DISTINCT entity) AS entities, collect(DISTINCT fact) AS facts "
            "FOREACH (entity IN entities | SET entity.dataset_id = $dataset_id, entity.owner_id = $owner_id, entity.updated_at = datetime()) "
            "FOREACH (fact IN facts | SET fact.dataset_id = $dataset_id, fact.owner_id = $owner_id, fact.updated_at = datetime())",
            source_file_id=source_file_id,
            dataset_id=dataset_id,
            owner_id=owner_id,
        ).consume()


def _bounded_batches(values: Iterable[Any], size: int) -> Iterator[list[Any]]:
    batch: list[Any] = []
    for value in values:
        batch.append(value)
        if len(batch) >= size:
            yield batch
            batch = []
    if batch:
        yield batch


def _append_warnings(stats: DatasetIngestStats, warnings: Iterable[str]) -> None:
    for warning in warnings:
        if len(stats.warnings) >= _MAX_REPORTED_WARNINGS:
            return
        stats.warnings.append(warning)
