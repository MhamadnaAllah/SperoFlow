"""Provider-aware LangChain chat model factory for private AI workloads."""

from __future__ import annotations

from typing import Any


def create_chat_model(
    *,
    provider: str,
    model: str,
    api_base: str,
    api_key: str,
    temperature: float,
    bedrock_region: str,
    max_tokens: int | None = None,
) -> Any:
    """Create a configured chat model without exposing provider details to routes."""
    normalized_provider = provider.strip().casefold()
    normalized_model = model.strip()
    if not normalized_model:
        raise ValueError("LLM_MODEL must be configured before inference can run.")

    if normalized_provider in {"openai", "openai-compatible", "vllm"}:
        if not api_base.strip():
            raise ValueError("LLM_API_BASE is required for an OpenAI-compatible provider.")
        from langchain_openai import ChatOpenAI

        return ChatOpenAI(
            base_url=api_base,
            api_key=api_key or "unused",
            model=normalized_model,
            temperature=temperature,
            max_tokens=max_tokens,
        )

    if normalized_provider == "bedrock":
        try:
            from langchain_aws import ChatBedrockConverse
        except ImportError as exc:
            raise RuntimeError(
                "langchain-aws is required for Bedrock chat inference. "
                "Install the ai-api requirements."
            ) from exc

        kwargs: dict[str, Any] = {
            "model": normalized_model,
            "region_name": bedrock_region,
            "temperature": temperature,
        }
        if max_tokens is not None:
            kwargs["max_tokens"] = max_tokens
        return ChatBedrockConverse(**kwargs)

    raise ValueError(
        "LLM_PROVIDER must be one of: bedrock, openai-compatible, openai, or vllm."
    )