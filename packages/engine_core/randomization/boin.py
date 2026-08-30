"""Bayesian Optimal Interval (BOIN) Design and 3+3 Adaptive Dose Escalation Engine.

Requirements: PRD-RTSM-002, PRD-SYS-001, 21 CFR Part 11
"""

import math
from collections.abc import Sequence

from packages.engine_core.randomization.models import (
    BOINConfig,
    BOINDoseRecommendation,
    DoseCohortState,
    DoseEscalationAction,
    DoseLevelDefinition,
    ThreePlusThreeConfig,
)


def calculate_boin_boundaries(
    target_dlt_rate: float,
    phi1_fraction: float = 0.6,
    phi2_fraction: float = 1.4,
) -> tuple[float, float]:
    """Calculates mathematical BOIN optimal decision boundaries (lambda_e, lambda_d).

    Args:
        target_dlt_rate: Target DLT probability phi (e.g. 0.25).
        phi1_fraction: Lower tolerance fraction (default 0.6 -> phi1 = 0.6 * phi).
        phi2_fraction: Upper tolerance fraction (default 1.4 -> phi2 = 1.4 * phi).

    Returns:
        tuple of (lambda_e, lambda_d).
    """
    phi = target_dlt_rate
    phi1 = phi * phi1_fraction
    phi2 = phi * phi2_fraction

    # lambda_e = ln((1 - phi1) / (1 - phi)) / ln((phi * (1 - phi1)) / (phi1 * (1 - phi)))
    num_e = math.log((1.0 - phi1) / (1.0 - phi))
    denom_e = math.log((phi * (1.0 - phi1)) / (phi1 * (1.0 - phi)))
    lambda_e = num_e / denom_e

    # lambda_d = ln((1 - phi) / (1 - phi2)) / ln((phi2 * (1 - phi)) / (phi * (1 - phi2)))
    num_d = math.log((1.0 - phi) / (1.0 - phi2))
    denom_d = math.log((phi2 * (1.0 - phi)) / (phi * (1.0 - phi2)))
    lambda_d = num_d / denom_d

    return round(lambda_e, 4), round(lambda_d, 4)


def beta_inc(a: float, b: float, x: float) -> float:
    """Calculates regularized incomplete beta function I_x(a, b)."""
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0

    # For integer parameters (standard in clinical trial dose finding)
    if float(a).is_integer() and float(b).is_integer() and a >= 1 and b >= 1:
        ia, ib = int(a), int(b)
        n = ia + ib - 1
        # I_x(a, b) = sum_{k=a}^{n} comb(n, k) * x^k * (1-x)^(n-k)
        total = 0.0
        for k in range(ia, n + 1):
            total += math.comb(n, k) * (x**k) * ((1.0 - x) ** (n - k))
        return round(total, 6)

    # Numerical integration via Simpson's rule
    lbeta = math.lgamma(a) + math.lgamma(b) - math.lgamma(a + b)
    steps = 1000
    h = x / steps
    total = 0.0
    for i in range(steps + 1):
        t = i * h
        if t <= 0.0 or t >= 1.0:
            val = 0.0
        else:
            val = math.exp(
                (a - 1.0) * math.log(t) + (b - 1.0) * math.log(1.0 - t) - lbeta
            )
        weight = 1.0 if i in (0, steps) else (4.0 if i % 2 == 1 else 2.0)
        total += weight * val
    return max(0.0, min(1.0, (h / 3.0) * total))


def calculate_posterior_prob_toxic(
    observed_dlts: int,
    evaluated_subjects: int,
    target_dlt_rate: float,
) -> float:
    """Calculates Bayesian posterior probability Pr(p_j > target_dlt_rate | data).

    Uses a flat conjugate Beta(1, 1) prior, yielding posterior Beta(1 + d_j, 1 + n_j - d_j).
    Pr(p_j > phi) = 1 - I_phi(1 + d_j, 1 + n_j - d_j).
    """
    if evaluated_subjects == 0:
        return 0.0

    a = 1.0 + observed_dlts
    b = 1.0 + evaluated_subjects - observed_dlts
    cdf = beta_inc(a, b, target_dlt_rate)
    prob_excess = 1.0 - cdf
    return round(max(0.0, min(1.0, prob_excess)), 4)


class BOINDoseEscalationEngine:
    """Zero-I/O Bayesian Optimal Interval (BOIN) Adaptive Dose Finding Engine."""

    @classmethod
    def evaluate_dose_level(
        cls,
        config: BOINConfig,
        dose_levels: Sequence[DoseLevelDefinition],
        current_state: DoseCohortState,
        all_dose_states: Sequence[DoseCohortState] | None = None,
    ) -> BOINDoseRecommendation:
        """Evaluates current dose cohort outcomes and recommends next dose action.

        Args:
            config: BOIN configuration with target DLT rate phi and stopping cutoff.
            dose_levels: Defined sequence of dose level tiers.
            current_state: Evaluated outcomes (n_j, d_j) at the current dose level.
            all_dose_states: Optional full history across all dose levels.

        Returns:
            BOINDoseRecommendation with escalation/de-escalation action, next dose, and safety stopping flags.
        """
        # 1. Compute boundaries
        lambda_e = config.escalation_boundary
        lambda_d = config.deescalation_boundary
        if lambda_e is None or lambda_d is None:
            auto_e, auto_d = calculate_boin_boundaries(config.target_dlt_rate)
            lambda_e = lambda_e or auto_e
            lambda_d = lambda_d or auto_d

        n_j = current_state.evaluated_subjects
        d_j = current_state.observed_dlts
        p_hat = current_state.empirical_dlt_rate

        # 2. Check Bayesian Safety Stopping Rule (if n_j >= 3)
        prob_toxic = (
            calculate_posterior_prob_toxic(d_j, n_j, config.target_dlt_rate)
            if n_j >= 3
            else 0.0
        )

        curr_idx = current_state.dose_index
        curr_def = next((d for d in dose_levels if d.dose_index == curr_idx), None)
        dose_amt = curr_def.dose_amount if curr_def else current_state.dose_amount
        dose_unit = curr_def.dose_unit if curr_def else current_state.dose_unit

        # Safety Elimination Check
        if n_j >= 3 and prob_toxic >= config.stopping_cutoff:
            # Dose level is eliminated for excessive toxicity
            if curr_idx == 0:
                # Lowest dose is excessively toxic -> STOP ENTIRE TRIAL
                return BOINDoseRecommendation(
                    current_dose_index=curr_idx,
                    current_dose_amount=dose_amt,
                    current_dose_unit=dose_unit,
                    action=DoseEscalationAction.STOP_TRIAL,
                    recommended_dose_index=None,
                    recommended_dose_amount=None,
                    empirical_dlt_rate=p_hat,
                    posterior_prob_toxic=prob_toxic,
                    is_trial_stopped=True,
                    rationale=(
                        f"Lowest dose tier #{curr_idx} ({dose_amt} {dose_unit}) is excessively toxic "
                        f"(Pr(toxicity > {config.target_dlt_rate}) = {prob_toxic:.4f} >= {config.stopping_cutoff}). "
                        f"Trial halted per 21 CFR Part 11 safety stopping rule."
                    ),
                )
            # De-escalate to dose below
            prev_def = next(
                (d for d in dose_levels if d.dose_index == curr_idx - 1), None
            )
            prev_amt = prev_def.dose_amount if prev_def else None
            return BOINDoseRecommendation(
                current_dose_index=curr_idx,
                current_dose_amount=dose_amt,
                current_dose_unit=dose_unit,
                action=DoseEscalationAction.DEESCALATE,
                recommended_dose_index=curr_idx - 1,
                recommended_dose_amount=prev_amt,
                empirical_dlt_rate=p_hat,
                posterior_prob_toxic=prob_toxic,
                is_trial_stopped=False,
                rationale=(
                    f"Dose tier #{curr_idx} eliminated due to excessive toxicity "
                    f"(Pr(toxicity > {config.target_dlt_rate}) = {prob_toxic:.4f} >= {config.stopping_cutoff}). "
                    f"De-escalating to tier #{curr_idx - 1}."
                ),
            )

        # 3. Standard BOIN Interval Decision Rules
        max_idx = max(d.dose_index for d in dose_levels)

        if p_hat <= lambda_e:
            # Escalate
            if curr_idx < max_idx:
                next_idx = curr_idx + 1
                next_def = next(d for d in dose_levels if d.dose_index == next_idx)
                return BOINDoseRecommendation(
                    current_dose_index=curr_idx,
                    current_dose_amount=dose_amt,
                    current_dose_unit=dose_unit,
                    action=DoseEscalationAction.ESCALATE,
                    recommended_dose_index=next_idx,
                    recommended_dose_amount=next_def.dose_amount,
                    empirical_dlt_rate=p_hat,
                    posterior_prob_toxic=prob_toxic,
                    rationale=(
                        f"Observed DLT rate {p_hat:.4f} ({d_j}/{n_j}) <= escalation boundary lambda_e ({lambda_e}). "
                        f"Escalating to dose tier #{next_idx} ({next_def.dose_amount} {next_def.dose_unit})."
                    ),
                )
            # Already at highest dose -> Stay
            return BOINDoseRecommendation(
                current_dose_index=curr_idx,
                current_dose_amount=dose_amt,
                current_dose_unit=dose_unit,
                action=DoseEscalationAction.STAY,
                recommended_dose_index=curr_idx,
                recommended_dose_amount=dose_amt,
                empirical_dlt_rate=p_hat,
                posterior_prob_toxic=prob_toxic,
                rationale=(
                    f"Observed DLT rate {p_hat:.4f} ({d_j}/{n_j}) <= lambda_e ({lambda_e}), "
                    f"but already at maximum dose tier #{curr_idx}. Staying at current dose."
                ),
            )

        if p_hat >= lambda_d:
            # De-escalate
            if curr_idx > 0:
                prev_idx = curr_idx - 1
                prev_def = next(d for d in dose_levels if d.dose_index == prev_idx)
                return BOINDoseRecommendation(
                    current_dose_index=curr_idx,
                    current_dose_amount=dose_amt,
                    current_dose_unit=dose_unit,
                    action=DoseEscalationAction.DEESCALATE,
                    recommended_dose_index=prev_idx,
                    recommended_dose_amount=prev_def.dose_amount,
                    empirical_dlt_rate=p_hat,
                    posterior_prob_toxic=prob_toxic,
                    rationale=(
                        f"Observed DLT rate {p_hat:.4f} ({d_j}/{n_j}) >= de-escalation boundary lambda_d ({lambda_d}). "
                        f"De-escalating to dose tier #{prev_idx} ({prev_def.dose_amount} {prev_def.dose_unit})."
                    ),
                )
            # At lowest dose, check stay
            return BOINDoseRecommendation(
                current_dose_index=curr_idx,
                current_dose_amount=dose_amt,
                current_dose_unit=dose_unit,
                action=DoseEscalationAction.STAY,
                recommended_dose_index=curr_idx,
                recommended_dose_amount=dose_amt,
                empirical_dlt_rate=p_hat,
                posterior_prob_toxic=prob_toxic,
                rationale=(
                    f"Observed DLT rate {p_hat:.4f} >= lambda_d ({lambda_d}), but at lowest dose. "
                    f"Staying at dose #{curr_idx} (safety threshold not breached)."
                ),
            )

        # Stay (lambda_e < p_hat < lambda_d)
        return BOINDoseRecommendation(
            current_dose_index=curr_idx,
            current_dose_amount=dose_amt,
            current_dose_unit=dose_unit,
            action=DoseEscalationAction.STAY,
            recommended_dose_index=curr_idx,
            recommended_dose_amount=dose_amt,
            empirical_dlt_rate=p_hat,
            posterior_prob_toxic=prob_toxic,
            rationale=(
                f"Observed DLT rate {p_hat:.4f} ({d_j}/{n_j}) is within optimal interval "
                f"({lambda_e}, {lambda_d}). Continuing at current dose tier #{curr_idx}."
            ),
        )


class ThreePlusThreeDoseEscalationEngine:
    """Zero-I/O Classical 3+3 Rule-Based Dose Escalation Engine."""

    @classmethod
    def evaluate_cohort(
        cls,
        config: ThreePlusThreeConfig,
        dose_levels: Sequence[DoseLevelDefinition],
        current_state: DoseCohortState,
    ) -> BOINDoseRecommendation:
        """Evaluates 3+3 escalation logic based on evaluated cohort counts."""
        n_j = current_state.evaluated_subjects
        d_j = current_state.observed_dlts
        p_hat = current_state.empirical_dlt_rate
        curr_idx = current_state.dose_index

        curr_def = next((d for d in dose_levels if d.dose_index == curr_idx), None)
        dose_amt = curr_def.dose_amount if curr_def else current_state.dose_amount
        dose_unit = curr_def.dose_unit if curr_def else current_state.dose_unit
        max_idx = max(d.dose_index for d in dose_levels)

        # 1. Initial 3 subjects evaluated
        if n_j == 3:
            if d_j == 0:
                # 0/3 -> Escalate
                if curr_idx < max_idx:
                    next_idx = curr_idx + 1
                    next_def = next(d for d in dose_levels if d.dose_index == next_idx)
                    return BOINDoseRecommendation(
                        current_dose_index=curr_idx,
                        current_dose_amount=dose_amt,
                        current_dose_unit=dose_unit,
                        action=DoseEscalationAction.ESCALATE,
                        recommended_dose_index=next_idx,
                        recommended_dose_amount=next_def.dose_amount,
                        empirical_dlt_rate=p_hat,
                        rationale=f"3+3 Rule: 0/3 DLTs observed. Escalating to dose tier #{next_idx}.",
                    )
                return BOINDoseRecommendation(
                    current_dose_index=curr_idx,
                    current_dose_amount=dose_amt,
                    current_dose_unit=dose_unit,
                    action=DoseEscalationAction.STAY,
                    recommended_dose_index=curr_idx,
                    recommended_dose_amount=dose_amt,
                    empirical_dlt_rate=p_hat,
                    rationale=f"3+3 Rule: 0/3 DLTs observed at maximum dose #{curr_idx}. Recommended MTD reached.",
                )
            if d_j == 1:
                # 1/3 -> Stay & Expand cohort to 6
                return BOINDoseRecommendation(
                    current_dose_index=curr_idx,
                    current_dose_amount=dose_amt,
                    current_dose_unit=dose_unit,
                    action=DoseEscalationAction.STAY,
                    recommended_dose_index=curr_idx,
                    recommended_dose_amount=dose_amt,
                    empirical_dlt_rate=p_hat,
                    rationale=f"3+3 Rule: 1/3 DLTs observed. Expanding cohort to 6 subjects at current dose #{curr_idx}.",
                )
            # >= 2/3 -> MTD exceeded, de-escalate
            if curr_idx > 0:
                prev_idx = curr_idx - 1
                prev_def = next(d for d in dose_levels if d.dose_index == prev_idx)
                return BOINDoseRecommendation(
                    current_dose_index=curr_idx,
                    current_dose_amount=dose_amt,
                    current_dose_unit=dose_unit,
                    action=DoseEscalationAction.DEESCALATE,
                    recommended_dose_index=prev_idx,
                    recommended_dose_amount=prev_def.dose_amount,
                    empirical_dlt_rate=p_hat,
                    rationale=f"3+3 Rule: {d_j}/3 DLTs observed. MTD exceeded. De-escalating to tier #{prev_idx}.",
                )
            return BOINDoseRecommendation(
                current_dose_index=curr_idx,
                current_dose_amount=dose_amt,
                current_dose_unit=dose_unit,
                action=DoseEscalationAction.STOP_TRIAL,
                recommended_dose_index=None,
                recommended_dose_amount=None,
                empirical_dlt_rate=p_hat,
                is_trial_stopped=True,
                rationale=f"3+3 Rule: {d_j}/3 DLTs observed at lowest dose #{curr_idx}. Trial halted.",
            )

        # 2. Expanded cohort (6 subjects)
        if n_j >= 6:
            if d_j <= 1:
                # <= 1/6 -> Escalate if possible, else MTD
                if curr_idx < max_idx:
                    next_idx = curr_idx + 1
                    next_def = next(d for d in dose_levels if d.dose_index == next_idx)
                    return BOINDoseRecommendation(
                        current_dose_index=curr_idx,
                        current_dose_amount=dose_amt,
                        current_dose_unit=dose_unit,
                        action=DoseEscalationAction.ESCALATE,
                        recommended_dose_index=next_idx,
                        recommended_dose_amount=next_def.dose_amount,
                        empirical_dlt_rate=p_hat,
                        rationale=f"3+3 Rule: {d_j}/{n_j} DLTs in expanded cohort. Escalating to tier #{next_idx}.",
                    )
                return BOINDoseRecommendation(
                    current_dose_index=curr_idx,
                    current_dose_amount=dose_amt,
                    current_dose_unit=dose_unit,
                    action=DoseEscalationAction.STAY,
                    recommended_dose_index=curr_idx,
                    recommended_dose_amount=dose_amt,
                    empirical_dlt_rate=p_hat,
                    rationale=f"3+3 Rule: {d_j}/{n_j} DLTs at maximum tier. Dose #{curr_idx} confirmed as MTD.",
                )
            # >= 2/6 -> MTD exceeded, de-escalate
            if curr_idx > 0:
                prev_idx = curr_idx - 1
                prev_def = next(d for d in dose_levels if d.dose_index == prev_idx)
                return BOINDoseRecommendation(
                    current_dose_index=curr_idx,
                    current_dose_amount=dose_amt,
                    current_dose_unit=dose_unit,
                    action=DoseEscalationAction.DEESCALATE,
                    recommended_dose_index=prev_idx,
                    recommended_dose_amount=prev_def.dose_amount,
                    empirical_dlt_rate=p_hat,
                    rationale=f"3+3 Rule: {d_j}/{n_j} DLTs in expanded cohort. MTD exceeded. De-escalating to tier #{prev_idx}.",
                )
            return BOINDoseRecommendation(
                current_dose_index=curr_idx,
                current_dose_amount=dose_amt,
                current_dose_unit=dose_unit,
                action=DoseEscalationAction.STOP_TRIAL,
                recommended_dose_index=None,
                recommended_dose_amount=None,
                empirical_dlt_rate=p_hat,
                is_trial_stopped=True,
                rationale=f"3+3 Rule: {d_j}/{n_j} DLTs at lowest dose #{curr_idx}. Trial halted.",
            )

        # In-progress cohort
        return BOINDoseRecommendation(
            current_dose_index=curr_idx,
            current_dose_amount=dose_amt,
            current_dose_unit=dose_unit,
            action=DoseEscalationAction.STAY,
            recommended_dose_index=curr_idx,
            recommended_dose_amount=dose_amt,
            empirical_dlt_rate=p_hat,
            rationale=f"Cohort in progress ({n_j} subjects evaluated). Continuing at current dose.",
        )
