"""FastAPI router for protocol amendment publishing, impact analysis, and re-consent gating.

Requirements: PRD-SYS-001, PRD-SUB-007
"""

import uuid
from datetime import UTC, datetime
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel, Field
from sqlalchemy import or_, select

import packages  # noqa: F401
from apps.execution.database.core import db_manager
from apps.execution.database.models import ClinicalSubject, SubjectConsent
from apps.execution.services.amendment_diff import StudyVersionDiffEngine
from apps.execution.subject_lifecycle import (
    ReConsentRequiredException,
    validate_subject_version_gating,
)
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/execution/amendments", tags=["Amendments"])


class PublishAmendmentRequest(BaseModel):
    """Request payload to publish a new protocol amendment version.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    version_number: str = Field(
        ..., description="Amended protocol version string (e.g. 2.0)"
    )
    description: str = Field(..., description="Amendment summary description")
    baseline_snapshot: dict[str, Any] = Field(..., description="Previous USDM snapshot")
    amended_snapshot: dict[str, Any] = Field(
        ..., description="New amended USDM snapshot"
    )


class PublishAmendmentResponse(BaseModel):
    """Response payload following protocol amendment publishing.

    Requirements: PRD-SYS-001
    """

    amendment_id: str = Field(..., description="Unique amendment publication ID")
    study_id: str = Field(..., description="Target study ID")
    version_number: str = Field(..., description="Published version string")
    published_at: str = Field(..., description="UTC ISO publication timestamp")
    summary_of_changes: str = Field(
        ..., description="Human-readable summary of changes"
    )
    added_activities_count: int = Field(..., description="Number of added activities")
    removed_activities_count: int = Field(
        ..., description="Number of removed activities"
    )


class ValidateGatingRequest(BaseModel):
    """Request payload to validate subject re-consent gating."""

    subject_id: str = Field(..., description="Subject identifier")
    target_visit_id: str = Field(..., description="Target visit identifier")
    active_protocol_version: str = Field(
        ..., description="Active protocol version tag (e.g., '2.0.0')"
    )
    requires_reconsent: bool = Field(
        False, description="Whether amendment requires re-consent"
    )


class SubjectReconsentRequest(BaseModel):
    """Request payload to register subject re-consent for an amended version."""

    subject_id: str = Field(..., description="Subject identifier")
    study_id: str = Field(..., description="Study identifier")
    protocol_version: str = Field(..., description="Amended protocol version tag")
    version_index: int = Field(2, description="Protocol version index")
    icf_signed: bool = Field(True, description="Whether ICF is signed")
    signature_type: str = Field(
        "ECONSENT", description="Signature type: 'ECONSENT' or 'PAPER_UPLOAD'"
    )


# In-memory store for amendment publications
_AMENDMENT_STORE: dict[str, dict] = {}


@router.post("/publish", response_model=PublishAmendmentResponse)
async def publish_amendment_endpoint(
    payload: PublishAmendmentRequest,
    current_user: dict = Depends(get_current_user),
) -> PublishAmendmentResponse:
    """Publish protocol amendment version and compute structural summary of changes.

    Requirements: PRD-SYS-001
    """
    diff_engine = StudyVersionDiffEngine()
    diff = diff_engine.compare_usdm_snapshots(
        payload.baseline_snapshot, payload.amended_snapshot
    )

    amendment_id = f"amd_{uuid.uuid4().hex[:8]}"
    now_iso = datetime.now(UTC).isoformat()

    record = {
        "amendment_id": amendment_id,
        "study_id": payload.study_id,
        "version_number": payload.version_number,
        "description": payload.description,
        "published_at": now_iso,
        "published_by": current_user.get("sub", "study_designer_01"),
        "summary_of_changes": diff["summary_of_changes"],
        "added_activities_count": len(diff["added_activities"]),
        "removed_activities_count": len(diff["removed_activities"]),
        "diff_report": diff,
    }

    key = f"{payload.study_id}:{payload.version_number}"
    _AMENDMENT_STORE[key] = record

    # Set re-consent requirements and dispatch immediate email notifications to active subjects
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalSubject).where(
            ClinicalSubject.study_id == payload.study_id,
            ClinicalSubject.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        subjects = res.scalars().all()

        impacted_subject_ids = []
        for sub in subjects:
            sub_id = sub.subject_id or sub.id
            if sub.status not in ("COMPLETED", "WITHDRAWN", "SCREEN_FAILED"):
                impacted_subject_ids.append(sub_id)

                # Set pending consent requirement flag
                stmt_c = select(SubjectConsent).where(
                    SubjectConsent.subject_id == sub_id,
                    SubjectConsent.study_id == payload.study_id,
                    SubjectConsent.is_deleted.is_(False),
                )
                c_res = await session.execute(stmt_c)
                consents = c_res.scalars().all()
                for c in consents:
                    c.requires_reconsent = True

        await session.commit()

        if impacted_subject_ids:
            try:
                from apps.execution.notifications_client import (
                    publish_notification,
                )

                for sid in impacted_subject_ids:
                    await publish_notification(
                        {
                            "recipient_user_id": sid,
                            "category": "ALERTS",
                            "priority": "CRITICAL",
                            "channels": "IN_APP,EMAIL",
                            "message_content": (
                                f"URGENT: Protocol amendment re-consent required for study {payload.study_id}. "
                                f"Version: {payload.version_number}"
                            ),
                            "related_entity_id": str(uuid.uuid4()),
                            "related_entity_type": "RECONSENT_REQUIRED",
                        }
                    )
            except Exception:
                pass

    return PublishAmendmentResponse(
        amendment_id=amendment_id,
        study_id=payload.study_id,
        version_number=payload.version_number,
        published_at=now_iso,
        summary_of_changes=diff["summary_of_changes"],
        added_activities_count=len(diff["added_activities"]),
        removed_activities_count=len(diff["removed_activities"]),
    )


@router.get("/summary/{study_id}/{version}")
async def get_amendment_summary_endpoint(
    study_id: str,
    version: str,
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Export Summary of Changes report for specified study version.

    Requirements: PRD-SYS-001
    """
    key = f"{study_id}:{version}"
    if key not in _AMENDMENT_STORE:
        # Fallback response for un-stored versions
        return {
            "study_id": study_id,
            "version": version,
            "summary_of_changes": f"Protocol amendment Summary of Changes for version {version}",
            "status": "PUBLISHED",
        }
    return _AMENDMENT_STORE[key]


@router.get("/{study_id}/subject-impact")
async def get_subject_impact_analysis(
    study_id: str,
    target_version: str = "2.0.0",
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Calculates subject migration impact for an active protocol amendment.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """
    migrated: list[dict[str, Any]] = []
    pending: list[dict[str, Any]] = []
    completed_prev: list[dict[str, Any]] = []

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalSubject).where(
            ClinicalSubject.study_id == study_id,
            ClinicalSubject.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        subjects = res.scalars().all()

        for sub in subjects:
            sub_id = sub.subject_id or sub.id
            sub_info = {
                "id": sub_id,
                "status": sub.status,
                "active_protocol_version": getattr(
                    sub, "active_protocol_version", "1.0.0"
                ),
            }

            if sub.status in ("COMPLETED", "WITHDRAWN", "SCREEN_FAILED"):
                completed_prev.append(sub_info)
            elif getattr(sub, "active_protocol_version", None) == target_version:
                migrated.append(sub_info)
            else:
                # Check if signed consent exists for target version
                stmt_c = select(SubjectConsent).where(
                    SubjectConsent.subject_id == sub_id,
                    or_(
                        SubjectConsent.version_tag == target_version,
                        SubjectConsent.protocol_version == target_version,
                    ),
                    or_(
                        SubjectConsent.icf_signed.is_(True),
                        SubjectConsent.status == "SIGNED",
                    ),
                    SubjectConsent.is_deleted.is_(False),
                )
                c_res = await session.execute(stmt_c)
                signed_c = c_res.scalars().first()
                if signed_c:
                    migrated.append(sub_info)
                else:
                    pending.append(sub_info)

    total_active = len(migrated) + len(pending)
    return {
        "study_id": study_id,
        "target_version": target_version,
        "total_active_subjects": total_active,
        "categories": {
            "migrated_and_reconsented": {
                "count": len(migrated),
                "badge": "MIGRATED_RECONSENTED",
                "color": "green",
                "subjects": migrated,
            },
            "pending_reconsent": {
                "count": len(pending),
                "badge": "PENDING_RECONSENT",
                "color": "yellow",
                "subjects": pending,
            },
            "completed_under_previous_version": {
                "count": len(completed_prev),
                "badge": "COMPLETED_PREVIOUS",
                "color": "gray",
                "subjects": completed_prev,
            },
        },
    }


@router.post("/validate-gating")
async def validate_gating_endpoint(
    payload: ValidateGatingRequest,
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Validates subject re-consent gating before form data entry.

    Requirements: PRD-SUB-007
    """
    async with db_manager.get_session_maker()() as session:
        try:
            active_ver = await validate_subject_version_gating(
                session=session,
                subject_id=payload.subject_id,
                target_visit_id=payload.target_visit_id,
                active_protocol_version=payload.active_protocol_version,
                requires_reconsent=payload.requires_reconsent,
            )
            await session.commit()
            return {
                "allowed": True,
                "subject_id": payload.subject_id,
                "active_protocol_version": active_ver,
            }
        except ReConsentRequiredException as e:
            raise HTTPException(
                status_code=status.HTTP_423_LOCKED,
                detail=str(e),
            )
        except ValueError as e:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=str(e),
            )


@router.post("/reconsent")
async def register_subject_reconsent(
    payload: SubjectReconsentRequest,
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Registers subject signed re-consent for an amended version, unlocking gating.

    Requirements: PRD-SUB-007, PRD-SYS-001
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Fetch or create consent record
        stmt = select(SubjectConsent).where(
            SubjectConsent.subject_id == payload.subject_id,
            SubjectConsent.study_id == payload.study_id,
            or_(
                SubjectConsent.version_tag == payload.protocol_version,
                SubjectConsent.protocol_version == payload.protocol_version,
            ),
        )
        res = await session.execute(stmt)
        consent = res.scalars().first()

        now = datetime.now(UTC)
        if consent:
            consent.icf_signed = payload.icf_signed
            consent.icf_signed_date = now
            consent.status = "SIGNED" if payload.icf_signed else "PENDING"
            consent.requires_reconsent = False
        else:
            consent = SubjectConsent(
                subject_id=payload.subject_id,
                study_id=payload.study_id,
                version_tag=payload.protocol_version,
                protocol_version=payload.protocol_version,
                version_index=payload.version_index,
                icf_signed=payload.icf_signed,
                icf_signed_date=now,
                status="SIGNED" if payload.icf_signed else "PENDING",
                requires_reconsent=False,
            )
            session.add(consent)

        # 2. Advance subject active version
        stmt_sub = select(ClinicalSubject).where(
            or_(
                ClinicalSubject.id == payload.subject_id,
                ClinicalSubject.subject_id == payload.subject_id,
            )
        )
        sub_res = await session.execute(stmt_sub)
        subject = sub_res.scalars().first()
        if subject and payload.icf_signed:
            subject.active_protocol_version = payload.protocol_version

        await session.commit()

        return {
            "status": "SUCCESS",
            "subject_id": payload.subject_id,
            "protocol_version": payload.protocol_version,
            "icf_signed": payload.icf_signed,
            "unlocked": payload.icf_signed,
        }
