"""
Post-submission cross-form & longitudinal edit checks with auto-query generation.
Verifies and evaluates complex AST conditions asynchronously for GxP clinical trial compliance.
Satisfies Phase 15 requirements: Post-submission edit-check evaluation queue, predecessor-visit pause/resume,
and automatic query creation/resolution with audit.
"""

import contextlib
import copy
import logging
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from apps.execution.database.context import audit_context
from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalQuery,
    ClinicalVisit,
    FormSubmission,
    PendingPredecessorCheck,
    StudyAuthoredRule,
)
from apps.execution.evaluator import evaluate_ast

logger = logging.getLogger(__name__)

VISIT_SEQUENCE = ["SCREENING", "BASELINE", "WEEK_4", "WEEK_8"]


def rewrite_condition_ast(node: Any) -> Any:
    if not node:
        return node
    if isinstance(node, dict):
        node_copy = copy.deepcopy(node)
        node_type = node_copy.get("type") or node_copy.get("node_type")
        if node_type == "field_ref" and "field_ref" in node_copy:
            ref = node_copy["field_ref"]
            f_id = ref.get("field_id")
            vis_id = ref.get("visit_id")
            vis_rel = ref.get("visit_relative")
            if vis_rel:
                ref["field_id"] = f"{f_id}_{vis_rel}"
            elif vis_id:
                ref["field_id"] = f"{f_id}_{vis_id}"
        operands_key = "operands" if "operands" in node_copy else "children"
        if operands_key in node_copy and node_copy[operands_key]:
            node_copy[operands_key] = [
                rewrite_condition_ast(child) for child in node_copy[operands_key]
            ]
        return node_copy
    return node


class BatchEvaluationContext:
    """In-memory evaluation context loaded in exactly three batch database queries.

    Consolidates lookups for visits, submission statuses, and observations
    per subject during form evaluation to eliminate database connection pool congestion.
    """

    def __init__(
        self,
        visits: list[ClinicalVisit],
        submissions: list[FormSubmission],
        observations: list[ClinicalObservation],
    ) -> None:
        """Initialize the evaluation context with pre-fetched records."""
        self.visits = visits
        self.submissions = submissions
        self.observations = observations

        # Build fast lookup maps
        self.visit_by_id: dict[str, ClinicalVisit] = {v.id: v for v in visits if v.id}
        self.visit_by_name: dict[str, ClinicalVisit] = {
            v.visit_name.upper(): v for v in visits if v.visit_name
        }

        self.submissions_by_visit: dict[str, list[FormSubmission]] = {}
        for sub in submissions:
            if sub.visit_id:
                self.submissions_by_visit.setdefault(sub.visit_id, []).append(sub)

        self.obs_by_visit_and_code: dict[
            tuple[str, str], list[ClinicalObservation]
        ] = {}
        self.obs_by_code: dict[str, list[ClinicalObservation]] = {}
        for obs in observations:
            code_upper = obs.test_code.upper() if obs.test_code else ""
            self.obs_by_code.setdefault(code_upper, []).append(obs)
            if obs.visit_id:
                self.obs_by_visit_and_code.setdefault(
                    (obs.visit_id, code_upper), []
                ).append(obs)

    @classmethod
    async def load(
        cls, session: AsyncSession, subject_id: str, study_id: str
    ) -> BatchEvaluationContext:
        """Consolidates lookups for visits, submission statuses, and observations into 3 queries.

        Args:
            session: Async database session.
            subject_id: Subject identifier.
            study_id: Study identifier.

        Returns:
            BatchEvaluationContext: Pre-loaded context for in-memory resolution.
        """
        # Query 1: Visits
        v_res = await session.execute(
            select(ClinicalVisit).where(
                ClinicalVisit.subject_id == subject_id,
                ClinicalVisit.study_id == study_id,
            )
        )
        visits = list(v_res.scalars().all())

        # Query 2: Form Submissions
        s_res = await session.execute(
            select(FormSubmission).where(
                FormSubmission.subject_id == subject_id,
                FormSubmission.study_id == study_id,
                FormSubmission.is_deleted.is_(False),
            )
        )
        submissions = list(s_res.scalars().all())

        # Query 3: Clinical Observations
        o_res = await session.execute(
            select(ClinicalObservation)
            .where(
                ClinicalObservation.subject_id == subject_id,
                ClinicalObservation.study_id == study_id,
                ClinicalObservation.is_deleted.is_(False),
            )
            .order_by(ClinicalObservation.observation_date.desc())
        )
        observations = list(o_res.scalars().all())

        return cls(visits=visits, submissions=submissions, observations=observations)


class EditCheckRule:
    rule_id: str
    rule_type: str  # "field_level", "cross_form", "longitudinal"
    message: str

    def applies_to_observation(self, observation: ClinicalObservation) -> bool:
        """Fast in-memory precondition check before running detailed rule logic.

        Args:
            observation: Target observation to evaluate.

        Returns:
            bool: True if observation matches rule preconditions.
        """
        return True

    async def evaluate(
        self,
        session: AsyncSession,
        observation: ClinicalObservation,
        batch_context: BatchEvaluationContext | None = None,
    ) -> str | None:
        raise NotImplementedError()


class OutlierCheckRule(EditCheckRule):
    rule_id = "OUTLIER_CHECK"
    rule_type = "field_level"
    message = "Observation is a statistical outlier within the cohort."

    def applies_to_observation(self, observation: ClinicalObservation) -> bool:
        return observation.value is not None or observation.is_outlier is not None

    async def evaluate(
        self,
        session: AsyncSession,
        observation: ClinicalObservation,
        batch_context: BatchEvaluationContext | None = None,
    ) -> str | None:
        if observation.is_outlier:
            return self.message
        return None


class HighSystolicBPCheckRule(EditCheckRule):
    rule_id = "SYSBP_HIGH_CHECK"
    rule_type = "field_level"
    message = "Systolic blood pressure entry of {value} mmHg is critically high."

    def applies_to_observation(self, observation: ClinicalObservation) -> bool:
        return (observation.test_code or "").upper() == "SYSBP"

    async def evaluate(
        self,
        session: AsyncSession,
        observation: ClinicalObservation,
        batch_context: BatchEvaluationContext | None = None,
    ) -> str | None:
        if (
            observation.test_code
            and observation.test_code.upper() == "SYSBP"
            and observation.value is not None
        ):
            if observation.value > 200.0:
                return self.message.format(value=observation.value)
        return None


class LabOutOfRangeCheckRule(EditCheckRule):
    rule_id = "LAB_OUT_OF_RANGE_CHECK"
    rule_type = "field_level"
    message = "Laboratory observation is out of range. Indicator: {indicator}, Normal bounds: {bounds}."

    def applies_to_observation(self, observation: ClinicalObservation) -> bool:
        return observation.domain == "LB" or observation.lab_out_of_range is not None

    async def evaluate(
        self,
        session: AsyncSession,
        observation: ClinicalObservation,
        batch_context: BatchEvaluationContext | None = None,
    ) -> str | None:
        if observation.lab_out_of_range:
            indicator = observation.lab_indicator or "UNKNOWN"
            bounds = observation.matched_normal_bounds or "None"
            return self.message.format(indicator=indicator, bounds=bounds)
        return None


class AEConsentTemporalCheckRule(EditCheckRule):
    rule_id = "AE_CONSENT_TEMPORAL_CHECK"
    rule_type = "cross_form"
    message = "Adverse event onset date cannot be before informed consent date."

    def applies_to_observation(self, observation: ClinicalObservation) -> bool:
        code = (observation.test_code or "").upper()
        domain = (observation.domain or "").upper()
        return domain in ("AE", "DS") or code in (
            "AESTDTC",
            "AE_ONSET",
            "DSSTDTC",
            "INFORMED_CONSENT",
            "INFORMED_CONSENT_DATE",
        )

    async def evaluate(
        self,
        session: AsyncSession,
        observation: ClinicalObservation,
        batch_context: BatchEvaluationContext | None = None,
    ) -> str | None:
        subject_id = observation.subject_id

        if batch_context is not None:
            ae_obs = next(
                (
                    o
                    for o in batch_context.observations
                    if o.subject_id == subject_id
                    and (o.test_code or "").upper() in ("AESTDTC", "AE_ONSET")
                ),
                None,
            )
            consent_obs = next(
                (
                    o
                    for o in batch_context.observations
                    if o.subject_id == subject_id
                    and (o.test_code or "").upper()
                    in ("DSSTDTC", "INFORMED_CONSENT", "INFORMED_CONSENT_DATE")
                ),
                None,
            )
        else:
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
            with contextlib.suppress(ValueError):
                ae_date = datetime.fromisoformat(ae_obs.value_string)

        consent_date = consent_obs.observation_date
        if consent_obs.value_string:
            with contextlib.suppress(ValueError):
                consent_date = datetime.fromisoformat(consent_obs.value_string)

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


def extract_fields_from_dict(node: dict) -> list:
    refs = []
    if not isinstance(node, dict):
        return refs
    node_type = node.get("type") or node.get("node_type")
    if node_type == "field_ref" and "field_ref" in node:
        refs.append(node["field_ref"])
    operands = node.get("operands") or node.get("children") or []
    for child in operands:
        refs.extend(extract_fields_from_dict(child))
    return refs


async def resolve_authored_rule_context(
    session: AsyncSession,
    observation: ClinicalObservation,
    condition: dict,
    batch_context: BatchEvaluationContext | None = None,
) -> tuple[dict | None, str | None]:
    """Resolves the data context for an authored rule's condition.

    Returns (context_dict, sentinel) where sentinel is "PENDING_PREDECESSOR" or None.
    If batch_context is provided, lookups execute in memory without additional database queries.
    """
    if batch_context is None:
        batch_context = await BatchEvaluationContext.load(
            session, observation.subject_id, observation.study_id
        )

    context = {}

    # 1. Fetch current visit
    current_visit = (
        batch_context.visit_by_id.get(observation.visit_id)
        if observation.visit_id
        else None
    )
    current_visit_name = (
        current_visit.visit_name.upper()
        if current_visit and current_visit.visit_name
        else "UNKNOWN"
    )
    current_idx = (
        VISIT_SEQUENCE.index(current_visit_name)
        if current_visit_name in VISIT_SEQUENCE
        else -1
    )

    # 2. Extract field references from condition
    refs = extract_fields_from_dict(condition)
    for ref in refs:
        field_id = ref.get("field_id")
        if not field_id:
            continue

        visit_id = ref.get("visit_id")
        visit_relative = ref.get("visit_relative")

        # Determine context key (matches the rewritten AST field_id)
        if visit_relative:
            context_key = f"{field_id}_{visit_relative}"
        elif visit_id:
            context_key = f"{field_id}_{visit_id}"
        else:
            context_key = field_id

        # Determine target visit name
        is_prior = False
        if visit_relative in ("previous", "predecessor"):
            is_prior = True
            if current_idx <= 0:
                # First visit has no predecessor, skip
                context[context_key] = None
                continue
            target_visit_name = VISIT_SEQUENCE[current_idx - 1]
        elif visit_id:
            target_visit_name = visit_id.upper()
            target_idx = (
                VISIT_SEQUENCE.index(target_visit_name)
                if target_visit_name in VISIT_SEQUENCE
                else -1
            )
            if target_idx < current_idx:
                is_prior = True
        else:
            target_visit_name = current_visit_name

        # 3. Look up target visit
        target_visit = batch_context.visit_by_name.get(target_visit_name)
        if not target_visit:
            if is_prior:
                return None, "PENDING_PREDECESSOR"
            context[context_key] = None
            continue

        # Check if target visit's FormSubmission is "DRAFT" (incomplete)
        subs = batch_context.submissions_by_visit.get(target_visit.id, [])
        if subs and any(sub.status == "DRAFT" for sub in subs) and is_prior:
            return None, "PENDING_PREDECESSOR"

        # 4. Look up target observation
        if (
            observation.visit_id == target_visit.id
            and observation.test_code
            and observation.test_code.upper() == field_id.upper()
        ):
            val = (
                observation.value
                if observation.value is not None
                else observation.value_string
            )
            context[context_key] = val
            continue

        target_obs_list = batch_context.obs_by_visit_and_code.get(
            (target_visit.id, field_id.upper()), []
        )
        target_obs = target_obs_list[0] if target_obs_list else None

        if not target_obs:
            if is_prior:
                return None, "PENDING_PREDECESSOR"
            context[context_key] = None
            continue

        val = (
            target_obs.value
            if target_obs.value is not None
            else target_obs.value_string
        )
        if val is None and is_prior:
            return None, "PENDING_PREDECESSOR"

        context[context_key] = val

    return context, None


class AuthoredCrossFormRule(EditCheckRule):
    def __init__(self, db_rule: StudyAuthoredRule):
        self.rule_id = db_rule.rule_id
        self.rule_type = db_rule.rule_type or "cross_form_check"
        self.message = db_rule.query_message
        self.condition = db_rule.condition
        self.publication_version = db_rule.publication_version

    def applies_to_observation(self, observation: ClinicalObservation) -> bool:
        if not self.condition or not isinstance(self.condition, dict):
            return True
        refs = extract_fields_from_dict(self.condition)
        if not refs:
            return True
        ref_field_ids = {
            r.get("field_id", "").upper() for r in refs if r.get("field_id")
        }
        obs_code = (observation.test_code or "").upper()
        return obs_code in ref_field_ids or not ref_field_ids

    async def evaluate(
        self,
        session: AsyncSession,
        observation: ClinicalObservation,
        batch_context: BatchEvaluationContext | None = None,
    ) -> str | None:
        # 1. Resolve context & check for pending predecessor
        context, sentinel = await resolve_authored_rule_context(
            session, observation, self.condition, batch_context=batch_context
        )
        if sentinel == "PENDING_PREDECESSOR":
            return "PENDING_PREDECESSOR"

        if context is None:
            return None

        # 2. Evaluate condition using AST evaluator
        try:
            rewritten_cond = rewrite_condition_ast(self.condition)
            res = evaluate_ast(rewritten_cond, context)
            if res is True:
                return self.message
        except Exception as e:
            logger.error(f"Error evaluating authored rule {self.rule_id}: {str(e)}")

        return None


async def load_active_authored_rules(
    session: AsyncSession, study_id: str
) -> list[AuthoredCrossFormRule]:
    stmt = select(StudyAuthoredRule).where(
        StudyAuthoredRule.study_id == study_id,
        StudyAuthoredRule.is_active.is_(True),
        StudyAuthoredRule.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    rows = res.scalars().all()
    return [AuthoredCrossFormRule(row) for row in rows]


class WeightLossCheckRule(EditCheckRule):
    rule_id = "WEIGHT_LOSS_CHECK"
    rule_type = "longitudinal"
    message = "Subject weight loss is greater than 20% compared to predecessor visit."

    def applies_to_observation(self, observation: ClinicalObservation) -> bool:
        return (observation.test_code or "").upper() in ("WEIGHT", "VSWT")

    async def evaluate(
        self,
        session: AsyncSession,
        observation: ClinicalObservation,
        batch_context: BatchEvaluationContext | None = None,
    ) -> str | None:
        # Only evaluates weight parameters
        if (observation.test_code or "").upper() not in ["WEIGHT", "VSWT"]:
            return None

        if observation.value is None:
            return None

        subject_id = observation.subject_id

        # 1. Get current visit name
        if not observation.visit_id:
            return None

        if batch_context is not None:
            current_visit = batch_context.visit_by_id.get(observation.visit_id)
        else:
            current_visit_stmt = select(ClinicalVisit).where(
                ClinicalVisit.id == observation.visit_id
            )
            current_visit_res = await session.execute(current_visit_stmt)
            current_visit = current_visit_res.scalars().first()

        if not current_visit or not current_visit.visit_name:
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
        if batch_context is not None:
            pred_visit = batch_context.visit_by_name.get(predecessor_visit_name)
        else:
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
        if batch_context is not None:
            pred_subs = batch_context.submissions_by_visit.get(pred_visit.id, [])
        else:
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

        code_upper = observation.test_code.upper()
        if batch_context is not None:
            pred_obs_list = batch_context.obs_by_visit_and_code.get(
                (pred_visit.id, code_upper), []
            )
            pred_obs = pred_obs_list[0] if pred_obs_list else None
        else:
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
FIELD_LEVEL_RULES: list[EditCheckRule] = [
    OutlierCheckRule(),
    HighSystolicBPCheckRule(),
    LabOutOfRangeCheckRule(),
]

CROSS_FORM_LONGITUDINAL_RULES: list[EditCheckRule] = [
    AEConsentTemporalCheckRule(),
    WeightLossCheckRule(),
]


async def run_synchronous_edit_checks(
    session: AsyncSession, observation: ClinicalObservation
) -> None:
    """Runs synchronous field-level same-record edit checks directly on the active database session."""
    logger.info(f"Running synchronous edit checks for observation {observation.id}")

    for rule in FIELD_LEVEL_RULES:
        if not rule.applies_to_observation(observation):
            continue

        err_msg = await rule.evaluate(session, observation)

        # Query coordinate filters
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
                existing_query.resolved_at = datetime.now(UTC)
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
    user_id: str | None = None,
    change_reason: str | None = None,
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

                # 3. Load BatchEvaluationContext (consolidates lookups into 3 queries)
                batch_context = await BatchEvaluationContext.load(
                    session, observation.subject_id, observation.study_id
                )

                # 4. Load active authored rules for the study and combine with static ones
                authored_rules = await load_active_authored_rules(
                    session, observation.study_id
                )
                combined_rules = list(CROSS_FORM_LONGITUDINAL_RULES) + authored_rules

                # 5. Evaluate each rule
                for rule in combined_rules:
                    if not rule.applies_to_observation(observation):
                        continue

                    eval_result = await rule.evaluate(
                        session, observation, batch_context=batch_context
                    )

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
                            current_visit = batch_context.visit_by_id.get(
                                observation.visit_id
                            )
                            current_visit_name = (
                                current_visit.visit_name.upper()
                                if current_visit and current_visit.visit_name
                                else "UNKNOWN"
                            )

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
                            existing_query.resolved_at = datetime.now(UTC)
                            existing_query.response = f"Auto-resolved: data corrected and {rule.rule_id} check passes."
                            existing_query.version += 1
                            logger.info(
                                f"Auto-resolved and closed clinical query in background for rule {rule.rule_id}"
                            )


async def _resolve_pending_predecessor_checks_for_form_in_session(
    session: AsyncSession,
    subject_id: str,
    visit_id: str,
) -> None:
    """In-session helper to re-evaluate and resume pending predecessor checks for a completed visit."""
    cv_stmt = select(ClinicalVisit).where(ClinicalVisit.id == visit_id)
    cv_res = await session.execute(cv_stmt)
    cv = cv_res.scalars().first()
    if not cv or not cv.visit_name:
        return

    visit_name = cv.visit_name.upper()

    stmt_pending = select(PendingPredecessorCheck).where(
        PendingPredecessorCheck.subject_id == subject_id,
        PendingPredecessorCheck.predecessor_visit_name.ilike(visit_name),
        PendingPredecessorCheck.is_deleted.is_(False),
    )
    res_pending = await session.execute(stmt_pending)
    pending_checks = res_pending.scalars().all()
    if not pending_checks:
        return

    batch_context = await BatchEvaluationContext.load(session, subject_id, cv.study_id)
    authored_rules = await load_active_authored_rules(session, cv.study_id)
    combined_rules = list(CROSS_FORM_LONGITUDINAL_RULES) + authored_rules

    for pending in pending_checks:
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

        rule = next((r for r in combined_rules if r.rule_id == pending.rule_id), None)
        if not rule:
            pending.is_deleted = True
            pending.version += 1
            continue

        eval_result = await rule.evaluate(
            session, deferred_obs, batch_context=batch_context
        )

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
            else:
                if existing_query:
                    existing_query.status = "CLOSED"
                    existing_query.resolver = "SYSTEM"
                    existing_query.resolved_at = datetime.now(UTC)
                    existing_query.response = f"Auto-resolved: data corrected and {rule.rule_id} check passes."
                    existing_query.version += 1

            pending.is_deleted = True
            pending.version += 1


async def run_asynchronous_form_edit_checks(
    session_factory: async_sessionmaker[AsyncSession],
    submission_id: str,
    user_id: str | None = None,
    change_reason: str | None = None,
) -> None:
    """Form-level background task for cross-form and longitudinal check evaluations with query coalescing."""
    logger.info(
        f"Form-level background edit checks started for submission {submission_id}"
    )

    with audit_context(user_id, change_reason):
        async with session_factory() as session:
            async with session.begin():
                # 1. Retrieve the form submission
                stmt = select(FormSubmission).where(
                    FormSubmission.id == submission_id,
                    FormSubmission.is_deleted.is_(False),
                )
                res = await session.execute(stmt)
                sub = res.scalars().first()
                if not sub:
                    logger.warning(
                        f"Form submission {submission_id} not found in background task."
                    )
                    return

                # 2. Retrieve form observations
                stmt_obs = select(ClinicalObservation).where(
                    ClinicalObservation.subject_id == sub.subject_id,
                    ClinicalObservation.visit_id == sub.visit_id,
                    ClinicalObservation.page_id == sub.form_id,
                    ClinicalObservation.is_deleted.is_(False),
                )
                res_obs = await session.execute(stmt_obs)
                form_obs = list(res_obs.scalars().all())

                if not form_obs and sub.visit_id:
                    stmt_obs_fallback = select(ClinicalObservation).where(
                        ClinicalObservation.subject_id == sub.subject_id,
                        ClinicalObservation.visit_id == sub.visit_id,
                        ClinicalObservation.is_deleted.is_(False),
                    )
                    res_obs_fallback = await session.execute(stmt_obs_fallback)
                    form_obs = list(res_obs_fallback.scalars().all())

                # 3. Resume any pending predecessor checks waiting on this completed visit/form
                if sub.visit_id:
                    await _resolve_pending_predecessor_checks_for_form_in_session(
                        session, sub.subject_id, sub.visit_id
                    )

                if not form_obs:
                    return

                # 4. Load BatchEvaluationContext (consolidates lookups into 3 queries)
                batch_context = await BatchEvaluationContext.load(
                    session, sub.subject_id, sub.study_id
                )

                # 5. Load active authored rules for the study and combine with static rules
                authored_rules = await load_active_authored_rules(session, sub.study_id)
                combined_rules = list(CROSS_FORM_LONGITUDINAL_RULES) + authored_rules

                # Batch query existing active queries and pending checks for this subject
                stmt_queries = select(ClinicalQuery).where(
                    ClinicalQuery.study_id == sub.study_id,
                    ClinicalQuery.subject_id == sub.subject_id,
                    ClinicalQuery.status.in_(["OPEN", "REOPENED", "ANSWERED"]),
                    ClinicalQuery.is_deleted.is_(False),
                )
                existing_queries_res = await session.execute(stmt_queries)
                existing_queries = list(existing_queries_res.scalars().all())
                query_map = {
                    (q.visit_id, q.domain, q.test_code, q.rule_id): q
                    for q in existing_queries
                }

                stmt_pending = select(PendingPredecessorCheck).where(
                    PendingPredecessorCheck.subject_id == sub.subject_id,
                    PendingPredecessorCheck.is_deleted.is_(False),
                )
                pending_res = await session.execute(stmt_pending)
                existing_pending = list(pending_res.scalars().all())
                pending_map = {
                    (p.rule_id, p.observation_id): p for p in existing_pending
                }

                # 6. Evaluate rules for each observation in the form using pre-filtering
                for obs in form_obs:
                    for rule in combined_rules:
                        if not rule.applies_to_observation(obs):
                            continue

                        eval_result = await rule.evaluate(
                            session, obs, batch_context=batch_context
                        )

                        if eval_result == "PENDING_PREDECESSOR":
                            pending_key = (rule.rule_id, obs.id)
                            if pending_key not in pending_map:
                                current_visit = batch_context.visit_by_id.get(
                                    obs.visit_id
                                )
                                current_visit_name = (
                                    current_visit.visit_name.upper()
                                    if current_visit and current_visit.visit_name
                                    else "UNKNOWN"
                                )
                                idx = (
                                    VISIT_SEQUENCE.index(current_visit_name)
                                    if current_visit_name in VISIT_SEQUENCE
                                    else 0
                                )
                                predecessor_visit_name = (
                                    VISIT_SEQUENCE[idx - 1] if idx > 0 else "UNKNOWN"
                                )

                                pending_check = PendingPredecessorCheck(
                                    subject_id=obs.subject_id,
                                    study_id=obs.study_id,
                                    current_visit_id=obs.visit_id,
                                    current_visit_name=current_visit_name,
                                    predecessor_visit_name=predecessor_visit_name,
                                    rule_id=rule.rule_id,
                                    observation_id=obs.id,
                                    test_code=obs.test_code,
                                )
                                session.add(pending_check)
                                pending_map[pending_key] = pending_check
                                logger.info(
                                    f"Deferred rule {rule.rule_id} and recorded PENDING predecessor dependency on visit {predecessor_visit_name}"
                                )
                            continue

                        query_key = (
                            obs.visit_id,
                            obs.domain,
                            obs.test_code,
                            rule.rule_id,
                        )
                        existing_query = query_map.get(query_key)

                        if eval_result:
                            # Rule failed: open system query if not present
                            if not existing_query:
                                site_id = sub.site_id
                                if not site_id and obs.visit_id:
                                    visit = batch_context.visit_by_id.get(obs.visit_id)
                                    if visit and hasattr(visit, "site_id"):
                                        site_id = visit.site_id
                                new_query = ClinicalQuery(
                                    study_id=obs.study_id,
                                    site_id=site_id or getattr(obs, "site_id", None),
                                    subject_id=obs.subject_id,
                                    visit_id=obs.visit_id,
                                    domain=obs.domain,
                                    test_code=obs.test_code,
                                    observation_id=obs.id,
                                    field_link=f"{obs.domain}.{obs.test_code}",
                                    rule_id=rule.rule_id,
                                    message=eval_result,
                                    explanation=eval_result,
                                    origin="SYSTEM",
                                    created_by="SYSTEM",
                                    status="OPEN",
                                )
                                session.add(new_query)
                                query_map[query_key] = new_query
                                logger.info(
                                    f"Created system clinical query in background for rule {rule.rule_id}"
                                )
                        else:
                            # Rule passed: auto-close existing active query
                            if existing_query:
                                existing_query.status = "CLOSED"
                                existing_query.resolver = "SYSTEM"
                                existing_query.resolved_at = datetime.now(UTC)
                                existing_query.response = f"Auto-resolved: data corrected and {rule.rule_id} check passes."
                                existing_query.version += 1
                                logger.info(
                                    f"Auto-resolved and closed clinical query in background for rule {rule.rule_id}"
                                )

                # 7. Evaluate cross-domain anomalies for the subject
                try:
                    from apps.execution.services.cross_domain_anomaly_service import (
                        CrossDomainAnomalyService,
                    )

                    anomaly_service = CrossDomainAnomalyService()
                    await anomaly_service.evaluate_subject_cross_domain_anomalies(
                        session=session,
                        subject_id=sub.subject_id,
                        study_id=sub.study_id,
                        enable_ai=False,
                        auto_stage_queries=True,
                    )
                except Exception as anomaly_err:
                    logger.warning(
                        f"Cross-domain anomaly evaluation failed in background for subject {sub.subject_id}: {anomaly_err}"
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

    authored_rules = await load_active_authored_rules(session, new_observation.study_id)
    combined_rules = list(CROSS_FORM_LONGITUDINAL_RULES) + authored_rules

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
            (r for r in combined_rules if r.rule_id == pending.rule_id),
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
                    existing_query.resolved_at = datetime.now(UTC)
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
    user_id: str | None = None,
    change_reason: str | None = None,
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

                # Load active authored rules
                study_id = cv.study_id
                authored_rules = await load_active_authored_rules(session, study_id)
                combined_rules = list(CROSS_FORM_LONGITUDINAL_RULES) + authored_rules

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
                        (r for r in combined_rules if r.rule_id == pending.rule_id),
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
                                existing_query.resolved_at = datetime.now(UTC)
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
