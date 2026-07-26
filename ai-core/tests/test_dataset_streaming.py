"""Unit coverage for bounded worker-only dataset ingestion."""

from __future__ import annotations

from types import SimpleNamespace
import unittest

from speroflow_ai.dataset_ingestion import ContentUnit, DatasetGraphRecord, DatasetIngestionError, SourceFileGraphRecord
from speroflow_ai.dataset_streaming import StreamingDatasetGraphIngester
from speroflow_ai.dataset_worker import TextractOcrCoordinator


class _Result:
    def consume(self):
        return self

    def single(self):
        return {"count": 0}


class _Transaction:
    def __init__(self) -> None:
        self.calls: list[tuple[str, dict]] = []

    def run(self, query: str, **kwargs):
        self.calls.append((query, kwargs))
        return _Result()


class _Session:
    def __init__(self, transaction: _Transaction) -> None:
        self._transaction = transaction

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False

    def run(self, query: str, **kwargs):
        return self._transaction.run(query, **kwargs)

    def execute_write(self, callback, *args):
        return callback(self._transaction, *args)


class _Driver:
    def __init__(self) -> None:
        self.transaction = _Transaction()
        self.sessions = 0

    def session(self, **_kwargs):
        self.sessions += 1
        return _Session(self.transaction)


def _unit(index: int) -> ContentUnit:
    return ContentUnit(
        content_unit_id=f"unit-{index}",
        dataset_id="dataset-1",
        source_file_id="source-1",
        owner_id="owner-1",
        source_hash="d" * 64,
        chunk_index=index,
        text=f"row {index}",
        citation=f"records.csv (chunk {index}; sha256:dddddddddddd)",
    )


class DatasetStreamingTests(unittest.TestCase):
    def test_streaming_ingester_bounds_embedding_and_marks_current_run(self) -> None:
        driver = _Driver()
        embedding_batch_sizes: list[int] = []

        def embeddings(values: list[str]) -> list[list[float]]:
            embedding_batch_sizes.append(len(values))
            return [[0.1] * 1024 for _ in values]

        stats = StreamingDatasetGraphIngester(driver, embedding_batch=embeddings, batch_size=2).ingest_stream(
            DatasetGraphRecord("dataset-1", "owner-1", "Dataset"),
            SourceFileGraphRecord("source-1", "dataset-1", "owner-1", "records.csv", "datasets/source.csv", "text/csv", "d" * 64),
            (_unit(index) for index in range(1, 6)),
            extractor=None,
            run_marker="job-1",
        )

        self.assertEqual(embedding_batch_sizes, [2, 2, 1])
        self.assertEqual(stats.content_units, 5)
        self.assertEqual(stats.vectors, 5)
        self.assertEqual(len(stats.warnings), 1)
        queries = "\n".join(query for query, _kwargs in driver.transaction.calls)
        self.assertIn("unit.ingestion_run_id = row.ingestion_run_id", queries)
        self.assertIn("coalesce(unit.ingestion_run_id, '') <> $run_marker", queries)

    def test_empty_stream_fails_before_opening_a_graph_session(self) -> None:
        driver = _Driver()
        ingester = StreamingDatasetGraphIngester(driver)

        with self.assertRaises(DatasetIngestionError):
            ingester.ingest_stream(
                DatasetGraphRecord("dataset-1", "owner-1", "Dataset"),
                SourceFileGraphRecord("source-1", "dataset-1", "owner-1", "records.csv", "datasets/source.csv", "text/csv", "d" * 64),
                iter(()),
                extractor=None,
                run_marker="job-1",
            )

        self.assertEqual(driver.sessions, 0)

    def test_textract_partial_success_is_ingested_with_a_warning(self) -> None:
        class Textract:
            def get_document_text_detection(self, **_kwargs):
                return {
                    "JobStatus": "PARTIAL_SUCCESS",
                    "Warnings": [{"ErrorCode": "PAGE_LIMIT", "Pages": [4]}],
                    "Blocks": [{"BlockType": "LINE", "Text": "Recovered text"}],
                }

        coordinator = TextractOcrCoordinator(SimpleNamespace(textract_sqs_queue_url=""))
        coordinator._textract = Textract()

        self.assertEqual(coordinator.read_if_complete("textract-1"), "Recovered text")
        self.assertTrue(coordinator.warnings)
        self.assertIn("partial success", coordinator.warnings[0].lower())


if __name__ == "__main__":
    unittest.main()
