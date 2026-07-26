"""AI-worker-only execution path for uploaded knowledge datasets."""

from __future__ import annotations

import json
import logging
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from knowledge_worker.config import Settings
from knowledge_worker.dataset_ingestion import (
    BedrockSemanticExtractor,
    DatasetGraphRecord,
    DatasetIngestionError,
    OcrRequired,
    SourceFileGraphRecord,
    iter_content_units,
    profile_source,
)
from knowledge_worker.dataset_streaming import StreamingDatasetGraphIngester

logger = logging.getLogger("knowledge.dataset_worker")


@dataclass(frozen=True)
class DatasetJobOutcome:
    state: str
    report: str
    content_units: int = 0
    entities: int = 0
    facts: int = 0
    vectors: int = 0
    error: str | None = None
    textract_job_id: str | None = None

    def completion_payload(self) -> dict[str, Any]:
        return {
            "state": self.state,
            "report": self.report,
            "contentUnits": self.content_units,
            "entities": self.entities,
            "facts": self.facts,
            "vectors": self.vectors,
            "error": self.error,
            "textractJobId": self.textract_job_id,
        }


class S3CompatibleSourceStore:
    """Downloads a canonical private source through S3 or local MinIO."""

    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    def download(self, object_key: str, destination: Path) -> None:
        try:
            import boto3
            from botocore.config import Config
        except ImportError as exc:
            raise DatasetIngestionError("boto3 is required in the ai-worker image for private source downloads.") from exc

        endpoint = self._settings.object_storage_endpoint_url.strip() or None
        if endpoint and "://" not in endpoint:
            endpoint = ("https://" if self._settings.object_storage_use_ssl else "http://") + endpoint
        kwargs: dict[str, Any] = {
            "service_name": "s3",
            "endpoint_url": endpoint,
            "region_name": self._settings.bedrock_region,
            "config": Config(s3={"addressing_style": "path"}),
        }
        if self._settings.object_storage_access_key and self._settings.object_storage_secret_key:
            kwargs["aws_access_key_id"] = self._settings.object_storage_access_key
            kwargs["aws_secret_access_key"] = self._settings.object_storage_secret_key
        client = boto3.client(**kwargs)
        response = client.get_object(Bucket=self._settings.object_storage_bucket, Key=object_key)
        with destination.open("wb") as stream:
            for block in response["Body"].iter_chunks(chunk_size=1024 * 1024):
                if block:
                    stream.write(block)


class TextractOcrCoordinator:
    """Starts async PDF OCR and consumes matching SNS-to-SQS completion messages."""

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._textract = None
        self._sqs = None
        self.warnings: list[str] = []

    def start(self, object_key: str) -> str:
        if not self._settings.textract_sns_topic_arn or not self._settings.textract_role_arn:
            raise DatasetIngestionError("Scanned PDF ingestion requires TEXTRACT_SNS_TOPIC_ARN and TEXTRACT_ROLE_ARN.")
        response = self._get_textract().start_document_text_detection(
            DocumentLocation={"S3Object": {"Bucket": self._settings.object_storage_bucket, "Name": object_key}},
            NotificationChannel={
                "SNSTopicArn": self._settings.textract_sns_topic_arn,
                "RoleArn": self._settings.textract_role_arn,
            },
        )
        return str(response["JobId"])

    def read_if_complete(self, textract_job_id: str) -> str | None:
        self._consume_matching_notification(textract_job_id)
        first = self._get_textract().get_document_text_detection(JobId=textract_job_id)
        status = str(first.get("JobStatus", "IN_PROGRESS"))
        if status == "IN_PROGRESS":
            return None
        if status not in {"SUCCEEDED", "PARTIAL_SUCCESS"}:
            raise DatasetIngestionError(f"Textract OCR ended with status {status}.")
        if status == "PARTIAL_SUCCESS":
            detail = first.get("Warnings") or first.get("StatusMessage") or "Textract returned only a subset of pages."
            self.warnings.append("Textract completed with partial success: " + json.dumps(detail, sort_keys=True)[:800])

        responses = [first]
        next_token = first.get("NextToken")
        while next_token:
            page = self._get_textract().get_document_text_detection(JobId=textract_job_id, NextToken=next_token)
            responses.append(page)
            next_token = page.get("NextToken")
        lines = [
            str(block.get("Text", "")).strip()
            for response in responses
            for block in response.get("Blocks", [])
            if block.get("BlockType") == "LINE" and block.get("Text")
        ]
        text = "\n".join(lines).strip()
        if not text:
            raise DatasetIngestionError("Textract completed without readable LINE blocks.")
        return text

    def _consume_matching_notification(self, textract_job_id: str) -> None:
        if not self._settings.textract_sqs_queue_url:
            return
        try:
            response = self._get_sqs().receive_message(
                QueueUrl=self._settings.textract_sqs_queue_url,
                MaxNumberOfMessages=10,
                WaitTimeSeconds=1,
                VisibilityTimeout=30,
            )
            for message in response.get("Messages", []):
                if _notification_job_id(message.get("Body", "")) != textract_job_id:
                    continue
                self._get_sqs().delete_message(
                    QueueUrl=self._settings.textract_sqs_queue_url,
                    ReceiptHandle=message["ReceiptHandle"],
                )
                return
        except Exception as exc:
            logger.warning("Textract SQS polling failed for %s: %s", textract_job_id, exc)

    def _get_textract(self) -> Any:
        if self._textract is None:
            try:
                import boto3
            except ImportError as exc:
                raise DatasetIngestionError("boto3 is required in the ai-worker image for Textract OCR.") from exc
            self._textract = boto3.client("textract", region_name=self._settings.bedrock_region)
        return self._textract

    def _get_sqs(self) -> Any:
        if self._sqs is None:
            try:
                import boto3
            except ImportError as exc:
                raise DatasetIngestionError("boto3 is required in the ai-worker image for SQS OCR notifications.") from exc
            self._sqs = boto3.client("sqs", region_name=self._settings.bedrock_region)
        return self._sqs


def _notification_job_id(body: str) -> str | None:
    try:
        envelope = json.loads(body)
        payload = json.loads(envelope.get("Message", "{}")) if isinstance(envelope, dict) else {}
        return str(payload.get("JobId")) if payload.get("JobId") else None
    except (TypeError, json.JSONDecodeError):
        return None


def process_dataset_job(settings: Settings, job: dict[str, Any]) -> DatasetJobOutcome:
    """Run a one-shot, idempotent dataset job; callers persist the returned outcome."""
    try:
        return _process_dataset_job(settings, job)
    except Exception as exc:
        logger.exception("Dataset ingestion %s failed.", job.get("jobId", "unknown"))
        return DatasetJobOutcome(
            state="failed",
            report=json.dumps({"status": "failed", "warnings": [], "error": str(exc)[:1_000]}, sort_keys=True),
            error=str(exc)[:1_000],
        )


def _process_dataset_job(settings: Settings, job: dict[str, Any]) -> DatasetJobOutcome:
    required = (
        "jobId",
        "datasetId",
        "sourceId",
        "releaseId",
        "releaseKey",
        "ownerSubject",
        "datasetName",
        "objectKey",
        "fileName",
        "contentType",
        "sizeBytes",
        "sha256",
    )
    missing = [key for key in required if not job.get(key)]
    if missing:
        raise DatasetIngestionError("Knowledge job payload is missing: " + ", ".join(missing))

    release_key = str(job["releaseKey"]).strip()
    source_original_id = str(job["sourceId"])
    source_graph_id = stable_id("knowledge-source-release", source_original_id, release_key)
    with tempfile.TemporaryDirectory(prefix="speroflow-knowledge-") as temp_dir:
        staged_path = Path(temp_dir) / _safe_stage_name(str(job["fileName"]))
        S3CompatibleSourceStore(settings).download(str(job["objectKey"]), staged_path)
        profile = profile_source(staged_path, str(job["contentType"]))
        if profile.sha256 != str(job["sha256"]).lower() or profile.size_bytes != int(job["sizeBytes"]):
            raise DatasetIngestionError("The staged source no longer matches the approved checksum or size.")

        extractor = BedrockSemanticExtractor(
            region=settings.bedrock_region,
            model_id=settings.dataset_extraction_model or settings.llm_model,
        )
        from neo4j import GraphDatabase
        from knowledge_worker.services.embedding import generate_embeddings_batch

        dataset = DatasetGraphRecord(str(job["datasetId"]), str(job["ownerSubject"]), str(job["datasetName"]))
        source = SourceFileGraphRecord(
            source_file_id=source_graph_id,
            source_original_id=source_original_id,
            release_key=release_key,
            dataset_id=dataset.dataset_id,
            owner_id=dataset.owner_id,
            file_name=str(job["fileName"]),
            object_key=str(job["objectKey"]),
            content_type=str(job["contentType"]),
            source_hash=profile.sha256,
        )

        driver = GraphDatabase.driver(settings.neo4j_uri, auth=(settings.neo4j_user, settings.neo4j_password))
        try:
            ingester = StreamingDatasetGraphIngester(
                driver,
                database=settings.neo4j_database,
                embedding_batch=lambda values: generate_embeddings_batch(values, settings.embedding_model),
            )
            try:
                stats = ingester.ingest_stream(
                    dataset,
                    source,
                    iter_content_units(
                        profile,
                        dataset_id=dataset.dataset_id,
                        source_file_id=source.source_file_id,
                        owner_id=dataset.owner_id,
                        release_key=source.release_key,
                        file_name=source.file_name,
                    ),
                    extractor=extractor,
                    run_marker=str(job["jobId"]),
                )
            except OcrRequired:
                ocr = TextractOcrCoordinator(settings)
                existing_ocr_job = job.get("textractJobId")
                if existing_ocr_job:
                    ocr_text = ocr.read_if_complete(str(existing_ocr_job))
                    if ocr_text is None:
                        return _waiting_for_ocr(str(existing_ocr_job))
                else:
                    return _waiting_for_ocr(ocr.start(str(job["objectKey"])))

                stats = ingester.ingest_stream(
                    dataset,
                    source,
                    iter_content_units(
                        profile,
                        dataset_id=dataset.dataset_id,
                        source_file_id=source.source_file_id,
                        owner_id=dataset.owner_id,
                        release_key=source.release_key,
                        file_name=source.file_name,
                        ocr_text=ocr_text,
                    ),
                    extractor=extractor,
                    run_marker=str(job["jobId"]),
                )
                for warning in ocr.warnings:
                    if len(stats.warnings) >= 50:
                        break
                    stats.warnings.append(warning)
        finally:
            driver.close()

    report = json.dumps(
        {
            "status": "succeeded_with_warnings" if stats.warnings else "succeeded",
            "release_key": release_key,
            "source_hash": profile.sha256,
            "content_units": stats.content_units,
            "entities": stats.entities,
            "facts": stats.facts,
            "vectors": stats.vectors,
            "inactive_units": stats.inactive_units,
            "warnings": stats.warnings,
        },
        sort_keys=True,
    )
    return DatasetJobOutcome(
        state="succeededWithWarnings" if stats.warnings else "succeeded",
        report=report,
        content_units=stats.content_units,
        entities=stats.entities,
        facts=stats.facts,
        vectors=stats.vectors,
    )

def _waiting_for_ocr(textract_job_id: str) -> DatasetJobOutcome:
    return DatasetJobOutcome(
        state="waitingForOcr",
        report=json.dumps({"status": "waiting_for_ocr", "textract_job_id": textract_job_id, "warnings": []}, sort_keys=True),
        textract_job_id=textract_job_id,
    )


def _safe_stage_name(file_name: str) -> str:
    suffix = Path(file_name).suffix.lower()
    return "source" + suffix if suffix else "source.bin"
