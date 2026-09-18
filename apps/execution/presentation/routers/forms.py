"""FastAPI presentation router for Form Submissions, completions, and PI approvals.

Requirements: PRD-SYS-001, PRD-SYS-002, PRD-SYS-004, PRD-SYS-121, PRD-TRN-002, ADR-2202, Trace-1, Trace-3, Trace-17
"""

import hashlib
import os
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    HTTPException,
    Request,
)
from pydantic import BaseModel, Field
from sqlalchemy import select

from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    ClinicalObservation,
    FormSubmission,
)
from apps.execution.edit_checks import run_asynchronous_form_edit_checks
from apps.execution.rtsm_authz import (
    redact_response,
    verify_site_access,
)
from apps.execution.site_execution_gatekeeper import (
    require_site_mutation_authorization,
)
from apps.execution.trial_lock import TrialLockManager
from apps.execution.ucum import get_normalized_representation
from packages.security.rbac import (
    ROLE_SITE_INVESTIGATOR,
    Principal,
    can_access_study,
    get_principal,
    require_roles,
    verify_not_auditor,
)
from packages.security.sig_token_verifier import verify_and_consume_sig_token

router = APIRouter(tags=["Forms"])

VALID_SIGNING_REASONS = {
    "I attest that this data is accurate and complete.",
    "PI approval and sign-off.",
    "Review and confirmation.",
    "DATA_RECORDING",
    "DATA_ENTRY_COMPLETED",
    "PI_REVIEW",
    "PI_SIGN_OFF",
    "COMPLIANCE_ATTESTATION",
}

SITE_SCOPED_ROLES = {"site_crc", "site_investigator", "investigator", "crc"}


class FormSubmissionCreate(BaseModel):
    """Pydantic schema for creating a FormSubmission."""

    study_id: str
    site_id: str
    subject_id: str
    visit_id: str | None = None
    form_id: str
    protocol_version: str | None = None
    payload: dict[str, Any] | None = None


class FormSubmissionApprove(BaseModel):
    """Pydantic schema for PI approval of a FormSubmission."""

    signing_reason: str
    signature_manifest: dict[str, Any]


class FormSubmissionStatusEnum(StrEnum):
    DRAFT = "DRAFT"
    COMPLETED = "COMPLETED"
    APPROVED = "APPROVED"


class FormSubmissionResponse(BaseModel):
    """Pydantic schema returning FormSubmission details."""

    id: str
    study_id: str
    site_id: str | None = Field(...)
    subject_id: str
    visit_id: str | None = None
    form_id: str
    status: FormSubmissionStatusEnum
    version: int
    is_deleted: bool
    signature_manifest: dict[str, Any] | None = None
    protocol_version: str | None = None
    payload: dict[str, Any] | None = None
    is_active: bool
    is_readonly: bool
    cloned_from_id: str | None = None


class BatchSignOffRequest(BaseModel):
    """Pydantic schema for requesting a batch electronic sign-off."""

    study_id: str
    target_type: str
    target_ids: list[str]
    signing_reason: str


class BatchSignOffResponse(BaseModel):
    """Pydantic schema returning batch electronic sign-off outcome."""

    status: str
    approved_submission_ids: list[str]
    skipped_submission_ids: list[str]
    skipped_targets: list[str]


@router.post(
    "/api/v1/execution/form-submissions",
    response_model=FormSubmissionResponse,
    status_code=201,
)
async def create_form_submission(
    request: Request,
    payload: FormSubmissionCreate,
    roles: list[str] = Depends(verify_not_auditor),
) -> FormSubmissionResponse:
    """Create a new FormSubmission in DRAFT status."""
    async with db_manager.get_session_maker()() as session:
        u_id = current_user_id.get() or "system"
        proto_tag = payload.protocol_version or "1.0.0"
        if payload.site_id:
            await require_site_mutation_authorization(
                site_id=payload.site_id,
                user_id=u_id,
                operation_type="FORM_SUBMISSION",
                study_id=payload.study_id,
                protocol_version=proto_tag,
            )

        sub = FormSubmission(
            study_id=payload.study_id,
            site_id=payload.site_id,
            subject_id=payload.subject_id,
            visit_id=payload.visit_id,
            form_id=payload.form_id,
            status="DRAFT",
            signature_manifest=None,
            protocol_version=payload.protocol_version,
            payload=payload.payload,
            is_active=True,
            is_readonly=False,
            cloned_from_id=None,
        )
        session.add(sub)

        if payload.payload and isinstance(payload.payload, dict):
            obs_list = payload.payload.get("observations")
            if isinstance(obs_list, list):
                for item in obs_list:
                    if isinstance(item, dict):
                        f_val = item.get("value")
                        if f_val is not None:
                            try:
                                f_val = float(f_val)
                            except (ValueError, TypeError):
                                f_val = None
                        unit = item.get("unit")
                        norm_val, norm_unit = (
                            get_normalized_representation(f_val, unit)
                            if f_val is not None and unit is not None
                            else (None, None)
                        )
                        obs = ClinicalObservation(
                            subject_id=payload.subject_id,
                            study_id=payload.study_id,
                            site_id=payload.site_id,
                            visit_id=payload.visit_id,
                            domain=item.get("domain", "VS"),
                            test_code=item.get("test_code", "UNKNOWN"),
                            test_name=item.get(
                                "test_name", item.get("test_code", "UNKNOWN")
                            ),
                            value=f_val,
                            value_string=str(
                                item.get("value_string") or item.get("value") or ""
                            ),
                            unit=unit,
                            normalized_value=norm_val,
                            normalized_unit=norm_unit,
                            protocol_version_tag=payload.protocol_version or "1.0.0",
                            protocol_version_index=1,
                        )
                        session.add(obs)
            else:
                field_mapping = {
                    "vssbp": ("VS", "VSSBP", "Systolic Blood Pressure", "mmHg"),
                    "vsdpb": ("VS", "VSDPB", "Diastolic Blood Pressure", "mmHg"),
                    "pulse": ("VS", "VSHR", "Pulse Rate", "bpm"),
                    "vshr": ("VS", "VSHR", "Pulse Rate", "bpm"),
                    "weight": ("VS", "WT", "Weight", "kg"),
                    "wt": ("VS", "WT", "Weight", "kg"),
                    "height": ("VS", "HT", "Height", "m"),
                    "ht": ("VS", "HT", "Height", "m"),
                    "temp": ("VS", "TEMP", "Body Temperature", "Cel"),
                    "vstemp": ("VS", "TEMP", "Body Temperature", "Cel"),
                    "wbc": ("LB", "WBC", "White Blood Cell Count", "10^9/L"),
                    "lb_wbc": ("LB", "WBC", "White Blood Cell Count", "10^9/L"),
                    "gluc": ("LB", "GLUC", "Glucose", "mg/dL"),
                    "lb_gluc": ("LB", "GLUC", "Glucose", "mg/dL"),
                    "alt": ("LB", "ALT", "Alanine Aminotransferase", "U/L"),
                    "lb_alt": ("LB", "ALT", "Alanine Aminotransferase", "U/L"),
                    "brthdt": ("DM", "BRTHDT", "Date of Birth", None),
                    "sex": ("DM", "SEX", "Sex at Birth", None),
                }
                for k, v in payload.payload.items():
                    k_clean = str(k).lower().replace(".", "_").replace("-", "_")
                    if (
                        k_clean in field_mapping
                        and v is not None
                        and str(v).strip() != ""
                    ):
                        dom, t_code, t_name, unit = field_mapping[k_clean]
                        try:
                            f_val = float(v)
                        except (ValueError, TypeError):
                            f_val = None
                        norm_val, norm_unit = (
                            get_normalized_representation(f_val, unit)
                            if f_val is not None and unit is not None
                            else (None, None)
                        )
                        obs = ClinicalObservation(
                            subject_id=payload.subject_id,
                            study_id=payload.study_id,
                            site_id=payload.site_id,
                            visit_id=payload.visit_id,
                            domain=dom,
                            test_code=t_code,
                            test_name=t_name,
                            value=f_val,
                            value_string=str(v),
                            unit=unit,
                            normalized_value=norm_val,
                            normalized_unit=norm_unit,
                            protocol_version_tag=payload.protocol_version or "1.0.0",
                            protocol_version_index=1,
                        )
                        session.add(obs)

        await session.commit()

        stmt = select(FormSubmission).where(FormSubmission.id == sub.id)
        res = await session.execute(stmt)
        sub_db = res.scalar_one()

        return FormSubmissionResponse(
            id=sub_db.id,
            study_id=sub_db.study_id,
            site_id=sub_db.site_id,
            subject_id=sub_db.subject_id,
            visit_id=sub_db.visit_id,
            form_id=sub_db.form_id,
            status=sub_db.status,
            version=sub_db.version,
            is_deleted=sub_db.is_deleted,
            signature_manifest=sub_db.signature_manifest,
            protocol_version=sub_db.protocol_version,
            payload=sub_db.payload,
            is_active=sub_db.is_active,
            is_readonly=sub_db.is_readonly,
            cloned_from_id=sub_db.cloned_from_id,
        )


@router.post(
    "/api/v1/execution/form-submissions/{submission_id}/complete",
    response_model=FormSubmissionResponse,
)
async def complete_form_submission(
    submission_id: str,
    request: Request,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(verify_not_auditor),
) -> FormSubmissionResponse:
    """Transition a FormSubmission from DRAFT to COMPLETED."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(
            FormSubmission.id == submission_id, FormSubmission.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        sub = res.scalars().first()
        if not sub:
            raise HTTPException(status_code=404, detail="Form submission not found")

        if sub.is_readonly or not sub.is_active:
            raise HTTPException(
                status_code=400,
                detail="Cannot modify a read-only or inactive form submission.",
            )

        if sub.status != "DRAFT":
            raise HTTPException(
                status_code=400,
                detail=f"Form submission can only be completed from DRAFT status. Current: {sub.status}",
            )

        sub.status = "COMPLETED"
        await session.commit()

        user_id = current_user_id.get() or "system"
        change_reason = current_change_reason.get() or "Form Completion Edit Checks"

        background_tasks.add_task(
            run_asynchronous_form_edit_checks,
            db_manager.get_session_maker(),
            sub.id,
            user_id=user_id,
            change_reason=change_reason,
        )

        stmt_ref = select(FormSubmission).where(FormSubmission.id == submission_id)
        res_ref = await session.execute(stmt_ref)
        sub_db = res_ref.scalar_one()

        return FormSubmissionResponse(
            id=sub_db.id,
            study_id=sub_db.study_id,
            site_id=sub_db.site_id,
            subject_id=sub_db.subject_id,
            visit_id=sub_db.visit_id,
            form_id=sub_db.form_id,
            status=sub_db.status,
            version=sub_db.version,
            is_deleted=sub_db.is_deleted,
            signature_manifest=sub_db.signature_manifest,
            protocol_version=sub_db.protocol_version,
            payload=sub_db.payload,
            is_active=sub_db.is_active,
            is_readonly=sub_db.is_readonly,
            cloned_from_id=sub_db.cloned_from_id,
        )


@router.post(
    "/api/v1/execution/form-submissions/{submission_id}/approve",
    response_model=FormSubmissionResponse,
)
async def approve_form_submission(
    submission_id: str,
    request: Request,
    payload: FormSubmissionApprove,
    roles: list[str] = Depends(require_roles(ROLE_SITE_INVESTIGATOR)),
) -> FormSubmissionResponse:
    """PI Approve/Sign-off a completed FormSubmission."""
    if payload.signing_reason not in VALID_SIGNING_REASONS:
        raise HTTPException(
            status_code=400,
            detail=f"Invalid signing reason. Must be one of: {sorted(list(VALID_SIGNING_REASONS))}",
        )

    if not payload.signature_manifest:
        raise HTTPException(
            status_code=400,
            detail="Signature manifest is required for PI sign-off.",
        )

    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(
            FormSubmission.id == submission_id, FormSubmission.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        sub = res.scalars().first()
        if not sub:
            raise HTTPException(status_code=404, detail="Form submission not found")

        if sub.is_readonly or not sub.is_active:
            raise HTTPException(
                status_code=400,
                detail="Cannot modify a read-only or inactive form submission.",
            )

        if sub.status != "COMPLETED":
            raise HTTPException(
                status_code=400,
                detail=f"PI approval must only be possible from COMPLETED status. Current: {sub.status}",
            )

        u_id = current_user_id.get() or "system"
        if sub.site_id:
            await require_site_mutation_authorization(
                site_id=sub.site_id,
                user_id=u_id,
                operation_type="PI_SIGNOFF",
                study_id=sub.study_id,
                protocol_version=sub.protocol_version or "1.0.0",
                user_role="Principal Investigator",
            )

        sub.status = "APPROVED"
        sub.signature_manifest = payload.signature_manifest
        await session.commit()

        stmt_ref = select(FormSubmission).where(FormSubmission.id == submission_id)
        res_ref = await session.execute(stmt_ref)
        sub_db = res_ref.scalar_one()

        return FormSubmissionResponse(
            id=sub_db.id,
            study_id=sub_db.study_id,
            site_id=sub_db.site_id,
            subject_id=sub_db.subject_id,
            visit_id=sub_db.visit_id,
            form_id=sub_db.form_id,
            status=sub_db.status,
            version=sub_db.version,
            is_deleted=sub_db.is_deleted,
            signature_manifest=sub_db.signature_manifest,
            protocol_version=sub_db.protocol_version,
            payload=sub_db.payload,
            is_active=sub_db.is_active,
            is_readonly=sub_db.is_readonly,
            cloned_from_id=sub_db.cloned_from_id,
        )


@router.post(
    "/api/v1/execution/batch-sign-off",
    response_model=BatchSignOffResponse,
)
async def post_batch_sign_off(
    request: Request,
    payload: BatchSignOffRequest,
    roles: list[str] = Depends(
        require_roles(
            ROLE_SITE_INVESTIGATOR,
            detail="Forbidden: Only a Principal Investigator (PI) can perform batch electronic sign-off.",
        )
    ),
) -> BatchSignOffResponse:
    """Perform a PI-only, atomic batch electronic-signature for form-, visit-, and subject-level sign-off."""
    sig_token = request.headers.get("X-Sig-Token")
    user_id = getattr(request.state, "user_id", "")

    sig_payload = verify_and_consume_sig_token(sig_token, user_id)
    token_batch_id = sig_payload.get("batch_id")
    if not token_batch_id:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    norm_study = str(payload.study_id).strip()
    norm_type = str(payload.target_type).strip().upper()
    sorted_ids = sorted([str(tid).strip() for tid in payload.target_ids])
    norm_ids = ",".join(sorted_ids)
    norm_reason = str(payload.signing_reason).strip()

    binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
    computed_batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()

    if token_batch_id != computed_batch_id:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    target_type_upper = payload.target_type.upper()

    async with db_manager.get_session_maker()() as session:
        if TrialLockManager.is_locked():
            raise PermissionError(
                "Trial is currently locked in a read-only state due to a security violation."
            )

        async with session.begin():
            if target_type_upper == "FORM":
                stmt = select(FormSubmission).where(
                    FormSubmission.id.in_(payload.target_ids),
                    FormSubmission.study_id == payload.study_id,
                    FormSubmission.is_deleted.is_(False),
                )
            elif target_type_upper == "VISIT":
                stmt = select(FormSubmission).where(
                    FormSubmission.visit_id.in_(payload.target_ids),
                    FormSubmission.study_id == payload.study_id,
                    FormSubmission.is_deleted.is_(False),
                )
            elif target_type_upper == "SUBJECT":
                stmt = select(FormSubmission).where(
                    FormSubmission.subject_id.in_(payload.target_ids),
                    FormSubmission.study_id == payload.study_id,
                    FormSubmission.is_deleted.is_(False),
                )
            else:
                raise HTTPException(status_code=400, detail="Invalid target type.")

            res = await session.execute(stmt)
            resolved_subs = list(res.scalars().all())

            approved_submission_ids = []
            skipped_submission_ids = []
            targets_with_approvals = set()

            secret = os.getenv(
                "GATEWAY_SECRET", "internal-gateway-secret-12345"
            ).encode()

            for sub in resolved_subs:
                if sub.is_readonly or not sub.is_active:
                    skipped_submission_ids.append(sub.id)
                    continue

                sub_target_id = None
                if target_type_upper == "FORM":
                    sub_target_id = sub.id
                elif target_type_upper == "VISIT":
                    sub_target_id = sub.visit_id
                elif target_type_upper == "SUBJECT":
                    sub_target_id = sub.subject_id

                if sub.status == "COMPLETED":
                    if sub.site_id and TrialLockManager.is_site_locked(sub.site_id):
                        raise PermissionError(
                            f"Site {sub.site_id} is currently locked in a read-only state."
                        )
                    if sub.visit_id and TrialLockManager.is_visit_locked(sub.visit_id):
                        raise PermissionError(
                            f"Visit {sub.visit_id} is currently locked in a read-only state."
                        )
                    if sub.subject_id and TrialLockManager.is_subject_locked(
                        sub.subject_id
                    ):
                        raise PermissionError(
                            f"Subject {sub.subject_id} is currently locked in a read-only state."
                        )
                    if sub.form_id and TrialLockManager.is_form_locked(sub.form_id):
                        raise PermissionError(
                            f"Form {sub.form_id} is currently locked in a read-only state."
                        )

                    if sub_target_id:
                        targets_with_approvals.add(sub_target_id)

                    form_payload = {
                        "study_id": sub.study_id,
                        "site_id": sub.site_id,
                        "subject_id": sub.subject_id,
                        "visit_id": sub.visit_id,
                        "form_id": sub.form_id,
                    }
                    binding_payload = {
                        "form_payload": form_payload,
                        "pre_approval_version": sub.version,
                    }
                    import apps.execution.main as main_mod

                    canonical_hash = main_mod.generate_canonical_signature(
                        binding_payload, secret
                    )

                    username = request.state.user_id or "unknown"
                    full_name = (
                        f"{username.replace('_', ' ').replace('.', ' ').title()}"
                    )
                    if "pi" in username.lower() or "investigator" in username.lower():
                        full_name += ", MD"

                    signing_timestamp_utc = (
                        datetime.now(UTC).isoformat().replace("+00:00", "Z")
                    )

                    reason_mapping = {
                        "I attest that this data is accurate and complete.": (
                            "DATA_RECORDING",
                            "I attest that this data is accurate and complete.",
                        ),
                        "PI approval and sign-off.": (
                            "PI_APPROVAL",
                            "I approve this clinical record and confirm medical responsibility.",
                        ),
                        "Review and confirmation.": (
                            "REVIEW_CONFIRMATION",
                            "Review and confirmation.",
                        ),
                        "DATA_RECORDING": ("DATA_RECORDING", "I author this data"),
                        "DATA_ENTRY_COMPLETED": (
                            "DATA_RECORDING",
                            "I author this data",
                        ),
                        "PI_REVIEW": ("PI_APPROVAL", "I approve this clinical record"),
                        "PI_SIGN_OFF": (
                            "PI_APPROVAL",
                            "I approve this clinical record and confirm medical responsibility.",
                        ),
                        "COMPLIANCE_ATTESTATION": (
                            "COMPLIANCE_ATTESTATION",
                            "I review and confirm this data",
                        ),
                    }

                    reason_key = payload.signing_reason
                    if reason_key in reason_mapping:
                        signing_reason_code, signing_reason_text = reason_mapping[
                            reason_key
                        ]
                    else:
                        signing_reason_code = reason_key.replace(" ", "_").upper()
                        signing_reason_text = reason_key

                    network_ip_address = request.headers.get("x-forwarded-for") or (
                        request.client.host if request.client else "127.0.0.1"
                    )
                    device_user_agent = request.headers.get("user-agent") or "Unknown"

                    manifest = {
                        "signer_id": username,
                        "timestamp": signing_timestamp_utc,
                        "signing_reason": payload.signing_reason,
                        "ip_address": network_ip_address,
                        "user_agent": device_user_agent,
                        "signed_version": sub.version + 1,
                        "canonical_signature_hash": canonical_hash,
                        "signature_manifestation": {
                            "signer_username": username,
                            "signer_full_name": full_name,
                            "signing_timestamp_utc": signing_timestamp_utc,
                            "signing_reason_code": signing_reason_code,
                            "signing_reason_text": signing_reason_text,
                            "network_ip_address": network_ip_address,
                            "device_user_agent": device_user_agent,
                            "record_id": sub.id,
                            "record_version": sub.version + 1,
                            "signature_hash_sha256": canonical_hash,
                        },
                    }

                    sub.status = "APPROVED"
                    sub.signature_manifest = manifest
                    session.add(sub)
                    approved_submission_ids.append(sub.id)
                else:
                    skipped_submission_ids.append(sub.id)

            skipped_targets = []
            for tid in payload.target_ids:
                if tid not in targets_with_approvals:
                    skipped_targets.append(tid)

            return BatchSignOffResponse(
                status="success",
                approved_submission_ids=approved_submission_ids,
                skipped_submission_ids=skipped_submission_ids,
                skipped_targets=skipped_targets,
            )


@router.get(
    "/api/v1/execution/form-submissions",
    response_model=list[FormSubmissionResponse],
)
async def list_form_submissions(
    study_id: str | None = None,
    subject_id: str | None = None,
    visit_id: str | None = None,
    form_id: str | None = None,
    include_inactive: bool = False,
    principal: Principal = Depends(get_principal),
) -> list[FormSubmissionResponse]:
    """List form submissions with filters."""
    if study_id and not can_access_study(principal, study_id):
        return []

    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(FormSubmission.is_deleted.is_(False))
        if not include_inactive:
            stmt = stmt.where(FormSubmission.is_active.is_(True))
        if study_id:
            stmt = stmt.where(FormSubmission.study_id == study_id)
        if subject_id:
            stmt = stmt.where(FormSubmission.subject_id == subject_id)
        if visit_id:
            stmt = stmt.where(FormSubmission.visit_id == visit_id)
        if form_id:
            stmt = stmt.where(FormSubmission.form_id == form_id)

        user_site_roles = [r for r in principal.roles if r in SITE_SCOPED_ROLES]
        if user_site_roles or principal.assigned_sites:
            stmt = stmt.where(FormSubmission.site_id.in_(principal.assigned_sites))

        if principal.assigned_studies:
            stmt = stmt.where(FormSubmission.study_id.in_(principal.assigned_studies))

        res = await session.execute(stmt)
        subs = res.scalars().all()

        return [
            redact_response(
                FormSubmissionResponse(
                    id=sub.id,
                    study_id=sub.study_id,
                    site_id=sub.site_id,
                    subject_id=sub.subject_id,
                    visit_id=sub.visit_id,
                    form_id=sub.form_id,
                    status=sub.status,
                    version=sub.version,
                    is_deleted=sub.is_deleted,
                    signature_manifest=sub.signature_manifest,
                    protocol_version=sub.protocol_version,
                    payload=sub.payload,
                    is_active=sub.is_active,
                    is_readonly=sub.is_readonly,
                    cloned_from_id=sub.cloned_from_id,
                ),
                principal,
            )
            for sub in subs
        ]


@router.get(
    "/api/v1/execution/form-submissions/{submission_id}",
    response_model=FormSubmissionResponse,
)
async def get_form_submission(
    submission_id: str,
    principal: Principal = Depends(get_principal),
) -> FormSubmissionResponse:
    """Retrieve a single form submission by ID."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(
            FormSubmission.id == submission_id, FormSubmission.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        sub = res.scalars().first()
        if not sub:
            raise HTTPException(status_code=404, detail="Form submission not found")

        verify_site_access(
            principal, sub.site_id, study_id=sub.study_id, subject_id=sub.subject_id
        )

        return redact_response(
            FormSubmissionResponse(
                id=sub.id,
                study_id=sub.study_id,
                site_id=sub.site_id,
                subject_id=sub.subject_id,
                visit_id=sub.visit_id,
                form_id=sub.form_id,
                status=sub.status,
                version=sub.version,
                is_deleted=sub.is_deleted,
                signature_manifest=sub.signature_manifest,
                protocol_version=sub.protocol_version,
                payload=sub.payload,
                is_active=sub.is_active,
                is_readonly=sub.is_readonly,
                cloned_from_id=sub.cloned_from_id,
            ),
            principal,
        )
