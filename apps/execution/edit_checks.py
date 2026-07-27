import logging
from datetime import datetime
from typing import List, Optional

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from apps.execution.database.context import audit_context
from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalQuery,
    ClinicalVisit,
    FormSubmission,
    PendingPredecessorCheck,
)
from apps.execution.evaluator import evaluate_ast

logger = logging.getLogger(__name__)

VISIT_SEQUENCE = ["SCREENING", "BASELINE", "WEEK_4", "WEEK_8"]


class EditCheckRule:
    rule_id: str
    rule_type: str  # "field_level", "cross_form", "longitudinal"
    message: str

    async def evaluate(
        self, session: AsyncSession, observation: ClinicalObservation
    ) -> Optional[str]:
        raise NotImplementedError()


class OutlierCheckRule(EditCheckRule):
    rule_id = "OUTLIER_CHECK"
    rule_type = "field_level"
    message = "Observation is a statistical outlier within the cohort."

    async def evaluate(
        self, session: AsyncSession, observation: ClinicalObservation
    ) -> Optional[str]:
        if observation.is_outlier:
            return self.message
        return None


class HighSystolicBPCheckRule(EditCheckRule):
    rule_id = "SYSBP_HIGH_CHECK"
    rule_type = "field_level"
    message = "Systolic blood pressure entry of {value} mmHg is critically high."

    async def evaluate(
        self, session: AsyncSession, observation: ClinicalObservation
    ) -> Optional[str]:
        if observation.test_code == "SYSBP" and observation.value is not None:
            if observation.value > 200.0:
                return self.message.format(value=observation.value)
        return None


class AEConsentTemporalCheckRule(EditCheckRule):
    rule_id = "AE_CONSENT_TEMPORAL_CHECK"
    rule_type = "cross_form"
    message = "Adverse event onset date cannot be before informed consent date."

    async def evaluate(
        self, session: AsyncSession, observation: ClinicalObservation
    ) -> Optional[str]:
        # This rule evaluates if we have both AE onset and Informed Consent date
        subject_id = observation.subject_id

        # Find the latest AE onset observation and latest Informed Consent observation for this subject
        ae_stmt = (
            select(ClinicalObservation)
            .where(
                ClinicalObservation.subject_id == subject_id,
                ClinicalObservation.test_code.in_(["AESTDTC", "AE_ONSET"]),
                ClinicalObservation.is_deleted.is_(False),
            )
            .order_by(ClinicalObservation.observation_date.desc())
        )

        consent_stmt = (
            select(ClinicalObservation)
            .where(
                ClinicalObservation.subject_id == subject_id,
                ClinicalObservation.test_code.in_(
                    ["DSSTDTC", "INFORMED_CONSENT", "INFORMED_CONSENT_DATE"]
                ),
                ClinicalObservation.is_deleted.is_(False),
            )
            .order_by(ClinicalObservation.observation_date.desc())
        )

        ae_res = await session.execute(ae_stmt)
        ae_obs = ae_res.scalars().first()

        consent_res = await session.execute(consent_stmt)
        consent_obs = consent_res.scalars().first()

        if not ae_obs or not consent_obs:
            return None

        # Compare dates using the AST evaluator
        ae_date = ae_obs.observation_date
        if ae_obs.value_string:
            try:
                ae_date = datetime.fromisoformat(ae_obs.value_string)
            except ValueError:
                pass

        consent_date = consent_obs.observation_date
        if consent_obs.value_string:
            try:
                consent_date = datetime.fromisoformat(consent_obs.value_string)
            except ValueError:
                pass

        # Build context and AST for evaluate_ast
        context = {
            "AE_DATE": ae_date.isoformat(),
            "CONSENT_DATE": consent_date.isoformat(),
        }
        node = {
            "type": "comparison",
            "operator": "<",
            "operands": [
                {"type": "field_ref", "field_ref": {"field_id": "AE_DATE"}},
                {"type": "field_ref", "field_ref": {"field_id": "CONSENT_DATE"}},
            ],
        }

        if evaluate_ast(node, context) is True:
            return self.message

        return None


class WeightLossCheckRule(EditCheckRule):
    rule_id = "WEIGHT_LOSS_CHECK"
    rule_type = "longitudinal"
    message = "Subject weight loss is greater than 20% compared to predecessor visit."

    async def evaluate(
        self, session: AsyncSession, observation: ClinicalObservation
    ) -> Optional[str]:
        # Only evaluates weight parameters
        if observation.test_code not in ["WEIGHT", "VSWT"]:
            return None

        if observation.value is None:
            return None

        subject_id = observation.subject_id

        # 1. Get current visit name
        if not observation.visit_id:
            return None

        current_visit_stmt = select(ClinicalVisit).where(
            ClinicalVisit.id == observation.visit_id
        )
        current_visit_res = await session.execute(current_visit_stmt)
        current_visit = current_visit_res.scalars().first()
        if not current_visit:
            return None

        current_visit_name = current_visit.visit_name.upper()
        if current_visit_name not in VISIT_SEQUENCE:
            return None

        idx = VISIT_SEQUENCE.index(current_visit_name)
        if idx == 0:
            # First visit, no predecessor visit exists to check against
            return None

        predecessor_visit_name = VISIT_SEQUENCE[idx - 1]

        # 2. Get predecessor visit and weight observation
        pred_visit_stmt = select(ClinicalVisit).where(
            ClinicalVisit.subject_id == subject_id,
            ClinicalVisit.visit_name.ilike(predecessor_visit_name),
            ClinicalVisit.study_id == observation.study_id,
        )
        pred_visit_res = await session.execute(pred_visit_stmt)
        pred_visit = pred_visit_res.scalars().first()

        if not pred_visit:
            # Predecessor visit is unavailable/incomplete: return "PENDING_PREDECESSOR" signal
            return "PENDING_PREDECESSOR"

        # Check if the predecessor visit's FormSubmission is "DRAFT" (incomplete)
        pred_sub_stmt = select(FormSubmission).where(
            FormSubmission.subject_id == subject_id,
            FormSubmission.visit_id == pred_visit.id,
            FormSubmission.is_deleted.is_(False),
        )
        pred_sub_res = await session.execute(pred_sub_stmt)
        pred_subs = pred_sub_res.scalars().all()
        if pred_subs and any(sub.status == "DRAFT" for sub in pred_subs):
            # Predecessor is Draft/incomplete: return "PENDING_PREDECESSOR"
            return "PENDING_PREDECESSOR"

        pred_obs_stmt = (
            select(ClinicalObservation)
            .where(
                ClinicalObservation.subject_id == subject_id,
                ClinicalObservation.visit_id == pred_visit.id,
                ClinicalObservation.test_code == observation.test_code,
                ClinicalObservation.is_deleted.is_(False),
            )
            .order_by(ClinicalObservation.observation_date.desc())
        )
        pred_obs_res = await session.execute(pred_obs_stmt)
        pred_obs = pred_obs_res.scalars().first()

        if not pred_obs or pred_obs.value is None:
            # Predecessor weight observation is unavailable: return signal
            return "PENDING_PREDECESSOR"

        # 3. Compare values using evaluate_ast
        current_val = observation.value
        pred_val = pred_obs.value
        if pred_val <= 0:
            return None

        context = {"CURRENT_WEIGHT": current_val, "PREDECESSOR_WEIGHT": pred_val}
        node = {
            "type": "comparison",
            "operator": "<",
            "operands": [
                {"type": "field_ref", "field_ref": {"field_id": "CURRENT_WEIGHT"}},
                {
                    "type": "comparison",
                    "operator": "*",
                    "operands": [
                        {"type": "constant", "value": 0.8},
                        {
                            "type": "field_ref",
                            "field_ref": {"field_id": "PREDECESSOR_WEIGHT"},
                        },
                    ],
                },
            ],
        }

        # If current weight is < 80% of predecessor weight, we have >20% weight loss
        if evaluate_ast(node, context) is True:
            return self.message

        return None


# Rule Registries
FIELD_LEVEL_RULES: List[EditCheckRule] = [
    OutlierCheckRule(),
    HighSystolicBPCheckRule(),
]

CROSS_FORM_LONGITUDINAL_RULES: List[EditCheckRule] = [
    AEConsentTemporalCheckRule(),
    WeightLossCheckRule(),
]


async def run_synchronous_edit_checks(
    session: AsyncSession, observation: ClinicalObservation
) -> None:
    """Runs synchronous field-level same-record edit checks directly on the active database session."""
    logger.info(f"Running synchronous edit checks for observation {observation.id}")

    for rule in FIELD_LEVEL_RULES:
        err_msg = await rule.evaluate(session, observation)

        # Query coordinate filters
        # Note: study_id can be inferred/stored on observation
        stmt_query = select(ClinicalQuery).where(
            ClinicalQuery.study_id == observation.study_id,
            ClinicalQuery.subject_id == observation.subject_id,
            ClinicalQuery.visit_id == observation.visit_id,
            ClinicalQuery.domain == observation.domain,
            ClinicalQuery.test_code == observation.test_code,
            ClinicalQuery.rule_id == rule.rule_id,
            ClinicalQuery.status.in_(["OPEN", "REOPENED", "ANSWERED"]),
            ClinicalQuery.is_deleted.is_(False),
        )
        res_query = await session.execute(stmt_query)
        existing_query = res_query.scalars().first()

        if err_msg:
            # Check failed! Open query if not already exists
            if not existing_query:
                new_query = ClinicalQuery(
                    study_id=observation.study_id,
                    subject_id=observation.subject_id,
                    visit_id=observation.visit_id,
                    domain=observation.domain,
                    test_code=observation.test_code,
                    observation_id=observation.id,
                    field_link=f"{observation.domain}.{observation.test_code}",
                    rule_id=rule.rule_id,
                    message=err_msg,
                    explanation=err_msg,
                    origin="SYSTEM",
                    created_by="SYSTEM",
                    status="OPEN",
                )
                session.add(new_query)
                logger.info(f"Created system clinical query for rule {rule.rule_id}")
        else:
            # Check passed! Auto-close any matching active query
            if existing_query:
                existing_query.status = "CLOSED"
                existing_query.resolver = "SYSTEM"
                existing_query.resolved_at = datetime.utcnow()
                existing_query.response = (
                    f"Auto-resolved: data corrected and {rule.rule_id} check passes."
                )
                existing_query.version += 1
                logger.info(
                    f"Auto-resolved and closed clinical query for rule {rule.rule_id}"
                )


async def run_asynchronous_edit_checks(
    session_factory: async_sessionmaker[AsyncSession],
    observation_id: str,
    user_id: Optional[str] = None,
    change_reason: Optional[str] = None,
) -> None:
    """Asynchronous background task runner for cross-form and longitudinal check evaluations."""
    logger.info(f"Background edit checks started for observation {observation_id}")

    with audit_context(user_id, change_reason):
        async with session_factory() as session:
            async with session.begin():
                # 1. Retrieve the target observation
                stmt = select(ClinicalObservation).where(
                    ClinicalObservation.id == observation_id,
                    ClinicalObservation.is_deleted.is_(False),
                )
                res = await session.execute(stmt)
                observation = res.scalars().first()
                if not observation:
                    logger.warning(
                        f"Observation {observation_id} not found in background task."
                    )
                    return

                # 2. Check if this newly added observation can resolve any pending predecessor dependencies
                await resolve_pending_predecessor_checks(session, observation)

                # 3. Evaluate each cross-form and longitudinal rule
                for rule in CROSS_FORM_LONGITUDINAL_RULES:
                    eval_result = await rule.evaluate(session, observation)

                    if eval_result == "PENDING_PREDECESSOR":
                        # Record pending predecessor state
                        # Avoid duplicates
                        stmt_pending = select(PendingPredecessorCheck).where(
                            PendingPredecessorCheck.subject_id
                            == observation.subject_id,
                            PendingPredecessorCheck.rule_id == rule.rule_id,
                            PendingPredecessorCheck.observation_id == observation.id,
                            PendingPredecessorCheck.is_deleted.is_(False),
                        )
                        res_pending = await session.execute(stmt_pending)
                        if not res_pending.scalars().first():
                            # Find current visit name
                            current_visit_name = "UNKNOWN"
                            if observation.visit_id:
                                cv_stmt = select(ClinicalVisit).where(
                                    ClinicalVisit.id == observation.visit_id
                                )
                                cv_res = await session.execute(cv_stmt)
                                cv = cv_res.scalars().first()
                                if cv:
                                    current_visit_name = cv.visit_name.upper()

                            idx = (
                                VISIT_SEQUENCE.index(current_visit_name)
                                if current_visit_name in VISIT_SEQUENCE
                                else 0
                            )
                            predecessor_visit_name = (
                                VISIT_SEQUENCE[idx - 1] if idx > 0 else "UNKNOWN"
                            )

                            pending_check = PendingPredecessorCheck(
                                subject_id=observation.subject_id,
                                study_id=observation.study_id,
                                current_visit_id=observation.visit_id,
                                current_visit_name=current_visit_name,
                                predecessor_visit_name=predecessor_visit_name,
                                rule_id=rule.rule_id,
                                observation_id=observation.id,
                                test_code=observation.test_code,
                            )
                            session.add(pending_check)
                            logger.info(
                                f"Deferred rule {rule.rule_id} and recorded PENDING predecessor dependency on visit {predecessor_visit_name}"
                            )
                        continue

                    # Retrieve matching existing query
                    stmt_query = select(ClinicalQuery).where(
                        ClinicalQuery.study_id == observation.study_id,
                        ClinicalQuery.subject_id == observation.subject_id,
                        ClinicalQuery.visit_id == observation.visit_id,
                        ClinicalQuery.domain == observation.domain,
                        ClinicalQuery.test_code == observation.test_code,
                        ClinicalQuery.rule_id == rule.rule_id,
                        ClinicalQuery.status.in_(["OPEN", "REOPENED", "ANSWERED"]),
                        ClinicalQuery.is_deleted.is_(False),
                    )
                    res_query = await session.execute(stmt_query)
                    existing_query = res_query.scalars().first()

                    if eval_result:
                        # Rule failed: open system query
                        if not existing_query:
                            new_query = ClinicalQuery(
                                study_id=observation.study_id,
                                subject_id=observation.subject_id,
                                visit_id=observation.visit_id,
                                domain=observation.domain,
                                test_code=observation.test_code,
                                observation_id=observation.id,
                                field_link=f"{observation.domain}.{observation.test_code}",
                                rule_id=rule.rule_id,
                                message=eval_result,
                                explanation=eval_result,
                                origin="SYSTEM",
                                created_by="SYSTEM",
                                status="OPEN",
                            )
                            session.add(new_query)
                            logger.info(
                                f"Created system clinical query in background for rule {rule.rule_id}"
                            )
                    else:
                        # Rule passed: close matching system query
                        if existing_query:
                            existing_query.status = "CLOSED"
                            existing_query.resolver = "SYSTEM"
                            existing_query.resolved_at = datetime.utcnow()
                            existing_query.response = f"Auto-resolved: data corrected and {rule.rule_id} check passes."
                            existing_query.version += 1
                            logger.info(
                                f"Auto-resolved and closed clinical query in background for rule {rule.rule_id}"
                            )


async def resolve_pending_predecessor_checks(
    session: AsyncSession, new_observation: ClinicalObservation
) -> None:
    """Checks if a newly created observation completes any pending predecessor visit dependencies and re-evaluates them."""
    if not new_observation.visit_id:
        return

    # Find visit name of the new observation
    cv_stmt = select(ClinicalVisit).where(ClinicalVisit.id == new_observation.visit_id)
    cv_res = await session.execute(cv_stmt)
    cv = cv_res.scalars().first()
    if not cv:
        return

    visit_name = cv.visit_name.upper()

    # Find pending predecessor checks where the predecessor_visit_name matches this new visit_name, for the same subject and test_code
    stmt_pending = select(PendingPredecessorCheck).where(
        PendingPredecessorCheck.subject_id == new_observation.subject_id,
        PendingPredecessorCheck.predecessor_visit_name.ilike(visit_name),
        PendingPredecessorCheck.test_code == new_observation.test_code,
        PendingPredecessorCheck.is_deleted.is_(False),
    )
    res_pending = await session.execute(stmt_pending)
    pending_checks = res_pending.scalars().all()

    for pending in pending_checks:
        logger.info(
            f"Re-evaluating pending predecessor check {pending.id} since visit {visit_name} was recorded."
        )

        # Load the observation that was deferred
        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.id == pending.observation_id,
            ClinicalObservation.is_deleted.is_(False),
        )
        res_obs = await session.execute(stmt_obs)
        deferred_obs = res_obs.scalars().first()

        if not deferred_obs:
            # Target observation was deleted or is missing, just soft-delete the pending check
            pending.is_deleted = True
            pending.version += 1
            continue

        # Find the rule
        rule = next(
            (r for r in CROSS_FORM_LONGITUDINAL_RULES if r.rule_id == pending.rule_id),
            None,
        )
        if not rule:
            pending.is_deleted = True
            pending.version += 1
            continue

        # Re-evaluate
        eval_result = await rule.evaluate(session, deferred_obs)

        # Since predecessor weight is now available, it shouldn't return PENDING_PREDECESSOR anymore
        if eval_result != "PENDING_PREDECESSOR":
            stmt_query = select(ClinicalQuery).where(
                ClinicalQuery.study_id == deferred_obs.study_id,
                ClinicalQuery.subject_id == deferred_obs.subject_id,
                ClinicalQuery.visit_id == deferred_obs.visit_id,
                ClinicalQuery.domain == deferred_obs.domain,
                ClinicalQuery.test_code == deferred_obs.test_code,
                ClinicalQuery.rule_id == rule.rule_id,
                ClinicalQuery.status.in_(["OPEN", "REOPENED", "ANSWERED"]),
                ClinicalQuery.is_deleted.is_(False),
            )
            res_query = await session.execute(stmt_query)
            existing_query = res_query.scalars().first()

            if eval_result:
                if not existing_query:
                    new_query = ClinicalQuery(
                        study_id=deferred_obs.study_id,
                        subject_id=deferred_obs.subject_id,
                        visit_id=deferred_obs.visit_id,
                        domain=deferred_obs.domain,
                        test_code=deferred_obs.test_code,
                        observation_id=deferred_obs.id,
                        field_link=f"{deferred_obs.domain}.{deferred_obs.test_code}",
                        rule_id=rule.rule_id,
                        message=eval_result,
                        explanation=eval_result,
                        origin="SYSTEM",
                        created_by="SYSTEM",
                        status="OPEN",
                    )
                    session.add(new_query)
                    logger.info(
                        f"Resolved pending check: created system clinical query for rule {rule.rule_id}"
                    )
            else:
                if existing_query:
                    existing_query.status = "CLOSED"
                    existing_query.resolver = "SYSTEM"
                    existing_query.resolved_at = datetime.utcnow()
                    existing_query.response = f"Auto-resolved: data corrected and {rule.rule_id} check passes."
                    existing_query.version += 1
                    logger.info(
                        f"Resolved pending check: auto-resolved and closed clinical query for rule {rule.rule_id}"
                    )

            # Soft-delete the pending predecessor check
            pending.is_deleted = True
            pending.version += 1


async def resolve_pending_predecessor_checks_for_form(
    session_factory: async_sessionmaker[AsyncSession],
    subject_id: str,
    visit_id: str,
    user_id: Optional[str] = None,
    change_reason: Optional[str] = None,
) -> None:
    """Background task to re-evaluate and resume any pending checks that were waiting for this visit to be completed."""
    logger.info(
        f"Checking for pending predecessor checks to resume for subject {subject_id} and visit {visit_id}"
    )

    with audit_context(user_id, change_reason):
        async with session_factory() as session:
            async with session.begin():
                # Get the visit name of this newly completed visit
                cv_stmt = select(ClinicalVisit).where(ClinicalVisit.id == visit_id)
                cv_res = await session.execute(cv_stmt)
                cv = cv_res.scalars().first()
                if not cv:
                    return

                visit_name = cv.visit_name.upper()

                # Find pending checks where the predecessor_visit_name matches this completed visit
                stmt_pending = select(PendingPredecessorCheck).where(
                    PendingPredecessorCheck.subject_id == subject_id,
                    PendingPredecessorCheck.predecessor_visit_name.ilike(visit_name),
                    PendingPredecessorCheck.is_deleted.is_(False),
                )
                res_pending = await session.execute(stmt_pending)
                pending_checks = res_pending.scalars().all()

                for pending in pending_checks:
                    logger.info(
                        f"Resuming pending predecessor check {pending.id} since predecessor visit {visit_name} was completed."
                    )

                    # Load the deferred observation
                    stmt_obs = select(ClinicalObservation).where(
                        ClinicalObservation.id == pending.observation_id,
                        ClinicalObservation.is_deleted.is_(False),
                    )
                    res_obs = await session.execute(stmt_obs)
                    deferred_obs = res_obs.scalars().first()

                    if not deferred_obs:
                        pending.is_deleted = True
                        pending.version += 1
                        continue

                    # Find the rule
                    rule = next(
                        (
                            r
                            for r in CROSS_FORM_LONGITUDINAL_RULES
                            if r.rule_id == pending.rule_id
                        ),
                        None,
                    )
                    if not rule:
                        pending.is_deleted = True
                        pending.version += 1
                        continue

                    # Re-evaluate
                    eval_result = await rule.evaluate(session, deferred_obs)

                    if eval_result != "PENDING_PREDECESSOR":
                        # Success or fail, it's no longer pending!
                        stmt_query = select(ClinicalQuery).where(
                            ClinicalQuery.study_id == deferred_obs.study_id,
                            ClinicalQuery.subject_id == deferred_obs.subject_id,
                            ClinicalQuery.visit_id == deferred_obs.visit_id,
                            ClinicalQuery.domain == deferred_obs.domain,
                            ClinicalQuery.test_code == deferred_obs.test_code,
                            ClinicalQuery.rule_id == rule.rule_id,
                            ClinicalQuery.status.in_(["OPEN", "REOPENED", "ANSWERED"]),
                            ClinicalQuery.is_deleted.is_(False),
                        )
                        res_query = await session.execute(stmt_query)
                        existing_query = res_query.scalars().first()

                        if eval_result:
                            if not existing_query:
                                new_query = ClinicalQuery(
                                    study_id=deferred_obs.study_id,
                                    subject_id=deferred_obs.subject_id,
                                    visit_id=deferred_obs.visit_id,
                                    domain=deferred_obs.domain,
                                    test_code=deferred_obs.test_code,
                                    observation_id=deferred_obs.id,
                                    field_link=f"{deferred_obs.domain}.{deferred_obs.test_code}",
                                    rule_id=rule.rule_id,
                                    message=eval_result,
                                    explanation=eval_result,
                                    origin="SYSTEM",
                                    created_by="SYSTEM",
                                    status="OPEN",
                                )
                                session.add(new_query)
                                logger.info(
                                    f"Resumed pending check: created system clinical query for rule {rule.rule_id}"
                                )
                        else:
                            if existing_query:
                                existing_query.status = "CLOSED"
                                existing_query.resolver = "SYSTEM"
                                existing_query.resolved_at = datetime.utcnow()
                                existing_query.response = f"Auto-resolved: data corrected and {rule.rule_id} check passes."
                                existing_query.version += 1
                                logger.info(
                                    f"Resumed pending check: auto-resolved and closed clinical query for rule {rule.rule_id}"
                                )

                        pending.is_deleted = True
                        pending.version += 1


async def handle_cascading_nullification(
    session: AsyncSession, observation: ClinicalObservation
) -> None:
    """Handles cascading dependent nullification (PRD-EDC-004).

    If PREG_STATUS is set to NO, any dependent child field (like DUE_DATE)
    is purged with the required system change reason.
    """
    if observation.test_code == "PREG_STATUS" and observation.value_string == "NO":
        # Find any dependent child observation (e.g., DUE_DATE) for this subject/visit
        stmt = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == observation.subject_id,
            ClinicalObservation.visit_id == observation.visit_id,
            ClinicalObservation.test_code == "DUE_DATE",
            ClinicalObservation.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        child_obs = res.scalars().first()
        if child_obs and (
            child_obs.value is not None or child_obs.value_string is not None
        ):
            # Nullify/purge the data in the child field
            # Use audit_context to record the required system change reason
            with audit_context(
                user_id="SYSTEM",
                change_reason="System-initiated purge of inactive child variable due to parent value mutation",
            ):
                child_obs.value = None
                child_obs.value_string = None
                # Mark as modified to trigger before_flush
                session.add(child_obs)
                await session.flush()
