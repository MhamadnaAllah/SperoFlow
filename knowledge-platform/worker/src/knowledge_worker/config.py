"""Configuration owned exclusively by the isolated knowledge worker."""

from __future__ import annotations

from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Worker configuration loaded from its private environment and secrets."""

    model_config = SettingsConfigDict(extra="ignore")

    knowledge_api_url: str = "http://knowledge-api:8080"
    knowledge_redis_url: str = "redis://knowledge-redis:6379/0"
    knowledge_jobs_stream: str = "speroflow:knowledge:jobs"
    knowledge_jobs_group: str = "speroflow-knowledge-workers"
    knowledge_worker_consumer: str = ""
    knowledge_reclaim_idle_ms: int = Field(default=300_000, ge=60_000, le=3_600_000)
    knowledge_lease_heartbeat_seconds: int = Field(default=120, ge=30, le=600)

    knowledge_neo4j_uri: str = "bolt://knowledge-neo4j:7687"
    knowledge_neo4j_user: str = "knowledge-writer"
    knowledge_neo4j_password: str = ""
    knowledge_neo4j_database: str = "neo4j"

    knowledge_object_storage_endpoint_url: str = "http://knowledge-object-storage:9000"
    knowledge_object_storage_bucket: str = "speroflow-knowledge"
    knowledge_object_storage_access_key: str = ""
    knowledge_object_storage_secret_key: str = ""
    knowledge_object_storage_use_ssl: bool = False

    bedrock_region: str = "us-east-1"
    knowledge_extraction_model: str = "amazon.nova-lite-v1:0"
    embedding_model: str = "BAAI/bge-m3"
    embedding_dimensions: int = Field(default=1024, ge=1)

    textract_sqs_queue_url: str = ""
    textract_sns_topic_arn: str = ""
    textract_role_arn: str = ""

    @property
    def object_storage_endpoint_url(self) -> str:
        return self.knowledge_object_storage_endpoint_url

    @property
    def object_storage_bucket(self) -> str:
        return self.knowledge_object_storage_bucket

    @property
    def object_storage_access_key(self) -> str:
        return self.knowledge_object_storage_access_key

    @property
    def object_storage_secret_key(self) -> str:
        return self.knowledge_object_storage_secret_key

    @property
    def object_storage_use_ssl(self) -> bool:
        return self.knowledge_object_storage_use_ssl

    @property
    def dataset_extraction_model(self) -> str:
        return self.knowledge_extraction_model

    @property
    def llm_model(self) -> str:
        return self.knowledge_extraction_model

    @property
    def neo4j_uri(self) -> str:
        return self.knowledge_neo4j_uri

    @property
    def neo4j_user(self) -> str:
        return self.knowledge_neo4j_user

    @property
    def neo4j_password(self) -> str:
        return self.knowledge_neo4j_password

    @property
    def neo4j_database(self) -> str:
        return self.knowledge_neo4j_database


@lru_cache
def get_settings() -> Settings:
    return Settings()