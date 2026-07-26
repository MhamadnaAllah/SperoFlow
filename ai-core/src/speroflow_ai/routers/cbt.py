"""Feature-gated CBT educational-resource retrieval endpoint."""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, HTTPException
from speroflow_ai.config import Settings
from speroflow_ai.dependencies import get_app_settings, get_neo4j
from speroflow_ai.models.requests import CBTAdviceRequest, CBTPreferenceFeedbackRequest, CBTQueryRequest
from speroflow_ai.models.responses import (
    CBTAdviceResponse,
    CBTAdviceSourceResponse,
    CBTHabitResponse,
    CBTPreferenceFeedbackResponse,
    CBTPersonalizationResponse,
    CBTQueryResponse,
    CBTResourceResponse,
    ErrorResponse,
)
from speroflow_ai.services.cbt_preference_learning import (
    CBTPreferenceLearner,
    CBTPreferenceLearningMetadata,
)
from speroflow_ai.services.cbt_rag import CBTEducationalRAG, EDUCATIONAL_DISCLAIMER
from speroflow_ai.services.cbt_retrieval import CBTResource
from speroflow_ai.services.cbt_retrieval import CBTResourceRetriever
from speroflow_ai.services.cbt_safety import evaluate_urgent_support_signal
from speroflow_ai.service_auth import require_verified_user


logger = logging.getLogger("speroflow.routers.cbt")
router = APIRouter(prefix="/api/cbt", tags=["CBT Educational Resources"])

@router.post(
    "/query",
    response_model=CBTQueryResponse,
    responses={
        503: {"model": ErrorResponse, "description": "CBT feature is not released"},
        500: {"model": ErrorResponse, "description": "CBT retrieval unavailable"},
    },
    summary="Retrieve cited CBT educational resources",
)
async def query_cbt_resources(
    request: CBTQueryRequest,
    current_user: dict = Depends(require_verified_user),
    settings: Settings = Depends(get_app_settings),
) -> CBTQueryResponse:
    """Return source-grounded educational resources without clinical synthesis."""
    # Urgent-support routing is intentionally available even while the
    # educational feature is disabled. It does not access embeddings or Neo4j.
    safety = evaluate_urgent_support_signal(request.query)
    if safety.should_escalate:
        return CBTQueryResponse(
            status="urgent_support",
            disclaimer=EDUCATIONAL_DISCLAIMER,
            urgent_support=True,
            urgent_support_message=safety.message,
            resources=[],
        )

    if not settings.cbt_release_enabled:
        raise HTTPException(
            status_code=503,
            detail="CBT educational-resource retrieval is not enabled for this environment.",
        )
    if settings.cbt_require_verified_auth and not current_user.get("auth_verified"):
        raise HTTPException(
            status_code=503,
            detail="CBT educational-resource retrieval requires verified production authentication.",
        )

    try:
        driver = await get_neo4j(settings)
        excerpt_characters = (
            settings.cbt_max_excerpt_chars if settings.cbt_content_excerpt_enabled else 0
        )
        result = await CBTEducationalRAG(
            CBTResourceRetriever(
                driver=driver,
                embedding_model=settings.embedding_model,
                index_name=settings.cbt_vector_index,
                document_index_name=settings.cbt_document_vector_index,
                database=settings.neo4j_database,
                min_similarity=settings.cbt_min_similarity,
            )
        ).retrieve(
            query=request.query,
            top_k=request.top_k,
            domain_ids=request.domain_ids,
            excerpt_characters=excerpt_characters,
        )
        resources, personalization = await _maybe_apply_preference_learning(
            resources=result.resources,
            current_user=current_user,
            settings=settings,
            driver=driver,
        )
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    except Exception:
        logger.exception("CBT educational-resource retrieval failed")
        raise HTTPException(status_code=500, detail="CBT resource retrieval is unavailable.")

    logger.info(
        "CBT resource query completed: resources=%d personalization=%s",
        len(resources),
        personalization.policy,
    )
    return CBTQueryResponse(
        status=result.status,
        disclaimer=result.disclaimer,
        urgent_support=result.urgent_support,
        urgent_support_message=result.urgent_support_message,
        personalization=_to_personalization_response(personalization),
        resources=[
            CBTResourceResponse(
                node_id=resource.node_id,
                title=resource.title,
                domain_id=resource.domain_id,
                domain_name=resource.domain_name,
                document_type=resource.document_type,
                source_reference=resource.source_relpath,
                source_url=resource.source_url,
                score=resource.score,
                retrieval_scope=resource.retrieval_scope,
                section_id=resource.section_id,
                section_title=resource.section_title,
                parent_section_title=resource.parent_section_title,
                source_anchor=resource.source_anchor,
                content_excerpt=resource.content_excerpt,
                reviewed_concepts=list(resource.reviewed_concepts),
            )
            for resource in resources
        ],
    )


@router.post(
    "/preference-feedback",
    response_model=CBTPreferenceFeedbackResponse,
    responses={
        503: {"model": ErrorResponse, "description": "CBT preference learning is not released"},
        422: {"model": ErrorResponse, "description": "Invalid feedback payload"},
        500: {"model": ErrorResponse, "description": "CBT preference feedback unavailable"},
    },
    summary="Record explicit CBT educational-resource helpfulness feedback",
)
async def record_cbt_preference_feedback(
    request: CBTPreferenceFeedbackRequest,
    current_user: dict = Depends(require_verified_user),
    settings: Settings = Depends(get_app_settings),
) -> CBTPreferenceFeedbackResponse:
    """Record resource helpfulness only after CBT and preference gates are open."""
    if not settings.cbt_release_enabled:
        raise HTTPException(
            status_code=503,
            detail="CBT educational-resource retrieval is not enabled for this environment.",
        )
    if not settings.cbt_preference_learning_release_enabled:
        raise HTTPException(
            status_code=503,
            detail="CBT preference learning is not enabled for this environment.",
        )
    if settings.cbt_require_verified_auth and not current_user.get("auth_verified"):
        raise HTTPException(
            status_code=503,
            detail="CBT preference learning requires verified production authentication.",
        )
    if not settings.cbt_preference_hash_salt:
        raise HTTPException(
            status_code=503,
            detail="CBT preference learning is not fully configured.",
        )

    user_id = _current_user_id(current_user)
    if not user_id:
        raise HTTPException(
            status_code=503,
            detail="CBT preference learning requires an authenticated user id.",
        )

    try:
        driver = await get_neo4j(settings)
        learner = _build_preference_learner(driver=driver, settings=settings)
        result = await learner.record_feedback(
            user_id=user_id,
            resource_node_id=request.resource_node_id,
            section_id=request.section_id,
            feedback=request.feedback,
            resource_title=request.resource_title,
            domain_id=request.domain_id,
            source_reference=request.source_reference,
        )
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    except Exception:
        logger.exception("CBT preference feedback failed")
        raise HTTPException(status_code=500, detail="CBT preference feedback is unavailable.")

    logger.info("CBT preference feedback recorded: resource_key=%s", result.resource_key)
    return CBTPreferenceFeedbackResponse(
        status="ok",
        resource_key=result.resource_key,
        preference_score=result.preference_score,
        feedback_count=result.feedback_count,
        helpful_count=result.helpful_count,
        not_helpful_count=result.not_helpful_count,
        message="Preference feedback recorded for educational resource ordering.",
    )


async def _maybe_apply_preference_learning(
    *,
    resources: list[CBTResource],
    current_user: dict,
    settings: Settings,
    driver,
) -> tuple[list[CBTResource], CBTPreferenceLearningMetadata]:
    """Apply optional bounded preference reranking without failing base retrieval."""
    if not settings.cbt_preference_learning_enabled:
        return resources, CBTPreferenceLearningMetadata(
            enabled=False,
            applied=False,
            reason="disabled",
            candidate_count=len(resources),
        )
    if not settings.cbt_preference_learning_release_enabled:
        return resources, CBTPreferenceLearningMetadata(
            enabled=False,
            applied=False,
            reason="not_approved",
            candidate_count=len(resources),
        )
    if settings.cbt_require_verified_auth and not current_user.get("auth_verified"):
        return resources, CBTPreferenceLearningMetadata(
            enabled=False,
            applied=False,
            reason="requires_verified_auth",
            candidate_count=len(resources),
        )
    if not settings.cbt_preference_hash_salt:
        return resources, CBTPreferenceLearningMetadata(
            enabled=False,
            applied=False,
            reason="missing_hash_salt",
            candidate_count=len(resources),
        )

    user_id = _current_user_id(current_user)
    if not user_id:
        return resources, CBTPreferenceLearningMetadata(
            enabled=False,
            applied=False,
            reason="missing_user_id",
            candidate_count=len(resources),
        )

    try:
        learner = _build_preference_learner(driver=driver, settings=settings)
        return await learner.rerank_resources(user_id=user_id, resources=resources)
    except Exception:
        logger.exception("CBT preference reranking failed; returning base retrieval order")
        return resources, CBTPreferenceLearningMetadata(
            enabled=False,
            applied=False,
            reason="unavailable",
            candidate_count=len(resources),
        )


def _build_preference_learner(*, driver, settings: Settings) -> CBTPreferenceLearner:
    return CBTPreferenceLearner(
        driver=driver,
        database=settings.neo4j_database,
        hash_salt=settings.cbt_preference_hash_salt,
        rerank_weight=settings.cbt_preference_rerank_weight,
        exploration_rate=settings.cbt_preference_exploration_rate,
        min_feedback_events=settings.cbt_preference_min_feedback_events,
    )


def _current_user_id(current_user: dict) -> str:
    return str(current_user.get("sub") or current_user.get("user_id") or "").strip()


def _to_personalization_response(
    metadata: CBTPreferenceLearningMetadata,
) -> CBTPersonalizationResponse:
    return CBTPersonalizationResponse(
        enabled=metadata.enabled,
        applied=metadata.applied,
        policy=metadata.policy,
        reason=metadata.reason,
        candidate_count=metadata.candidate_count,
        personalized_count=metadata.personalized_count,
        exploration_applied=metadata.exploration_applied,
    )


# ── Singleton pipeline instance ──────────────────────────────────────────────

_advice_pipeline = None


def _get_advice_pipeline(settings: Settings):
    global _advice_pipeline
    if _advice_pipeline is None:
        from speroflow_ai.services.cbt_graph_rag import CBTGraphRAGPipeline
        _advice_pipeline = CBTGraphRAGPipeline(settings)
    return _advice_pipeline


@router.post(
    "/advice",
    response_model=CBTAdviceResponse,
    responses={
        500: {"model": ErrorResponse, "description": "CBT advice generation failed"},
    },
    summary="Generate CBT-grounded advice and micro-habit recommendations",
)
async def generate_cbt_advice(
    request: CBTAdviceRequest,
    current_user: dict = Depends(require_verified_user),
    settings: Settings = Depends(get_app_settings),
) -> CBTAdviceResponse:
    """Retrieve CBT content from the knowledge graph and synthesize
    personalized advice and actionable micro-habits using Gemma4 via Bedrock.

    Pipeline: User text → Safety Check → Vector Retrieval → Graph Traversal
              → Gemma4 LLM Synthesis → Grounded Advice + Habits
    """
    logger.info(
        "CBT advice request from user %s: %s",
        current_user.get("sub", "?"), request.text[:80],
    )

    try:
        pipeline = _get_advice_pipeline(settings)
        result = await pipeline.generate_advice(
            user_text=request.text,
            emotions=request.emotions,
            domain_filter=request.domain_ids or None,
            top_k=request.top_k,
        )
    except Exception as exc:
        logger.error("CBT advice generation failed: %s", exc, exc_info=True)
        raise HTTPException(status_code=500, detail=f"CBT advice generation failed: {exc}")

    return CBTAdviceResponse(
        status=result.status,
        advice=result.advice,
        habits=[
            CBTHabitResponse(
                title=h.title,
                description=h.description,
                frequency=h.frequency,
                duration_minutes=h.duration_minutes,
                domain=h.domain,
                source_title=h.source_title,
            )
            for h in result.habits
        ],
        detected_themes=result.detected_themes,
        sources=[
            CBTAdviceSourceResponse(
                node_id=s.node_id,
                title=s.title,
                domain=s.domain,
                document_type=s.document_type,
                score=s.score,
                content_excerpt=s.content_excerpt,
            )
            for s in result.sources
        ],
        model_used=result.model_used,
        disclaimer=result.disclaimer,
        urgent_support=result.urgent_support,
        urgent_support_message=result.urgent_support_message,
        error=result.error,
    )
