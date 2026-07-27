import pytest
import random

from apps.execution.rtsm import (
    InvalidRandomizationConfigError,
    MissingStratificationFactorError,
    RTSMAllocationEngine,
    generate_canonical_stratum_key,
    get_random_generator,
)


def test_get_random_generator():
    """Verify get_random_generator behaves as expected for both seeded and unseeded cases."""
    rng_unseeded = get_random_generator(None)
    assert isinstance(rng_unseeded, random.SystemRandom)

    rng_seeded = get_random_generator(42)
    assert isinstance(rng_seeded, random.Random)
    assert not isinstance(rng_seeded, random.SystemRandom)


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


def test_generate_canonical_stratum_key_invalid_factors_type():
    """Verify that invalid types for stratification_factors trigger InvalidRandomizationConfigError."""
    with pytest.raises(
        InvalidRandomizationConfigError, match="stratification_factors must be a list"
    ):
        generate_canonical_stratum_key(12345, {"gender": "M"})


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


def test_permuted_block_invalid_arm_name():
    """Verify input validation for non-empty arm names."""
    with pytest.raises(InvalidRandomizationConfigError, match="Arm names must be non-empty strings"):
        RTSMAllocationEngine.allocate(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"Arm A": 1, "": 2},
            covariates={},
        )


def test_permuted_block_invalid_ratio():
    """Verify input validation for arm ratio >= 1."""
    with pytest.raises(InvalidRandomizationConfigError, match="must be an integer >= 1"):
        RTSMAllocationEngine.allocate(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"Arm A": 1, "Arm B": 0},
            covariates={},
        )


def test_permuted_block_sizes_not_list():
    """Verify block_sizes is validated to be a list."""
    with pytest.raises(InvalidRandomizationConfigError, match="block_sizes must be a list"):
        RTSMAllocationEngine.allocate(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            covariates={},
            block_sizes="not-a-list",
        )


def test_permuted_block_size_zero():
    """Verify each block size must be positive."""
    with pytest.raises(InvalidRandomizationConfigError, match="Block sizes must be positive integers"):
        RTSMAllocationEngine.allocate(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            covariates={},
            block_sizes=[0],
        )


def test_permuted_block_defaulting_and_unseeded():
    """Verify block strategy defaulting behavior and unseeded generation."""
    arms_ratios = {"Arm A": 1, "Arm B": 1}
    # Test block sizes defaulting to sum of ratios
    arm, state = RTSMAllocationEngine.allocate(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios=arms_ratios,
        covariates={},
        block_sizes=None,
        seed=None,
    )
    assert arm in arms_ratios
    assert len(state["sequence"]) == 2


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


def test_minimization_invalid_stratification_factors_type():
    """Verify validation for stratification_factors type in MinimizationStrategy."""
    with pytest.raises(InvalidRandomizationConfigError, match="stratification_factors must be a list"):
        RTSMAllocationEngine.allocate(
            algorithm_type="MINIMIZATION",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            covariates={"gender": "M"},
            stratification_factors=12345,
        )


def test_minimization_missing_covariates():
    """Verify validation for missing factors in covariates for MinimizationStrategy."""
    with pytest.raises(MissingStratificationFactorError, match="Missing required stratification factor"):
        RTSMAllocationEngine.allocate(
            algorithm_type="MINIMIZATION",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            covariates={"age": "30"},
            stratification_factors=["gender"],
        )


def test_minimization_blank_covariates():
    """Verify validation for empty factors in covariates for MinimizationStrategy."""
    with pytest.raises(MissingStratificationFactorError, match="cannot be empty or blank"):
        RTSMAllocationEngine.allocate(
            algorithm_type="MINIMIZATION",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            covariates={"gender": "   "},
            stratification_factors=["gender"],
        )


def test_minimization_invalid_probability_weight():
    """Verify validation for probability weight in MinimizationStrategy."""
    with pytest.raises(InvalidRandomizationConfigError, match="probability weight must be a float"):
        RTSMAllocationEngine.allocate(
            algorithm_type="MINIMIZATION",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            covariates={"gender": "M"},
            stratification_factors=["gender"],
            probability=-0.5,
        )


def test_minimization_with_custom_weights_and_assignments():
    """Verify minimization with custom weights and various previous assignments structure."""
    arms_ratios = {"Arm A": 1, "Arm B": 1}
    stratification_factors = ["gender", "age_group"]
    previous_assignments = [
        {"assigned_arm": "Arm A", "covariates": None},  # Missing covariates dictionary
        {"assigned_arm": "Arm B", "covariates": {"gender": "F", "age_group": "<65"}},
    ]

    arm, _ = RTSMAllocationEngine.allocate(
        algorithm_type="MINIMIZATION",
        arms_ratios=arms_ratios,
        covariates={"gender": "F", "age_group": "<65"},
        stratification_factors=stratification_factors,
        previous_assignments=previous_assignments,
        factor_weights={"gender": 2, "age_group": 1},
        seed=12345,
    )
    assert arm in arms_ratios


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
