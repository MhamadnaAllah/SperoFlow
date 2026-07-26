"""
Amazon Bedrock runtime client for SperoFlow model routing.

The boto3 Bedrock runtime client is synchronous, so invocations are offloaded
with asyncio.to_thread to keep FastAPI's event loop responsive.
"""

from __future__ import annotations

import asyncio
import json
import logging
from threading import Lock
from typing import Any

from speroflow_ai.config import Settings

logger = logging.getLogger("speroflow.services.bedrock_client")

_client: Any | None = None
_client_lock = Lock()


def _init_bedrock_client(settings: Settings) -> Any:
    """Initialize the process-wide Bedrock runtime client."""
    global _client
    with _client_lock:
        if _client is not None:
            return _client

        try:
            import boto3
            from botocore.config import Config as BotoConfig
        except ImportError as exc:
            raise RuntimeError(
                "boto3 and botocore are required for Bedrock routing. "
                "Install the ai-api requirements or configure ROUTER_PROVIDER=keyword."
            ) from exc

        config = BotoConfig(
            retries={"max_attempts": 3, "mode": "adaptive"},
            connect_timeout=getattr(settings, "bedrock_connect_timeout", 5.0),
            read_timeout=getattr(settings, "bedrock_read_timeout", 30.0),
            max_pool_connections=getattr(settings, "bedrock_max_pool_connections", 25),
        )

        client_kwargs = {
            "service_name": "bedrock-runtime",
            "region_name": settings.bedrock_region,
            "config": config,
        }
        if settings.aws_access_key_id and settings.aws_secret_access_key:
            client_kwargs["aws_access_key_id"] = settings.aws_access_key_id
            client_kwargs["aws_secret_access_key"] = settings.aws_secret_access_key
            session_token = getattr(settings, "aws_session_token", "")
            if session_token:
                client_kwargs["aws_session_token"] = session_token

        _client = boto3.client(**client_kwargs)
        logger.info("Bedrock runtime client initialized (region=%s).", settings.bedrock_region)
        return _client


def get_bedrock_client(settings: Settings) -> Any:
    """Return the shared Bedrock runtime client, creating it on first use."""
    if _client is None:
        return _init_bedrock_client(settings)
    return _client


async def invoke_bedrock(
    *,
    model_id: str,
    system_prompt: str,
    user_text: str,
    settings: Settings,
    max_tokens: int = 1024,
    temperature: float = 0.0,
) -> str:
    """Invoke a Bedrock-hosted model and return the generated text."""
    if not model_id:
        raise ValueError("model_id is required for Bedrock invocation.")
    if max_tokens <= 0:
        raise ValueError("max_tokens must be greater than zero.")

    client = get_bedrock_client(settings)
    body = _format_request_body(
        model_id=model_id,
        system_prompt=system_prompt,
        user_text=user_text,
        max_tokens=max_tokens,
        temperature=temperature,
    )

    def _invoke() -> Any:
        return client.invoke_model(
            body=json.dumps(body),
            modelId=model_id,
            accept="application/json",
            contentType="application/json",
        )

    response = await asyncio.to_thread(_invoke)
    response_body = json.loads(response["body"].read())
    return _extract_text(response_body)


def _format_request_body(
    *,
    model_id: str,
    system_prompt: str,
    user_text: str,
    max_tokens: int,
    temperature: float,
) -> dict[str, Any]:
    model_name = model_id.lower()
    if "gemma" in model_name or "google" in model_name:
        return _format_gemma_request(system_prompt, user_text, max_tokens, temperature)
    if "glm" in model_name or "zai" in model_name:
        return _format_glm_request(system_prompt, user_text, max_tokens, temperature)
    if "llama" in model_name or "meta" in model_name:
        return _format_meta_request(system_prompt, user_text, max_tokens, temperature)
    return _format_anthropic_request(system_prompt, user_text, max_tokens, temperature)


def _format_glm_request(
    system_prompt: str,
    user_text: str,
    max_tokens: int,
    temperature: float,
) -> dict[str, Any]:
    """Format request body for ZAI GLM-5 models on Bedrock."""
    return {
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_text},
        ],
        "max_tokens": max_tokens,
        "temperature": temperature,
    }


def _format_anthropic_request(
    system_prompt: str,
    user_text: str,
    max_tokens: int,
    temperature: float,
) -> dict[str, Any]:
    return {
        "anthropic_version": "bedrock-2023-05-31",
        "max_tokens": max_tokens,
        "temperature": temperature,
        "system": system_prompt,
        "messages": [{"role": "user", "content": user_text}],
    }


def _format_meta_request(
    system_prompt: str,
    user_text: str,
    max_tokens: int,
    temperature: float,
) -> dict[str, Any]:
    prompt = f"<s>[INST] <<SYS>>\n{system_prompt}\n<</SYS>>\n\n{user_text} [/INST]"
    return {
        "prompt": prompt,
        "max_gen_len": max_tokens,
        "temperature": temperature,
    }


def _format_gemma_request(
    system_prompt: str,
    user_text: str,
    max_tokens: int,
    temperature: float,
) -> dict[str, Any]:
    """Format request body for Google Gemma models on Bedrock."""
    return {
        "contents": [
            {
                "role": "user",
                "parts": [
                    {"text": f"{system_prompt}\n\n{user_text}"}
                ],
            }
        ],
        "generationConfig": {
            "maxOutputTokens": max_tokens,
            "temperature": temperature,
        },
    }


def _extract_text(response_body: dict[str, Any]) -> str:
    """Normalize common Bedrock response shapes to plain text."""
    # Gemma-style: {"candidates": [{"content": {"parts": [{"text": "..."}]}}]}
    candidates = response_body.get("candidates")
    if isinstance(candidates, list) and candidates:
        first_candidate = candidates[0]
        if isinstance(first_candidate, dict):
            candidate_content = first_candidate.get("content", {})
            parts = candidate_content.get("parts", [])
            if isinstance(parts, list) and parts:
                first_part = parts[0]
                if isinstance(first_part, dict) and "text" in first_part:
                    return str(first_part["text"])

    # Anthropic-style: {"content": [{"text": "..."}]}
    content = response_body.get("content")
    if isinstance(content, list) and content:
        first = content[0]
        if isinstance(first, dict):
            return str(first.get("text", ""))
        return str(first)

    if "generation" in response_body:
        return str(response_body["generation"])

    outputs = response_body.get("outputs")
    if isinstance(outputs, list) and outputs:
        first = outputs[0]
        if isinstance(first, dict):
            return str(first.get("text", first.get("outputText", "")))
        return str(first)

    if "outputText" in response_body:
        return str(response_body["outputText"])

    return json.dumps(response_body)
