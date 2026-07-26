"""Private GraphRAG query routes for roadmap and selected dataset scopes."""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, HTTPException

from speroflow_ai.config import Settings
from speroflow_ai.db.neo4j_client import get_driver, get_knowledge_driver
from speroflow_ai.dependencies import get_app_settings
from speroflow_ai.models.requests import QueryRequest
from speroflow_ai.models.responses import ErrorResponse, QueryResponse, VectorMatchResponse
from speroflow_ai.service_auth import require_verified_user
from speroflow_ai.services.dataset_retrieval import DatasetGraphRAG
from speroflow_ai.services.knowledge_grants import validate_knowledge_access_grant

logger = logging.getLogger("speroflow.routers.query")

router = APIRouter(prefix="/api/query", tags=["Hybrid RAG Query"])

_pipeline = None


def get_rag_pipeline(settings: Settings):
    """Keep roadmap GraphRAG lazy and unchanged for the legacy roadmap scope."""
    global _pipeline
    if _pipeline is None:
        from speroflow_ai.services.graph_rag import HybridRAGPipeline

        _pipeline = HybridRAGPipeline(
            neo4j_uri=settings.neo4j_uri,
            neo4j_user=settings.neo4j_user,
            neo4j_password=settings.neo4j_password,
            llm_provider=settings.llm_provider,
            llm_api_base=settings.llm_api_base,
            llm_api_key=settings.llm_api_key,
            llm_model=settings.llm_model,
            llm_temperature=settings.llm_temperature,
            bedrock_region=settings.bedrock_region,
            embedding_model=settings.embedding_model,
            vector_index_name=settings.rag_vector_index_topic,
            top_k=settings.rag_top_k,
            traversal_depth=settings.rag_traversal_depth,
        )
    return _pipeline


_get_pipeline = get_rag_pipeline


@router.post(
    "",
    response_model=QueryResponse,
    responses={400: {"model": ErrorResponse}, 500: {"model": ErrorResponse}},
    summary="Query a roadmap graph or explicitly selected owner-scoped datasets",
)
async def query_knowledge_graph(
    request: QueryRequest,
    current_user: dict = Depends(require_verified_user),
    settings: Settings = Depends(get_app_settings),
) -> QueryResponse:
    logger.info(
        "Query scope=%s strategy=%s user=%s question=%s",
        request.scope,
        request.strategy,
        current_user.get("sub", "?"),
        request.question[:80],
    )

    if request.scope == "dataset":
        return await _query_selected_datasets(request, current_user, settings)

    try:
        pipeline = get_rag_pipeline(settings)
        result = await pipeline.query(
            question=request.question,
            strategy=request.strategy,
            top_k=request.top_k,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as exc:
        logger.error("Roadmap query failed: %s", exc, exc_info=True)
        raise HTTPException(status_code=500, detail="Roadmap query failed.") from exc

    return QueryResponse(
        answer=result.answer,
        strategy_used=result.strategy_used,
        sources=result.sources,
        vector_matches=[
            VectorMatchResponse(
                node_id=match.node_id,
                label_text=match.label_text,
                roadmap_name=match.roadmap_name,
                score=match.score,
                content_snippet=match.content_snippet,
                neighbors=match.neighbors,
            )
            for match in result.vector_matches
        ],
        generated_cypher=result.generated_cypher,
        error=result.error,
        scope="roadmap",
    )


async def _query_selected_datasets(
    request: QueryRequest,
    current_user: dict,
    settings: Settings,
) -> QueryResponse:
    """Retrieve only fixed, parameterized content-unit graph paths for selected IDs."""
    try:
        grant = validate_knowledge_access_grant(request.knowledge_access_grant, settings)
        subject = str(current_user.get("sub", "")).strip()
        if not subject or grant.subject != subject:
            raise HTTPException(status_code=403, detail="Knowledge access grant does not belong to the authenticated subject.")

        requested_dataset_ids = {value.strip() for value in request.dataset_ids}
        granted_dataset_ids = {value.dataset_id for value in grant.datasets}
        if requested_dataset_ids != granted_dataset_ids:
            raise HTTPException(status_code=403, detail="Knowledge access grant does not match the selected datasets.")

        driver = await get_knowledge_driver(settings)
        pipeline = DatasetGraphRAG(
            driver=driver,
            database=settings.knowledge_neo4j_database,
            embedding_model=settings.embedding_model,
            llm_provider=settings.llm_provider,
            llm_model=settings.llm_model,
            llm_api_base=settings.llm_api_base,
            llm_api_key=settings.llm_api_key,
            llm_temperature=settings.llm_temperature,
            bedrock_region=settings.bedrock_region,
        )
        result = await pipeline.query(
            question=request.question,
            dataset_grants=grant.datasets,
            top_k=request.top_k or settings.dataset_retrieval_top_k,
        )
    except HTTPException:
        raise
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as exc:
        logger.error("Dataset query failed: %s", exc, exc_info=True)
        raise HTTPException(status_code=500, detail="Dataset query failed.") from exc

    citations = [
        {
            "dataset_id": citation.dataset_id,
            "content_unit_id": citation.content_unit_id,
            "citation": citation.citation,
            "score": citation.score,
        }
        for citation in result.citations
    ]
    return QueryResponse(
        answer=result.answer,
        strategy_used="dataset-vector-graph",
        sources=[citation.citation for citation in result.citations],
        vector_matches=[],
        generated_cypher=None,
        error=None,
        scope="dataset",
        citations=citations,
    )