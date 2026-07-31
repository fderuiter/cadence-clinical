"""Shared validation and upsert logic for single-target and bulk SDV sign-off.

Requirements: PRD-QRY-005, PRD-QRY-006, PRD-QRY-007
"""

from datetime import datetime, timezone
from typing import Optional, Tuple

from sqlalchemy import select

from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalSubject,
    ClinicalVisit,
    SDVSignOff,
)


async def validate_and_upsert_sdv_target(
    session,
    scope,
    target_id: str,
    subject_id: str,
    study_id: str,
    site_id: Optional[str],
    verifier_id: str,
) -> Tuple[Optional[SDVSignOff], Optional[str]]:
    """Validates subject/study consistency and target existence, then upserts the SDVSignOff record.

    For FIELD scope, also updates the corresponding ClinicalObservation's verification fields.
    Does not commit the session.

    Returns:
        (sdv_signoff, error_message)
    """
    # 1. Resolve scope value
    scope_val = scope.value if hasattr(scope, "value") else str(scope)
    if scope_val not in ["FIELD", "PAGE", "VISIT"]:
        return None, f"Invalid scope: {scope_val}"

    # 2. Validate Subject exists and is consistent with Study
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.subject_id == subject_id,
        ClinicalSubject.study_id == study_id,
    )
    res_subj = await session.execute(stmt_subj)
    subj_db = res_subj.scalars().first()
    if not subj_db:
        return None, "Subject not found or inconsistent study reference."

    # 3. Scope-specific validation
    obs_db = None
    if scope_val == "FIELD":
        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.id == target_id,
            ClinicalObservation.subject_id == subject_id,
            ClinicalObservation.study_id == study_id,
        )
        res_obs = await session.execute(stmt_obs)
        obs_db = res_obs.scalars().first()
        if not obs_db:
            return (
                None,
                "Clinical observation not found or inconsistent target/subject/study reference.",
            )
    elif scope_val == "VISIT":
        stmt_visit = select(ClinicalVisit).where(
            ClinicalVisit.id == target_id,
            ClinicalVisit.subject_id == subject_id,
            ClinicalVisit.study_id == study_id,
        )
        res_visit = await session.execute(stmt_visit)
        visit_db = res_visit.scalars().first()
        if not visit_db:
            return (
                None,
                "Clinical visit not found or inconsistent target/subject/study reference.",
            )
    elif scope_val == "PAGE":
        stmt_page_obs = select(ClinicalObservation).where(
            ClinicalObservation.page_id == target_id,
            ClinicalObservation.subject_id == subject_id,
            ClinicalObservation.study_id == study_id,
        )
        res_page_obs = await session.execute(stmt_page_obs)
        if not res_page_obs.scalars().first():
            return (
                None,
                "Page ID not found or inconsistent target/subject/study reference.",
            )

    # 4. Apply sign-off behavior
    verified_at = datetime.now(timezone.utc).replace(tzinfo=None)

    stmt_signoff = select(SDVSignOff).where(
        SDVSignOff.scope == scope_val,
        SDVSignOff.target_id == target_id,
        SDVSignOff.subject_id == subject_id,
        SDVSignOff.study_id == study_id,
    )
    res_signoff = await session.execute(stmt_signoff)
    signoff_db = res_signoff.scalars().first()

    resolved_site_id = site_id or (
        subj_db.site_id if hasattr(subj_db, "site_id") else None
    )

    if signoff_db:
        signoff_db.is_verified = True
        signoff_db.verified_by = verifier_id
        signoff_db.verified_at = verified_at
        signoff_db.dropped_reason = None
        signoff_db.dropped_at = None
    else:
        signoff_db = SDVSignOff(
            scope=scope_val,
            target_id=target_id,
            subject_id=subject_id,
            study_id=study_id,
            site_id=resolved_site_id,
            is_verified=True,
            verified_by=verifier_id,
            verified_at=verified_at,
        )
        session.add(signoff_db)

    # For FIELD scope, update the ClinicalObservation too
    if scope_val == "FIELD" and obs_db:
        obs_db.is_sdv_verified = True
        obs_db.sdv_verified_by = verifier_id
        obs_db.sdv_verified_at = verified_at

    return signoff_db, None
