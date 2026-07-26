"""Structured JSON and text logging configuration for SperoFlow AI services.

Outputs ISO-8601 timestamped JSON logs in production for AWS CloudWatch.
"""

from __future__ import annotations

import json
import logging
import os
import sys
from datetime import datetime, timezone


class JSONFormatter(logging.Formatter):
    """Format log records as structured JSON for AWS CloudWatch / OpenTelemetry."""

    def format(self, record: logging.LogRecord) -> str:
        log_entry = {
            "timestamp": datetime.fromtimestamp(record.created, tz=timezone.utc).isoformat(),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
            "service": os.getenv("SERVICE_NAME", "speroflow-ai"),
            "environment": os.getenv("APP_ENV", "production"),
        }
        if record.exc_info and record.exc_text:
            log_entry["exception"] = record.exc_text
        elif record.exc_info:
            log_entry["exception"] = self.formatException(record.exc_info)
        return json.dumps(log_entry, ensure_ascii=False)


def setup_logging(service_name: str = "speroflow-ai") -> None:
    log_format = os.getenv("LOG_FORMAT", "text").lower()
    app_env = os.getenv("APP_ENV", "development").lower()
    log_level = getattr(logging, os.getenv("LOG_LEVEL", "INFO").upper(), logging.INFO)

    root_logger = logging.getLogger()
    root_logger.setLevel(log_level)

    # Remove existing handlers
    for handler in root_logger.handlers[:]:
        root_logger.removeHandler(handler)

    handler = logging.StreamHandler(sys.stdout)
    if log_format == "json" or app_env == "production":
        handler.setFormatter(JSONFormatter())
    else:
        handler.setFormatter(
            logging.Formatter("%(asctime)s | %(levelname)-7s | %(name)s | %(message)s", datefmt="%H:%M:%S")
        )

    root_logger.addHandler(handler)

