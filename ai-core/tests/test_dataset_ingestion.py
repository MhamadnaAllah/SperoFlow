"""Focused unit coverage for provenance-first dataset ingestion boundaries."""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from speroflow_ai.dataset_ingestion import (
    ContentUnit,
    DatasetGraphIngester,
    DatasetIngestionError,
    SemanticEntity,
    SemanticExtraction,
    SemanticFact,
    _semantic_graph_rows,
    extract_semantics_safely,
    iter_content_units,
    profile_source,
)
from speroflow_ai.dataset_worker import _notification_job_id
from speroflow_ai.services.dataset_retrieval import DATASET_RETRIEVAL_QUERY


class FailingExtractor:
    def extract(self, _unit: ContentUnit) -> SemanticExtraction:
        raise RuntimeError("Bedrock is unavailable")


class DatasetIngestionTests(unittest.TestCase):
    def test_csv_profile_and_content_units_are_deterministic(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "records.csv"
            source.write_text("name,topic\nAda,graphs\n", encoding="utf-8")

            profile = profile_source(source, "text/csv")
            first = list(
                iter_content_units(
                    profile,
                    dataset_id="dataset-1",
                    source_file_id="source-1",
                    owner_id="owner-1",
                    file_name=source.name,
                )
            )
            second = list(
                iter_content_units(
                    profile,
                    dataset_id="dataset-1",
                    source_file_id="source-1",
                    owner_id="owner-1",
                    file_name=source.name,
                )
            )

            self.assertEqual(profile.sha256, hashlib.sha256(source.read_bytes()).hexdigest())
            self.assertEqual(len(first), 1)
            self.assertEqual(first[0].content_unit_id, second[0].content_unit_id)
            self.assertIn("records.csv", first[0].citation)
            self.assertIn("sha256:", first[0].citation)

    def test_signature_mismatch_is_rejected_before_ingestion(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "not-a-pdf.pdf"
            source.write_text("this is plain text", encoding="utf-8")
            with self.assertRaises(DatasetIngestionError):
                profile_source(source, "application/pdf")

    def test_semantic_extraction_fallback_keeps_source_units(self) -> None:
        unit = ContentUnit(
            content_unit_id="unit-1",
            dataset_id="dataset-1",
            source_file_id="source-1",
            owner_id="owner-1",
            source_hash="a" * 64,
            chunk_index=1,
            text="Ada described graph traversal.",
            citation="notes.md (chunk 1; sha256:aaaaaaaaaaaa)",
        )

        result = extract_semantics_safely([unit], FailingExtractor())

        self.assertEqual(result.by_content_unit, {})
        self.assertEqual(len(result.warnings), 1)
        self.assertIn("Semantic extraction skipped", result.warnings[0])

    def test_entity_fact_rows_carry_dataset_owner_and_citation(self) -> None:
        unit = ContentUnit(
            content_unit_id="unit-1",
            dataset_id="dataset-1",
            source_file_id="source-1",
            owner_id="owner-1",
            source_hash="b" * 64,
            chunk_index=1,
            text="Ada authored notes.",
            citation="notes.md (chunk 1; sha256:bbbbbbbbbbbb)",
        )
        extraction = SemanticExtraction(
            entities=[SemanticEntity(name="Ada", entity_type="person", confidence=0.9)],
            facts=[SemanticFact(subject="Ada", predicate="authored", object="notes", confidence=0.8)],
        )

        entities, mentions, facts = _semantic_graph_rows([unit], {unit.content_unit_id: extraction})

        self.assertGreaterEqual(len(entities), 2)
        self.assertGreaterEqual(len(mentions), 2)
        self.assertEqual(len(facts), 1)
        self.assertEqual(facts[0]["dataset_id"], "dataset-1")
        self.assertEqual(facts[0]["owner_id"], "owner-1")
        self.assertEqual(facts[0]["citation"], unit.citation)

    def test_embedding_failure_is_warning_not_ingestion_failure(self) -> None:
        unit = ContentUnit(
            content_unit_id="unit-1",
            dataset_id="dataset-1",
            source_file_id="source-1",
            owner_id="owner-1",
            source_hash="c" * 64,
            chunk_index=1,
            text="Some text",
            citation="notes.txt (chunk 1; sha256:cccccccccccc)",
        )
        ingester = DatasetGraphIngester(object(), embedding_batch=lambda _values: [[0.1]])

        rows, count, warning = ingester._content_rows([unit])

        self.assertEqual(count, 0)
        self.assertIsNotNone(warning)
        self.assertIsNone(rows[0]["embedding"])

    def test_textract_notification_and_retrieval_query_remain_bounded(self) -> None:
        body = json.dumps({"Message": json.dumps({"JobId": "textract-123"})})
        self.assertEqual(_notification_job_id(body), "textract-123")
        self.assertIsNone(_notification_job_id("not-json"))
        self.assertIn("any(grant IN $dataset_grants", DATASET_RETRIEVAL_QUERY)
        self.assertIn("unit.dataset_id = grant.dataset_id", DATASET_RETRIEVAL_QUERY)
        self.assertIn("unit.release_key = grant.release_key", DATASET_RETRIEVAL_QUERY)
        self.assertIn("unit.owner_id = grant.owner_subject", DATASET_RETRIEVAL_QUERY)
        self.assertIn("LIMIT $top_k", DATASET_RETRIEVAL_QUERY)
        self.assertNotIn("apoc.", DATASET_RETRIEVAL_QUERY.lower())


if __name__ == "__main__":
    unittest.main()
