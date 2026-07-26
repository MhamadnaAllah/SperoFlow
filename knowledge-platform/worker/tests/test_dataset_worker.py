import json
import unittest
from unittest.mock import patch

from knowledge_worker.config import Settings
from knowledge_worker.dataset_worker import (
    DatasetJobOutcome,
    _notification_job_id,
    _safe_stage_name,
    process_dataset_job,
)


class DatasetWorkerTests(unittest.TestCase):
    def test_outcome_uses_the_api_contract_field_names(self):
        payload = DatasetJobOutcome(
            state="succeeded",
            report="{}",
            content_units=3,
            entities=2,
            facts=1,
            vectors=3,
        ).completion_payload()

        self.assertEqual("succeeded", payload["state"])
        self.assertEqual(3, payload["contentUnits"])
        self.assertEqual(2, payload["entities"])
        self.assertEqual(1, payload["facts"])
        self.assertEqual(3, payload["vectors"])

    def test_notification_parser_accepts_only_matching_sns_payloads(self):
        body = json.dumps({"Message": json.dumps({"JobId": "textract-42"})})

        self.assertEqual("textract-42", _notification_job_id(body))
        self.assertIsNone(_notification_job_id("not-json"))
        self.assertIsNone(_notification_job_id(json.dumps({"Message": "{}"})))

    def test_stage_name_drops_untrusted_path_components(self):
        self.assertEqual("source.pdf", _safe_stage_name("../private/scan.PDF"))
        self.assertEqual("source.bin", _safe_stage_name("no-extension"))

    def test_invalid_job_is_reported_without_attempting_external_work(self):
        with patch("knowledge_worker.dataset_worker.logger.exception"):
            outcome = process_dataset_job(Settings(), {"jobId": "missing-fields"})

        self.assertEqual("failed", outcome.state)
        self.assertIn("missing", outcome.error.lower())
        self.assertIn("failed", outcome.report)