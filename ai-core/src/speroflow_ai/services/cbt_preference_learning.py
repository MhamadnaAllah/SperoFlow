"""Preference learning for CBT educational-resource ordering.

This module deliberately learns resource helpfulness preferences only. It does
not infer clinical state, treatment effectiveness, risk, or diagnosis.
"""

from __future__ import annotations

import hashlib
import hmac
import logging
import random
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Literal, Protocol

from neo4j import AsyncDriver

from speroflow_ai.services.cbt_retrieval import CBTResource


logger = logging.getLogger("speroflow.services.cbt_preference_learning")

FeedbackValue = Literal["helpful", "not_helpful"]
NEUTRAL_PREFERENCE_SCORE = 0.5
FEEDBACK_STEP = 0.1
SCORE_FLOOR = 0.0
SCORE_CEILING = 1.0
MAX_RERANK_WEIGHT = 0.30
MAX_EXPLORATION_RATE = 0.20


class RandomLike(Protocol):
    """Small protocol for deterministic tests and secure production randomness."""

    def random(self) -> float:
        ...

    def choice(self, seq):  # type: ignore[no-untyped-def]
        ...


@dataclass(frozen=True)
class CBTPreferenceRecord:
    """Stored explicit preference aggregate for one user/resource pair."""

    resource_key: str
    preference_score: float
    feedback_count: int
    helpful_count: int
    not_helpful_count: int


@dataclass(frozen=True)
class CBTPreferenceLearningMetadata:
    """Audit-safe metadata about whether ranking personalization was applied."""

    enabled: bool
    applied: bool
    policy: str = "disabled"
    reason: str = ""
    candidate_count: int = 0
    personalized_count: int = 0
    exploration_applied: bool = False


@dataclass(frozen=True)
class CBTPreferenceFeedbackResult:
    """Result after recording explicit resource-helpfulness feedback."""

    resource_key: str
    preference_score: float
    feedback_count: int
    helpful_count: int
    not_helpful_count: int
    last_feedback: FeedbackValue


class CBTPreferenceLearner:
    """Bounded preference learner for CBT educational resources.

    The learner can only reorder resources that were already returned by the
    source-grounded retrieval pipeline. It never introduces new resources or
    writes user-state facts into the CBT knowledge graph.
    """

    def __init__(
        self,
        *,
        driver: AsyncDriver,
        database: str,
        hash_salt: str,
        rerank_weight: float = 0.15,
        exploration_rate: float = 0.05,
        min_feedback_events: int = 2,
        rng: RandomLike | None = None,
    ) -> None:
        if not 0.0 <= rerank_weight <= MAX_RERANK_WEIGHT:
            raise ValueError(
                f"CBT preference rerank_weight must be between 0.0 and {MAX_RERANK_WEIGHT}"
            )
        if not 0.0 <= exploration_rate <= MAX_EXPLORATION_RATE:
            raise ValueError(
                "CBT preference exploration_rate must be between "
                f"0.0 and {MAX_EXPLORATION_RATE}"
            )
        if min_feedback_events < 0:
            raise ValueError("CBT preference min_feedback_events must be non-negative")

        self._driver = driver
        self._database = database
        self._hash_salt = hash_salt
        self._rerank_weight = rerank_weight
        self._exploration_rate = exploration_rate
        self._min_feedback_events = min_feedback_events
        self._rng = rng or random.SystemRandom()

    async def rerank_resources(
        self,
        *,
        user_id: str,
        resources: list[CBTResource],
    ) -> tuple[list[CBTResource], CBTPreferenceLearningMetadata]:
        """Return the same resources, optionally reordered by explicit preferences."""
        if not resources:
            return resources, CBTPreferenceLearningMetadata(
                enabled=True,
                applied=False,
                policy="preference_rerank",
                reason="no_candidates",
            )

        user_hash = self._hash_user_id(user_id)
        resource_keys = [self.resource_key(resource.node_id, resource.section_id) for resource in resources]
        preferences = await self._fetch_preferences(user_hash, resource_keys)

        scored: list[tuple[float, int, CBTResource, CBTPreferenceRecord | None]] = []
        personalized_count = 0
        for index, resource in enumerate(resources):
            key = self.resource_key(resource.node_id, resource.section_id)
            preference = preferences.get(key)
            preference_score = NEUTRAL_PREFERENCE_SCORE
            if preference and preference.feedback_count >= self._min_feedback_events:
                preference_score = preference.preference_score
                personalized_count += 1
            adjustment = self._rerank_weight * (preference_score - NEUTRAL_PREFERENCE_SCORE)
            scored.append((resource.score + adjustment, index, resource, preference))

        scored.sort(key=lambda item: (-item[0], item[1]))
        exploration_applied = self._maybe_apply_exploration(scored)
        reranked = [item[2] for item in scored]

        return reranked, CBTPreferenceLearningMetadata(
            enabled=True,
            applied=personalized_count > 0 or exploration_applied,
            policy="preference_explore" if exploration_applied else "preference_rerank",
            reason="ok",
            candidate_count=len(resources),
            personalized_count=personalized_count,
            exploration_applied=exploration_applied,
        )

    async def record_feedback(
        self,
        *,
        user_id: str,
        resource_node_id: str,
        section_id: str = "",
        feedback: FeedbackValue,
        resource_title: str = "",
        domain_id: str = "",
        source_reference: str = "",
    ) -> CBTPreferenceFeedbackResult:
        """Persist explicit helpful/not-helpful feedback for a returned resource."""
        normalized_node_id = resource_node_id.strip()
        normalized_section_id = section_id.strip()
        if not normalized_node_id:
            raise ValueError("resource_node_id is required")
        if feedback not in ("helpful", "not_helpful"):
            raise ValueError("feedback must be 'helpful' or 'not_helpful'")

        user_hash = self._hash_user_id(user_id)
        resource_key = self.resource_key(normalized_node_id, normalized_section_id)
        preference_id = self.preference_id(user_hash, resource_key)
        now = datetime.now(timezone.utc).isoformat()

        return await self._upsert_feedback(
            preference_id=preference_id,
            user_hash=user_hash,
            resource_key=resource_key,
            resource_node_id=normalized_node_id,
            section_id=normalized_section_id,
            feedback=feedback,
            resource_title=resource_title.strip(),
            domain_id=domain_id.strip(),
            source_reference=source_reference.strip(),
            now=now,
        )

    @staticmethod
    def resource_key(resource_node_id: str, section_id: str = "") -> str:
        """Build a stable key for document-level or section-level resources."""
        node = resource_node_id.strip()
        section = section_id.strip()
        return f"{node}::{section}" if section else node

    @staticmethod
    def preference_id(user_hash: str, resource_key: str) -> str:
        """Build an opaque preference identifier."""
        return hashlib.sha256(f"{user_hash}::{resource_key}".encode("utf-8")).hexdigest()

    @staticmethod
    def clamp_score(score: float) -> float:
        return max(SCORE_FLOOR, min(SCORE_CEILING, score))

    def _hash_user_id(self, user_id: str) -> str:
        normalized = user_id.strip()
        if not normalized:
            raise ValueError("authenticated user id is required")
        if not self._hash_salt:
            raise ValueError("CBT_PREFERENCE_HASH_SALT must be configured")
        return hmac.new(
            self._hash_salt.encode("utf-8"),
            normalized.encode("utf-8"),
            hashlib.sha256,
        ).hexdigest()

    async def _fetch_preferences(
        self,
        user_hash: str,
        resource_keys: list[str],
    ) -> dict[str, CBTPreferenceRecord]:
        if not resource_keys:
            return {}

        cypher = """
            MATCH (p:CBTResourcePreference)
            WHERE p.user_hash = $user_hash
              AND p.resource_key IN $resource_keys
            RETURN
                p.resource_key AS resource_key,
                coalesce(p.preference_score, 0.5) AS preference_score,
                coalesce(p.feedback_count, 0) AS feedback_count,
                coalesce(p.helpful_count, 0) AS helpful_count,
                coalesce(p.not_helpful_count, 0) AS not_helpful_count
        """
        async with self._driver.session(database=self._database) as session:
            result = await session.run(
                cypher,
                user_hash=user_hash,
                resource_keys=resource_keys,
            )
            rows = await result.data()

        records: dict[str, CBTPreferenceRecord] = {}
        for row in rows:
            resource_key = str(row.get("resource_key", ""))
            if not resource_key:
                continue
            records[resource_key] = CBTPreferenceRecord(
                resource_key=resource_key,
                preference_score=self.clamp_score(float(row.get("preference_score", 0.5))),
                feedback_count=int(row.get("feedback_count", 0)),
                helpful_count=int(row.get("helpful_count", 0)),
                not_helpful_count=int(row.get("not_helpful_count", 0)),
            )
        return records

    async def _upsert_feedback(
        self,
        *,
        preference_id: str,
        user_hash: str,
        resource_key: str,
        resource_node_id: str,
        section_id: str,
        feedback: FeedbackValue,
        resource_title: str,
        domain_id: str,
        source_reference: str,
        now: str,
    ) -> CBTPreferenceFeedbackResult:
        delta = FEEDBACK_STEP if feedback == "helpful" else -FEEDBACK_STEP
        cypher = """
            MERGE (p:CBTResourcePreference {preference_id: $preference_id})
            ON CREATE SET
                p.user_hash = $user_hash,
                p.resource_key = $resource_key,
                p.resource_node_id = $resource_node_id,
                p.section_id = $section_id,
                p.preference_score = 0.5,
                p.feedback_count = 0,
                p.helpful_count = 0,
                p.not_helpful_count = 0,
                p.created_at = datetime($now)
            WITH p,
                 coalesce(p.preference_score, 0.5) AS current_score,
                 coalesce(p.feedback_count, 0) AS current_feedback_count,
                 coalesce(p.helpful_count, 0) AS current_helpful_count,
                 coalesce(p.not_helpful_count, 0) AS current_not_helpful_count
            SET
                p.preference_score =
                    CASE
                        WHEN current_score + $delta < 0.0 THEN 0.0
                        WHEN current_score + $delta > 1.0 THEN 1.0
                        ELSE current_score + $delta
                    END,
                p.feedback_count = current_feedback_count + 1,
                p.helpful_count = current_helpful_count + $helpful_increment,
                p.not_helpful_count = current_not_helpful_count + $not_helpful_increment,
                p.last_feedback = $feedback,
                p.last_feedback_at = datetime($now),
                p.updated_at = datetime($now),
                p.resource_title = $resource_title,
                p.domain_id = $domain_id,
                p.source_reference = $source_reference
            RETURN
                p.resource_key AS resource_key,
                p.preference_score AS preference_score,
                p.feedback_count AS feedback_count,
                p.helpful_count AS helpful_count,
                p.not_helpful_count AS not_helpful_count,
                p.last_feedback AS last_feedback
        """
        async with self._driver.session(database=self._database) as session:
            result = await session.run(
                cypher,
                preference_id=preference_id,
                user_hash=user_hash,
                resource_key=resource_key,
                resource_node_id=resource_node_id,
                section_id=section_id,
                delta=delta,
                helpful_increment=1 if feedback == "helpful" else 0,
                not_helpful_increment=1 if feedback == "not_helpful" else 0,
                feedback=feedback,
                resource_title=resource_title,
                domain_id=domain_id,
                source_reference=source_reference,
                now=now,
            )
            record = await result.single()

        if record is None:
            raise RuntimeError("CBT preference feedback was not recorded")

        return CBTPreferenceFeedbackResult(
            resource_key=str(record["resource_key"]),
            preference_score=self.clamp_score(float(record["preference_score"])),
            feedback_count=int(record["feedback_count"]),
            helpful_count=int(record["helpful_count"]),
            not_helpful_count=int(record["not_helpful_count"]),
            last_feedback=record["last_feedback"],
        )

    def _maybe_apply_exploration(
        self,
        scored: list[tuple[float, int, CBTResource, CBTPreferenceRecord | None]],
    ) -> bool:
        if len(scored) < 2 or self._exploration_rate <= 0:
            return False
        if self._rng.random() >= self._exploration_rate:
            return False

        eligible_indexes = [
            index
            for index, item in enumerate(scored[1:], start=1)
            if item[3] is None or item[3].feedback_count < self._min_feedback_events
        ]
        if not eligible_indexes:
            return False

        selected_index = self._rng.choice(eligible_indexes)
        selected = scored.pop(selected_index)
        scored.insert(0, selected)
        return True
