"""Non-diagnostic urgency routing for CBT-related user input.

This module is deliberately narrow. It does not assess, score, or predict risk;
it only recognizes explicit urgency signals so the service can avoid AI analysis
and direct the person to immediate human support.
"""

from __future__ import annotations

import re
from dataclasses import dataclass


URGENT_SUPPORT_MESSAGE = (
    "Your safety matters. I cannot support a possible immediate safety concern through "
    "this AI chat. Please contact local emergency services or go to the nearest emergency "
    "department now. If you can, tell a trusted person nearby and contact a licensed health "
    "professional or local crisis service."
)

_URGENT_PATTERNS = (
    re.compile(r"\b(?:kill myself|end my life|take my life|suicide|suicidal)\b", re.IGNORECASE),
    re.compile(r"\b(?:hurt|harm|cut)\s+myself\b", re.IGNORECASE),
    re.compile("\u0623\u0631\u064a\u062f \u0623\u0646 \u0623\u0642\u062a\u0644 \u0646\u0641\u0633\u064a"),
    re.compile("\u0623\u0631\u064a\u062f \u0623\u0646 \u0623\u0624\u0630\u064a \u0646\u0641\u0633\u064a"),
    re.compile("\u0627\u0646\u062a\u062d\u0627\u0631"),
)


@dataclass(frozen=True)
class UrgentSupportDecision:
    """A routing decision, not a clinical assessment."""

    should_escalate: bool
    message: str = ""


def evaluate_urgent_support_signal(text: str) -> UrgentSupportDecision:
    """Return an urgent-support decision for explicit safety language only."""
    if not text or not text.strip():
        return UrgentSupportDecision(should_escalate=False)
    if any(pattern.search(text) for pattern in _URGENT_PATTERNS):
        return UrgentSupportDecision(should_escalate=True, message=URGENT_SUPPORT_MESSAGE)
    return UrgentSupportDecision(should_escalate=False)
