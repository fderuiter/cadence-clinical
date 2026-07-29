"""
Quality Control (QC) review lifecycle state machine for the eTMF module.
Defines allowed transitions, role permissions, and a helper to execute status changes
with immutable audit trail logging in compliance with 21 CFR Part 11.
"""

from typing import Dict, Set

from sqlalchemy import select, func
from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.models import DocumentQCTransition, DocumentStatus, TMFDocument
from packages.security.rbac import Principal, has_permission, normalize_role

# Defined allowed forward and rejection transitions
ALLOWED_TRANSITIONS: Dict[str, Set[str]] = {
    DocumentStatus.DRAFT: {DocumentStatus.TECHNICAL_QC},
    DocumentStatus.TECHNICAL_QC: {DocumentStatus.CLINICAL_QC, DocumentStatus.REJECTED},
    DocumentStatus.CLINICAL_QC: {
        DocumentStatus.APPROVED,
        DocumentStatus.REJECTED,
        DocumentStatus.SIGNED,
    },
    DocumentStatus.APPROVED: {
        DocumentStatus.ARCHIVED,
        DocumentStatus.REJECTED,
        DocumentStatus.SIGNED,
    },
    DocumentStatus.REJECTED: {DocumentStatus.DRAFT},
    DocumentStatus.ARCHIVED: set(),
    DocumentStatus.SIGNED: set(),
}


def has_required_role(actor_role: str | list[str], target_status: str) -> bool:
    """
    Checks if the given actor roles possess the permission to transition to the target status.
    All transitions are authorized against centralized role-to-permission mappings.
    """
    if isinstance(actor_role, str):
        raw_roles = [r.strip() for r in actor_role.split(",") if r.strip()]
    else:
        raw_roles = [str(r).strip() for r in actor_role if str(r).strip()]
    actor_roles = [normalize_role(r) for r in raw_roles]

    principal = Principal(
        user_id="lifecycle_actor",
        roles=actor_roles,
        raw_roles=raw_roles,
    )
    perm_to_check = f"etmf_document:transition_{target_status.lower()}"
    return has_permission(principal, perm_to_check)


async def validate_and_transition_document_status(
    session: AsyncSession,
    document: TMFDocument,
    to_status: str,
    actor_id: str,
    actor_role: str,
    reason_for_change: str,
) -> None:
    """
    Validates and executes a status transition on an eTMF document.
    Ensures that transitions conform to the state-machine and that
    the user is authorized based on target-stage required roles.
    Saves an append-only DocumentQCTransition record to record the history.

    Args:
        session: Database session.
        document: The TMFDocument instance being transitioned.
        to_status: The target status.
        actor_id: Identity of the user executing the transition.
        actor_role: Roles of the user executing the transition.
        reason_for_change: Part 11 justification reason for change.

    Raises:
        ValueError: If status is invalid, transition is disallowed, or change reason is invalid.
        PermissionError: If the actor is not authorized due to insufficient role permissions.
    """
    valid_statuses = {
        DocumentStatus.DRAFT,
        DocumentStatus.TECHNICAL_QC,
        DocumentStatus.CLINICAL_QC,
        DocumentStatus.APPROVED,
        DocumentStatus.ARCHIVED,
        DocumentStatus.REJECTED,
    }

    if to_status not in valid_statuses:
        raise ValueError(
            f"Invalid status: '{to_status}'. Must be one of {sorted(list(valid_statuses))}."
        )

    # Validate state-machine transition
    current = document.status or DocumentStatus.DRAFT
    allowed = ALLOWED_TRANSITIONS.get(current, set())
    if to_status not in allowed:
        raise ValueError(
            f"Invalid transition: Cannot transition document from '{current}' to '{to_status}'."
        )

    # Validate actor role authorization (RBAC gates)
    if not has_required_role(actor_role, to_status):
        raise PermissionError(
            f"Permission Denied: User with role(s) '{actor_role}' is not authorized to transition document to status '{to_status}'."
        )

    # Validate Part 11 reason for change
    if not reason_for_change or len(reason_for_change.strip()) < 10:
        raise ValueError(
            "Reason for change is mandatory and must be at least 10 characters long."
        )

    # Execute transition
    from_status = current
    document.status = to_status

    # Record append-only history log
    role_str = (
        ",".join(actor_role)
        if isinstance(actor_role, (list, tuple, set))
        else str(actor_role)
    )

    # Sequentially calculate transition_sequence
    stmt_seq = select(func.max(DocumentQCTransition.transition_sequence)).where(
        DocumentQCTransition.document_id == document.id
    )
    res_seq = await session.execute(stmt_seq)
    max_seq = res_seq.scalar()
    next_seq = (max_seq or 0) + 1

    transition_record = DocumentQCTransition(
        document_id=document.id,
        transition_sequence=next_seq,
        from_status=from_status,
        to_status=to_status,
        actor_id=actor_id,
        actor_role=role_str,
        reason_for_change=reason_for_change.strip(),
    )
    session.add(transition_record)
    await session.flush()
