"""FastAPI presentation router for Protocol Rules, version migrations, and study publication events.

Requirements: PRD-SYS-001, PRD-SYS-042, PRD-MDR-001, Trace-1, Trace-13
"""

import uuid
from datetime import UTC, datetime
from typing import Any

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    HTTPException,
)
from pydantic import BaseModel, Field
from sqlalchemy import select

from apps.execution.database.context import (
    audit_context,
    current_change_reason,
    current_ip_address,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    MigrationRule,
    StudyAuthoredRule,
    TranslationJob,
)
from apps.execution.translator import process_translation
from packages.security.rbac import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    get_normalized_roles,
    require_roles,
)

router = APIRouter(tags=["Rules & Migrations"])


class StudyEvent(BaseModel):
    """Pydantic schema for study-published webhook event payload."""

    study_id: str
    payload: dict[str, Any] = Field(default_factory=dict)


class TranslationJobResponse(BaseModel):
    """Pydantic schema returning translation job status and metadata."""

    id: str
    study_id: str
    status: str
    odm_payload: str | None = None
    openrosa_payload: str | None = None
    error_message: str | None = None
    warnings: list[dict[str, Any]] | None = Field(default_factory=list)


class MigrationRuleCreate(BaseModel):
    """Pydantic schema for creating a protocol migration rule."""

    study_id: str
    source_version: str
    target_version: str
    rule_type: str
    source_field: str | None = None
    target_field: str | None = None
    default_value_string: str | None = None
    default_value_float: float | None = None


class MigrationRuleResponse(BaseModel):
    """Pydantic schema returning protocol migration rule details."""

    id: str
    study_id: str
    source_version: str
    target_version: str
    rule_type: str
    source_field: str | None = None
    target_field: str | None = None
    default_value_string: str | None = None
    default_value_float: float | None = None


@router.post("/events/study-published")
async def study_published(
    event: StudyEvent, background_tasks: BackgroundTasks
) -> dict[str, str]:
    """Ingest study publication events and trigger layout generation asynchronously."""
    user_id = current_user_id.get()
    change_reason = current_change_reason.get()

    payload = event.payload or {}

    layout_warnings = payload.get("layout_warnings") or payload.get("layoutWarnings")
    if (
        not layout_warnings
        and "protocol" in payload
        and isinstance(payload["protocol"], dict)
    ):
        layout_warnings = payload["protocol"].get("layout_warnings") or payload[
            "protocol"
        ].get("layoutWarnings")

    has_warnings = False
    if (
        isinstance(layout_warnings, list)
        and len(layout_warnings) > 0
        or isinstance(layout_warnings, bool)
        and layout_warnings
        or isinstance(layout_warnings, (int, float))
        and layout_warnings > 0
    ):
        has_warnings = True

    justification = (
        payload.get("layout_justification")
        or payload.get("layoutJustification")
        or payload.get("justification")
    )
    if (
        not justification
        and "protocol" in payload
        and isinstance(payload["protocol"], dict)
    ):
        justification = (
            payload["protocol"].get("layout_justification")
            or payload["protocol"].get("layoutJustification")
            or payload["protocol"].get("justification")
        )

    if has_warnings:
        if (
            not justification
            or not isinstance(justification, str)
            or not justification.strip()
        ):
            raise HTTPException(
                status_code=400,
                detail="Layout validation failed: unresolved layout warnings exist, but no clinical justification was provided.",
            )

        u_id = user_id or "system"
        with audit_context(u_id, justification):
            async with db_manager.get_session_maker()() as session:
                async with session.begin():
                    deviation_audit = AuditLog(
                        id=str(uuid.uuid4()),
                        table_name="layout_deviation_audit",
                        record_id=event.study_id,
                        action="DEVIATION",
                        user_id=u_id,
                        ip_address=current_ip_address.get() or "127.0.0.1",
                        timestamp=datetime.now(UTC).replace(tzinfo=None),
                        old_values=None,
                        new_values={
                            "status": "APPROVED",
                            "justification": justification,
                            "layout_warnings": layout_warnings,
                            "timestamp": datetime.now(UTC).isoformat(),
                        },
                        change_reason=justification,
                    )
                    session.add(deviation_audit)

    cross_form_rules = (
        event.payload.get("cross_form_check")
        or event.payload.get("cross_form_checks")
        or []
    )
    if cross_form_rules:
        u_id = user_id or "system"
        reason = change_reason or "Ingest published cross-form rules"
        with audit_context(u_id, reason):
            async with db_manager.get_session_maker()() as session:
                async with session.begin():
                    stmt = select(StudyAuthoredRule).where(
                        StudyAuthoredRule.study_id == event.study_id,
                        StudyAuthoredRule.is_active.is_(True),
                        StudyAuthoredRule.is_deleted.is_(False),
                    )
                    res = await session.execute(stmt)
                    prior_rules = res.scalars().all()
                    for r in prior_rules:
                        r.is_active = False
                        r.version += 1
                        session.add(r)

                    pub_ver = str(event.payload.get("version") or "1.0")
                    for r_data in cross_form_rules:
                        new_rule = StudyAuthoredRule(
                            study_id=event.study_id,
                            rule_id=r_data["id"],
                            rule_type=r_data.get("type") or "cross_form_check",
                            condition=r_data["condition"],
                            query_message=r_data["query_message"],
                            message=r_data["query_message"],
                            publication_version=pub_ver,
                            is_active=True,
                        )
                        session.add(new_rule)

    job_id = str(uuid.uuid4())
    background_tasks.add_task(
        process_translation,
        event.study_id,
        event.payload,
        db_manager.get_session_maker(),
        user_id=user_id,
        change_reason=change_reason,
        job_id=job_id,
    )
    return {
        "status": "accepted",
        "message": "Translation job queued in background.",
        "job_id": job_id,
        "id": job_id,
    }


@router.get(
    "/api/v1/execution/translation/jobs", response_model=list[TranslationJobResponse]
)
async def list_translation_jobs() -> list[TranslationJobResponse]:
    """Retrieve a list of historical translation jobs."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(TranslationJob)
        res = await session.execute(stmt)
        jobs = res.scalars().all()
        return [
            TranslationJobResponse(
                id=job.id,
                study_id=job.study_id,
                status=job.status,
                odm_payload=job.odm_payload,
                openrosa_payload=job.openrosa_payload,
                error_message=job.error_message,
                warnings=job.warnings or [],
            )
            for job in jobs
        ]


@router.get(
    "/api/v1/execution/translation/jobs/{job_id}", response_model=TranslationJobResponse
)
async def get_translation_job(job_id: str) -> TranslationJobResponse:
    """Query the execution status, output metadata, and error messages of a single translation job by ID."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(TranslationJob).where(TranslationJob.id == job_id)
        res = await session.execute(stmt)
        job = res.scalars().first()
        if not job:
            raise HTTPException(status_code=404, detail="Translation job not found")
        return TranslationJobResponse(
            id=job.id,
            study_id=job.study_id,
            status=job.status,
            odm_payload=job.odm_payload,
            openrosa_payload=job.openrosa_payload,
            error_message=job.error_message,
            warnings=job.warnings or [],
        )


@router.post(
    "/api/v1/execution/migration-rules",
    response_model=MigrationRuleResponse,
    status_code=201,
)
async def create_migration_rule(
    payload: MigrationRuleCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> MigrationRuleResponse:
    """Create a new protocol version migration rule."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            rule = MigrationRule(
                study_id=payload.study_id,
                source_version=payload.source_version,
                target_version=payload.target_version,
                rule_type=payload.rule_type,
                source_field=payload.source_field,
                target_field=payload.target_field,
                default_value_string=payload.default_value_string,
                default_value_float=payload.default_value_float,
            )
            session.add(rule)

        stmt = select(MigrationRule).where(MigrationRule.id == rule.id)
        res = await session.execute(stmt)
        saved = res.scalar_one()

        return MigrationRuleResponse(
            id=saved.id,
            study_id=saved.study_id,
            source_version=saved.source_version,
            target_version=saved.target_version,
            rule_type=saved.rule_type,
            source_field=saved.source_field,
            target_field=saved.target_field,
            default_value_string=saved.default_value_string,
            default_value_float=saved.default_value_float,
        )


@router.get(
    "/api/v1/execution/migration-rules",
    response_model=list[MigrationRuleResponse],
)
async def list_migration_rules(
    study_id: str,
    roles: list[str] = Depends(get_normalized_roles),
) -> list[MigrationRuleResponse]:
    """List migration rules for a clinical study."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(MigrationRule).where(
            MigrationRule.study_id == study_id,
            MigrationRule.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        rules = res.scalars().all()
        return [
            MigrationRuleResponse(
                id=r.id,
                study_id=r.study_id,
                source_version=r.source_version,
                target_version=r.target_version,
                rule_type=r.rule_type,
                source_field=r.source_field,
                target_field=r.target_field,
                default_value_string=r.default_value_string,
                default_value_float=r.default_value_float,
            )
            for r in rules
        ]
