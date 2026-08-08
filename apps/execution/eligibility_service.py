"""
Eligibility Evaluation Service for clinical trials.

This service retrieves eligibility criteria from the designer service,
builds the clinical data context from decrypted demographics and observations,
evaluates the subject eligibility against the criteria, and manages
the randomization guard.
"""

import logging
from typing import Any

from apps.execution.designer_client import fetch_study_criteria
from apps.execution.eligibility_context import build_eligibility_context
from apps.execution.exceptions import SubjectEligibilityError
from apps.execution.src.domain.acl.designer_eligibility_dto import (
    AggregateEligibilityResultDTO,
    evaluate_eligibility_dto,
)

logger = logging.getLogger("execution-eligibility-service")


async def evaluate_subject_eligibility(
    study_id: str,
    subject: Any,
    session: Any,
) -> AggregateEligibilityResultDTO:
    """Fetch eligibility criteria, build subject context, and evaluate overall eligibility.

    Args:
        study_id (str): Clinical study identifier.
        subject (Any): Subject ID, UUID, dict, or clinical subject representation.
        session (Any): Active database session.

    Returns:
        AggregateEligibilityResultDTO: Detailed individual and aggregated eligibility outcomes.
    """
    # 1. Fetch criteria from the Designer service client
    criteria = await fetch_study_criteria(study_id)

    # 2. Build non-PHI clinical data context
    context = await build_eligibility_context(subject, session)

    # 3. Evaluate criteria
    return evaluate_eligibility_dto(criteria, context)


def verify_subject_eligible_for_randomization(subject: Any) -> None:
    """Guard function ensuring only definitively eligible (ENROLLED) subjects proceed to randomization.

    Args:
        subject (Any): The clinical subject representation (dict or object).

    Raises:
        SubjectEligibilityError: rejection if subject is not ENROLLED.
    """
    if isinstance(subject, dict):
        current_status = subject.get("status")
        subject_id = subject.get("subject_id")
    else:
        current_status = getattr(subject, "status", None)
        subject_id = getattr(subject, "subject_id", None)

    if current_status != "ENROLLED":
        logger.warning(
            "Allocation Rejected: Subject %s has state '%s'. Only ENROLLED subjects can proceed.",
            subject_id,
            current_status,
        )
        raise SubjectEligibilityError(
            f"Allocation Rejected: Subject is in state '{current_status}'. Only ENROLLED subjects can proceed to randomization."
        )
