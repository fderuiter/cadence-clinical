"""RTSM Adaptive Oncology Dose Escalation & Response-Adaptive Randomization (RAR) Service.

Implements 21 CFR Part 11 compliant state machines and audit logging for:
- Standard 3+3 rule-based design.
- Bayesian Optimal Interval (BOIN) real-time DLT monitoring and overdose safety elimination.
- Continual Reassessment Method (CRM / mCRM) Bayesian posterior modeling.
- Response-Adaptive Randomization (RAR) dynamic allocation probability recalculation.

Requirements: PRD-SCM-004, ADR-2199, FDA Adaptive Design Guidance, 21 CFR Part 11
"""

import hashlib
import json
import uuid
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adaptive_math import (
    BoinDoseEscalationEngine,
    CrmDoseEscalationEngine,
    ResponseAdaptiveRandomizationEngine,
    ThreePlusThreeDoseEscalationEngine,
)
from apps.execution.database.models import (
    AdaptiveCohortTransitionLog,
    AdaptiveTrialConfig,
    DoseCohortState,
    RARAllocationLog,
    RARArmState,
)
from apps.execution.domain.adaptive_models import (
    AdaptiveTrialConfigDTO,
    BoinConfig,
    CohortEvaluationInput,
    CohortEvaluationResult,
    CohortRecommendationAction,
    CohortTransitionLogResponse,
    CohortTransitionRequest,
    CrmConfig,
    DoseEscalationDesignType,
    DoseLevelDef,
    DoseLevelEvaluationSummary,
    RARArmEvaluationDTO,
    RarConfig,
    RAREvaluationResult,
    RARExpandOrTerminateArmRequest,
    ThreePlusThreeConfig,
)


class AdaptiveTrialConfigNotFoundError(Exception):
    """Raised when no adaptive trial configuration exists for the requested study."""

    pass


class InvalidDoseLevelError(Exception):
    """Raised when an invalid dose level index or parameters are supplied."""

    pass


class CohortTransitionError(Exception):
    """Raised when an invalid or unverified cohort transition is attempted."""

    pass


class RARArmNotFoundError(Exception):
    """Raised when a requested treatment arm is not found in RAR state."""

    pass


# =========================================================================
# Transactional Services & 21 CFR Part 11 State Machines
# =========================================================================


def _val(x: Any) -> str:
    """Safely extracts string value from enum or string."""
    if hasattr(x, "value"):
        return str(x.value)
    return str(x)


async def create_or_update_adaptive_config_transaction(
    session: AsyncSession,
    config: AdaptiveTrialConfigDTO,
    user_id: str,
    change_reason: str | None = None,
) -> AdaptiveTrialConfig:
    """Configures or updates adaptive trial protocol parameters (BOIN, CRM, 3+3, RAR)."""
    stmt = select(AdaptiveTrialConfig).where(
        AdaptiveTrialConfig.study_id == config.study_id,
        AdaptiveTrialConfig.is_deleted.is_(False),
    )
    result = await session.execute(stmt)
    existing = result.scalars().first()

    dose_levels_json = [d.model_dump() for d in config.dose_levels]
    boin_json = config.boin_config.model_dump() if config.boin_config else None
    crm_json = config.crm_config.model_dump() if config.crm_config else None
    three_plus_three_json = (
        config.three_plus_three_config.model_dump()
        if config.three_plus_three_config
        else None
    )
    rar_json = config.rar_config.model_dump() if config.rar_config else None

    design_type_str = _val(config.design_type)

    if existing:
        existing.design_type = design_type_str
        existing.target_toxicity_rate = (
            config.boin_config.target_toxicity_rate
            if config.boin_config
            else (config.crm_config.target_toxicity_rate if config.crm_config else None)
        )
        existing.dose_levels = dose_levels_json
        existing.boin_config = boin_json
        existing.crm_config = crm_json
        existing.three_plus_three_config = three_plus_three_json
        existing.rar_config = rar_json
        existing.cohort_size = config.cohort_size
        existing.max_sample_size = config.max_sample_size
        existing.is_active = config.is_active
        record = existing
    else:
        record = AdaptiveTrialConfig(
            study_id=config.study_id,
            design_type=design_type_str,
            target_toxicity_rate=(
                config.boin_config.target_toxicity_rate
                if config.boin_config
                else (
                    config.crm_config.target_toxicity_rate
                    if config.crm_config
                    else None
                )
            ),
            dose_levels=dose_levels_json,
            boin_config=boin_json,
            crm_config=crm_json,
            three_plus_three_config=three_plus_three_json,
            rar_config=rar_json,
            cohort_size=config.cohort_size,
            max_sample_size=config.max_sample_size,
            is_active=config.is_active,
        )
        session.add(record)

    await session.flush()

    # Initialize initial cohort state if dose escalation design and none exists
    if (
        design_type_str
        in (
            "THREE_PLUS_THREE",
            "BOIN",
            "CRM",
        )
        and config.dose_levels
    ):
        stmt_cohort = select(DoseCohortState).where(
            DoseCohortState.study_id == config.study_id,
            DoseCohortState.is_deleted.is_(False),
        )
        res_cohort = await session.execute(stmt_cohort)
        existing_cohort = res_cohort.scalars().first()

        if not existing_cohort:
            initial_dose = config.dose_levels[0]
            first_cohort = DoseCohortState(
                study_id=config.study_id,
                cohort_number=1,
                current_dose_level_index=initial_dose.index,
                current_dose_name=initial_dose.name,
                enrolled_count=0,
                dlt_count=0,
                cumulative_dose_evaluations={},
                eliminated_doses=[],
                recommendation_action="STAY",
                recommended_dose_level_index=initial_dose.index,
                recommended_dose_name=initial_dose.name,
                status="ENROLLING",
                rationale="Trial initiated at starting dose level 1.",
            )
            session.add(first_cohort)
            await session.flush()

    return record


async def evaluate_cohort_outcomes_transaction(
    session: AsyncSession,
    payload: CohortEvaluationInput,
    user_id: str,
    change_reason: str | None = None,
) -> CohortEvaluationResult:
    """Evaluates safety cohort DLTs and computes real-time recommendation flags."""
    # 1. Fetch study config
    stmt_cfg = select(AdaptiveTrialConfig).where(
        AdaptiveTrialConfig.study_id == payload.study_id,
        AdaptiveTrialConfig.is_deleted.is_(False),
    )
    res_cfg = await session.execute(stmt_cfg)
    trial_cfg = res_cfg.scalars().first()
    if not trial_cfg or not trial_cfg.is_active:
        raise AdaptiveTrialConfigNotFoundError(
            f"Active adaptive trial configuration not found for study {payload.study_id}."
        )

    # 2. Fetch active cohort state
    stmt_cohort = (
        select(DoseCohortState)
        .where(
            DoseCohortState.study_id == payload.study_id,
            DoseCohortState.cohort_number == payload.cohort_number,
            DoseCohortState.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_cohort = await session.execute(stmt_cohort)
    cohort_state = res_cohort.scalars().first()
    if not cohort_state:
        raise CohortTransitionError(
            f"Cohort {payload.cohort_number} not found for study {payload.study_id}."
        )

    dose_levels = [DoseLevelDef(**d) for d in trial_cfg.dose_levels]
    total_doses = len(dose_levels)
    if payload.dose_level_index < 1 or payload.dose_level_index > total_doses:
        raise InvalidDoseLevelError(
            f"Dose level index {payload.dose_level_index} is out of valid range (1..{total_doses})."
        )

    current_dose_def = next(
        (d for d in dose_levels if d.index == payload.dose_level_index),
        None,
    )
    if not current_dose_def:
        raise InvalidDoseLevelError(
            f"Dose level index {payload.dose_level_index} not found."
        )

    # 3. Update cumulative statistics for evaluated dose
    cum_evals: dict[str, dict[str, int]] = dict(
        cohort_state.cumulative_dose_evaluations or {}
    )
    dose_key = str(payload.dose_level_index)

    prior_enrolled = cum_evals.get(dose_key, {}).get("enrolled", 0)
    prior_dlts = cum_evals.get(dose_key, {}).get("dlts", 0)

    new_enrolled = prior_enrolled + payload.subjects_evaluated
    new_dlts = prior_dlts + payload.dlts_observed

    cum_evals[dose_key] = {"enrolled": new_enrolled, "dlts": new_dlts}
    cohort_state.cumulative_dose_evaluations = cum_evals
    cohort_state.enrolled_count = payload.subjects_evaluated
    cohort_state.dlt_count = payload.dlts_observed

    # Convert integer keys for engine calculation
    dose_stats_int = {
        int(k): {"enrolled": v["enrolled"], "dlts": v["dlts"]}
        for k, v in cum_evals.items()
    }
    eliminated_set = set(cohort_state.eliminated_doses or [])

    # 4. Execute design-specific dose-escalation engine
    design_type = DoseEscalationDesignType(trial_cfg.design_type)
    action: CohortRecommendationAction = CohortRecommendationAction.STAY
    rec_dose_idx: int | None = payload.dose_level_index
    rec_dose_name: str | None = current_dose_def.name
    rationale = ""
    lambda_e_val: float | None = None
    lambda_d_val: float | None = None
    overdose_prob: float | None = None
    is_safety_stop = False
    is_mtd_declared = False
    mtd_idx: int | None = None
    mtd_name: str | None = None
    summaries: list[DoseLevelEvaluationSummary] = []

    if design_type == DoseEscalationDesignType.BOIN:
        boin_cfg = BoinConfig(**(trial_cfg.boin_config or {}))
        engine = BoinDoseEscalationEngine(boin_cfg)
        lambda_e_val = engine.lambda_e
        lambda_d_val = engine.lambda_d

        action, rec_dose_idx, summaries, rationale, is_safety_stop = (
            engine.evaluate_cohort(
                current_dose_index=payload.dose_level_index,
                total_doses=total_doses,
                dose_stats=dose_stats_int,
                eliminated_doses=eliminated_set,
            )
        )
        # Update eliminated doses in state
        elim_list = [
            s.dose_level_index for s in summaries if s.is_eliminated_for_safety
        ]
        cohort_state.eliminated_doses = elim_list

        # Attach dose names to summaries
        for s in summaries:
            matching = next(
                (d for d in dose_levels if d.index == s.dose_level_index), None
            )
            if matching:
                s.dose_name = matching.name
                s.dose_value = matching.dose_value

        if rec_dose_idx:
            matching_rec = next(
                (d for d in dose_levels if d.index == rec_dose_idx), None
            )
            rec_dose_name = (
                matching_rec.name if matching_rec else f"Dose {rec_dose_idx}"
            )

        # Current dose overdose prob
        curr_sum = next(
            (s for s in summaries if s.dose_level_index == payload.dose_level_index),
            None,
        )
        if curr_sum:
            overdose_prob = curr_sum.overdose_probability

    elif design_type == DoseEscalationDesignType.CRM:
        crm_cfg = CrmConfig(**(trial_cfg.crm_config or {}))
        crm_engine = CrmDoseEscalationEngine(crm_cfg)
        action, rec_dose_idx, post_means, overdose_p1, rationale, is_safety_stop = (
            crm_engine.evaluate_cohort(
                current_dose_index=payload.dose_level_index,
                dose_stats=dose_stats_int,
            )
        )
        overdose_prob = overdose_p1

        for idx, dose_def in enumerate(dose_levels, start=1):
            st = dose_stats_int.get(idx, {"enrolled": 0, "dlts": 0})
            post_p = post_means[idx - 1] if idx - 1 < len(post_means) else None
            obs_p = round(st["dlts"] / st["enrolled"], 4) if st["enrolled"] > 0 else 0.0
            summaries.append(
                DoseLevelEvaluationSummary(
                    dose_level_index=idx,
                    dose_name=dose_def.name,
                    dose_value=dose_def.dose_value,
                    total_enrolled=st["enrolled"],
                    total_dlts=st["dlts"],
                    observed_dlt_rate=obs_p,
                    posterior_dlt_rate=post_p,
                    is_eliminated_for_safety=False,
                    overdose_probability=overdose_p1 if idx == 1 else None,
                )
            )

        if rec_dose_idx:
            matching_rec = next(
                (d for d in dose_levels if d.index == rec_dose_idx), None
            )
            rec_dose_name = (
                matching_rec.name if matching_rec else f"Dose {rec_dose_idx}"
            )

    elif design_type == DoseEscalationDesignType.THREE_PLUS_THREE:
        tpt_cfg = ThreePlusThreeConfig(**(trial_cfg.three_plus_three_config or {}))
        tpt_engine = ThreePlusThreeDoseEscalationEngine(tpt_cfg)
        action, rec_dose_idx, rationale = tpt_engine.evaluate(
            current_dose_index=payload.dose_level_index,
            total_doses=total_doses,
            cohort_subjects=payload.subjects_evaluated,
            cohort_dlts=payload.dlts_observed,
            cumulative_enrolled_at_dose=new_enrolled,
            cumulative_dlts_at_dose=new_dlts,
        )
        if action == CohortRecommendationAction.STOP_SAFETY:
            is_safety_stop = True
        elif action in (
            CohortRecommendationAction.MTD_FOUND,
            CohortRecommendationAction.STOP_SUCCESS,
        ):
            is_mtd_declared = True
            mtd_idx = rec_dose_idx
            matching_mtd = next((d for d in dose_levels if d.index == mtd_idx), None)
            mtd_name = matching_mtd.name if matching_mtd else f"Dose {mtd_idx}"

        for idx, dose_def in enumerate(dose_levels, start=1):
            st = dose_stats_int.get(idx, {"enrolled": 0, "dlts": 0})
            obs_p = round(st["dlts"] / st["enrolled"], 4) if st["enrolled"] > 0 else 0.0
            summaries.append(
                DoseLevelEvaluationSummary(
                    dose_level_index=idx,
                    dose_name=dose_def.name,
                    dose_value=dose_def.dose_value,
                    total_enrolled=st["enrolled"],
                    total_dlts=st["dlts"],
                    observed_dlt_rate=obs_p,
                    posterior_dlt_rate=None,
                    is_eliminated_for_safety=False,
                    overdose_probability=None,
                )
            )

        if rec_dose_idx:
            matching_rec = next(
                (d for d in dose_levels if d.index == rec_dose_idx), None
            )
            rec_dose_name = (
                matching_rec.name if matching_rec else f"Dose {rec_dose_idx}"
            )

    # 5. Update cohort state record
    cohort_state.recommendation_action = _val(action)
    cohort_state.recommended_dose_level_index = rec_dose_idx
    cohort_state.recommended_dose_name = rec_dose_name
    cohort_state.status = "TERMINATED" if is_safety_stop else "EVALUATED"
    cohort_state.rationale = rationale
    if is_mtd_declared:
        cohort_state.mtd_declared_dose_level = mtd_idx
        cohort_state.mtd_declared_dose_name = mtd_name

    await session.flush()

    return CohortEvaluationResult(
        study_id=payload.study_id,
        cohort_number=payload.cohort_number,
        evaluated_dose_level_index=payload.dose_level_index,
        evaluated_dose_name=current_dose_def.name,
        subjects_in_cohort=payload.subjects_evaluated,
        dlts_in_cohort=payload.dlts_observed,
        recommendation_action=_val(action),
        recommended_dose_level_index=rec_dose_idx,
        recommended_dose_name=rec_dose_name,
        lambda_e=lambda_e_val,
        lambda_d=lambda_d_val,
        target_toxicity_rate=trial_cfg.target_toxicity_rate,
        overdose_probability=overdose_prob,
        is_safety_stop_triggered=is_safety_stop,
        is_mtd_declared=is_mtd_declared,
        mtd_dose_level_index=mtd_idx,
        mtd_dose_name=mtd_name,
        dose_summaries=summaries,
        rationale=rationale,
    )


async def advance_cohort_decision_transaction(
    session: AsyncSession,
    payload: CohortTransitionRequest,
    user_id: str,
    change_reason: str | None = None,
) -> CohortTransitionLogResponse:
    """Advances or overrides a dose escalation cohort with 21 CFR Part 11 audit logging."""
    # 1. Fetch study config
    stmt_cfg = select(AdaptiveTrialConfig).where(
        AdaptiveTrialConfig.study_id == payload.study_id,
        AdaptiveTrialConfig.is_deleted.is_(False),
    )
    res_cfg = await session.execute(stmt_cfg)
    trial_cfg = res_cfg.scalars().first()
    if not trial_cfg:
        raise AdaptiveTrialConfigNotFoundError(
            f"Config not found for study {payload.study_id}."
        )

    # 2. Fetch active cohort state
    stmt_cohort = (
        select(DoseCohortState)
        .where(
            DoseCohortState.study_id == payload.study_id,
            DoseCohortState.cohort_number == payload.cohort_number,
            DoseCohortState.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_cohort = await session.execute(stmt_cohort)
    curr_cohort = res_cohort.scalars().first()
    if not curr_cohort:
        raise CohortTransitionError(f"Cohort {payload.cohort_number} not found.")

    dose_levels = [DoseLevelDef(**d) for d in trial_cfg.dose_levels]
    total_doses = len(dose_levels)
    if (
        payload.target_dose_level_index < 1
        or payload.target_dose_level_index > total_doses
    ):
        raise InvalidDoseLevelError(
            f"Target dose level {payload.target_dose_level_index} out of range (1..{total_doses})."
        )

    # 3. Detect override condition and enforce change justification
    action_str = _val(payload.decision_action)
    override_occurred = (
        action_str != curr_cohort.recommendation_action
        or payload.target_dose_level_index != curr_cohort.recommended_dose_level_index
    )

    if override_occurred and not (payload.override_reason or change_reason):
        raise CohortTransitionError(
            "An explicit override reason is mandatory under 21 CFR Part 11 when departing from algorithmic recommendation."
        )

    # 4. Generate cryptographic seal
    now = datetime.now(UTC)
    raw_seal_content = f"{payload.study_id}|{payload.cohort_number}|{curr_cohort.current_dose_level_index}|{payload.target_dose_level_index}|{action_str}|{user_id}|{now.isoformat()}"
    checksum = hashlib.sha256(raw_seal_content.encode("utf-8")).hexdigest()

    # 5. Create immutable transition log record
    transition_log = AdaptiveCohortTransitionLog(
        study_id=payload.study_id,
        cohort_number=payload.cohort_number,
        from_dose_level=curr_cohort.current_dose_level_index,
        to_dose_level=payload.target_dose_level_index,
        action_taken=action_str,
        algorithm_recommendation=curr_cohort.recommendation_action,
        override_occurred=override_occurred,
        override_reason=payload.override_reason or change_reason,
        evaluated_dlt_rate=(
            curr_cohort.dlt_count / curr_cohort.enrolled_count
            if curr_cohort.enrolled_count > 0
            else 0.0
        ),
        posterior_toxicity_estimates=curr_cohort.cumulative_dose_evaluations,
        authorized_by=user_id,
        transitioned_at=now,
        signature_token=payload.electronic_signature
        or f"SIG-{uuid.uuid4().hex[:12].upper()}",
        cryptographic_checksum=checksum,
    )
    session.add(transition_log)

    # 6. Update current cohort status
    curr_cohort.status = "OVERRIDDEN" if override_occurred else "COMPLETED"

    # 7. Spawn next cohort if not stopped
    is_terminal = action_str in (
        "STOP_SAFETY",
        "STOP_SUCCESS",
        "MTD_FOUND",
    )

    if not is_terminal:
        target_dose_def = next(
            (d for d in dose_levels if d.index == payload.target_dose_level_index),
            None,
        )
        target_name = (
            target_dose_def.name
            if target_dose_def
            else f"Dose {payload.target_dose_level_index}"
        )

        next_cohort = DoseCohortState(
            study_id=payload.study_id,
            cohort_number=payload.cohort_number + 1,
            current_dose_level_index=payload.target_dose_level_index,
            current_dose_name=target_name,
            enrolled_count=0,
            dlt_count=0,
            cumulative_dose_evaluations=curr_cohort.cumulative_dose_evaluations,
            eliminated_doses=curr_cohort.eliminated_doses,
            recommendation_action="STAY",
            recommended_dose_level_index=payload.target_dose_level_index,
            recommended_dose_name=target_name,
            status="ENROLLING",
            rationale=f"Advanced from cohort {payload.cohort_number} via {action_str}.",
        )
        session.add(next_cohort)

    await session.flush()

    return CohortTransitionLogResponse(
        id=transition_log.id,
        study_id=transition_log.study_id,
        cohort_number=transition_log.cohort_number,
        from_dose_level=transition_log.from_dose_level,
        to_dose_level=transition_log.to_dose_level,
        action_taken=transition_log.action_taken,
        algorithm_recommendation=transition_log.algorithm_recommendation,
        override_occurred=transition_log.override_occurred,
        override_reason=transition_log.override_reason,
        evaluated_dlt_rate=transition_log.evaluated_dlt_rate,
        posterior_toxicity_estimates=transition_log.posterior_toxicity_estimates,
        authorized_by=transition_log.authorized_by,
        transitioned_at=transition_log.transitioned_at,
        signature_token=transition_log.signature_token,
        cryptographic_checksum=transition_log.cryptographic_checksum,
    )


async def evaluate_and_update_rar_transaction(
    session: AsyncSession,
    study_id: str,
    user_id: str,
    change_reason: str | None = None,
) -> RAREvaluationResult:
    """Evaluates accumulated response data and updates Response-Adaptive Randomization allocation weights."""
    stmt_cfg = select(AdaptiveTrialConfig).where(
        AdaptiveTrialConfig.study_id == study_id,
        AdaptiveTrialConfig.is_deleted.is_(False),
    )
    res_cfg = await session.execute(stmt_cfg)
    trial_cfg = res_cfg.scalars().first()
    if not trial_cfg:
        raise AdaptiveTrialConfigNotFoundError(
            f"Config not found for study {study_id}."
        )

    rar_cfg = RarConfig(**(trial_cfg.rar_config or {}))
    engine = ResponseAdaptiveRandomizationEngine(rar_cfg)

    stmt_arms = (
        select(RARArmState)
        .where(RARArmState.study_id == study_id, RARArmState.is_deleted.is_(False))
        .with_for_update()
    )
    res_arms = await session.execute(stmt_arms)
    arm_records = list(res_arms.scalars().all())
    if not arm_records:
        raise RARArmNotFoundError(f"No treatment arms defined for study {study_id}.")

    arms_dict_list = [
        {
            "arm_id": a.arm_id,
            "arm_name": a.arm_name,
            "is_control": a.is_control,
            "status": a.status,
            "enrolled_count": a.enrolled_count,
            "response_count": a.response_count,
            "prior_alpha": a.prior_alpha,
            "prior_beta": a.prior_beta,
        }
        for a in arm_records
    ]

    new_probs, dropped, graduated, rationale = engine.update_probabilities(
        arms_dict_list
    )

    # Persist updated metrics to arm records
    total_enrolled = sum(a.enrolled_count for a in arm_records)
    dtos: list[RARArmEvaluationDTO] = []
    now = datetime.now(UTC)

    for record, updated_data in zip(arm_records, arms_dict_list, strict=True):
        record.posterior_alpha = updated_data["posterior_alpha"]
        record.posterior_beta = updated_data["posterior_beta"]
        record.posterior_mean = updated_data["posterior_mean"]
        record.allocation_probability = updated_data["allocation_probability"]
        record.status = _val(updated_data["status"])
        record.superiority_vs_control_prob = updated_data.get(
            "superiority_vs_control_prob"
        )
        record.updated_at = now

        dtos.append(
            RARArmEvaluationDTO(
                arm_id=record.arm_id,
                arm_name=record.arm_name,
                is_control=record.is_control,
                status=record.status,
                enrolled_count=record.enrolled_count,
                response_count=record.response_count,
                posterior_alpha=record.posterior_alpha,
                posterior_beta=record.posterior_beta,
                posterior_mean=record.posterior_mean,
                allocation_probability=record.allocation_probability,
                superiority_vs_control_prob=record.superiority_vs_control_prob,
            )
        )

    # Immutable RAR Allocation Log record
    sample_snapshot = {a.arm_id: a.enrolled_count for a in arm_records}
    response_snapshot = {a.arm_id: a.response_count for a in arm_records}
    checksum = hashlib.sha256(
        f"{study_id}|{json.dumps(new_probs, sort_keys=True)}|{now.isoformat()}|{user_id}".encode()
    ).hexdigest()

    alloc_log = RARAllocationLog(
        study_id=study_id,
        event_type="UPDATE_PROBABILITIES",
        probabilities_snapshot=new_probs,
        sample_sizes_snapshot=sample_snapshot,
        responses_snapshot=response_snapshot,
        triggered_by=user_id,
        timestamp=now,
        change_reason=change_reason,
        cryptographic_hash=checksum,
    )
    session.add(alloc_log)
    await session.flush()

    return RAREvaluationResult(
        study_id=study_id,
        total_enrolled=total_enrolled,
        arms=dtos,
        allocation_probabilities=new_probs,
        dropped_arms=dropped,
        graduated_arms=graduated,
        rationale=rationale,
        updated_at=now,
    )


async def add_or_terminate_rar_arm_transaction(
    session: AsyncSession,
    payload: RARExpandOrTerminateArmRequest,
    user_id: str,
    change_reason: str | None = None,
) -> RAREvaluationResult:
    """Dynamically adds, drops, or graduates an arm in Response-Adaptive Randomization with zero study downtime."""
    stmt_cfg = select(AdaptiveTrialConfig).where(
        AdaptiveTrialConfig.study_id == payload.study_id,
        AdaptiveTrialConfig.is_deleted.is_(False),
    )
    res_cfg = await session.execute(stmt_cfg)
    trial_cfg = res_cfg.scalars().first()
    if not trial_cfg:
        raise AdaptiveTrialConfigNotFoundError(
            f"Config not found for study {payload.study_id}."
        )

    stmt_arm = (
        select(RARArmState)
        .where(
            RARArmState.study_id == payload.study_id,
            RARArmState.arm_id == payload.arm_id,
            RARArmState.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_arm = await session.execute(stmt_arm)
    existing_arm = res_arm.scalars().first()

    now = datetime.now(UTC)

    if payload.action == "ADD_ARM":
        if existing_arm:
            existing_arm.status = "ACTIVE"
            existing_arm.arm_name = payload.arm_name
        else:
            new_arm = RARArmState(
                study_id=payload.study_id,
                arm_id=payload.arm_id,
                arm_name=payload.arm_name,
                is_control=payload.is_control,
                status="ACTIVE",
                enrolled_count=0,
                response_count=0,
                prior_alpha=payload.prior_alpha,
                prior_beta=payload.prior_beta,
                posterior_alpha=payload.prior_alpha,
                posterior_beta=payload.prior_beta,
                posterior_mean=payload.prior_alpha
                / (payload.prior_alpha + payload.prior_beta),
                allocation_probability=0.0,
                updated_at=now,
            )
            session.add(new_arm)
    elif payload.action == "DROP_FUTILITY":
        if not existing_arm:
            raise RARArmNotFoundError(f"Arm {payload.arm_id} not found.")
        existing_arm.status = "DROPPED_FUTILITY"
        existing_arm.updated_at = now
    elif payload.action == "GRADUATE_SUCCESS":
        if not existing_arm:
            raise RARArmNotFoundError(f"Arm {payload.arm_id} not found.")
        existing_arm.status = "GRADUATED_SUCCESS"
        existing_arm.updated_at = now
    elif payload.action == "TERMINATE":
        if not existing_arm:
            raise RARArmNotFoundError(f"Arm {payload.arm_id} not found.")
        existing_arm.status = "TERMINATED"
        existing_arm.updated_at = now
    else:
        raise CohortTransitionError(f"Invalid RAR arm action '{payload.action}'.")

    await session.flush()

    # Re-evaluate dynamic weights among active arms
    return await evaluate_and_update_rar_transaction(
        session=session,
        study_id=payload.study_id,
        user_id=user_id,
        change_reason=f"Arm {payload.arm_id} {payload.action}: {payload.reason}",
    )


async def get_adaptive_study_state_transaction(
    session: AsyncSession,
    study_id: str,
) -> dict[str, Any]:
    """Retrieves current adaptive trial configuration, active cohort state, and transition log history."""
    stmt_cfg = select(AdaptiveTrialConfig).where(
        AdaptiveTrialConfig.study_id == study_id,
        AdaptiveTrialConfig.is_deleted.is_(False),
    )
    res_cfg = await session.execute(stmt_cfg)
    config_row = res_cfg.scalars().first()

    stmt_cohort = (
        select(DoseCohortState)
        .where(
            DoseCohortState.study_id == study_id,
            DoseCohortState.is_deleted.is_(False),
        )
        .order_by(DoseCohortState.cohort_number.desc())
    )
    res_cohort = await session.execute(stmt_cohort)
    cohorts = list(res_cohort.scalars().all())

    stmt_logs = (
        select(AdaptiveCohortTransitionLog)
        .where(
            AdaptiveCohortTransitionLog.study_id == study_id,
            AdaptiveCohortTransitionLog.is_deleted.is_(False),
        )
        .order_by(AdaptiveCohortTransitionLog.transitioned_at.desc())
    )
    res_logs = await session.execute(stmt_logs)
    logs = list(res_logs.scalars().all())

    stmt_rar = select(RARArmState).where(
        RARArmState.study_id == study_id,
        RARArmState.is_deleted.is_(False),
    )
    res_rar = await session.execute(stmt_rar)
    rar_arms = list(res_rar.scalars().all())

    return {
        "study_id": study_id,
        "config": config_row,
        "active_cohort": cohorts[0] if cohorts else None,
        "cohorts_history": cohorts,
        "transition_logs": logs,
        "rar_arms": rar_arms,
    }
