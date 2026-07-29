"""
eCRF Context Builder for Clinical Eligibility Engine.

This module derives a unified, safe (non-PHI) context dictionary from a subject's
demographics and historical eCRF observations, strictly obeying established
clinical and database precedence rules.

Canonical Key Format:
    - Demographics: eCRF.DM.AGE, eCRF.DM.SEX
    - Observations: eCRF.<DOMAIN>.<VARIABLE> (e.g. eCRF.LB.ALT, eCRF.VS.SYSBP)

Precedence Rules:
    - Multiple observations of the same variable (CDASH/SDTM test_code) are resolved
      using the latest observation date.
    - Ties are broken deterministically by ordering on the record's database version
      and sequential primary ID descending (order_by(observation_date.desc(), version.desc(), id.desc())).
    - Only the highest-precedence observation's value is exposed.
    - Standard numeric 'value' is prioritized, falling back to 'value_string' if numeric is None.
    - Missing or None values are entirely omitted from the returned context dictionary
      to preserve absent semantics (indeterminate outcomes in Kleene 3-valued logic).
"""

import logging
from datetime import date
from typing import Any, Dict, Optional, Union

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import ClinicalObservation, ClinicalSubject
from apps.execution.demographics import get_safe_demographics

logger = logging.getLogger("execution-eligibility-context")


async def build_eligibility_context(
    subject: Union[str, ClinicalSubject],
    session: AsyncSession,
    observation_date: Optional[Union[date, Any]] = None,
) -> Dict[str, Any]:
    """Derive demographic and observation key-values for clinical eligibility evaluation.

    Args:
        subject (Union[str, ClinicalSubject]): Subject ID, UUID, or ClinicalSubject instance.
        session (AsyncSession): Active async SQLAlchemy database session.
        observation_date (Optional[Union[date, Any]]): Date to compute age against. If None,
            automatically defaults to the latest observation date or the current date.

    Returns:
        Dict[str, Any]: Mapping of eCRF.<DOMAIN>.<VARIABLE> to non-PHI values.
    """
    context: Dict[str, Any] = {}

    # 1. Resolve Subject object
    subject_obj: Optional[ClinicalSubject] = None
    subject_id: Optional[str] = None

    if isinstance(subject, str):
        # Resolve by subject_id or id (UUID)
        stmt_subj = select(ClinicalSubject).where(
            (ClinicalSubject.subject_id == subject) | (ClinicalSubject.id == subject)
        )
        res_subj = await session.execute(stmt_subj)
        subject_obj = res_subj.scalars().first()
        subject_id = subject
    else:
        subject_obj = subject
        if subject_obj is not None:
            subject_id = subject_obj.subject_id or subject_obj.id

    if not subject_id:
        logger.warning("Could not determine subject_id for eligibility context construction.")
        return context

    # 2. Fetch observations with precedence rules
    # Order: latest observation_date first, tie-break on version desc, id desc
    stmt_obs = (
        select(ClinicalObservation)
        .where(
            ClinicalObservation.subject_id == subject_id,
            ClinicalObservation.is_deleted.is_(False),
        )
        .order_by(
            ClinicalObservation.observation_date.desc(),
            ClinicalObservation.version.desc(),
            ClinicalObservation.id.desc(),
        )
    )
    res_obs = await session.execute(stmt_obs)
    observations = list(res_obs.scalars().all())

    # 3. Derive observation keys: latest wins
    latest_obs_date: Optional[Any] = None
    if observations:
        latest_obs_date = observations[0].observation_date

    for obs in observations:
        domain = (obs.domain or "").strip().upper()
        test_code = (obs.test_code or "").strip().upper()
        if not domain or not test_code:
            continue

        key = f"eCRF.{domain}.{test_code}"
        if key not in context:
            # Prioritize standard numeric value over string value
            val = obs.value if obs.value is not None else obs.value_string
            if val is not None:
                context[key] = val

    # 4. Resolve observation_date for demographics
    resolved_date = observation_date
    if resolved_date is None:
        if latest_obs_date is not None:
            # Convert datetime to date if needed
            if hasattr(latest_obs_date, "date"):
                resolved_date = latest_obs_date.date()
            else:
                resolved_date = latest_obs_date
        else:
            resolved_date = date.today()

    # 5. Populate demographics with safe non-PHI values
    if subject_obj is not None:
        demo = get_safe_demographics(subject_obj, resolved_date)
        age = demo.get("age")
        gender = demo.get("gender")

        if age is not None:
            context["eCRF.DM.AGE"] = age
        if gender is not None and gender != "U":
            context["eCRF.DM.SEX"] = gender

    return context
