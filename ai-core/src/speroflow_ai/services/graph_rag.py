"""
HybridRAGPipeline — Combines Neo4j vector search + Cypher graph traversal + LLM synthesis.

Uses lazy initialization so heavy components (Neo4j driver, LLM, embedding model)
are loaded only on first query — not at import time.
"""

from __future__ import annotations

import logging
import os
import operator
from typing import Any, Optional, Annotated
from typing_extensions import TypedDict

from langchain_core.prompts import PromptTemplate

from speroflow_ai.models.graph import RAGResult, VectorMatch

logger = logging.getLogger("speroflow.services.graph_rag")


class GraphState(TypedDict):
    question: str
    strategy: str
    top_k: int
    vector_context: str
    vector_matches: Annotated[list[VectorMatch], operator.add]
    cypher_context: str
    generated_cypher: str
    cypher_results: list[dict[str, Any]]
    sources: Annotated[list[str], operator.add]
    answer: str
    cypher_answer: str
    strategy_used: str
    vector_error: Optional[str]
    cypher_error: Optional[str]
    synthesis_error: Optional[str]


# ─── Prompt Templates ─────────────────────────────────────────────────────────

CYPHER_GENERATION_TEMPLATE = """\
You are an expert Neo4j Cypher query generator for an educational roadmap
knowledge graph. Your task is to convert natural language questions into
precise, read-only Cypher queries.

=== GRAPH SCHEMA ===
{schema}

=== NODE LABELS AND KEY PROPERTIES ===
- (:Roadmap)   — roadmap_name (unique)
- (:Topic)     — node_id (unique), label_text, roadmap_name, content, url, embedding
- (:Subtopic)  — node_id (unique), label_text, roadmap_name, content, url, embedding

=== RELATIONSHIP TYPES ===
- (:Roadmap)-[:CONTAINS]->(:Topic)
- (:Topic)-[:LEADS_TO]->(:Topic|Subtopic)
- (:Topic)-[:RELATED_TO]->(:Topic|Subtopic)
- (:Subtopic)-[:LEADS_TO]->(:Subtopic)
- (:Subtopic)-[:RELATED_TO]->(:Subtopic)

=== STRICT RULES ===
1. Generate ONLY read-only Cypher (MATCH, RETURN, WITH, WHERE, ORDER BY, LIMIT).
2. NEVER generate CREATE, MERGE, DELETE, SET, REMOVE, DROP, or CALL statements.
3. Use case-insensitive matching: WHERE toLower(n.label_text) CONTAINS toLower("term")
4. Always LIMIT results to at most 25 rows unless the user asks for all.
5. Return label_text, roadmap_name, and content when relevant.

=== FEW-SHOT EXAMPLES ===

Question: What topics lead to Prompt Engineering?
Cypher:
MATCH (source)-[:LEADS_TO]->(target)
WHERE toLower(target.label_text) CONTAINS toLower("prompt engineering")
RETURN source.label_text AS prerequisite, target.label_text AS target_topic,
       source.roadmap_name AS roadmap
LIMIT 10

Question: List all topics in the AI Agents roadmap.
Cypher:
MATCH (r:Roadmap {{roadmap_name: "ai-agents"}})-[:CONTAINS]->(t:Topic)
RETURN t.label_text AS topic, t.node_id AS node_id
ORDER BY t.label_text

=== GENERATE CYPHER ===
Question: {question}
Cypher:
"""

QA_SYNTHESIS_TEMPLATE = """\
You are a knowledgeable AI tutor specializing in technology roadmaps and
learning paths. Answer the user's question using ONLY the context provided.

=== RETRIEVED CONTEXT ===
{context}

=== USER QUESTION ===
{question}

=== YOUR ANSWER ===
"""

HYBRID_MERGE_TEMPLATE = """\
You are an AI tutor answering a question about technology learning roadmaps.

=== VECTOR SEARCH RESULTS (Semantic Similarity) ===
{vector_context}

=== CYPHER QUERY RESULTS (Structured Graph Traversal) ===
{cypher_context}

=== USER QUESTION ===
{question}

=== YOUR ANSWER ===
"""

CYPHER_GENERATION_PROMPT = PromptTemplate(
    input_variables=["schema", "question"],
    template=CYPHER_GENERATION_TEMPLATE,
)
QA_SYNTHESIS_PROMPT = PromptTemplate(
    input_variables=["context", "question"],
    template=QA_SYNTHESIS_TEMPLATE,
)
HYBRID_MERGE_PROMPT = PromptTemplate(
    input_variables=["vector_context", "cypher_context", "question"],
    template=HYBRID_MERGE_TEMPLATE,
)


# ─── Retrieval Query ──────────────────────────────────────────────────────────

def _build_retrieval_query(depth: int = 2) -> str:
    return f"""
        WITH node, score
        OPTIONAL MATCH (node)-[r:LEADS_TO|RELATED_TO*1..{depth}]-(neighbor)
        WHERE (neighbor:Topic OR neighbor:Subtopic)
        WITH node, score,
             collect(DISTINCT {{
                 label: neighbor.label_text,
                 roadmap: neighbor.roadmap_name,
                 type: labels(neighbor)[0],
                 relationship: type(last(r)),
                 content_snippet: left(coalesce(neighbor.content, ''), 500)
             }}) AS neighbors
        RETURN
            coalesce(node.content, node.label_text) AS text,
            score,
            {{
                node_id: node.node_id,
                label_text: node.label_text,
                roadmap_name: node.roadmap_name,
                url: node.url,
                neighbor_count: size(neighbors),
                neighbors: neighbors[0..10]
            }} AS metadata
    """


# ─── Pipeline ─────────────────────────────────────────────────────────────────

class HybridRAGPipeline:
    """
    Production-grade Hybrid RAG pipeline.

    Combines:
      1. Vector similarity search (Neo4j + bge-m3)
      2. Structured Cypher generation (LangChain GraphCypherQAChain)
      3. LLM synthesis (Bedrock by default, vLLM when explicitly enabled)

    All heavy components are lazy-loaded on first use.
    """

    def __init__(
        self,
        neo4j_uri: str,
        neo4j_user: str,
        neo4j_password: str,
        llm_provider: str = "bedrock",
        llm_api_base: str = "",
        llm_api_key: str = "",
        llm_model: str = "amazon.nova-lite-v1:0",
        llm_temperature: float = 0.0,
        bedrock_region: str = "us-east-1",
        embedding_model: str = "BAAI/bge-m3",
        vector_index_name: str = "topic_embedding_index",
        top_k: int = 5,
        traversal_depth: int = 2,
    ) -> None:
        self._neo4j_uri = neo4j_uri
        self._neo4j_user = neo4j_user
        self._neo4j_password = neo4j_password
        self._llm_provider = llm_provider
        self._llm_api_base = llm_api_base
        self._llm_api_key = llm_api_key
        self._llm_model = llm_model
        self._llm_temperature = llm_temperature
        self._bedrock_region = bedrock_region
        self._embedding_model_name = embedding_model
        self._vector_index_name = vector_index_name
        self._top_k = top_k
        self._traversal_depth = traversal_depth

        self._llm: Optional[Any] = None
        self._neo4j_graph: Optional[Any] = None
        self._vector_store: Optional[Any] = None
        self._cypher_chain: Optional[Any] = None
        self._embedder: Optional[Any] = None
        self._graph: Optional[Any] = None

        logger.info("HybridRAGPipeline created (components lazy-loaded).")

    async def query(
        self,
        question: str,
        strategy: str = "hybrid",
        top_k: Optional[int] = None,
    ) -> RAGResult:
        strategy = strategy.lower().strip()
        if strategy not in ("vector", "cypher", "hybrid"):
            raise ValueError(f"Invalid strategy '{strategy}'. Choose: vector, cypher, hybrid.")

        logger.info("Query [%s]: %s", strategy, question)
        self._ensure_graph()

        state_input = {
            "question": question,
            "strategy": strategy,
            "top_k": top_k or self._top_k,
            "vector_context": "",
            "vector_matches": [],
            "cypher_context": "",
            "generated_cypher": "",
            "cypher_results": [],
            "sources": [],
            "answer": "",
            "cypher_answer": "",
            "strategy_used": strategy,
            "vector_error": None,
            "cypher_error": None,
            "synthesis_error": None,
        }

        try:
            final_state = await self._graph.ainvoke(state_input)
            sources = list(set(final_state.get("sources", [])))
            return RAGResult(
                answer=final_state.get("answer", ""),
                strategy_used=final_state.get("strategy_used", strategy),
                sources=sources,
                vector_matches=final_state.get("vector_matches", []),
                generated_cypher=final_state.get("generated_cypher", ""),
                cypher_results=final_state.get("cypher_results", []),
                error=final_state.get("synthesis_error") or final_state.get("vector_error") or final_state.get("cypher_error"),
            )
        except Exception as exc:
            logger.error("LangGraph execution failed: %s", exc, exc_info=True)
            return RAGResult(answer="", strategy_used=strategy, error=str(exc))

    # ── Private: Lazy Initialization & Graph Setup ────────────────────────────

    def _ensure_graph(self) -> None:
        if self._graph is not None:
            return

        from langgraph.graph import StateGraph, START, END
        from langgraph.types import Send

        builder = StateGraph(GraphState)
        builder.add_node("retrieve_vector", self._retrieve_vector)
        builder.add_node("retrieve_cypher", self._retrieve_cypher)
        builder.add_node("synthesize_answer", self._synthesize_answer)

        def route_start(state: GraphState):
            """Fan-out to both retrieval nodes in parallel for hybrid strategy."""
            strat = state["strategy"].lower().strip()
            if strat == "vector":
                return [Send("retrieve_vector", state)]
            elif strat == "cypher":
                return [Send("retrieve_cypher", state)]
            else:  # hybrid — run both in parallel
                return [Send("retrieve_vector", state), Send("retrieve_cypher", state)]

        builder.add_conditional_edges(START, route_start)
        builder.add_edge("retrieve_vector", "synthesize_answer")
        builder.add_edge("retrieve_cypher", "synthesize_answer")
        builder.add_edge("synthesize_answer", END)

        self._graph = builder.compile()


    async def _retrieve_vector(self, state: GraphState) -> dict:
        question = state["question"]
        top_k = state["top_k"]
        try:
            self._ensure_vector_store()
            docs = await self._vector_store.asimilarity_search_with_score(question, k=top_k)
            if not docs:
                return {
                    "vector_context": "",
                    "vector_matches": [],
                    "sources": [],
                    "vector_error": None,
                }
            context_parts, matches, sources = [], [], []
            for doc, score in docs:
                meta = doc.metadata or {}
                label = meta.get("label_text", "Unknown")
                roadmap = meta.get("roadmap_name", "Unknown")
                context_parts.append(
                    f"--- Topic: {label} (Roadmap: {roadmap}, Similarity: {score:.4f}) ---\n"
                    f"{doc.page_content}\n"
                )
                for neighbor in meta.get("neighbors", []):
                    if neighbor.get("label"):
                        context_parts.append(
                            f"  → {neighbor.get('relationship', 'CONNECTED')}: "
                            f"{neighbor['label']} ({neighbor.get('roadmap', '')})\n"
                            f"    {neighbor.get('content_snippet', '')[:200]}\n"
                        )
                matches.append(VectorMatch(
                    node_id=meta.get("node_id", ""),
                    label_text=label,
                    roadmap_name=roadmap,
                    score=float(score),
                    content_snippet=doc.page_content[:300],
                    neighbors=meta.get("neighbors", []),
                ))
                sources.append(f"{label} ({roadmap})")
            return {
                "vector_context": "\n".join(context_parts),
                "vector_matches": matches,
                "sources": sources,
                "vector_error": None,
            }
        except Exception as exc:
            logger.warning("Vector retrieval failed: %s", exc)
            return {
                "vector_context": "",
                "vector_matches": [],
                "sources": [],
                "vector_error": str(exc),
            }

    async def _retrieve_cypher(self, state: GraphState) -> dict:
        question = state["question"]
        try:
            self._ensure_cypher_chain()
            result = await self._cypher_chain.ainvoke({"query": question})
            intermediate = result.get("intermediate_steps", [])
            generated_cypher = ""
            cypher_results = []
            if intermediate:
                q_info = intermediate[0]
                generated_cypher = q_info.get("query", "") if isinstance(q_info, dict) else str(q_info)
                if len(intermediate) >= 2:
                    raw = intermediate[1]
                    cypher_results = [raw] if isinstance(raw, dict) else (raw if isinstance(raw, list) else [])
            answer = result.get("result", "No answer generated.")
            sources = self._extract_sources_from_cypher(cypher_results)
            
            cypher_ctx_parts = []
            if generated_cypher:
                cypher_ctx_parts.append(f"Query: {generated_cypher.strip()}\n")
            for row in cypher_results[:10]:
                cypher_ctx_parts.append(" | ".join(f"{k}: {v}" for k, v in row.items()) if isinstance(row, dict) else str(row))
            cypher_context = "\n".join(cypher_ctx_parts)

            return {
                "cypher_context": cypher_context,
                "generated_cypher": generated_cypher,
                "cypher_results": cypher_results,
                "sources": sources,
                "cypher_answer": answer,
                "cypher_error": None,
            }
        except Exception as exc:
            logger.warning("Cypher retrieval failed: %s", exc)
            return {
                "cypher_context": "",
                "generated_cypher": "",
                "cypher_results": [],
                "sources": [],
                "cypher_answer": "",
                "cypher_error": str(exc),
            }

    async def _synthesize_answer(self, state: GraphState) -> dict:
        question = state["question"]
        strategy = state["strategy"].lower().strip()
        vector_err = state.get("vector_error")
        cypher_err = state.get("cypher_error")

        if strategy == "hybrid" and vector_err and cypher_err:
            return {
                "answer": "Both retrieval strategies failed. Check Neo4j connection.",
                "synthesis_error": f"Vector: {vector_err} | Cypher: {cypher_err}",
            }
        elif strategy == "vector" and vector_err:
            return {
                "answer": f"Vector retrieval failed: {vector_err}",
                "synthesis_error": vector_err,
            }
        elif strategy == "cypher" and cypher_err:
            return {
                "answer": f"Cypher retrieval failed: {cypher_err}",
                "synthesis_error": cypher_err,
            }

        if strategy == "hybrid":
            if vector_err:
                cypher_ans = state.get("cypher_answer") or "No answer generated."
                return {
                    "answer": cypher_ans,
                    "strategy_used": "hybrid (cypher-only fallback)",
                }
            elif cypher_err:
                self._ensure_llm()
                prompt = QA_SYNTHESIS_PROMPT.format(context=state["vector_context"], question=question)
                response = await self._llm.ainvoke(prompt)
                answer = response.content if hasattr(response, "content") else str(response)
                return {
                    "answer": answer,
                    "strategy_used": "hybrid (vector-only fallback)",
                }

        if strategy == "vector":
            if not state["vector_context"]:
                return {
                    "answer": "No relevant topics found. Try rephrasing your question.",
                }
            self._ensure_llm()
            prompt = QA_SYNTHESIS_PROMPT.format(context=state["vector_context"], question=question)
            response = await self._llm.ainvoke(prompt)
            answer = response.content if hasattr(response, "content") else str(response)
            return {"answer": answer}

        elif strategy == "cypher":
            cypher_ans = state.get("cypher_answer") or "No answer generated."
            return {"answer": cypher_ans}

        else:  # hybrid
            self._ensure_llm()
            prompt = HYBRID_MERGE_PROMPT.format(
                vector_context=state["vector_context"] or "(No vector results)",
                cypher_context=state["cypher_context"] or "(No Cypher results)",
                question=question,
            )
            response = await self._llm.ainvoke(prompt)
            answer = response.content if hasattr(response, "content") else str(response)
            return {"answer": answer}

    # ── Private: Lazy Initialization ──────────────────────────────────────────

    def _ensure_llm(self) -> None:
        if self._llm is not None:
            return

        from speroflow_ai.services.chat_model import create_chat_model

        self._llm = create_chat_model(
            provider=self._llm_provider,
            model=self._llm_model,
            api_base=self._llm_api_base,
            api_key=self._llm_api_key,
            temperature=self._llm_temperature,
            bedrock_region=self._bedrock_region,
            max_tokens=1024,
        )
        logger.info("LLM initialized: provider=%s model=%s", self._llm_provider, self._llm_model)

    def _ensure_embedder(self) -> None:
        if self._embedder is not None:
            return
        from langchain_huggingface import HuggingFaceEmbeddings

        model_name = self._embedding_model_name or "BAAI/bge-m3"
        if ":" in model_name or not ("/" in model_name or "-" in model_name):
            model_name = "BAAI/bge-m3"

        try:
            self._embedder = HuggingFaceEmbeddings(
                model_name=model_name,
                model_kwargs={"device": "cpu"},
                encode_kwargs={"normalize_embeddings": True},
            )
        except Exception:
            model_name = "BAAI/bge-m3"
            self._embedder = HuggingFaceEmbeddings(
                model_name=model_name,
                model_kwargs={"device": "cpu"},
                encode_kwargs={"normalize_embeddings": True},
            )
        logger.info("Embedder initialized: %s", model_name)

    def _ensure_neo4j_graph(self) -> None:
        if self._neo4j_graph is not None:
            return
        from langchain_neo4j import Neo4jGraph
        self._neo4j_graph = Neo4jGraph(
            url=self._neo4j_uri,
            username=self._neo4j_user,
            password=self._neo4j_password or "none",
        )
        self._neo4j_graph.refresh_schema()
        logger.info("Neo4jGraph connected. Schema refreshed.")

    def _ensure_vector_store(self) -> None:
        if self._vector_store is not None:
            return
        self._ensure_embedder()
        from langchain_neo4j import Neo4jVector
        self._vector_store = Neo4jVector.from_existing_index(
            embedding=self._embedder,
            url=self._neo4j_uri,
            username=self._neo4j_user,
            password=self._neo4j_password or "none",
            index_name=self._vector_index_name,
            text_node_property="content",
            embedding_node_property="embedding",
            retrieval_query=_build_retrieval_query(self._traversal_depth),
        )
        logger.info("Neo4jVector connected to index '%s'.", self._vector_index_name)

    def _ensure_cypher_chain(self) -> None:
        if self._cypher_chain is not None:
            return
        self._ensure_llm()
        self._ensure_neo4j_graph()
        from langchain_neo4j import GraphCypherQAChain
        self._cypher_chain = GraphCypherQAChain.from_llm(
            llm=self._llm,
            graph=self._neo4j_graph,
            cypher_prompt=CYPHER_GENERATION_PROMPT,
            qa_chain_prompt=QA_SYNTHESIS_PROMPT,
            verbose=False,
            return_intermediate_steps=True,
            validate_cypher=True,
            allow_dangerous_requests=True,
            top_k=10,
        )
        logger.info("GraphCypherQAChain ready.")

    # ── Private: Helpers ──────────────────────────────────────────────────────

    @staticmethod
    def _extract_sources_from_cypher(results: list) -> list[str]:
        sources = []
        for row in results:
            if isinstance(row, dict):
                label = row.get("topic") or row.get("label_text") or ""
                roadmap = row.get("roadmap") or row.get("roadmap_name") or ""
                if label:
                    sources.append(f"{label} ({roadmap})" if roadmap else label)
        return sources[:10]

    def close(self) -> None:
        for store in [self._neo4j_graph, self._vector_store]:
            if store:
                try:
                    # Try common private driver attribute names used by langchain-neo4j
                    driver = getattr(store, "_driver", None) or getattr(store, "driver", None)
                    if driver:
                        driver.close()
                except Exception:
                    pass
        logger.info("Pipeline connections closed.")
