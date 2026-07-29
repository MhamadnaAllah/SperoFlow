"""Unit tests for process-local Prometheus metrics."""

from __future__ import annotations

import unittest

from speroflow_ai import request_metrics as metrics


class RequestMetricsTests(unittest.TestCase):
    def test_render_includes_counters_after_record(self) -> None:
        metrics.record("GET", 200, 12.5)
        metrics.record("POST", 500, 3.0)
        text = metrics.render_prometheus("speroflow-ai-api")
        self.assertIn("speroflow_http_requests_total", text)
        self.assertIn('service="speroflow-ai-api"', text)
        self.assertIn('method="GET",status="200"', text)
        self.assertIn('method="POST",status="500"', text)
        self.assertIn("speroflow_http_errors_total", text)


if __name__ == "__main__":
    unittest.main()
