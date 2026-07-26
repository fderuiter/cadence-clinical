import pytest

from apps.execution.rtsm import (
    InvalidRandomizationConfigError,
    MissingStratificationFactorError,
    RTSMAllocationEngine,
    generate_canonical_stratum_key,
)


def test_generate_canonical_stratum_key_stability():
    """Verify that canonical stratum keys are stable and sorted alphabetically regardless of input order."""
    factors = ["gender", "age_group"]
    covariates1 = {"gender": "M", "age_group": "GE_65"}
    covariates2 = {"age_group": "GE_65", "gender": "M"}

    key1 = generate_canonical_stratum_key(factors, covariates1)
    key2 = generate_canonical_stratum_key(factors, covariates2)

    assert key1 == "age_group=GE_65&gender=M"
    assert key1 == key2

    # Verify that passing dictionary configuration is supported
    factors_dict = {"gender": ["M", "F"], "age_group": ["<65", "GE_65"]}
    key3 = generate_canonical_stratum_key(factors_dict, covariates1)
    assert key3 == "age_group=GE_65&gender=M"


def test_generate_canonical_stratum_key_empty_factors():
    """Verify empty/null stratification factors produce the default stratum key."""
    assert generate_canonical_stratum_key(None, {"gender": "M"}) == "DEFAULT"
    assert generate_canonical_stratum_key([], {"gender": "M"}) == "DEFAULT"
    assert generate_canonical_stratum_key({}, {"gender": "M"}) == "DEFAULT"


def test_generate_canonical_stratum_key_missing_or_blank_factor():
    """Verify missing or blank stratification factors trigger ValueError/MissingStratificationFactorError."""
    factors = ["gender", "age_group"]

    # Missing factor
    with pytest.raises(
        MissingStratificationFactorError, match="Missing required stratification factor"
    ):
        generate_canonical_stratum_key(factors, {"gender": "M"})

    # Null value
    with pytest.raises(
        MissingStratificationFactorError, match="Missing required stratification factor"
    ):
        generate_canonical_stratum_key(factors, {"gender": "M", "age_group": None})

    # Empty string
    with pytest.raises(
        MissingStratificationFactorError, match="cannot be empty or blank"
    ):
        generate_canonical_stratum_key(factors, {"gender": "M", "age_group": "   "})


def test_permuted_block_strategy_valid_allocations():
    """Verify block strategy generates block correctly, shuffles, and sequential state indexes increment."""
    arms_ratios = {"Arm A": 1, "Arm B": 1}
    covariates = {}

    # Run twice with same seed to ensure reproducibility
    arm1, state1 = RTSMAllocationEngine.allocate(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios=arms_ratios,
        covariates=covariates,
        state_data=None,
        seed=12345,
    )

    assert arm1 in arms_ratios
    assert state1 is not None
    assert len(state1["sequence"]) == 2
    assert state1["index"] == 1

    # Allocate the second item in the block
    arm2, state2 = RTSMAllocationEngine.allocate(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios=arms_ratios,
        covariates=covariates,
        state_data=state1,
        seed=12345,
    )
    assert arm2 in arms_ratios
    assert state2["index"] == 2
    assert {arm1, arm2} == {
        "Arm A",
        "Arm B",
    }  # Block size 2 should contain exactly one of each

    # Allocating the third item should generate a new block
    arm3, state3 = RTSMAllocationEngine.allocate(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios=arms_ratios,
        covariates=covariates,
        state_data=state2,
        seed=12345,
    )
    assert arm3 in arms_ratios
    assert len(state3["sequence"]) == 2
    assert state3["index"] == 1


def test_permuted_block_strategy_uneven_ratios_and_block_size_multiples():
    """Verify that block allocation respects uneven ratios and validates block sizes."""
    arms_ratios = {"Arm A": 1, "Arm B": 2}
    covariates = {}

    # Invalid block size (not a multiple of sum of ratios: 3)
    with pytest.raises(
        InvalidRandomizationConfigError, match="must be a multiple of the sum of ratios"
    ):
        RTSMAllocationEngine.allocate(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios=arms_ratios,
            covariates=covariates,
            state_data=None,
            block_sizes=[4],
        )

    # Valid block size 6
    _, state = RTSMAllocationEngine.allocate(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios=arms_ratios,
        covariates=covariates,
        state_data=None,
        block_sizes=[6],
        seed=42,
    )
    assert len(state["sequence"]) == 6
    # Should have exactly 2 'Arm A' and 4 'Arm B'
    assert state["sequence"].count("Arm A") == 2
    assert state["sequence"].count("Arm B") == 4


def test_stratified_block_strategy_requires_stratification_factors():
    """Verify stratified block strategy enforces stratification factors configuration."""
    arms_ratios = {"Arm A": 1, "Arm B": 1}
    covariates = {"gender": "F"}

    with pytest.raises(
        InvalidRandomizationConfigError, match="stratification_factors are required"
    ):
        RTSMAllocationEngine.allocate(
            algorithm_type="STRATIFIED_BLOCK",
            arms_ratios=arms_ratios,
            covariates=covariates,
            stratification_factors=None,
        )


def test_minimization_strategy_first_subject():
    """Verify minimization handles first subject correctly by randomizing equally."""
    arms_ratios = {"Arm A": 1, "Arm B": 1}
    stratification_factors = ["gender"]
    covariates = {"gender": "F"}

    arm, state = RTSMAllocationEngine.allocate(
        algorithm_type="MINIMIZATION",
        arms_ratios=arms_ratios,
        covariates=covariates,
        stratification_factors=stratification_factors,
        previous_assignments=None,
        seed=100,
    )
    assert arm in arms_ratios
    assert state is None  # Minimization is state-less


def test_minimization_strategy_imbalance_evaluation_and_weighted_probability():
    """Verify minimization calculates imbalance across factors and applies weighted probability."""
    arms_ratios = {"Arm A": 1, "Arm B": 1}
    stratification_factors = ["gender", "age_group"]

    # Let's change previous assignments to force an imbalance.
    # Arm A: 3 females, 2 under-65
    # Arm B: 1 female, 1 under-65
    # New subject: female, under-65
    # If assign to A:
    #   gender: A:4, B:1 (disp 3); age: A:3, B:1 (disp 2); total = 5
    # If assign to B:
    #   gender: A:3, B:2 (disp 1); age: A:2, B:2 (disp 0); total = 1
    # Clearly, assigning to B minimizes the imbalance (1 < 5).
    # With probability p = 0.8, Arm B should have 80% chance and Arm A has 20%.

    previous_assignments_unbalanced = [
        {"assigned_arm": "Arm A", "covariates": {"gender": "F", "age_group": "<65"}},
        {"assigned_arm": "Arm A", "covariates": {"gender": "F", "age_group": "<65"}},
        {"assigned_arm": "Arm A", "covariates": {"gender": "F", "age_group": "GE_65"}},
        {"assigned_arm": "Arm B", "covariates": {"gender": "F", "age_group": "<65"}},
    ]
    new_covs = {"gender": "F", "age_group": "<65"}

    # Let's perform many allocations with fixed seed to verify the distribution of allocations
    results = []
    for i in range(1000):
        arm, _ = RTSMAllocationEngine.allocate(
            algorithm_type="MINIMIZATION",
            arms_ratios=arms_ratios,
            covariates=new_covs,
            stratification_factors=stratification_factors,
            previous_assignments=previous_assignments_unbalanced,
            seed=i,
            probability=0.8,
        )
        results.append(arm)

    b_count = results.count("Arm B")
    a_count = results.count("Arm A")
    # Expected: Arm B ~ 800, Arm A ~ 200
    assert 750 <= b_count <= 850
    assert 150 <= a_count <= 250


def test_minimization_strategy_uneven_ratios():
    """Verify minimization strategy handles uneven ratios correctly."""
    arms_ratios = {"Arm A": 1, "Arm B": 2}
    stratification_factors = ["gender"]

    # 2 females already assigned to Arm A, and 1 female assigned to Arm B
    previous_assignments = [
        {"assigned_arm": "Arm A", "covariates": {"gender": "F"}},
        {"assigned_arm": "Arm A", "covariates": {"gender": "F"}},
        {"assigned_arm": "Arm B", "covariates": {"gender": "F"}},
    ]

    # New subject is Female
    # If assign to A:
    #   counts: A:3, B:1
    #   scaled counts: A:3/1=3, B:1/2=0.5
    #   dispersion = 3 - 0.5 = 2.5
    # If assign to B:
    #   counts: A:2, B:2
    #   scaled counts: A:2/1=2, B:2/2=1
    #   dispersion = 2 - 1 = 1
    # B minimizes imbalance, so B is preferred with prob 0.8

    results = []
    for i in range(500):
        arm, _ = RTSMAllocationEngine.allocate(
            algorithm_type="MINIMIZATION",
            arms_ratios=arms_ratios,
            covariates={"gender": "F"},
            stratification_factors=stratification_factors,
            previous_assignments=previous_assignments,
            seed=i,
            probability=0.9,
        )
        results.append(arm)

    assert results.count("Arm B") > results.count("Arm A")


def test_rtsm_reproducibility_boundaries():
    """Verify GxP reproducibility boundary limits by ensuring identical random sequence under identical seeds."""
    arms_ratios = {"Arm A": 1, "Arm B": 1}
    covariates = {}

    # Permuted block sequence with seed 9999
    seq1 = []
    state = None
    for _ in range(10):
        arm, state = RTSMAllocationEngine.allocate(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios=arms_ratios,
            covariates=covariates,
            state_data=state,
            seed=9999,
        )
        seq1.append(arm)

    # Same with seed 9999 again
    seq2 = []
    state = None
    for _ in range(10):
        arm, state = RTSMAllocationEngine.allocate(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios=arms_ratios,
            covariates=covariates,
            state_data=state,
            seed=9999,
        )
        seq2.append(arm)

    assert seq1 == seq2


def test_invalid_configurations_and_errors():
    """Verify proper errors are raised for invalid randomization configurations."""
    # Invalid algorithm type
    with pytest.raises(
        InvalidRandomizationConfigError, match="Unsupported or invalid algorithm_type"
    ):
        RTSMAllocationEngine.get_strategy("UNKNOWN_ALGO")

    # Empty ratios
    with pytest.raises(
        InvalidRandomizationConfigError,
        match="arms_ratios must be a non-empty dictionary",
    ):
        RTSMAllocationEngine.allocate(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={},
            covariates={},
        )

    # Invalid probability for minimization
    with pytest.raises(
        InvalidRandomizationConfigError, match="probability weight must be a float"
    ):
        RTSMAllocationEngine.allocate(
            algorithm_type="MINIMIZATION",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            covariates={"gender": "M"},
            stratification_factors=["gender"],
            probability=2.0,
        )
