"""Pure mathematical algorithms and statistical engines for adaptive oncology trials.

Covers:
- Standard 3+3 rule-based dose escalation state machine.
- Bayesian Optimal Interval (BOIN) design and theoretical boundary derivations.
- Continual Reassessment Method (CRM / mCRM) Bayesian posterior modeling.
- Response-Adaptive Randomization (RAR) dynamic allocation probability engine.
- High-precision Incomplete Beta function and Bayesian conjugate updating.

Requirements: PRD-SCM-004, ADR-2199, FDA Adaptive Design Guidance
"""

import math
from typing import Any

from apps.execution.domain.adaptive_models import (
    BoinConfig,
    CohortRecommendationAction,
    CrmConfig,
    DoseLevelEvaluationSummary,
    RARArmStatus,
    RarConfig,
    ThreePlusThreeConfig,
)

# =========================================================================
# High-Precision Special Mathematical Functions
# =========================================================================


def lbeta(a: float, b: float) -> float:
    """Natural logarithm of the complete Beta function B(a, b)."""
    return math.lgamma(a) + math.lgamma(b) - math.lgamma(a + b)


def beta_pdf(x: float, a: float, b: float) -> float:
    """Probability density function of Beta(a, b) at point x in [0, 1]."""
    if x <= 0.0 or x >= 1.0:
        return 0.0
    log_pdf = (a - 1.0) * math.log(x) + (b - 1.0) * math.log(1.0 - x) - lbeta(a, b)
    return math.exp(log_pdf)


def beta_cdf(x: float, a: float, b: float) -> float:
    """Regularized incomplete beta function I_x(a, b) = P(X <= x) for X ~ Beta(a, b).

    Evaluated using numerical Simpson quadrature with symmetry transformation.
    """
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0

    # Symmetry transformation for rapid convergence
    if x > (a + 1.0) / (a + b + 2.0):
        return 1.0 - beta_cdf(1.0 - x, b, a)

    # High-precision numerical Simpson quadrature integration
    # for absolute stability across extreme parameter ranges
    n_steps = 1000
    h = x / n_steps
    simpson_sum = 0.0
    for i in range(n_steps + 1):
        t = i * h
        if t <= 0.0 or t >= 1.0:
            val = 0.0
        else:
            log_val = (
                (a - 1.0) * math.log(t) + (b - 1.0) * math.log(1.0 - t) - lbeta(a, b)
            )
            val = math.exp(log_val)

        if i == 0 or i == n_steps:
            simpson_sum += val
        elif i % 2 == 1:
            simpson_sum += 4.0 * val
        else:
            simpson_sum += 2.0 * val

    res = (h / 3.0) * simpson_sum
    return min(max(res, 0.0), 1.0)


def calculate_beta_overdose_prob(
    alpha: float, beta: float, target_toxicity: float
) -> float:
    """Calculates posterior overdose probability P(p > phi | alpha, beta)."""
    return 1.0 - beta_cdf(target_toxicity, alpha, beta)


# =========================================================================
# 1. Standard 3+3 Rule-Based Dose Escalation Engine
# =========================================================================


class ThreePlusThreeDoseEscalationEngine:
    """Deterministic rule-based 3+3 dose escalation engine."""

    def __init__(self, config: ThreePlusThreeConfig | None = None) -> None:
        self.config = config or ThreePlusThreeConfig()

    def evaluate(
        self,
        current_dose_index: int,
        total_doses: int,
        cohort_subjects: int,
        cohort_dlts: int,
        cumulative_enrolled_at_dose: int,
        cumulative_dlts_at_dose: int,
        prev_dose_safe: bool = True,
    ) -> tuple[CohortRecommendationAction, int | None, str]:
        """Evaluates 3+3 cohort results and emits recommendation."""
        # Total subjects and DLTs at this dose level including current cohort
        n = cumulative_enrolled_at_dose
        d = cumulative_dlts_at_dose

        if n < 3:
            return (
                CohortRecommendationAction.STAY,
                current_dose_index,
                f"Cohort has {n}/3 subjects evaluated. Continue enrolling to complete cohort of 3.",
            )

        if n == 3:
            if d == 0:
                if current_dose_index < total_doses:
                    return (
                        CohortRecommendationAction.ESCALATE,
                        current_dose_index + 1,
                        f"0/3 DLTs observed at dose level {current_dose_index}. Escalate to dose level {current_dose_index + 1}.",
                    )
                return (
                    CohortRecommendationAction.MTD_FOUND,
                    current_dose_index,
                    f"0/3 DLTs observed at maximum dose level {current_dose_index}. Maximum dose level reached without toxicity.",
                )
            if d == 1:
                return (
                    CohortRecommendationAction.EXPAND_COHORT,
                    current_dose_index,
                    f"1/3 DLTs observed at dose level {current_dose_index}. Expand cohort by 3 additional subjects (total 6).",
                )
            # d >= 2
            if current_dose_index > 1:
                rec_dose = current_dose_index - 1
                return (
                    CohortRecommendationAction.DE_ESCALATE,
                    rec_dose,
                    f"{d}/3 DLTs observed at dose level {current_dose_index} (>= 2/3 toxic). De-escalate to dose level {rec_dose}.",
                )
            return (
                CohortRecommendationAction.STOP_SAFETY,
                None,
                f"{d}/3 DLTs observed at lowest dose level 1 (>= 2/3 toxic). Stop trial immediately for safety.",
            )

        if n >= 6:
            if d <= 1:
                if current_dose_index < total_doses:
                    return (
                        CohortRecommendationAction.ESCALATE,
                        current_dose_index + 1,
                        f"{d}/{n} DLTs observed at dose level {current_dose_index} (<= 1/6 safe). Escalate to dose level {current_dose_index + 1}.",
                    )
                return (
                    CohortRecommendationAction.MTD_FOUND,
                    current_dose_index,
                    f"{d}/{n} DLTs observed at maximum dose level {current_dose_index}. Dose level {current_dose_index} declared MTD.",
                )
            # d >= 2
            if current_dose_index > 1:
                rec_dose = current_dose_index - 1
                return (
                    CohortRecommendationAction.DE_ESCALATE,
                    rec_dose,
                    f"{d}/{n} DLTs observed at dose level {current_dose_index} (>= 2/6 toxic). Dose exceeds MTD; previous dose {rec_dose} is declared MTD.",
                )
            return (
                CohortRecommendationAction.STOP_SAFETY,
                None,
                f"{d}/{n} DLTs observed at lowest dose level 1 (>= 2/6 toxic). Stop trial immediately for safety.",
            )

        # Between 4 and 5 subjects
        return (
            CohortRecommendationAction.STAY,
            current_dose_index,
            f"Cohort has {n} subjects evaluated (in progress). Target cohort size is 6.",
        )


# =========================================================================
# 2. Bayesian Optimal Interval (BOIN) Design Engine
# =========================================================================


def calculate_boin_boundaries(
    target_toxicity_rate: float,
    factor_e: float = 0.6,
    factor_d: float = 1.4,
) -> tuple[float, float, float, float]:
    """Calculates theoretical BOIN escalation boundary lambda_e and de-escalation boundary lambda_d.

    Formulas:
        phi_1 = factor_e * phi
        phi_2 = factor_d * phi
        lambda_e = ln((1 - phi_1)/(1 - phi)) / ln((phi * (1 - phi_1)) / (phi_1 * (1 - phi)))
        lambda_d = ln((1 - phi)/(1 - phi_2)) / ln((phi_2 * (1 - phi)) / (phi * (1 - phi_2)))
    """
    phi = target_toxicity_rate
    phi_1 = factor_e * phi
    phi_2 = factor_d * phi

    num_e = math.log((1.0 - phi_1) / (1.0 - phi))
    den_e = math.log((phi * (1.0 - phi_1)) / (phi_1 * (1.0 - phi)))
    lambda_e = num_e / den_e

    num_d = math.log((1.0 - phi) / (1.0 - phi_2))
    den_d = math.log((phi_2 * (1.0 - phi)) / (phi * (1.0 - phi_2)))
    lambda_d = num_d / den_d

    return round(lambda_e, 6), round(lambda_d, 6), round(phi_1, 6), round(phi_2, 6)


class BoinDoseEscalationEngine:
    """Bayesian Optimal Interval (BOIN) design engine with real-time safety elimination."""

    def __init__(self, config: BoinConfig | None = None) -> None:
        self.config = config or BoinConfig()
        self.lambda_e, self.lambda_d, self.phi_1, self.phi_2 = (
            calculate_boin_boundaries(
                self.config.target_toxicity_rate,
                self.config.factor_e,
                self.config.factor_d,
            )
        )

    def evaluate_cohort(
        self,
        current_dose_index: int,
        total_doses: int,
        dose_stats: dict[int, dict[str, int]],
        eliminated_doses: set[int] | None = None,
    ) -> tuple[
        CohortRecommendationAction,
        int | None,
        list[DoseLevelEvaluationSummary],
        str,
        bool,
    ]:
        """Evaluates trial dose levels, performs Bayesian overdose checks, and emits BOIN recommendation."""
        eliminated = set(eliminated_doses) if eliminated_doses else set()
        summaries: list[DoseLevelEvaluationSummary] = []
        is_safety_stop = False

        # 1. Update Bayesian overdose probabilities and check elimination for all evaluated doses
        for dose_idx in range(1, total_doses + 1):
            stats = dose_stats.get(dose_idx, {"enrolled": 0, "dlts": 0})
            n_j = stats["enrolled"]
            y_j = stats["dlts"]
            p_obs = y_j / n_j if n_j > 0 else 0.0

            post_alpha = self.config.prior_alpha + y_j
            post_beta = self.config.prior_beta + (n_j - y_j)
            post_mean = post_alpha / (post_alpha + post_beta)
            overdose_p = calculate_beta_overdose_prob(
                post_alpha, post_beta, self.config.target_toxicity_rate
            )

            # Check if dose meets Bayesian overdose elimination rule
            if (
                n_j >= self.config.min_subjects_for_elimination
                and overdose_p > self.config.safety_cutoff
            ):
                # Eliminate this dose and all higher doses
                for k in range(dose_idx, total_doses + 1):
                    eliminated.add(k)

            is_elim = dose_idx in eliminated

            summaries.append(
                DoseLevelEvaluationSummary(
                    dose_level_index=dose_idx,
                    dose_name=f"Dose {dose_idx}",
                    dose_value=float(dose_idx),
                    total_enrolled=n_j,
                    total_dlts=y_j,
                    observed_dlt_rate=round(p_obs, 4),
                    posterior_dlt_rate=round(post_mean, 4),
                    is_eliminated_for_safety=is_elim,
                    overdose_probability=round(overdose_p, 4),
                )
            )

        # Check if lowest dose level 1 is eliminated -> Full trial safety halt
        if 1 in eliminated:
            is_safety_stop = True
            return (
                CohortRecommendationAction.STOP_SAFETY,
                None,
                summaries,
                f"Lowest dose level 1 has been eliminated due to excessive toxicity (overdose probability > {self.config.safety_cutoff}). Trial must stop for patient safety.",
                is_safety_stop,
            )

        # 2. Evaluate current dose level under BOIN decision rules
        curr_stats = dose_stats.get(current_dose_index, {"enrolled": 0, "dlts": 0})
        n_curr = curr_stats["enrolled"]
        y_curr = curr_stats["dlts"]
        p_hat = y_curr / n_curr if n_curr > 0 else 0.0

        # If current dose was just eliminated
        if current_dose_index in eliminated:
            # Must de-escalate to highest non-eliminated dose below current
            rec_dose = current_dose_index - 1
            while rec_dose > 0 and rec_dose in eliminated:
                rec_dose -= 1
            if rec_dose <= 0:
                return (
                    CohortRecommendationAction.STOP_SAFETY,
                    None,
                    summaries,
                    "All dose levels eliminated for toxicity. Stop trial for safety.",
                    True,
                )
            return (
                CohortRecommendationAction.DE_ESCALATE,
                rec_dose,
                summaries,
                f"Current dose {current_dose_index} eliminated for safety. De-escalate to dose level {rec_dose}.",
                False,
            )

        # BOIN Standard Interval Comparison
        if p_hat <= self.lambda_e:
            # Toxicity is below lower threshold: Escalate
            if (
                current_dose_index < total_doses
                and (current_dose_index + 1) not in eliminated
            ):
                rec_dose = current_dose_index + 1
                action = CohortRecommendationAction.ESCALATE
                rationale = (
                    f"Observed DLT rate {p_hat:.3f} <= lambda_e ({self.lambda_e}). "
                    f"Recommend ESCALATE to dose level {rec_dose}."
                )
            else:
                rec_dose = current_dose_index
                action = CohortRecommendationAction.STAY
                rationale = (
                    f"Observed DLT rate {p_hat:.3f} <= lambda_e ({self.lambda_e}), "
                    f"but at maximum safe dose level. Recommend STAY at dose {rec_dose}."
                )
        elif p_hat >= self.lambda_d:
            # Toxicity is above upper threshold: De-escalate
            if current_dose_index > 1:
                rec_dose = current_dose_index - 1
                action = CohortRecommendationAction.DE_ESCALATE
                rationale = (
                    f"Observed DLT rate {p_hat:.3f} >= lambda_d ({self.lambda_d}). "
                    f"Recommend DE_ESCALATE to dose level {rec_dose}."
                )
            else:
                rec_dose = 1
                action = CohortRecommendationAction.STAY
                rationale = (
                    f"Observed DLT rate {p_hat:.3f} >= lambda_d ({self.lambda_d}), "
                    f"at lowest dose level. Recommend STAY or safety review."
                )
        else:
            # In interval (lambda_e, lambda_d): Stay
            rec_dose = current_dose_index
            action = CohortRecommendationAction.STAY
            rationale = (
                f"Observed DLT rate {p_hat:.3f} is within optimal interval "
                f"({self.lambda_e}, {self.lambda_d}). Recommend STAY at dose {rec_dose}."
            )

        return action, rec_dose, summaries, rationale, is_safety_stop

    def select_mtd(
        self,
        total_doses: int,
        dose_stats: dict[int, dict[str, int]],
        eliminated_doses: set[int] | None = None,
    ) -> tuple[int | None, str]:
        """Selects Maximum Tolerated Dose (MTD) among non-eliminated doses minimizing distance to target phi."""
        eliminated = set(eliminated_doses) if eliminated_doses else set()
        best_dose: int | None = None
        min_diff = float("inf")

        for dose_idx in range(1, total_doses + 1):
            if dose_idx in eliminated:
                continue
            stats = dose_stats.get(dose_idx, {"enrolled": 0, "dlts": 0})
            if stats["enrolled"] == 0:
                continue
            p_hat = stats["dlts"] / stats["enrolled"]
            diff = abs(p_hat - self.config.target_toxicity_rate)
            if diff < min_diff:
                min_diff = diff
                best_dose = dose_idx

        if best_dose is None:
            return None, "No safe dose level with evaluated subjects found."

        return (
            best_dose,
            f"Dose level {best_dose} declared MTD with estimated DLT rate closest to target {self.config.target_toxicity_rate}.",
        )


# =========================================================================
# 3. Continual Reassessment Method (CRM / Modified CRM) Engine
# =========================================================================


class CrmDoseEscalationEngine:
    """Continual Reassessment Method (CRM) engine with Bayesian posterior numerical integration."""

    def __init__(self, config: CrmConfig) -> None:
        self.config = config

    def calculate_posterior(
        self,
        dose_stats: dict[int, dict[str, int]],
    ) -> tuple[list[float], float, float]:
        """Calculates Bayesian posterior mean toxicity probabilities for all dose levels.

        Empiric power model: p_j(theta) = p_{0,j}^{exp(theta)}
        Prior on theta: Normal(0, sigma^2) where sigma = config.prior_std.
        """
        skeleton = self.config.skeleton_probs
        num_doses = len(skeleton)
        sigma = self.config.prior_std

        # Integration grid over [-6*sigma, 6*sigma]
        n_points = 1000
        theta_min = -6.0 * sigma
        theta_max = 6.0 * sigma
        h = (theta_max - theta_min) / n_points

        grid_theta = [theta_min + i * h for i in range(n_points + 1)]

        # Precompute log-likelihood + log-prior
        log_unnorm_weights = []
        for theta in grid_theta:
            exp_theta = math.exp(theta)
            # Log Prior
            log_prior = -0.5 * (theta / sigma) ** 2 - math.log(
                sigma * math.sqrt(2.0 * math.pi)
            )

            # Log Likelihood
            log_lik = 0.0
            for j in range(1, num_doses + 1):
                stats = dose_stats.get(j, {"enrolled": 0, "dlts": 0})
                n_j = stats["enrolled"]
                y_j = stats["dlts"]
                if n_j > 0:
                    p_j_theta = skeleton[j - 1] ** exp_theta
                    p_j_theta = max(min(p_j_theta, 1.0 - 1e-15), 1e-15)
                    log_lik += y_j * math.log(p_j_theta) + (n_j - y_j) * math.log(
                        1.0 - p_j_theta
                    )

            log_unnorm_weights.append(log_prior + log_lik)

        # Numerical stability via log-sum-exp
        max_log_w = max(log_unnorm_weights)
        unnorm_weights = [math.exp(lw - max_log_w) for lw in log_unnorm_weights]

        # Simpson integration for denominator (Normalizing constant)
        simpson_sum_denom = 0.0
        for i, w in enumerate(unnorm_weights):
            mult = 1.0 if (i == 0 or i == n_points) else (4.0 if i % 2 == 1 else 2.0)
            simpson_sum_denom += mult * w
        denom = (h / 3.0) * simpson_sum_denom

        if denom <= 0.0:
            # Fallback to prior skeleton if degenerate
            return list(skeleton), 0.0, 0.0

        # Posterior mean toxicity probability per dose
        posterior_means = []
        for j in range(1, num_doses + 1):
            p0_j = skeleton[j - 1]
            simpson_sum_num = 0.0
            for i, (theta, w) in enumerate(
                zip(grid_theta, unnorm_weights, strict=True)
            ):
                exp_theta = math.exp(theta)
                p_j_theta = p0_j**exp_theta
                mult = (
                    1.0 if (i == 0 or i == n_points) else (4.0 if i % 2 == 1 else 2.0)
                )
                simpson_sum_num += mult * w * p_j_theta
            num = (h / 3.0) * simpson_sum_num
            p_mean = num / denom
            posterior_means.append(round(min(max(p_mean, 0.0), 1.0), 4))

        # Posterior overdose probability on Dose 1: P(p_1(theta) > phi | D)
        # p_1(theta) > phi <=> p_{0,1}^{exp(theta)} > phi <=> exp(theta) < ln(phi)/ln(p_{0,1})
        phi = self.config.target_toxicity_rate
        p0_1 = skeleton[0]
        simpson_sum_overdose1 = 0.0
        for i, (theta, w) in enumerate(zip(grid_theta, unnorm_weights, strict=True)):
            exp_theta = math.exp(theta)
            p1_theta = p0_1**exp_theta
            if p1_theta > phi:
                mult = (
                    1.0 if (i == 0 or i == n_points) else (4.0 if i % 2 == 1 else 2.0)
                )
                simpson_sum_overdose1 += mult * w

        overdose_prob_dose_1 = ((h / 3.0) * simpson_sum_overdose1) / denom
        overdose_prob_dose_1 = round(min(max(overdose_prob_dose_1, 0.0), 1.0), 4)

        # Posterior mean of theta
        simpson_sum_theta = 0.0
        for i, (theta, w) in enumerate(zip(grid_theta, unnorm_weights, strict=True)):
            mult = 1.0 if (i == 0 or i == n_points) else (4.0 if i % 2 == 1 else 2.0)
            simpson_sum_theta += mult * w * theta
        theta_hat = ((h / 3.0) * simpson_sum_theta) / denom

        return posterior_means, overdose_prob_dose_1, round(theta_hat, 4)

    def evaluate_cohort(
        self,
        current_dose_index: int,
        dose_stats: dict[int, dict[str, int]],
    ) -> tuple[CohortRecommendationAction, int | None, list[float], float, str, bool]:
        """Emits modified CRM recommendation with no-skipping escalation and safety halting."""
        posterior_means, overdose_p_1, _ = self.calculate_posterior(dose_stats)
        phi = self.config.target_toxicity_rate

        # 1. Safety stopping rule: Excessive toxicity at dose level 1
        if overdose_p_1 > self.config.safety_cutoff:
            return (
                CohortRecommendationAction.STOP_SAFETY,
                None,
                posterior_means,
                overdose_p_1,
                f"Overdose probability at lowest dose level 1 is {overdose_p_1:.3f} > {self.config.safety_cutoff}. Safety stop triggered.",
                True,
            )

        # 2. Optimal dose selection: find j* that minimizes |p_j - phi|
        best_dose_idx = 1
        min_diff = float("inf")
        for j, p_j in enumerate(posterior_means, start=1):
            diff = abs(p_j - phi)
            if diff < min_diff:
                min_diff = diff
                best_dose_idx = j

        # 3. Modified CRM escalation constraint: do not skip untried higher doses
        rec_dose = best_dose_idx
        if not self.config.allow_dose_skipping:
            if rec_dose > current_dose_index + 1:
                rec_dose = current_dose_index + 1

        # 4. Formulate recommendation action
        if rec_dose > current_dose_index:
            action = CohortRecommendationAction.ESCALATE
            rationale = (
                f"Model posterior estimate for dose {rec_dose} ({posterior_means[rec_dose - 1]:.3f}) is closest to target {phi}. "
                f"Recommend ESCALATE to dose level {rec_dose}."
            )
        elif rec_dose < current_dose_index:
            action = CohortRecommendationAction.DE_ESCALATE
            rationale = (
                f"Current dose {current_dose_index} has posterior toxicity {posterior_means[current_dose_index - 1]:.3f} > target {phi}. "
                f"Recommend DE_ESCALATE to dose level {rec_dose}."
            )
        else:
            action = CohortRecommendationAction.STAY
            rationale = (
                f"Current dose {current_dose_index} remains closest to target toxicity {phi} "
                f"(posterior {posterior_means[current_dose_index - 1]:.3f}). Recommend STAY at dose {rec_dose}."
            )

        return action, rec_dose, posterior_means, overdose_p_1, rationale, False


# =========================================================================
# 4. Response-Adaptive Randomization (RAR) Engine
# =========================================================================


class ResponseAdaptiveRandomizationEngine:
    """Response-Adaptive Randomization (RAR) engine with dynamic probability updating,

    arm dropping (futility), arm graduation (efficacy), and zero-downtime arm addition.
    """

    def __init__(self, config: RarConfig | None = None) -> None:
        self.config = config or RarConfig()

    def update_probabilities(
        self,
        arms_data: list[dict[str, Any]],
    ) -> tuple[dict[str, float], list[str], list[str], str]:
        """Calculates dynamic allocation probabilities across active treatment arms.

        Each item in arms_data:
            {
                "arm_id": str,
                "is_control": bool,
                "status": RARArmStatus,
                "enrolled_count": int,
                "response_count": int,
                "prior_alpha": float,
                "prior_beta": float,
            }
        """
        # Find control arm
        control_arm = next((a for a in arms_data if a.get("is_control")), None)

        dropped_arms = []
        graduated_arms = []
        active_arms = []

        # 1. Update posterior Beta parameters and check futility/efficacy rules
        for arm in arms_data:
            status = arm.get("status", RARArmStatus.ACTIVE)
            if status in (RARArmStatus.DROPPED_FUTILITY, RARArmStatus.TERMINATED):
                continue

            n_k = arm.get("enrolled_count", 0)
            s_k = arm.get("response_count", 0)
            f_k = n_k - s_k

            alpha_0 = arm.get("prior_alpha", self.config.prior_alpha)
            beta_0 = arm.get("prior_beta", self.config.prior_beta)

            post_alpha = alpha_0 + s_k
            post_beta = beta_0 + f_k
            post_mean = post_alpha / (post_alpha + post_beta)

            arm["posterior_alpha"] = post_alpha
            arm["posterior_beta"] = post_beta
            arm["posterior_mean"] = round(post_mean, 4)

            # Superiority probability vs control
            p_superior: float | None = None
            if control_arm and not arm.get("is_control"):
                ctrl_alpha = control_arm.get(
                    "prior_alpha", self.config.prior_alpha
                ) + control_arm.get("response_count", 0)
                ctrl_beta = control_arm.get("prior_beta", self.config.prior_beta) + (
                    control_arm.get("enrolled_count", 0)
                    - control_arm.get("response_count", 0)
                )

                # Compute P(Theta_k > Theta_ctrl) via numerical integration
                # int_0^1 BetaPDF(x, alpha_k, beta_k) * BetaCDF(x, ctrl_alpha, ctrl_beta) dx
                p_superior = self._compute_superiority_probability(
                    post_alpha, post_beta, ctrl_alpha, ctrl_beta
                )
                arm["superiority_vs_control_prob"] = round(p_superior, 4)

                # Check futility and efficacy thresholds (after burn-in sample reached)
                if n_k >= self.config.burn_in_sample_per_arm:
                    if p_superior < self.config.futility_threshold:
                        arm["status"] = RARArmStatus.DROPPED_FUTILITY
                        dropped_arms.append(arm["arm_id"])
                        continue
                    if p_superior > self.config.efficacy_threshold:
                        arm["status"] = RARArmStatus.GRADUATED_SUCCESS
                        graduated_arms.append(arm["arm_id"])

            active_arms.append(arm)

        if not active_arms:
            return (
                {},
                dropped_arms,
                graduated_arms,
                "No active treatment arms remaining.",
            )

        # 2. Compute allocation weights for active arms
        # Thall & Wathen / Thompson weighting: w_k ~ (post_mean)^c or (q_k / (n_k + 1))^c
        c = self.config.power_tuning_param
        raw_weights: dict[str, float] = {}

        for arm in active_arms:
            arm_id = arm["arm_id"]
            n_k = arm.get("enrolled_count", 0)

            if n_k < self.config.burn_in_sample_per_arm:
                # During burn-in, use uniform weight
                raw_weights[arm_id] = 1.0
            else:
                score = arm["posterior_mean"]
                raw_weights[arm_id] = (score**c) if score > 0 else 0.01

        sum_raw = sum(raw_weights.values())
        if sum_raw <= 0:
            sum_raw = 1.0
            for k in raw_weights:
                raw_weights[k] = 1.0 / len(raw_weights)

        # 3. Apply min/max bounding and re-normalize dynamically
        min_p = self.config.min_allocation_prob
        max_p = self.config.max_allocation_prob

        # First pass normalization
        probs = {
            arm_id: max(min(raw_weights[arm_id] / sum_raw, max_p), min_p)
            for arm_id in raw_weights
        }
        # Re-normalize to exactly 1.0
        total_p = sum(probs.values())
        final_probs = {arm_id: round(p / total_p, 4) for arm_id, p in probs.items()}

        # Correct small rounding delta
        diff = round(1.0 - sum(final_probs.values()), 4)
        if diff != 0 and active_arms:
            first_arm = active_arms[0]["arm_id"]
            final_probs[first_arm] = round(final_probs[first_arm] + diff, 4)

        for arm in arms_data:
            arm_id = arm["arm_id"]
            arm["allocation_probability"] = final_probs.get(arm_id, 0.0)

        rationale = (
            f"Updated allocation probabilities across {len(active_arms)} active arms. "
            f"Dropped {len(dropped_arms)} futility arms; graduated {len(graduated_arms)} efficacy arms."
        )

        return final_probs, dropped_arms, graduated_arms, rationale

    def _compute_superiority_probability(
        self,
        alpha_k: float,
        beta_k: float,
        alpha_ctrl: float,
        beta_ctrl: float,
    ) -> float:
        """Computes P(Theta_k > Theta_ctrl) for Theta_k ~ Beta(a_k, b_k), Theta_ctrl ~ Beta(a_c, b_c)."""
        n_steps = 500
        h = 1.0 / n_steps
        simpson_sum = 0.0

        for i in range(n_steps + 1):
            x = i * h
            if x <= 0.0 or x >= 1.0:
                val = 0.0
            else:
                pdf_k = beta_pdf(x, alpha_k, beta_k)
                cdf_ctrl = beta_cdf(x, alpha_ctrl, beta_ctrl)
                val = pdf_k * cdf_ctrl

            mult = 1.0 if (i == 0 or i == n_steps) else (4.0 if i % 2 == 1 else 2.0)
            simpson_sum += mult * val

        res = (h / 3.0) * simpson_sum
        return min(max(res, 0.0), 1.0)
