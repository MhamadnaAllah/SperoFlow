"""Release-gated settings for the internal Balance Agent evaluator."""

from __future__ import annotations

from functools import lru_cache

from pydantic import Field, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class BalanceAgentSettings(BaseSettings):
    """Settings kept local so the feature stays disabled by default."""

    model_config = SettingsConfigDict(extra="ignore")

    balance_agent_enabled: bool = False
    balance_lookback_days: int = Field(default=7, ge=1, le=31)
    balance_max_lookback_days: int = Field(default=31, ge=1, le=90)
    balance_min_classified_tasks: int = Field(default=3, ge=1, le=100)
    balance_min_classification_coverage: float = Field(default=0.60, ge=0.0, le=1.0)
    balance_medium_concentration_threshold: float = Field(default=0.70, gt=0.0, lt=1.0)
    balance_high_concentration_threshold: float = Field(default=0.85, gt=0.0, lt=1.0)
    balance_suggestion_duration_minutes: int = Field(default=15, ge=5, le=30)

    @model_validator(mode="after")
    def validate_thresholds(self) -> "BalanceAgentSettings":
        if self.balance_medium_concentration_threshold >= self.balance_high_concentration_threshold:
            raise ValueError(
                "balance_medium_concentration_threshold must be below "
                "balance_high_concentration_threshold"
            )
        if self.balance_lookback_days > self.balance_max_lookback_days:
            raise ValueError("balance_lookback_days must not exceed balance_max_lookback_days")
        return self

    @property
    def release_enabled(self) -> bool:
        """The endpoint is enabled only by an explicit feature flag; service JWTs authenticate callers."""
        return self.balance_agent_enabled


@lru_cache
def get_balance_agent_settings() -> BalanceAgentSettings:
    """Return cached local feature settings."""
    return BalanceAgentSettings()
