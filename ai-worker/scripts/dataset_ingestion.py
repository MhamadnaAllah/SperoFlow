#!/usr/bin/env python3
"""Operational CLI for safe, worker-only dataset ingestion and recovery."""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path

import httpx

from speroflow_ai.config import get_settings
from speroflow_ai.dataset_ingestion import (
    BedrockSemanticExtractor,
    DatasetGraphRecord,
    OcrRequired,
    SourceFileGraphRecord,
    iter_content_units,
    profile_source,
    stable_id,
)
from speroflow_ai.dataset_streaming import StreamingDatasetGraphIngester
from speroflow_ai.dataset_worker import process_dataset_job


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="SperoFlow provenance-first dataset ingestion")
    commands = parser.add_subparsers(dest="command", required=True)

    inspect = commands.add_parser("inspect", help="Profile a staged dataset source without graph writes")
    inspect.add_argument("--file", required=True, type=Path)
    inspect.add_argument("--content-type", required=True)

    ingest = commands.add_parser("ingest", help="Ingest a local source through the worker graph contract")
    ingest.add_argument("--file", required=True, type=Path)
    ingest.add_argument("--content-type", required=True)
    ingest.add_argument("--dataset-id", required=True)
    ingest.add_argument("--source-file-id", required=True)
    ingest.add_argument("--owner-id", required=True)
    ingest.add_argument("--dataset-name", required=True)
    ingest.add_argument("--object-key", default="local-cli")
    ingest.add_argument("--no-semantic-extraction", action="store_true")

    validate = commands.add_parser("validate", help="Validate active source-provenance graph counts")
    validate.add_argument("--dataset-id", required=True)
    validate.add_argument("--owner-id", required=True)

    recover = commands.add_parser("recover", help="Recover a durable dataset job by ID using its callback capability")
    recover.add_argument("--job-id", required=True)
    recover.add_argument("--callback-token", default=os.environ.get("DATASET_JOB_CALLBACK_TOKEN", ""))
    recover.add_argument("--api-url", default=os.environ.get("PRIMARY_API_URL", "http://api:8080"))
    return parser


def inspect_file(args: argparse.Namespace) -> int:
    profile = profile_source(args.file, args.content_type)
    print(json.dumps({
        "file": str(profile.path),
        "extension": profile.extension,
        "content_type": profile.content_type,
        "size_bytes": profile.size_bytes,
        "sha256": profile.sha256,
        "signature_valid": profile.signature_valid,
        "record_hint": profile.record_hint,
    }, sort_keys=True))
    return 0


def ingest_file(args: argparse.Namespace) -> int:
    settings = get_settings()
    profile = profile_source(args.file, args.content_type)
    extractor = None if args.no_semantic_extraction else BedrockSemanticExtractor(
        region=settings.bedrock_region,
        model_id=settings.dataset_extraction_model or settings.llm_model,
    )
    from neo4j import GraphDatabase
    from speroflow_ai.services.embedding import generate_embeddings_batch

    dataset = DatasetGraphRecord(args.dataset_id, args.owner_id, args.dataset_name)
    source = SourceFileGraphRecord(
        args.source_file_id,
        args.dataset_id,
        args.owner_id,
        args.file.name,
        args.object_key,
        profile.content_type,
        profile.sha256,
    )
    driver = GraphDatabase.driver(settings.neo4j_uri, auth=(settings.neo4j_user, settings.neo4j_password))
    try:
        try:
            stats = StreamingDatasetGraphIngester(
                driver,
                database=settings.neo4j_database,
                embedding_batch=lambda values: generate_embeddings_batch(values, settings.embedding_model),
            ).ingest_stream(
                dataset,
                source,
                iter_content_units(
                    profile,
                    dataset_id=dataset.dataset_id,
                    source_file_id=source.source_file_id,
                    owner_id=dataset.owner_id,
                    file_name=args.file.name,
                ),
                extractor=extractor,
                run_marker="cli-" + stable_id(args.dataset_id, args.source_file_id, profile.sha256),
            )
        except OcrRequired as exc:
            print(json.dumps({"status": "waiting_for_ocr", "error": str(exc)}))
            return 75
    finally:
        driver.close()
    print(json.dumps({"status": "succeeded_with_warnings" if stats.warnings else "succeeded", **stats.__dict__}, sort_keys=True))
    return 0


def validate_dataset(args: argparse.Namespace) -> int:
    settings = get_settings()
    from neo4j import GraphDatabase

    driver = GraphDatabase.driver(settings.neo4j_uri, auth=(settings.neo4j_user, settings.neo4j_password))
    try:
        counts = StreamingDatasetGraphIngester(driver, database=settings.neo4j_database).validate(args.dataset_id, args.owner_id)
    finally:
        driver.close()
    print(json.dumps({"status": "valid", **counts}, sort_keys=True))
    return 0


async def recover_job(args: argparse.Namespace) -> int:
    if not args.callback_token:
        raise ValueError("recover requires --callback-token or DATASET_JOB_CALLBACK_TOKEN.")
    headers = {"Authorization": "Bearer " + args.callback_token}
    timeout = httpx.Timeout(connect=5.0, read=120.0, write=30.0, pool=5.0)
    async with httpx.AsyncClient(base_url=args.api_url, timeout=timeout) as client:
        response = await client.get(f"/internal/v1/dataset-jobs/{args.job_id}", headers=headers)
        response.raise_for_status()
        outcome = await asyncio.to_thread(process_dataset_job, get_settings(), response.json())
        completion = await client.post(
            f"/internal/v1/dataset-jobs/{args.job_id}/complete",
            headers=headers,
            json=outcome.completion_payload(),
        )
        completion.raise_for_status()
    print(outcome.report)
    return 75 if outcome.state == "waitingForOcr" else 0


def main() -> int:
    args = build_parser().parse_args()
    if args.command == "inspect":
        return inspect_file(args)
    if args.command == "ingest":
        return ingest_file(args)
    if args.command == "validate":
        return validate_dataset(args)
    if args.command == "recover":
        return asyncio.run(recover_job(args))
    raise AssertionError("Unhandled CLI command")


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(json.dumps({"status": "failed", "error": str(exc)}), file=sys.stderr)
        raise SystemExit(1)
