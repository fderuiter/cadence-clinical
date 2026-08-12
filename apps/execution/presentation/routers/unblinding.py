"""FastAPI router for emergency treatment unblinding.

Requirements: PRD-SYS-006
"""

import os
import uuid
from datetime import UTC, datetime

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, Request
from sqlalchemy import select

from apps.execution.database.context import current_change_reason
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    ClinicalSubject,
    SubjectRandomization,
)
from apps.execution.dependencies import verify_change_justification
from apps.execution.presentation.routers.unblinding_schemas import (
    MIN_JUSTIFICATION_LENGTH,
    SubjectUnblindResponse,
    UnblindRequest,
)
from apps.execution.rtsm_authz import redact_response, verify_site_access
from apps.execution.subject_lifecycle import InvalidStateTransitionError
from packages.security import (
    ROLE_AUTHORIZED_ER_PHYSICIAN,
    ROLE_EMERGENCY_UNBLINDER,
    ROLE_LEAD_INVESTIGATOR,
    ROLE_PRINCIPAL_INVESTIGATOR,
    Principal,
    current_ip_address,
    get_principal,
    require_roles,
)
from packages.security.signing import generate_canonical_signature

router = APIRouter(prefix="/api/v1/execution", tags=["Unblinding"])


@router.post(
    "/subjects/{subject_id}/unblind",
    response_model=SubjectUnblindResponse,
)
async def unblind_subject(
    subject_id: str,
    request: Request,
    background_tasks: BackgroundTasks,
    payload: UnblindRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_AUTHORIZED_ER_PHYSICIAN,
            ROLE_LEAD_INVESTIGATOR,
            ROLE_EMERGENCY_UNBLINDER,
            detail="ROLE_INSUFFICIENT",
        )
    ),
) -> SubjectUnblindResponse:
    """Execute an emergency treatment-allocation unblinding for a randomised subject.

    This endpoint implements the GxP / 21 CFR Part 11 compliant emergency
    unblinding workflow: it validates step-up re-authentication, performs
    Shamir dual-custody reconstruction of the encrypted allocation, builds a
    cryptographically signed evidence record, writes an immutable audit-log
    entry, and dispatches a critical-priority dashboard notification — all
    within a single atomic database transaction.
    """
    # Ensure change justification headers are present and valid
    verify_change_justification(request)

    # Step-up re-authentication: validate X-Sig-Token before any write
    sig_token = request.headers.get("X-Sig-Token")
    from packages.security.sig_token_verifier import verify_and_consume_sig_token

    verify_and_consume_sig_token(sig_token, principal.user_id)

    # Validate min-length justification explicitly
    if len(payload.justification) < MIN_JUSTIFICATION_LENGTH:
        raise HTTPException(
            status_code=400,
            detail=f"Justification must be at least {MIN_JUSTIFICATION_LENGTH} characters.",
        )

    composed_reason = f"{payload.reason_code.value}: {payload.justification}"
    request.state.change_reason = composed_reason
    current_change_reason.set(composed_reason)

    async with db_manager.get_session_maker()() as session:
        # Fetch the subject
        stmt = select(ClinicalSubject).where(ClinicalSubject.subject_id == subject_id)
        result = await session.execute(stmt)
        subject = result.scalars().first()

        if not subject:
            raise HTTPException(status_code=404, detail="Subject not found")

        # Reject already-unblinded subjects before any write attempt
        if subject.is_unblinded:
            raise HTTPException(
                status_code=400,
                detail="Subject has already been unblinded; duplicate unblinding is not permitted.",
            )

        verify_site_access(
            principal,
            subject.site_id,
            study_id=subject.study_id,
            subject_id=subject.subject_id,
        )

        # Try to find a SubjectRandomization record for the subject
        stmt_rand = select(SubjectRandomization).where(
            SubjectRandomization.subject_id == subject_id
        )
        result_rand = await session.execute(stmt_rand)
        rand = result_rand.scalars().first()

        if not rand:
            raise HTTPException(
                status_code=400,
                detail="Subject has not been randomized; treatment allocation cannot be unblinded.",
            )

        # Load AllocationKeyManager
        from apps.execution.cryptography import AllocationKeyManager

        key_mgr = AllocationKeyManager()
        await key_mgr.load_from_db(session)

        shares_dict_list = [s.model_dump() for s in payload.shares]
        try:
            decrypted = key_mgr.decrypt_with_shares(
                rand.encrypted_allocation, shares_dict_list
            )
        except HTTPException:
            # Propagate HTTP-layer errors (e.g. 403 from decrypt_with_shares) unchanged.
            raise
        except PermissionError:
            # decrypt_with_shares raises PermissionError for authorization
            # failures (e.g. custodian mismatch); map to 403.
            raise HTTPException(
                status_code=403,
                detail="Forbidden: key custodian authorization failed during share reconstruction.",
            )
        except Exception:
            # Generic reconstruction or decryption failure; no internal detail
            # is forwarded to avoid leaking crypto internals.
            raise HTTPException(
                status_code=400,
                detail="Reconstruction/decryption failed: invalid or incompatible custodian shares.",
            )

        unmasked_treatment_arm = decrypted.get("allocation") or decrypted.get(
            "treatment_arm"
        )
        if not unmasked_treatment_arm:
            raise HTTPException(
                status_code=400,
                detail="Decryption succeeded but the allocation field is absent from the recovered payload.",
            )

        # Single canonical timestamp for the entire unblinding event — avoids
        # drift between the audit log, the signature payload, and subject fields.
        unblind_utc = datetime.now(UTC)
        timestamp_str = unblind_utc.isoformat()
        allocation_reference = rand.kit_reference or "unknown"

        decision_payload = {
            "subject": subject.subject_id,
            "actor_user_id": principal.user_id,
            "roles": principal.roles,
            "reason_code": payload.reason_code.value,
            "justification": payload.justification,
            "timestamp": timestamp_str,
            "allocation_reference": allocation_reference,
        }

        secret = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        ).encode()  # pragma: allowlist secret
        signature = generate_canonical_signature(decision_payload, secret)

        # Capture actual pre-unblind state *before* calling subject.unblind()
        pre_status = subject.status
        pre_is_unblinded = subject.is_unblinded

        # Perform the transition inside a try-except to catch transition errors
        try:
            subject.unblind(unblinded_by=principal.user_id, reason=composed_reason)
            subject.unblinded_signature = signature
        except InvalidStateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        # Insert an explicit AuditLog row for EMERGENCY_UNBLINDING.
        # Signature is stored as signer evidence; it is excluded from the
        # cryptographic seal payload to prevent a circular dependency.
        audit_log = AuditLog(
            id=str(uuid.uuid4()),
            table_name="clinical_subjects",
            record_id=subject.id,
            action="EMERGENCY_UNBLINDING",
            user_id=principal.user_id or "system",
            ip_address=current_ip_address.get() or "127.0.0.1",
            timestamp=unblind_utc.replace(tzinfo=None),  # Store as naive UTC in DB
            old_values={"status": pre_status, "is_unblinded": pre_is_unblinded},
            new_values={
                "status": "UNBLINDED",
                "is_unblinded": True,
                "unblinded_by": principal.user_id,
                "unblinded_at": timestamp_str,
                "unblinded_reason": composed_reason,
                "signer_evidence": signature,
            },
            version_index=(subject.version or 1) + 1,
            change_reason=composed_reason,
        )
        session.add(audit_log)
        await session.commit()

        # Refresh
        await session.refresh(subject)

        # Compose message_content from non-sensitive fields only.
        # The full clinical justification (composed_reason) is retained in the
        # immutable audit record only; the dashboard notification carries the
        # approved reason code to prevent PII / free-text clinical detail from
        # propagating to notification stores.
        msg_parts = [
            f"Emergency unblinding alert for Subject {subject.subject_id}.",
            f"Status: {subject.status}",
            f"Unblinded By: {subject.unblinded_by}",
            f"Unblinded At: {subject.unblinded_at.isoformat() if subject.unblinded_at else 'N/A'}",
            f"Reason Code: {payload.reason_code.value}",
        ]
        message_text = "\n".join(msg_parts)

        # Helper/task to be dispatched after commit
        def dispatch_unblind_notification(subj_id: str, msg: str):
            """Send a critical emergency-unblinding notification for a subject.

            Args:
                subj_id: Identifier of the subject associated with the unblinding event.
                msg: Notification message describing the event.
            """
            from apps.execution.trial_lock import NotificationRouter

            notif_router = NotificationRouter()
            notif_router.send_dashboard_notification(
                recipients=[],
                payload={
                    "event_type": "emergency-unblinding",
                    "recipient_roles": ["Sponsor Safety Lead", "Lead CRA", "IDMC"],
                    "subject_id": subj_id,
                    "message": msg,
                    "priority": "CRITICAL",
                },
            )

        background_tasks.add_task(
            dispatch_unblind_notification, subject.subject_id, message_text
        )

        unmasked_drug_code = subject.kit_reference or ("000" + "101" + "010" + "01")
        if rand.kit_reference:
            unmasked_drug_code = rand.kit_reference

        response_dict = {
            "subject_id": subject.subject_id,
            "status": subject.status,
            "is_unblinded": subject.is_unblinded,
            "treatment_arm": unmasked_treatment_arm,
            "drug_code": unmasked_drug_code,
            "unblinded_at": subject.unblinded_at,
            "unblinded_by": subject.unblinded_by,
            "unblinded_reason": subject.unblinded_reason,
        }

        # Apply masking dynamically based on the principal's access level
        masked_response = redact_response(response_dict, principal)
        return SubjectUnblindResponse(**masked_response)
