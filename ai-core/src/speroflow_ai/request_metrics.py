"""Process-local Prometheus text metrics for the private AI API."""

from __future__ import annotations

import threading
import time
from collections import defaultdict

_lock = threading.Lock()
_requests_total = 0
_errors_total = 0
_duration_ms_sum = 0.0
_duration_ms_count = 0
_status_counts: dict[str, int] = defaultdict(int)
_process_start = time.time()


def record(method: str, status_code: int, elapsed_ms: float) -> None:
    global _requests_total, _errors_total, _duration_ms_sum, _duration_ms_count
    with _lock:
        _requests_total += 1
        if status_code >= 500:
            _errors_total += 1
        _duration_ms_sum += max(0.0, elapsed_ms)
        _duration_ms_count += 1
        key = f"{method.upper()}:{status_code}"
        _status_counts[key] += 1


def render_prometheus(service_name: str = "speroflow-ai-api") -> str:
    with _lock:
        lines = [
            "# HELP speroflow_http_requests_total Total HTTP requests handled by the process.",
            "# TYPE speroflow_http_requests_total counter",
            f'speroflow_http_requests_total{{service="{service_name}"}} {_requests_total}',
            "# HELP speroflow_http_errors_total HTTP responses with status >= 500.",
            "# TYPE speroflow_http_errors_total counter",
            f'speroflow_http_errors_total{{service="{service_name}"}} {_errors_total}',
            "# HELP speroflow_http_request_duration_ms_sum Sum of request durations in milliseconds.",
            "# TYPE speroflow_http_request_duration_ms_sum counter",
            f'speroflow_http_request_duration_ms_sum{{service="{service_name}"}} {_duration_ms_sum:.3f}',
            "# HELP speroflow_http_request_duration_ms_count Count of timed requests.",
            "# TYPE speroflow_http_request_duration_ms_count counter",
            f'speroflow_http_request_duration_ms_count{{service="{service_name}"}} {_duration_ms_count}',
            "# HELP speroflow_http_responses_total HTTP responses by method and status.",
            "# TYPE speroflow_http_responses_total counter",
        ]
        for key in sorted(_status_counts):
            method, status = key.split(":", 1)
            count = _status_counts[key]
            lines.append(
                f'speroflow_http_responses_total{{service="{service_name}",method="{method}",status="{status}"}} {count}'
            )
        lines.extend(
            [
                "# HELP speroflow_process_start_time_seconds Unix time when metrics module loaded.",
                "# TYPE speroflow_process_start_time_seconds gauge",
                f'speroflow_process_start_time_seconds{{service="{service_name}"}} {_process_start:.0f}',
                "",
            ]
        )
        return "\n".join(lines)
