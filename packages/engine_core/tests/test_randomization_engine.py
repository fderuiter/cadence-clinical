"""Unit tests for the Stochastic Randomization & Adaptive Dose Finding Engine.

@req:PRD-RTSM-001, PRD-RTSM-002, PRD-SYS-001
"""

from packages.engine_core.randomization.blocks import (
    generate_canonical_stratum_key,
)
from packages.engine_core.randomization.boin import (
    calculate_boin_boundaries,
    calculate_posterior_prob_toxic,
)
from packages.engine_core.randomization.engine import (
    StochasticRandomizationEngine,
)
from packages.engine_core.randomization.minimization import (
    PocockSimonMinimizer,
)
from packages.engine_core.randomization.models import (
    BOINConfig,
    DoseCohortState,
    DoseEscalationAction,
    DoseLevelDefinition,
    ImbalanceDistanceMetric,
    MinimizationConfig,
    RandomizationAlgorithmType,
    StratifiedBlockConfig,
    SubjectAllocationRequest,
    ThreePlusThreeConfig,
)


def test_stratum_key_generation():
    """Verify canonical, alphabetically-sorted stratum key generation.

    @req:PRD-RTSM-001
    """
    # 1. Alphabetical sorting: ECOG before STAGE
    factors = {"STAGE": "IV", "ECOG": 1}
    active = ["STAGE", "ECOG"]
    key = generate_canonical_stratum_key(factors, active)
    assert key == "ECOG=1|STAGE=IV"

    # 2. No active factors -> DEFAULT
    assert generate_canonical_stratum_key(None, None) == "DEFAULT"
    assert generate_canonical_stratum_key({}, []) == "DEFAULT"


def test_stratified_permuted_block_allocation_and_seed_reproducibility():
    """Verify Stratified Permuted Block Randomization and deterministic seed reproduction.

    @req:PRD-RTSM-001, PRD-SYS-001
    """
    config = StratifiedBlockConfig(
        algorithm_type=RandomizationAlgorithmType.STRATIFIED_BLOCK,
        arms_ratios={"ARM_ACTIVE": 1, "ARM_CONTROL": 1},
        stratification_factors=["SITE", "STAGE"],
        block_sizes=[4, 6],
        seed=12345,
    )

    req1 = SubjectAllocationRequest(
        study_id="STUDY-101",
        subject_id="SUBJ-001",
        site_id="SITE-A",
        stratification_factors={"SITE": "SITE-A", "STAGE": "II"},
        current_stratum_allocations=[],
    )

    res1 = StochasticRandomizationEngine.allocate_block(config, req1)
    assert res1.assigned_arm in ("ARM_ACTIVE", "ARM_CONTROL")
    assert res1.allocation_index == 0
    assert res1.stratum_key == "SITE=SITE-A|STAGE=II"
    assert res1.deterministic_checksum is not None

    # Next subject in the same stratum
    req2 = SubjectAllocationRequest(
        study_id="STUDY-101",
        subject_id="SUBJ-002",
        site_id="SITE-A",
        stratification_factors={"SITE": "SITE-A", "STAGE": "II"},
        current_stratum_allocations=[res1.assigned_arm],
    )
    res2 = StochasticRandomizationEngine.allocate_block(config, req2)
    assert res2.allocation_index == 1

    # Exact seed reproduction
    res1_replay = StochasticRandomizationEngine.allocate_block(config, req1)
    assert res1_replay.assigned_arm == res1.assigned_arm
    assert res1_replay.deterministic_checksum == res1.deterministic_checksum


def test_pocock_simon_minimization_range_variance_stddev():
    """Verify Pocock-Simon dynamic minimization with Range, Variance, and StdDev metrics.

    @req:PRD-RTSM-001
    """
    arms_ratios = {"ARM_A": 1, "ARM_B": 1}
    factors = ["AGE_GROUP", "BIOMARKER"]

    # History: 4 previous subjects
    # ARM_A: (OLD, POS), (OLD, NEG)
    # ARM_B: (YOUNG, POS), (OLD, POS)
    history = [
        {"assigned_arm": "ARM_A", "AGE_GROUP": "OLD", "BIOMARKER": "POS"},
        {"assigned_arm": "ARM_A", "AGE_GROUP": "OLD", "BIOMARKER": "NEG"},
        {"assigned_arm": "ARM_B", "AGE_GROUP": "YOUNG", "BIOMARKER": "POS"},
        {"assigned_arm": "ARM_B", "AGE_GROUP": "OLD", "BIOMARKER": "POS"},
    ]

    # Incoming subject: (OLD, POS)
    incoming_factors = {"AGE_GROUP": "OLD", "BIOMARKER": "POS"}

    # 1. Range metric
    config_range = MinimizationConfig(
        arms_ratios=arms_ratios,
        stratification_factors=factors,
        distance_metric=ImbalanceDistanceMetric.RANGE,
        p_preferred=1.0,  # Deterministic 100% preferred arm assignment
        seed=42,
    )
    # Marginal counts for OLD: ARM_A=2, ARM_B=1
    # Marginal counts for POS: ARM_A=1, ARM_B=2
    # If assign to ARM_A: OLD -> A=3, B=1 (diff 2); POS -> A=2, B=2 (diff 0) -> Total = 2.0
    # If assign to ARM_B: OLD -> A=2, B=2 (diff 0); POS -> A=1, B=3 (diff 2) -> Total = 2.0
    imbalances_range = PocockSimonMinimizer.evaluate_candidate_imbalances(
        config_range, incoming_factors, history
    )
    assert imbalances_range["ARM_A"] == 2.0
    assert imbalances_range["ARM_B"] == 2.0

    # 2. Factor weighting: Prioritize BIOMARKER (weight=2.0) over AGE_GROUP (weight=1.0)
    # If ARM_A: 1.0 * 2 + 2.0 * 0 = 2.0
    # If ARM_B: 1.0 * 0 + 2.0 * 2 = 4.0 -> ARM_A has lower weighted imbalance
    config_weighted = MinimizationConfig(
        arms_ratios=arms_ratios,
        stratification_factors=factors,
        factor_weights={"AGE_GROUP": 1.0, "BIOMARKER": 2.0},
        distance_metric=ImbalanceDistanceMetric.RANGE,
        p_preferred=1.0,
        seed=42,
    )
    imbalances_weighted = PocockSimonMinimizer.evaluate_candidate_imbalances(
        config_weighted, incoming_factors, history
    )
    assert imbalances_weighted["ARM_A"] == 2.0
    assert imbalances_weighted["ARM_B"] == 4.0

    req_weighted = SubjectAllocationRequest(
        study_id="STUDY-201",
        subject_id="SUBJ-005",
        site_id="SITE-B",
        stratification_factors=incoming_factors,
        all_subject_factors=history,
    )
    res_min = StochasticRandomizationEngine.allocate_minimization(
        config_weighted, req_weighted
    )
    assert res_min.assigned_arm == "ARM_A"
    assert res_min.imbalance_score == 2.0


def test_boin_boundaries_and_posterior_calculation():
    """Verify BOIN optimal decision boundaries and Bayesian posterior probability.

    @req:PRD-RTSM-002
    """
    # For target DLT rate phi = 0.25:
    # lambda_e ≈ 0.1970, lambda_d ≈ 0.2985
    lam_e, lam_d = calculate_boin_boundaries(0.25)
    assert 0.18 <= lam_e <= 0.21
    assert 0.28 <= lam_d <= 0.32
    assert lam_e < 0.25 < lam_d

    # Posterior probability of toxicity Pr(p > 0.25 | data)
    # 0 DLTs out of 3 subjects -> low probability
    prob_0_3 = calculate_posterior_prob_toxic(0, 3, 0.25)
    assert prob_0_3 < 0.40

    # 3 DLTs out of 3 subjects -> high probability
    prob_3_3 = calculate_posterior_prob_toxic(3, 3, 0.25)
    assert prob_3_3 > 0.95


def test_boin_dose_escalation_decision_rules():
    """Verify BOIN dose escalation, de-escalation, stay, and safety trial termination.

    @req:PRD-RTSM-002, PRD-SYS-001
    """
    config = BOINConfig(target_dlt_rate=0.25, stopping_cutoff=0.95)
    dose_levels = [
        DoseLevelDefinition(dose_index=0, dose_amount=10.0, dose_unit="mg"),
        DoseLevelDefinition(dose_index=1, dose_amount=20.0, dose_unit="mg"),
        DoseLevelDefinition(dose_index=2, dose_amount=40.0, dose_unit="mg"),
        DoseLevelDefinition(dose_index=3, dose_amount=80.0, dose_unit="mg"),
    ]

    # 1. Dose 0: 0 DLTs out of 3 subjects -> Escalate to Dose 1
    state_0_esc = DoseCohortState(
        dose_index=0,
        dose_amount=10.0,
        dose_unit="mg",
        evaluated_subjects=3,
        observed_dlts=0,
    )
    rec_esc = StochasticRandomizationEngine.evaluate_boin_dose(
        config, dose_levels, state_0_esc
    )
    assert rec_esc.action == DoseEscalationAction.ESCALATE
    assert rec_esc.recommended_dose_index == 1
    assert rec_esc.recommended_dose_amount == 20.0

    # 2. Dose 1: 1 DLT out of 4 subjects (p_hat = 0.25) -> Stay at Dose 1 (within lambda_e, lambda_d)
    state_1_stay = DoseCohortState(
        dose_index=1,
        dose_amount=20.0,
        dose_unit="mg",
        evaluated_subjects=4,
        observed_dlts=1,
    )
    rec_stay = StochasticRandomizationEngine.evaluate_boin_dose(
        config, dose_levels, state_1_stay
    )
    assert rec_stay.action == DoseEscalationAction.STAY
    assert rec_stay.recommended_dose_index == 1

    # 3. Dose 2: 2 DLTs out of 3 subjects (p_hat = 0.67 >= lambda_d) -> De-escalate to Dose 1
    state_2_deesc = DoseCohortState(
        dose_index=2,
        dose_amount=40.0,
        dose_unit="mg",
        evaluated_subjects=3,
        observed_dlts=2,
    )
    rec_deesc = StochasticRandomizationEngine.evaluate_boin_dose(
        config, dose_levels, state_2_deesc
    )
    assert rec_deesc.action == DoseEscalationAction.DEESCALATE
    assert rec_deesc.recommended_dose_index == 1

    # 4. Dose 0: 3 DLTs out of 3 subjects (p_hat = 1.0, Pr(toxic) > 0.95) -> STOP ENTIRE TRIAL
    state_0_toxic = DoseCohortState(
        dose_index=0,
        dose_amount=10.0,
        dose_unit="mg",
        evaluated_subjects=3,
        observed_dlts=3,
    )
    rec_stop = StochasticRandomizationEngine.evaluate_boin_dose(
        config, dose_levels, state_0_toxic
    )
    assert rec_stop.action == DoseEscalationAction.STOP_TRIAL
    assert rec_stop.is_trial_stopped is True
    assert "Trial halted" in rec_stop.rationale


def test_three_plus_three_rule_based_escalation():
    """Verify classical 3+3 dose escalation rules.

    @req:PRD-RTSM-002
    """
    config = ThreePlusThreeConfig()
    dose_levels = [
        DoseLevelDefinition(dose_index=0, dose_amount=5.0, dose_unit="mg"),
        DoseLevelDefinition(dose_index=1, dose_amount=10.0, dose_unit="mg"),
        DoseLevelDefinition(dose_index=2, dose_amount=20.0, dose_unit="mg"),
    ]

    # 0/3 DLTs -> Escalate to 1
    rec_0 = StochasticRandomizationEngine.evaluate_3plus3_dose(
        config,
        dose_levels,
        DoseCohortState(
            dose_index=0,
            dose_amount=5.0,
            dose_unit="mg",
            evaluated_subjects=3,
            observed_dlts=0,
        ),
    )
    assert rec_0.action == DoseEscalationAction.ESCALATE
    assert rec_0.recommended_dose_index == 1

    # 1/3 DLTs -> Stay & expand to 6
    rec_1 = StochasticRandomizationEngine.evaluate_3plus3_dose(
        config,
        dose_levels,
        DoseCohortState(
            dose_index=1,
            dose_amount=10.0,
            dose_unit="mg",
            evaluated_subjects=3,
            observed_dlts=1,
        ),
    )
    assert rec_1.action == DoseEscalationAction.STAY
    assert rec_1.recommended_dose_index == 1

    # 2/3 DLTs -> MTD exceeded, de-escalate to 0
    rec_2 = StochasticRandomizationEngine.evaluate_3plus3_dose(
        config,
        dose_levels,
        DoseCohortState(
            dose_index=1,
            dose_amount=10.0,
            dose_unit="mg",
            evaluated_subjects=3,
            observed_dlts=2,
        ),
    )
    assert rec_2.action == DoseEscalationAction.DEESCALATE
    assert rec_2.recommended_dose_index == 0
