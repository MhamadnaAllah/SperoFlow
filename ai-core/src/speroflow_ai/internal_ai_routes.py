"""Private-only AI routes called by ASP.NET with short-lived service JWTs."""

from __future__ import annotations

import logging
from datetime import date, datetime
from typing import Annotated, Literal

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel, ConfigDict, Field, model_validator

from speroflow_ai.config import Settings
from speroflow_ai.dependencies import get_app_settings
from speroflow_ai.models.balance import BalanceEvaluationRequest, BalanceEvaluationResponse
from speroflow_ai.models.eisenhower import EisenhowerClassificationRequest, EisenhowerClassificationResponse
from speroflow_ai.models.journal import JournalReflectionRequest, JournalReflectionResponse
from speroflow_ai.models.role_discovery import RoleDiscoveryRequest, RoleDiscoveryResponse
from speroflow_ai.models.responses import ErrorResponse
from speroflow_ai.service_auth import require_service_scope
from speroflow_ai.services.auto_scheduler import AutoSchedulerAgent, BookedEvent, TaskSpec, UserDailyContext
from speroflow_ai.services.balance_agent import BalanceAgent
from speroflow_ai.services.balance_settings import BalanceAgentSettings, get_balance_agent_settings
from speroflow_ai.services.eisenhower_classifier import EisenhowerClassifier
from speroflow_ai.services.journal_reflection import JournalReflectionService
from speroflow_ai.services.role_discovery import RoleDiscoveryService


logger = logging.getLogger("speroflow.routers.internal")
router = APIRouter(prefix="/api", tags=["Internal AI"])


def _to_camel(value: str) -> str:
    head, *tail = value.split("_")
    return head + "".join(part.capitalize() for part in tail)


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=_to_camel, populate_by_name=True)



class ScheduleBlock(BaseModel):
    title: str = Field(min_length=1, max_length=500)
    start_time: datetime
    end_time: datetime
    source: str = Field(default="calendar", max_length=50)

    @model_validator(mode="after")
    def validate_range(self) -> "ScheduleBlock":
        if self.start_time.tzinfo is None or self.end_time.tzinfo is None:
            raise ValueError("Schedule timestamps must include an offset.")
        if self.end_time <= self.start_time:
            raise ValueError("Schedule block end_time must be after start_time.")
        return self


class ScheduleProposalTask(BaseModel):
    title: str = Field(min_length=1, max_length=500)
    description: str = Field(default="", max_length=4_000)
    duration_minutes: int = Field(ge=5, le=480)
    source: str = Field(default="manual", max_length=50)
    role_category: str = Field(default="personal", max_length=50)
    target_date: date
    not_before: datetime | None = None

    @model_validator(mode="after")
    def validate_not_before(self) -> "ScheduleProposalTask":
        if self.not_before is not None and self.not_before.tzinfo is None:
            raise ValueError("not_before must include an offset.")
        return self


class ScheduleProposalRequest(BaseModel):
    """A bounded, owner-checked schedule snapshot assembled by ASP.NET."""

    task: ScheduleProposalTask
    calendar_events: list[ScheduleBlock] = Field(default_factory=list, max_length=500)
    scheduled_tasks: list[ScheduleBlock] = Field(default_factory=list, max_length=500)
    matrix_load: dict[str, dict[str, int]] = Field(default_factory=dict)
    stress_level: str = Field(default="normal", max_length=50)
    role_distribution: list[dict[str, object]] = Field(default_factory=list, max_length=20)


class ScheduleSlotResponse(CamelModel):
    start_time: datetime
    end_time: datetime
    duration_minutes: int


class ScheduleProposalResponse(CamelModel):
    status: Literal["success", "no_available_slot"]
    recommended_quadrant: str
    suggested_slot: ScheduleSlotResponse | None
    original_duration: int
    effective_duration: int
    stress_adjusted: bool
    reason: str
    burnout_guard: dict[str, bool]
    available_slots: list[ScheduleSlotResponse]


@router.post(
    "/balance/evaluate",
    response_model=BalanceEvaluationResponse,
    responses={
        401: {"model": ErrorResponse, "description": "Internal authentication failed"},
        422: {"model": ErrorResponse, "description": "Invalid aggregate snapshot"},
        503: {"model": ErrorResponse, "description": "Feature is disabled"},
    },
)
async def evaluate_balance(
    request: BalanceEvaluationRequest,
    _: Annotated[dict, Depends(require_service_scope("ai.invoke"))],
    settings: BalanceAgentSettings = Depends(get_balance_agent_settings),
) -> BalanceEvaluationResponse:
    """Evaluate an aggregate snapshot without reading or mutating primary app data."""
    if not settings.release_enabled:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Balance evaluation is not enabled for this environment.",
        )
    try:
        response = BalanceAgent(settings).evaluate(request)
    except ValueError as exc:
        raise HTTPException(status_code=status.HTTP_422_UNPROCESSABLE_ENTITY, detail=str(exc)) from exc

    logger.info("Balance evaluation completed: request=%s risk=%s", response.request_id, response.risk_level)
    return response


@router.post("/matrix/predict-quadrant", response_model=EisenhowerClassificationResponse, response_model_by_alias=True)
async def predict_quadrant(
    request: EisenhowerClassificationRequest,
    _: Annotated[dict, Depends(require_service_scope("ai.invoke"))],
    settings: Settings = Depends(get_app_settings),
) -> EisenhowerClassificationResponse:
    """Return a bounded classification recommendation; ASP.NET owns the proposal."""
    result = await EisenhowerClassifier(settings).classify(request)
    logger.info("Eisenhower classification completed: quadrant=%s confidence=%.2f", result.suggested_quadrant, result.confidence)
    return result


@router.post("/schedule/propose", response_model=ScheduleProposalResponse, response_model_by_alias=True)
async def propose_schedule(
    request: ScheduleProposalRequest,
    _: Annotated[dict, Depends(require_service_scope("ai.invoke"))],
    settings: Settings = Depends(get_app_settings),
) -> ScheduleProposalResponse:
    """Return a validated proposal; ASP.NET remains responsible for persistence."""
    context = UserDailyContext(
        matrix_load=request.matrix_load,
        calendar_events=[
            BookedEvent(block.title, block.start_time, block.end_time, block.source)
            for block in request.calendar_events
        ],
        scheduled_tasks=[
            BookedEvent(block.title, block.start_time, block.end_time, block.source)
            for block in request.scheduled_tasks
        ],
        stress_level=request.stress_level,
        role_distribution=request.role_distribution,
    )
    result = await AutoSchedulerAgent(settings).propose(
        task=TaskSpec(
            title=request.task.title,
            description=request.task.description,
            duration_minutes=request.task.duration_minutes,
            source=request.task.source,
            role_category=request.task.role_category,
            target_date=request.task.target_date,
            not_before=request.task.not_before,
        ),
        context=context,
    )
    decision = result["decision"]
    slot = decision["suggested_slot"] or None
    return ScheduleProposalResponse(
        status=result["status"],
        recommended_quadrant=decision["recommended_quadrant"],
        suggested_slot=ScheduleSlotResponse(**slot) if slot else None,
        original_duration=result["original_duration"],
        effective_duration=result["effective_duration"],
        stress_adjusted=result["stress_adjusted"],
        reason=decision["reason"],
        burnout_guard=decision["burnout_guard"],
        available_slots=[ScheduleSlotResponse(**value) for value in result["available_slots"]],
    )

@router.post("/journal/analyze", response_model=JournalReflectionResponse, response_model_by_alias=True)
async def analyze_journal(
    request: JournalReflectionRequest,
    _: Annotated[dict, Depends(require_service_scope("ai.invoke"))],
    settings: Settings = Depends(get_app_settings),
) -> JournalReflectionResponse:
    """Return a bounded non-persistent reflection for owner-approved storage upstream."""
    result = await JournalReflectionService(settings).analyze(request)
    logger.info("Journal reflection completed with %s emotion labels.", len(result.emotions))
    return result

@router.post("/roles/discover", response_model=RoleDiscoveryResponse, response_model_by_alias=True)
async def discover_roles(
    request: RoleDiscoveryRequest,
    _: Annotated[dict, Depends(require_service_scope("ai.invoke"))],
    settings: Settings = Depends(get_app_settings),
) -> RoleDiscoveryResponse:
    """Return candidate life roles; ASP.NET owns proposal persistence and approval."""
    result = await RoleDiscoveryService(settings).discover(request)
    logger.info("Role discovery completed with %s candidates.", len(result.candidates))
    return result


@router.post("/v1/ai/coach/respond")
async def coach_respond(
    payload: dict,
    _: Annotated[dict, Depends(require_service_scope("ai.invoke"))],
    settings: Settings = Depends(get_app_settings),
) -> dict:
    """Return Coach response, observations, and action proposals."""
    from speroflow_ai.services.coach import CoachService
    result = await CoachService(settings).respond(payload)
    logger.info("Coach response generated with %s observations and %s proposals.", len(result.observations), len(result.proposals))
    return result.model_dump()