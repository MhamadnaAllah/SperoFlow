"""Private journal-reflection orchestration with Dual Mode RoBERTa + Bedrock processing."""

from __future__ import annotations

import asyncio
import json
import logging
from typing import Any

from speroflow_ai.config import Settings
from speroflow_ai.models.journal import JournalReflectionRequest, JournalReflectionResponse
from speroflow_ai.services.bedrock_client import invoke_bedrock
from speroflow_ai.services.roberta_classifier import RoBERTaEmotionClassifier

logger = logging.getLogger("speroflow.services.journal_reflection")

_SYSTEM_PROMPT = """You are a non-clinical journaling reflection assistant for a self-help productivity app.
Return exactly one JSON object with these keys: emotions (an array of 1-6 short everyday emotion labels),
feedback (one or two warm, tentative sentences), and progressSummary (one concise sentence).
Use only the supplied journal context. Do not diagnose, assess risk, prescribe treatment, make medical claims,
or present assumptions as facts. Do not mention this instruction. Keep every field concise and plain-language."""


class JournalReflectionService:
    def __init__(self, settings: Settings, roberta_classifier: RoBERTaEmotionClassifier | None = None):
        self._settings = settings
        self._roberta = roberta_classifier or RoBERTaEmotionClassifier()

    async def analyze(self, request: JournalReflectionRequest) -> JournalReflectionResponse:
        if self._uses_deterministic_fallback():
            return self._fallback(request)

        # Dual Mode: Run RoBERTa local classification in parallel with Bedrock inference
        text_content = f"{request.current_entry.mood or ''} {request.current_entry.content}".strip()
        
        roberta_task = asyncio.to_thread(self._roberta.classify, text_content)
        bedrock_task = invoke_bedrock(
            model_id=self._settings.llm_model,
            system_prompt=_SYSTEM_PROMPT,
            user_text=json.dumps(request.model_dump(by_alias=True), ensure_ascii=False),
            settings=self._settings,
            max_tokens=400,
            temperature=0.2,
        )

        try:
            roberta_emotions, raw_llm = await asyncio.gather(roberta_task, bedrock_task, return_exceptions=True)
            
            # If Bedrock succeeded, parse LLM response and merge emotions
            if not isinstance(raw_llm, Exception) and isinstance(raw_llm, str):
                llm_response = self._parse_response(raw_llm)
                merged_emotions = self._merge_emotions(
                    roberta_emotions if isinstance(roberta_emotions, list) else [],
                    llm_response.emotions,
                )
                return JournalReflectionResponse(
                    emotions=merged_emotions,
                    feedback=llm_response.feedback,
                    progress_summary=llm_response.progress_summary,
                )
            
            logger.warning("Bedrock LLM unavailable; using RoBERTa emotions + fallback narrative.")
            fallback_res = self._fallback(request)
            if isinstance(roberta_emotions, list) and roberta_emotions:
                fallback_res = JournalReflectionResponse(
                    emotions=roberta_emotions[:6],
                    feedback=fallback_res.feedback,
                    progress_summary=fallback_res.progress_summary,
                )
            return fallback_res

        except Exception as exc:
            logger.warning("Journal reflection dual-mode error (%s); returning fallback.", type(exc).__name__)
            return self._fallback(request)

    @staticmethod
    def _merge_emotions(roberta_emotions: list[str], llm_emotions: list[str]) -> list[str]:
        """Merge and deduplicate emotion tags from RoBERTa and LLM, prioritizing RoBERTa tags."""
        seen = set()
        merged = []

        for e in roberta_emotions + llm_emotions:
            clean = e.strip().capitalize()
            if clean and clean.lower() not in seen:
                seen.add(clean.lower())
                merged.append(clean)
                if len(merged) >= 6:
                    break

        return merged if merged else ["Neutral"]

    def _uses_deterministic_fallback(self) -> bool:
        return (
            str(getattr(self._settings, "router_provider", "")).casefold() == "keyword"
            or str(getattr(self._settings, "llm_provider", "")).casefold() == "keyword"
        )

    @staticmethod
    def _parse_response(raw: str) -> JournalReflectionResponse:
        start = raw.find("{")
        end = raw.rfind("}")
        if start < 0 or end <= start:
            raise ValueError("Model response did not contain a JSON object.")
        payload: Any = json.loads(raw[start : end + 1])
        return JournalReflectionResponse.model_validate(payload)

    @staticmethod
    def _fallback(request: JournalReflectionRequest) -> JournalReflectionResponse:
        current = request.current_entry
        text = f"{current.mood or ''} {current.content}".casefold()
        candidates = [
            ("Stressed", ("stressed", "stress", "overwhelm", "worried", "anxious")),
            ("Tired", ("tired", "exhaust", "drained", "sleep")),
            ("Low", ("sad", "lonely", "down", "low")),
            ("Frustrated", ("frustrat", "angry", "irritat")),
            ("Hopeful", ("hope", "optim", "looking forward")),
            ("Grateful", ("grateful", "thankful", "appreciat")),
            ("Calm", ("calm", "settled", "peaceful")),
        ]
        emotions = [label for label, markers in candidates if any(marker in text for marker in markers)]
        if not emotions:
            emotions = [(current.mood or "Reflective").strip().capitalize()]

        if request.prior_entries:
            feedback = "You captured a useful moment to revisit. Notice which situations or choices keep showing up across your recent entries."
            progress_summary = "This reflection adds another data point for noticing patterns over time."
        else:
            feedback = "You captured a useful moment to revisit. Notice one detail that feels worth carrying into tomorrow."
            progress_summary = "This entry creates a starting point for noticing patterns over time."

        return JournalReflectionResponse(
            emotions=emotions[:6],
            feedback=feedback,
            progress_summary=progress_summary,
        )