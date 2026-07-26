"""Provenance-first dataset profiling, extraction, and Neo4j ingestion.

This module is intentionally usable by the ai-worker and its CLI only. It has
no FastAPI routes and never accepts browser-provided owner identifiers.
"""

from __future__ import annotations

import csv
import hashlib
import io
import json
import logging
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable, Iterator, Sequence

from pydantic import BaseModel, Field, field_validator

logger = logging.getLogger("knowledge.dataset_ingestion")

MAX_UPLOAD_BYTES = 100 * 1024 * 1024
EMBEDDING_DIMENSIONS = 1024
DATASET_VECTOR_INDEX = "dataset_content_embedding_index"
SUPPORTED_EXTENSIONS = frozenset({".csv", ".json", ".md", ".txt", ".docx", ".pdf"})
CONTENT_TYPES = {
    ".csv": frozenset({"text/csv", "application/csv", "application/vnd.ms-excel"}),
    ".json": frozenset({"application/json", "text/json"}),
    ".md": frozenset({"text/markdown", "text/plain"}),
    ".txt": frozenset({"text/plain"}),
    ".docx": frozenset({"application/vnd.openxmlformats-officedocument.wordprocessingml.document"}),
    ".pdf": frozenset({"application/pdf"}),
}


class DatasetIngestionError(ValueError):
    """Raised when a source cannot safely enter the ingestion pipeline."""


class OcrRequired(DatasetIngestionError):
    """Raised when a PDF does not contain enough native text to ingest."""


@dataclass(frozen=True)
class SourceProfile:
    path: Path
    extension: str
    content_type: str
    size_bytes: int
    sha256: str
    signature_valid: bool
    requires_ocr: bool = False
    native_text_characters: int = 0
    record_hint: str | None = None


@dataclass(frozen=True)
class ContentUnit:
    content_unit_id: str
    dataset_id: str
    source_file_id: str
    owner_id: str
    release_key: str
    source_hash: str
    chunk_index: int
    text: str
    citation: str
    page: int | None = None


@dataclass(frozen=True)
class DatasetGraphRecord:
    dataset_id: str
    owner_id: str
    name: str
    active: bool = True


@dataclass(frozen=True)
class SourceFileGraphRecord:
    source_file_id: str
    source_original_id: str
    release_key: str
    dataset_id: str
    owner_id: str
    file_name: str
    object_key: str
    content_type: str
    source_hash: str
    active: bool = True


class SemanticEntity(BaseModel):
    name: str = Field(min_length=1, max_length=300)
    entity_type: str = Field(default="concept", min_length=1, max_length=80)
    confidence: float = Field(default=0.5, ge=0.0, le=1.0)

    @field_validator("name", "entity_type")
    @classmethod
    def normalize_text(cls, value: str) -> str:
        value = " ".join(value.split())
        if not value:
            raise ValueError("Semantic values cannot be blank.")
        return value


class SemanticFact(BaseModel):
    subject: str = Field(min_length=1, max_length=300)
    predicate: str = Field(min_length=1, max_length=160)
    object: str = Field(min_length=1, max_length=500)
    confidence: float = Field(default=0.5, ge=0.0, le=1.0)

    @field_validator("subject", "predicate", "object")
    @classmethod
    def normalize_text(cls, value: str) -> str:
        value = " ".join(value.split())
        if not value:
            raise ValueError("Semantic values cannot be blank.")
        return value


class SemanticExtraction(BaseModel):
    entities: list[SemanticEntity] = Field(default_factory=list, max_length=80)
    facts: list[SemanticFact] = Field(default_factory=list, max_length=80)


@dataclass
class SemanticExtractionResult:
    by_content_unit: dict[str, SemanticExtraction] = field(default_factory=dict)
    warnings: list[str] = field(default_factory=list)


@dataclass
class DatasetIngestStats:
    sources: int = 0
    content_units: int = 0
    entities: int = 0
    facts: int = 0
    vectors: int = 0
    inactive_units: int = 0
    warnings: list[str] = field(default_factory=list)


def stable_id(*parts: object) -> str:
    """Build a deterministic, namespace-friendly identifier from stable fields."""
    raw = "\x1f".join(str(part).strip() for part in parts)
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def sha256_file(path: Path, chunk_size: int = 1024 * 1024) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while block := source.read(chunk_size):
            digest.update(block)
    return digest.hexdigest()


def allowed_content_type(extension: str, content_type: str) -> bool:
    normalized_extension = extension.lower()
    normalized_type = content_type.split(";", 1)[0].strip().lower()
    return normalized_extension in CONTENT_TYPES and normalized_type in CONTENT_TYPES[normalized_extension]


def _first_non_whitespace(data: bytes) -> bytes:
    return data.lstrip(b"\xef\xbb\xbf \t\r\n")[:1]


def _signature_is_valid(path: Path, extension: str) -> bool:
    with path.open("rb") as source:
        prefix = source.read(16)
    if extension == ".pdf":
        return prefix.startswith(b"%PDF-")
    if extension == ".docx":
        return prefix.startswith(b"PK\x03\x04")
    if extension == ".json":
        return _first_non_whitespace(prefix) in {b"{", b"["}
    return True


def profile_source(path: str | Path, declared_content_type: str | None = None) -> SourceProfile:
    """Inspect a local staged file without parsing the entire content into memory."""
    source = Path(path)
    if not source.is_file():
        raise DatasetIngestionError("The staged source file does not exist.")
    extension = source.suffix.lower()
    if extension not in SUPPORTED_EXTENSIONS:
        raise DatasetIngestionError(f"Unsupported file extension '{extension or '(none)'}'.")
    size_bytes = source.stat().st_size
    if size_bytes <= 0 or size_bytes > MAX_UPLOAD_BYTES:
        raise DatasetIngestionError("Dataset files must be between 1 byte and 100 MB.")
    if declared_content_type and not allowed_content_type(extension, declared_content_type):
        raise DatasetIngestionError("The declared content type is not allowed for this file extension.")
    signature_valid = _signature_is_valid(source, extension)
    if not signature_valid:
        raise DatasetIngestionError("The file signature does not match the declared file type.")
    return SourceProfile(
        path=source,
        extension=extension,
        content_type=(declared_content_type or next(iter(CONTENT_TYPES[extension]))).split(";", 1)[0].lower(),
        size_bytes=size_bytes,
        sha256=sha256_file(source),
        signature_valid=signature_valid,
        record_hint="tabular" if extension in {".csv", ".json"} else "document",
    )


def _decode_text(data: bytes) -> str:
    return data.decode("utf-8-sig", errors="replace").replace("\x00", "")


def _chunk_text(text: str, max_chars: int = 1_200, overlap: int = 160) -> Iterator[str]:
    normalized = re.sub(r"[ \t]+", " ", text).strip()
    if not normalized:
        return
    cursor = 0
    while cursor < len(normalized):
        end = min(len(normalized), cursor + max_chars)
        if end < len(normalized):
            boundary = normalized.rfind(" ", cursor + max_chars - 220, end)
            if boundary > cursor:
                end = boundary
        chunk = normalized[cursor:end].strip()
        if chunk:
            yield chunk
        if end >= len(normalized):
            return
        cursor = max(end - overlap, cursor + 1)


def _iter_csv_segments(path: Path) -> Iterator[tuple[int | None, str]]:
    with path.open("r", encoding="utf-8-sig", errors="replace", newline="") as source:
        reader = csv.DictReader(source)
        if not reader.fieldnames:
            raise DatasetIngestionError("CSV sources must include a header row.")
        for index, row in enumerate(reader, start=1):
            normalized = {str(key): value for key, value in row.items() if key is not None}
            yield index, json.dumps(normalized, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def _iter_json_segments(path: Path) -> Iterator[tuple[int | None, str]]:
    """Stream JSON arrays when ijson is installed; keep object inputs deterministic."""
    try:
        import ijson  # type: ignore[import-not-found]

        with path.open("rb") as source:
            first = _first_non_whitespace(source.read(32))
            source.seek(0)
            if first == b"[":
                for index, value in enumerate(ijson.items(source, "item"), start=1):
                    yield index, json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
                return
            if first == b"{":
                for index, (key, value) in enumerate(ijson.kvitems(source, ""), start=1):
                    yield index, json.dumps({"key": key, "value": value}, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
                return
    except ImportError:
        logger.warning("ijson is unavailable; using the bounded JSON fallback for %s.", path.name)

    if path.stat().st_size > 8 * 1024 * 1024:
        raise DatasetIngestionError("Streaming JSON support is unavailable for a source larger than 8 MB.")
    with path.open("r", encoding="utf-8-sig", errors="replace") as source:
        value = json.load(source)
    if isinstance(value, list):
        for index, item in enumerate(value, start=1):
            yield index, json.dumps(item, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    elif isinstance(value, dict):
        for index, (key, item) in enumerate(value.items(), start=1):
            yield index, json.dumps({"key": key, "value": item}, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    else:
        yield 1, json.dumps(value, ensure_ascii=False)


def _iter_docx_segments(path: Path) -> Iterator[tuple[int | None, str]]:
    try:
        from docx import Document  # type: ignore[import-not-found]
    except ImportError as exc:
        raise DatasetIngestionError("DOCX parsing requires python-docx in the ai-worker image.") from exc
    document = Document(path)
    paragraphs = [paragraph.text.strip() for paragraph in document.paragraphs if paragraph.text.strip()]
    if not paragraphs:
        raise DatasetIngestionError("The DOCX file has no readable paragraphs.")
    yield None, "\n\n".join(paragraphs)


def _iter_pdf_segments(path: Path, native_text_threshold: int = 80) -> Iterator[tuple[int | None, str]]:
    try:
        from pypdf import PdfReader  # type: ignore[import-not-found]
    except ImportError as exc:
        raise DatasetIngestionError("PDF parsing requires pypdf in the ai-worker image.") from exc
    reader = PdfReader(path)
    total_text = 0
    pages: list[tuple[int, str]] = []
    for index, page in enumerate(reader.pages, start=1):
        text = (page.extract_text() or "").strip()
        total_text += len(text)
        if text:
            pages.append((index, text))
    if total_text < native_text_threshold:
        raise OcrRequired("The PDF has insufficient native text and requires Textract OCR.")
    yield from pages


def _iter_raw_segments(profile: SourceProfile, ocr_text: str | None = None) -> Iterator[tuple[int | None, str]]:
    if profile.extension == ".csv":
        yield from _iter_csv_segments(profile.path)
        return
    if profile.extension == ".json":
        yield from _iter_json_segments(profile.path)
        return
    if profile.extension in {".md", ".txt"}:
        yield None, _decode_text(profile.path.read_bytes())
        return
    if profile.extension == ".docx":
        yield from _iter_docx_segments(profile.path)
        return
    if profile.extension == ".pdf":
        if ocr_text is not None:
            yield None, ocr_text
            return
        yield from _iter_pdf_segments(profile.path)
        return
    raise DatasetIngestionError("No extractor is registered for this source type.")


def iter_content_units(
    profile: SourceProfile,
    *,
    dataset_id: str,
    source_file_id: str,
    owner_id: str,
    release_key: str,
    file_name: str,
    ocr_text: str | None = None,
    max_chars: int = 1_200,
    overlap: int = 160,
) -> Iterator[ContentUnit]:
    """Yield stable chunks while keeping CSV and JSON record traversal streaming."""
    if not dataset_id or not source_file_id or not owner_id or not release_key:
        raise DatasetIngestionError("Dataset, source file, and owner identifiers are required.")
    chunk_index = 0
    for page, segment in _iter_raw_segments(profile, ocr_text):
        for chunk in _chunk_text(segment, max_chars=max_chars, overlap=overlap):
            chunk_index += 1
            content_unit_id = stable_id(release_key, source_file_id, profile.sha256, page or 0, chunk_index, chunk)
            location = f"page {page}" if page else f"chunk {chunk_index}"
            citation = f"{file_name} ({location}; sha256:{profile.sha256[:12]})"
            yield ContentUnit(
                content_unit_id=content_unit_id,
                dataset_id=dataset_id,
                source_file_id=source_file_id,
                owner_id=owner_id,
                release_key=release_key,
                source_hash=profile.sha256,
                chunk_index=chunk_index,
                text=chunk,
                citation=citation,
                page=page,
            )


class BedrockSemanticExtractor:
    """Bedrock JSON extraction with strict Pydantic validation at the boundary."""

    def __init__(self, *, region: str, model_id: str) -> None:
        self._region = region
        self._model_id = model_id

    def extract(self, content: ContentUnit) -> SemanticExtraction:
        if not self._model_id.strip():
            raise DatasetIngestionError("A Bedrock extraction model must be configured.")
        try:
            import boto3  # type: ignore[import-not-found]
        except ImportError as exc:
            raise DatasetIngestionError("boto3 is required for Bedrock semantic extraction.") from exc
        prompt = (
            "Extract only supported factual concepts from the source below. Return valid JSON with "
            "exactly this shape: {\"entities\":[{\"name\":string,\"entity_type\":string,\"confidence\":number}],"
            "\"facts\":[{\"subject\":string,\"predicate\":string,\"object\":string,\"confidence\":number}]}. "
            "Do not infer facts absent from the source.\n\nSOURCE:\n" + content.text
        )
        try:
            client = boto3.client("bedrock-runtime", region_name=self._region)
            response = client.converse(
                modelId=self._model_id,
                messages=[{"role": "user", "content": [{"text": prompt}]}],
                inferenceConfig={"maxTokens": 1200, "temperature": 0},
            )
            text = response["output"]["message"]["content"][0]["text"]
            return SemanticExtraction.model_validate(_load_json_object(text))
        except Exception as exc:
            raise DatasetIngestionError(f"Bedrock semantic extraction failed: {exc}") from exc


def _load_json_object(value: str) -> dict[str, Any]:
    candidate = value.strip()
    if candidate.startswith("```"):
        candidate = re.sub(r"^```(?:json)?\s*|\s*```$", "", candidate, flags=re.IGNORECASE)
    decoded = json.loads(candidate)
    if not isinstance(decoded, dict):
        raise DatasetIngestionError("Bedrock returned a non-object semantic response.")
    return decoded


def extract_semantics_safely(
    units: Sequence[ContentUnit],
    extractor: Any | None,
) -> SemanticExtractionResult:
    """Capture per-unit semantic failures as warnings without losing source chunks."""
    result = SemanticExtractionResult()
    if extractor is None:
        result.warnings.append("Semantic extraction was not configured; source content was ingested without entities or facts.")
        return result
    for unit in units:
        try:
            extraction = extractor.extract(unit)
            result.by_content_unit[unit.content_unit_id] = SemanticExtraction.model_validate(extraction)
        except Exception as exc:
            result.warnings.append(f"Semantic extraction skipped for {unit.content_unit_id[:12]}: {exc}")
    return result


DATASET_SCHEMA_STATEMENTS = (
    "CREATE CONSTRAINT dataset_dataset_id IF NOT EXISTS FOR (n:Dataset) REQUIRE n.dataset_id IS UNIQUE",
    "CREATE CONSTRAINT source_file_source_file_id IF NOT EXISTS FOR (n:SourceFile) REQUIRE n.source_file_id IS UNIQUE",
    "CREATE CONSTRAINT content_unit_content_unit_id IF NOT EXISTS FOR (n:ContentUnit) REQUIRE n.content_unit_id IS UNIQUE",
    "CREATE CONSTRAINT entity_entity_id IF NOT EXISTS FOR (n:Entity) REQUIRE n.entity_id IS UNIQUE",
    "CREATE CONSTRAINT fact_fact_id IF NOT EXISTS FOR (n:Fact) REQUIRE n.fact_id IS UNIQUE",
    "CREATE INDEX dataset_owner_lookup IF NOT EXISTS FOR (n:Dataset) ON (n.owner_id, n.active)",
    "CREATE INDEX source_file_dataset_lookup IF NOT EXISTS FOR (n:SourceFile) ON (n.dataset_id, n.active)",
    "CREATE INDEX content_unit_dataset_lookup IF NOT EXISTS FOR (n:ContentUnit) ON (n.dataset_id, n.release_key, n.owner_id, n.active)",
    "CREATE INDEX entity_dataset_lookup IF NOT EXISTS FOR (n:Entity) ON (n.dataset_id, n.owner_id, n.active)",
)

VECTOR_INDEX_STATEMENT = """
CREATE VECTOR INDEX dataset_content_embedding_index IF NOT EXISTS
FOR (n:ContentUnit) ON (n.embedding)
OPTIONS {indexConfig: {`vector.dimensions`: 1024, `vector.similarity_function`: 'cosine'}}
"""


class DatasetGraphIngester:
    """The only graph writer for owner-scoped uploaded datasets."""

    def __init__(
        self,
        driver: Any,
        *,
        database: str = "neo4j",
        embedding_batch: Callable[[list[str]], list[list[float]]] | None = None,
        batch_size: int = 64,
    ) -> None:
        self._driver = driver
        self._database = database
        self._embedding_batch = embedding_batch
        self._batch_size = max(1, min(batch_size, 250))

    def ensure_schema(self) -> None:
        with self._driver.session(database=self._database) as session:
            for statement in DATASET_SCHEMA_STATEMENTS:
                session.run(statement).consume()
            session.run(VECTOR_INDEX_STATEMENT).consume()

    def ingest(
        self,
        dataset: DatasetGraphRecord,
        source: SourceFileGraphRecord,
        units: Sequence[ContentUnit],
        semantic: SemanticExtractionResult | None = None,
    ) -> DatasetIngestStats:
        if dataset.dataset_id != source.dataset_id or dataset.owner_id != source.owner_id:
            raise DatasetIngestionError("Dataset and source ownership must match.")
        if any(unit.dataset_id != dataset.dataset_id or unit.owner_id != dataset.owner_id for unit in units):
            raise DatasetIngestionError("All content units must belong to the dataset owner.")

        semantic = semantic or SemanticExtractionResult()
        stats = DatasetIngestStats(sources=1, content_units=len(units), warnings=list(semantic.warnings))
        self.ensure_schema()
        with self._driver.session(database=self._database) as session:
            session.execute_write(self._merge_dataset, dataset)
            session.execute_write(self._merge_source, source)
            active_ids: list[str] = []
            for batch in _batches(list(units), self._batch_size):
                rows, vector_count, vector_warning = self._content_rows(batch)
                if vector_warning:
                    stats.warnings.append(vector_warning)
                stats.vectors += vector_count
                active_ids.extend(row["content_unit_id"] for row in rows)
                session.execute_write(self._merge_content_units, rows)
            stats.inactive_units = session.execute_write(self._mark_removed_units_inactive, source.source_file_id, active_ids)

            entities, mentions, facts = _semantic_graph_rows(units, semantic.by_content_unit)
            for batch in _batches(entities, self._batch_size):
                session.execute_write(self._merge_entities, batch)
            for batch in _batches(mentions, self._batch_size):
                session.execute_write(self._merge_mentions, batch)
            for batch in _batches(facts, self._batch_size):
                session.execute_write(self._merge_facts, batch)
            stats.entities = len(entities)
            stats.facts = len(facts)
        return stats

    def validate(self, dataset_id: str, owner_id: str) -> dict[str, int]:
        query = """
        MATCH (dataset:Dataset {dataset_id: $dataset_id, owner_id: $owner_id, active: true})
        OPTIONAL MATCH (dataset)-[:HAS_SOURCE]->(source:SourceFile {active: true})
        OPTIONAL MATCH (source)-[:HAS_CONTENT]->(unit:ContentUnit {active: true})
        OPTIONAL MATCH (unit)-[:MENTIONS]->(entity:Entity {active: true})
        OPTIONAL MATCH (unit)-[:ASSERTS]->(fact:Fact {active: true})
        RETURN count(DISTINCT source) AS sources, count(DISTINCT unit) AS content_units,
               count(DISTINCT entity) AS entities, count(DISTINCT fact) AS facts
        """
        with self._driver.session(database=self._database) as session:
            record = session.run(query, dataset_id=dataset_id, owner_id=owner_id).single()
        if record is None:
            raise DatasetIngestionError("The requested active dataset graph was not found.")
        return {key: int(record[key]) for key in ("sources", "content_units", "entities", "facts")}

    def _content_rows(self, units: Sequence[ContentUnit]) -> tuple[list[dict[str, Any]], int, str | None]:
        embeddings: list[list[float] | None] = [None] * len(units)
        warning: str | None = None
        if self._embedding_batch and units:
            try:
                generated = self._embedding_batch([unit.text for unit in units])
                if len(generated) != len(units) or any(len(vector) != EMBEDDING_DIMENSIONS for vector in generated):
                    raise DatasetIngestionError("The content embedding model returned an invalid vector shape.")
                embeddings = generated
            except Exception as exc:
                warning = f"Embedding skipped for {len(units)} content units: {exc}"
        rows = [
            {
                "content_unit_id": unit.content_unit_id,
                "dataset_id": unit.dataset_id,
                "owner_id": unit.owner_id,
                "release_key": unit.release_key,
                "source_file_id": unit.source_file_id,
                "source_hash": unit.source_hash,
                "chunk_index": unit.chunk_index,
                "page": unit.page,
                "text": unit.text,
                "citation": unit.citation,
                "active": True,
                "embedding": embeddings[index],
            }
            for index, unit in enumerate(units)
        ]
        return rows, sum(embedding is not None for embedding in embeddings), warning

    @staticmethod
    def _merge_dataset(tx: Any, dataset: DatasetGraphRecord) -> None:
        tx.run(
            "MERGE (node:Dataset {dataset_id: $dataset_id}) "
            "SET node.owner_id = $owner_id, node.name = $name, node.active = $active, node.updated_at = datetime() "
            "ON CREATE SET node.created_at = datetime()",
            dataset_id=dataset.dataset_id,
            owner_id=dataset.owner_id,
            name=dataset.name,
            active=dataset.active,
        ).consume()

    @staticmethod
    def _merge_source(tx: Any, source: SourceFileGraphRecord) -> None:
        tx.run(
            "MATCH (dataset:Dataset {dataset_id: $dataset_id, owner_id: $owner_id}) "
            "MERGE (node:SourceFile {source_file_id: $source_file_id}) "
            "SET node.dataset_id = $dataset_id, node.owner_id = $owner_id, node.source_original_id = $source_original_id, node.release_key = $release_key, node.file_name = $file_name, "
            "node.object_key = $object_key, node.content_type = $content_type, node.source_hash = $source_hash, "
            "node.active = $active, node.updated_at = datetime() "
            "ON CREATE SET node.created_at = datetime() "
            "MERGE (dataset)-[:HAS_SOURCE]->(node)",
            dataset_id=source.dataset_id,
            owner_id=source.owner_id,
            source_file_id=source.source_file_id,
            source_original_id=source.source_original_id,
            release_key=source.release_key,
            file_name=source.file_name,
            object_key=source.object_key,
            content_type=source.content_type,
            source_hash=source.source_hash,
            active=source.active,
        ).consume()

    @staticmethod
    def _merge_content_units(tx: Any, rows: list[dict[str, Any]]) -> None:
        tx.run(
            "UNWIND $rows AS row "
            "MATCH (source:SourceFile {source_file_id: row.source_file_id, active: true}) "
            "MERGE (unit:ContentUnit {content_unit_id: row.content_unit_id}) "
            "SET unit.dataset_id = row.dataset_id, unit.owner_id = row.owner_id, unit.release_key = row.release_key, unit.source_file_id = row.source_file_id, "
            "unit.source_hash = row.source_hash, unit.chunk_index = row.chunk_index, unit.page = row.page, "
            "unit.text = row.text, unit.citation = row.citation, unit.active = row.active, unit.updated_at = datetime(), "
            "unit.embedding = CASE WHEN row.embedding IS NULL THEN unit.embedding ELSE row.embedding END "
            "ON CREATE SET unit.created_at = datetime() "
            "MERGE (source)-[:HAS_CONTENT]->(unit)",
            rows=rows,
        ).consume()

    @staticmethod
    def _mark_removed_units_inactive(tx: Any, source_file_id: str, active_ids: list[str]) -> int:
        result = tx.run(
            "MATCH (unit:ContentUnit {source_file_id: $source_file_id}) "
            "WHERE NOT unit.content_unit_id IN $active_ids AND unit.active = true "
            "SET unit.active = false, unit.updated_at = datetime() "
            "RETURN count(unit) AS count",
            source_file_id=source_file_id,
            active_ids=active_ids,
        ).single()
        return int(result["count"]) if result else 0

    @staticmethod
    def _merge_entities(tx: Any, rows: list[dict[str, Any]]) -> None:
        if not rows:
            return
        tx.run(
            "UNWIND $rows AS row "
            "MERGE (entity:Entity {entity_id: row.entity_id}) "
            "SET entity.dataset_id = row.dataset_id, entity.owner_id = row.owner_id, entity.release_key = row.release_key, entity.canonical_name = row.canonical_name, "
            "entity.entity_type = row.entity_type, entity.confidence = row.confidence, entity.active = true, entity.updated_at = datetime() "
            "ON CREATE SET entity.created_at = datetime()",
            rows=rows,
        ).consume()

    @staticmethod
    def _merge_mentions(tx: Any, rows: list[dict[str, Any]]) -> None:
        if not rows:
            return
        tx.run(
            "UNWIND $rows AS row "
            "MATCH (unit:ContentUnit {content_unit_id: row.content_unit_id, active: true}) "
            "MATCH (entity:Entity {entity_id: row.entity_id, active: true}) "
            "MERGE (unit)-[mention:MENTIONS]->(entity) "
            "SET mention.confidence = row.confidence, mention.source_hash = row.source_hash",
            rows=rows,
        ).consume()

    @staticmethod
    def _merge_facts(tx: Any, rows: list[dict[str, Any]]) -> None:
        if not rows:
            return
        tx.run(
            "UNWIND $rows AS row "
            "MATCH (unit:ContentUnit {content_unit_id: row.content_unit_id, active: true}) "
            "MATCH (subject:Entity {entity_id: row.subject_entity_id, active: true}) "
            "MATCH (object:Entity {entity_id: row.object_entity_id, active: true}) "
            "MERGE (fact:Fact {fact_id: row.fact_id}) "
            "SET fact.dataset_id = row.dataset_id, fact.owner_id = row.owner_id, fact.release_key = row.release_key, fact.predicate = row.predicate, "
            "fact.confidence = row.confidence, fact.citation = row.citation, fact.source_hash = row.source_hash, "
            "fact.active = true, fact.updated_at = datetime() "
            "ON CREATE SET fact.created_at = datetime() "
            "MERGE (unit)-[:ASSERTS]->(fact) "
            "MERGE (fact)-[:SUBJECT]->(subject) "
            "MERGE (fact)-[:OBJECT]->(object)",
            rows=rows,
        ).consume()


def _batches(values: Sequence[Any], size: int) -> Iterator[list[Any]]:
    for start in range(0, len(values), size):
        yield list(values[start : start + size])


def _semantic_graph_rows(
    units: Sequence[ContentUnit],
    by_content_unit: dict[str, SemanticExtraction],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]]]:
    entity_rows: dict[str, dict[str, Any]] = {}
    mentions: dict[tuple[str, str], dict[str, Any]] = {}
    facts: dict[str, dict[str, Any]] = {}
    for unit in units:
        extraction = by_content_unit.get(unit.content_unit_id)
        if extraction is None:
            continue
        local_entities: dict[str, str] = {}
        for entity in extraction.entities:
            entity_id = stable_id(unit.dataset_id, unit.release_key, entity.entity_type.casefold(), entity.name.casefold())
            local_entities[entity.name.casefold()] = entity_id
            entity_rows[entity_id] = {
                "entity_id": entity_id,
                "dataset_id": unit.dataset_id,
                "owner_id": unit.owner_id,
                "release_key": unit.release_key,
                "canonical_name": entity.name,
                "entity_type": entity.entity_type.casefold(),
                "confidence": entity.confidence,
            }
            mentions[(unit.content_unit_id, entity_id)] = {
                "content_unit_id": unit.content_unit_id,
                "entity_id": entity_id,
                "confidence": entity.confidence,
                "source_hash": unit.source_hash,
            }
        for fact in extraction.facts:
            subject_id = _ensure_semantic_entity(entity_rows, unit, fact.subject)
            object_id = _ensure_semantic_entity(entity_rows, unit, fact.object)
            mentions[(unit.content_unit_id, subject_id)] = {
                "content_unit_id": unit.content_unit_id,
                "entity_id": subject_id,
                "confidence": fact.confidence,
                "source_hash": unit.source_hash,
            }
            mentions[(unit.content_unit_id, object_id)] = {
                "content_unit_id": unit.content_unit_id,
                "entity_id": object_id,
                "confidence": fact.confidence,
                "source_hash": unit.source_hash,
            }
            fact_id = stable_id(unit.dataset_id, unit.content_unit_id, fact.subject.casefold(), fact.predicate.casefold(), fact.object.casefold())
            facts[fact_id] = {
                "fact_id": fact_id,
                "dataset_id": unit.dataset_id,
                "owner_id": unit.owner_id,
                "release_key": unit.release_key,
                "content_unit_id": unit.content_unit_id,
                "subject_entity_id": subject_id,
                "object_entity_id": object_id,
                "predicate": fact.predicate.casefold(),
                "confidence": fact.confidence,
                "citation": unit.citation,
                "source_hash": unit.source_hash,
            }
    return list(entity_rows.values()), list(mentions.values()), list(facts.values())


def _ensure_semantic_entity(rows: dict[str, dict[str, Any]], unit: ContentUnit, name: str) -> str:
    entity_id = stable_id(unit.dataset_id, unit.release_key, "concept", name.casefold())
    rows.setdefault(
        entity_id,
        {
            "entity_id": entity_id,
            "dataset_id": unit.dataset_id,
            "owner_id": unit.owner_id,
            "release_key": unit.release_key,
            "canonical_name": name,
            "entity_type": "concept",
            "confidence": 0.5,
        },
    )
    return entity_id