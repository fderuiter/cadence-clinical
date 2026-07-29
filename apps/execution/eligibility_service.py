"""
Eligibility Evaluation Service for clinical trials.

This service retrieves eligibility criteria from the designer service,
builds the clinical data context from decrypted demographics and observations,
evaluates the subject eligibility against the criteria, and manages
the randomization guard.
"""

import logging
from typing import Union

from eligibility.evaluator import evaluate_eligibility
from eligibility.models import AggregateEligibilityResult
from fastapi import HTTPException
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import ClinicalSubject
from apps.execution.designer_client import fetch_study_criteria
from apps.execution.eligibility_context import build_eligibility_context

logger = logging.getLogger("execution-eligibility-service")


async def evaluate_subject_eligibility(
    study_id: str,
    subject: Union[str, ClinicalSubject],
    session: AsyncSession,
) -> AggregateEligibilityResult:
    """Fetch eligibility criteria, build subject context, and evaluate overall eligibility.

    Args:
        study_id (str): Clinical study identifier.
        subject (Union[str, ClinicalSubject]): Subject ID, UUID, or ClinicalSubject model.
        session (AsyncSession): Active SQLAlchemy session.

    Returns:
        AggregateEligibilityResult: Detailed individual and aggregated eligibility outcomes.
    """
    # 1. Fetch criteria from the Designer service client
    criteria = await fetch_study_criteria(study_id)

    # 2. Build non-PHI clinical data context
    context = await build_eligibility_context(subject, session)

    # 3. Evaluate criteria
    result = evaluate_eligibility(criteria, context)
    return result


def verify_subject_eligible_for_randomization(subject: ClinicalSubject) -> None:
    """Guard function ensuring only definitively eligible (ENROLLED) subjects proceed to randomization.

    Args:
        subject (ClinicalSubject): The ClinicalSubject database model instance.

    Raises:
        HTTPException: HTTP 400 rejection if subject is not ENROLLED.
    """
    current_status = getattr(subject, "status", None)
    if current_status != "ENROLLED":
        logger.warning(
            "Allocation Rejected: Subject %s has state '%s'. Only ENROLLED subjects can proceed.",
            subject.subject_id,
            current_status,
        )
        raise HTTPException(
            status_code=400,
            detail=f"Allocation Rejected: Subject is in state '{current_status}'. Only ENROLLED subjects can proceed to randomization.",
        )
