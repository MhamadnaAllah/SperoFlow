"""
SperoFlow AI Service — Configuration.

All configuration is supplied by the process environment and Docker secrets,
then validated at startup using Pydantic Settings v2.
"""

from __future__ import annotations

from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Application settings loaded from environment variables."""

    model_config = SettingsConfigDict(extra="ignore")

    # ── Application ──────────────────────────────────────────────
    app_env: str = "development"

    # ── Neo4j ───────────────────────────────────────────────
    neo4j_uri: str = "bolt://neo4j:7687"
    neo4j_user: str = "neo4j"
    neo4j_password: str = ""
    neo4j_database: str = "neo4j"

    # The isolated knowledge graph is physically separate. The AI API receives
    # only a read-only identity and a public key for short-lived access grants.
    knowledge_neo4j_uri: str = "bolt://knowledge-neo4j:7687"
    knowledge_neo4j_user: str = "knowledge-reader"
    knowledge_neo4j_password: str = ""
    knowledge_neo4j_database: str = "neo4j"
    knowledge_grant_issuer: str = "speroflow-knowledge-api"
    knowledge_grant_audience: str = "speroflow-ai"
    knowledge_grant_public_key_path: str = "/run/secrets/knowledge_grant_public_key"
    knowledge_grant_clock_skew_seconds: int = Field(default=30, ge=0, le=120)

    # ── LLM ───────────────────────
    llm_provider: str = "bedrock"
    llm_api_base: str = "http://llm-runtime:8000/v1"
    llm_api_key: str = ""
    llm_model: str = "google.gemma-4-31b"
    llm_temperature: float = 0.0

    # Model Routing (Triage Architecture)
    router_provider: str = "bedrock"    # "bedrock", "local", or "keyword"
    router_model_id: str = "google.gemma-4-31b"
    cbt_model_id: str = "zai.glm-5"
    cbt_gemma_model_id: str = "google.gemma-4-31b"
    scheduler_model_id: str = "google.gemma-4-31b"

    # AWS Bedrock
    bedrock_region: str = "us-east-1"
    bedrock_connect_timeout: float = 5.0
    bedrock_read_timeout: float = 30.0
    bedrock_max_pool_connections: int = 25
    aws_access_key_id: str = ""
    aws_secret_access_key: str = ""
    aws_session_token: str = ""

    # ── Embedding ────────────────────────────────────────────────
    embedding_model: str = "cohere.embed-v4:0"
    embedding_dimensions: int = 1024

    # ── RAG Tuning ───────────────────────────────────────────────
    rag_vector_index_topic: str = "topic_embedding_index"
    rag_vector_index_subtopic: str = "subtopic_embedding_index"
    rag_top_k: int = 5
    rag_traversal_depth: int = 2
    dataset_vector_index: str = "dataset_content_embedding_index"
    dataset_retrieval_top_k: int = Field(default=5, ge=1, le=12)
    dataset_extraction_model: str = "google.gemma-4-31b"

    # Private S3-compatible source storage and asynchronous PDF OCR. The worker
    # receives these settings only; the browser never receives these credentials.
    object_storage_endpoint_url: str = ""
    object_storage_bucket: str = "speroflow-documents"
    object_storage_access_key: str = ""
    object_storage_secret_key: str = ""
    object_storage_use_ssl: bool = False
    textract_sqs_queue_url: str = ""
    textract_sns_topic_arn: str = ""
    textract_role_arn: str = ""

    # CBT educational-resource graph. All release gates remain false until
    # licence, clinical review, and production authorization are confirmed.
    cbt_source_data_dir: str = "./knowledge-base/cbt/source"
    cbt_graph_data_dir: str = "./knowledge-base/cbt/graph"
    cbt_feature_enabled: bool = False
    cbt_content_license_approved: bool = False
    cbt_clinical_review_approved: bool = False
    cbt_chat_augmentation_enabled: bool = False
    cbt_require_verified_auth: bool = True
    cbt_vector_index: str = "cbtsection_embedding_index"
    cbt_document_vector_index: str = "cbtdocument_embedding_index"
    cbt_resource_top_k: int = Field(default=3, ge=1, le=10)
    cbt_min_similarity: float = 0.35
    cbt_content_excerpt_enabled: bool = False
    cbt_max_excerpt_chars: int = Field(default=500, ge=0, le=1200)
    cbt_preference_learning_enabled: bool = False
    cbt_preference_learning_approved: bool = False
    cbt_preference_hash_salt: str = ""
    cbt_preference_rerank_weight: float = Field(default=0.15, ge=0.0, le=0.30)
    cbt_preference_exploration_rate: float = Field(default=0.05, ge=0.0, le=0.20)
    cbt_preference_min_feedback_events: int = Field(default=2, ge=0, le=20)

    # Auto-Scheduler Agent
    scheduler_day_start: str = "08:00"
    scheduler_day_end: str = "22:00"
    scheduler_buffer_minutes: int = 15
    scheduler_min_slot_minutes: int = 20
    scheduler_q1_overflow: int = 5
    scheduler_stress_reduction: float = 0.25
    scheduler_timezone: str = "UTC"

    # ── Ingestion Tuning ─────────────────────────────────────────
    batch_size: int = 500
    roadmaps_base_dir: str = "./knowledge-base/roadmaps"

    # ── Node Classification ──────────────────────────────────────
    meaningful_node_types: frozenset[str] = frozenset({"topic", "subtopic"})
    ui_layout_node_types: frozenset[str] = frozenset({
        "horizontal", "vertical", "section", "label",
        "paragraph", "button", "title", "linksgroup",
    })
    edge_style_map: dict[str, str] = {"solid": "LEADS_TO", "dashed": "RELATED_TO"}
    default_relationship_type: str = "LEADS_TO"

    @property
    def cbt_release_enabled(self) -> bool:
        """Return true only after every CBT production release gate is enabled."""
        return (
            self.cbt_feature_enabled
            and self.cbt_content_license_approved
            and self.cbt_clinical_review_approved
        )

    @property
    def cbt_preference_learning_release_enabled(self) -> bool:
        """Return true only when governed CBT preference learning may run."""
        return (
            self.cbt_release_enabled
            and self.cbt_preference_learning_enabled
            and self.cbt_preference_learning_approved
        )


@lru_cache
def get_settings() -> Settings:
    """Cached singleton — parsed once, reused on every request."""
    return Settings()
