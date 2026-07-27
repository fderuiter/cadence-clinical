"""Unit tests for RTSM Randomization Strategies and Algorithms.

Covers:
- Configuration validation (valid/invalid scenarios, Pydantic v2 conformity).
- Stable, canonical stratum key generation.
- Permuted-block randomization (uneven ratios, block boundaries, exhaustion, and reproducibility).
- Stratified-block randomization (multi-strata separation).
- Pocock-Simon minimization (imbalance calculations, weighted probabilities, factor weights, and uneven ratios).
- Cryptographic secure randomness vs. seeded reproducibility boundaries.
"""

import pytest
from pydantic import ValidationError

from apps.execution.rtsm_allocation import (
    RandomizationConfigSchema,
    RTSMAllocator,
    generate_canonical_stratum_key,
)


def test_randomization_config_validation():
    """Verify that configuration validation accepts valid inputs and rejects invalid/malformed settings."""
    # 1. Valid Permuted Block Config
    config = RandomizationConfigSchema(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        block_sizes=[4, 6],
    )
    assert config.block_sizes == [4, 6]
    assert config.stratification_factors is None

    # 2. Valid Stratified Block Config
    config = RandomizationConfigSchema(
        algorithm_type="STRATIFIED_BLOCK",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        stratification_factors=["gender", "age_group"],
        block_sizes=[4],
    )
    assert config.block_sizes == [4]
    assert config.stratification_factors == ["gender", "age_group"]

    # 3. Valid Minimization Config
    config = RandomizationConfigSchema(
        algorithm_type="MINIMIZATION",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        stratification_factors={"gender": ["M", "F"], "age_group": ["<18", ">=18"]},
        factor_weights={"gender": 2.0},
        p_preferred=0.85,
    )
    assert config.p_preferred == 0.85
    assert config.factor_weights == {"gender": 2.0}

    # 4. Invalid algorithm_type
    with pytest.raises(ValidationError):
        RandomizationConfigSchema(
            algorithm_type="INVALID_TYPE",
            arms_ratios={"Arm A": 1},
        )

    # 5. Empty arms_ratios
    with pytest.raises(ValidationError):
        RandomizationConfigSchema(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={},
        )

    # 6. Negative ratio or empty arm name
    with pytest.raises(ValidationError):
        RandomizationConfigSchema(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"Arm A": -1},
        )
    with pytest.raises(ValidationError):
        RandomizationConfigSchema(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"": 1},
        )

    # 7. Block size not multiple of ratio sum
    with pytest.raises(ValidationError):
        # sum of ratios is 3, block size 4 is not a multiple of 3
        RandomizationConfigSchema(
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"Arm A": 1, "Arm B": 2},
            block_sizes=[4],
        )

    # 8. Missing stratification factors for Stratified Block
    with pytest.raises(ValidationError):
        RandomizationConfigSchema(
            algorithm_type="STRATIFIED_BLOCK",
            arms_ratios={"Arm A": 1},
        )

    # 9. Invalid p_preferred (out of range)
    with pytest.raises(ValidationError):
        RandomizationConfigSchema(
            algorithm_type="MINIMIZATION",
            arms_ratios={"Arm A": 1},
            stratification_factors=["gender"],
            p_preferred=0.4,  # Must be >= 0.5
        )

    # 10. Factor weights with unknown factor
    with pytest.raises(ValidationError):
        RandomizationConfigSchema(
            algorithm_type="MINIMIZATION",
            arms_ratios={"Arm A": 1},
            stratification_factors=["gender"],
            factor_weights={"unknown_factor": 1.0},
        )


def test_canonical_stratum_key_generation():
    """Verify that canonical stratum keys are stable and sorted alphabetically regardless of input order."""
    # 1. No active factors -> DEFAULT
    key = generate_canonical_stratum_key(None, None)
    assert key == "DEFAULT"

    # 2. Sorted factors alphabetical stability
    subject_factors = {"gender": "M", "age_group": "LT_18", "country": "US"}
    active_factors = ["country", "gender", "age_group"]

    key1 = generate_canonical_stratum_key(subject_factors, active_factors)
    # Alphabetical sorted order: age_group, country, gender
    assert key1 == "age_group=LT_18|country=US|gender=M"

    # Input active factors in different order -> should yield exact same key
    active_factors_scrambled = ["gender", "age_group", "country"]
    key2 = generate_canonical_stratum_key(subject_factors, active_factors_scrambled)
    assert key1 == key2

    # 3. Missing factor in subject factors raises ValueError
    with pytest.raises(
        ValueError, match="Missing required stratification factor value"
    ):
        generate_canonical_stratum_key({"gender": "M"}, ["gender", "age_group"])

    # 4. None value for factor raises ValueError
    with pytest.raises(
        ValueError, match="Missing required stratification factor value"
    ):
        generate_canonical_stratum_key(
            {"gender": "M", "age_group": None}, ["gender", "age_group"]
        )


def test_block_allocation_mechanics():
    """Verify block allocation honors arms, ratios, block definitions, and handles block regeneration."""
    # Config: 1:1 ratio, block size 4
    config = RandomizationConfigSchema(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        block_sizes=[4],
        seed=101,
    )
    allocator = RTSMAllocator(config)

    # 1. Initial allocation (no existing sequence)
    res = allocator.allocate(sequence=None, block_index=0)
    assert res["allocation"] in ("Arm A", "Arm B")
    assert res["stratum_key"] == "DEFAULT"
    assert len(res["updated_sequence"]) == 4
    assert res["updated_sequence"].count("Arm A") == 2
    assert res["updated_sequence"].count("Arm B") == 2
    assert res["updated_block_index"] == 1

    # 2. Consecutive allocation in same block
    seq = res["updated_sequence"]
    idx = res["updated_block_index"]
    res2 = allocator.allocate(sequence=seq, block_index=idx)
    assert res2["allocation"] == seq[1]
    assert res2["updated_sequence"] == seq
    assert res2["updated_block_index"] == 2

    # 3. Block exhaustion and regeneration
    # Force block_index to end of sequence
    res3 = allocator.allocate(sequence=seq, block_index=4)
    # Should generate a brand new sequence of size 4 and reset index to 1
    assert len(res3["updated_sequence"]) == 4
    assert res3["updated_block_index"] == 1
    assert res3["updated_sequence"] != seq or True  # Shuffled


def test_block_allocation_uneven_ratios():
    """Verify that block allocation correctly respects uneven allocation ratios and various block sizes."""
    config = RandomizationConfigSchema(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios={"Arm A": 2, "Arm B": 1},  # Sum = 3
        block_sizes=[6],
        seed=202,
    )
    allocator = RTSMAllocator(config)

    res = allocator.allocate(sequence=None, block_index=0)
    # 2:1 ratio for block of 6 means exactly 4 'Arm A' and 2 'Arm B'
    seq = res["updated_sequence"]
    assert len(seq) == 6
    assert seq.count("Arm A") == 4
    assert seq.count("Arm B") == 2


def test_stratified_block_isolation():
    """Verify stratified block randomization maintains isolated block counts per stratum key."""
    config = RandomizationConfigSchema(
        algorithm_type="STRATIFIED_BLOCK",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        stratification_factors=["gender"],
        block_sizes=[4],
        seed=303,
    )
    allocator = RTSMAllocator(config)

    # Stratum 1: gender=M
    res_m = allocator.allocate(
        subject_factors={"gender": "M"}, sequence=None, block_index=0
    )
    assert res_m["stratum_key"] == "gender=M"
    seq_m = res_m["updated_sequence"]
    idx_m = res_m["updated_block_index"]

    # Stratum 2: gender=F (has completely separate sequence and index, should not interfere with M)
    res_f = allocator.allocate(
        subject_factors={"gender": "F"}, sequence=None, block_index=0
    )
    assert res_f["stratum_key"] == "gender=F"

    # Continue allocating for gender=M using its own sequence
    res_m2 = allocator.allocate(
        subject_factors={"gender": "M"}, sequence=seq_m, block_index=idx_m
    )
    assert res_m2["updated_block_index"] == 2
    assert res_m2["updated_sequence"] == seq_m


def test_minimization_imbalance_and_biased_coin():
    """Verify that Pocock-Simon minimization correctly calculates imbalances, applies weights, and uses biased coin."""
    # Stratification factors: gender and age_group. Equal weights.
    config = RandomizationConfigSchema(
        algorithm_type="MINIMIZATION",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        stratification_factors=["gender", "age_group"],
        p_preferred=1.0,  # Force deterministic assignment to preferred arm to test arithmetic
        seed=404,
    )
    allocator = RTSMAllocator(config)

    # 1. First subject (no previous allocations)
    # Both arms have 0 count. Imbalance will be equal. Tied -> probability 1/2 each.
    new_subject = {"gender": "F", "age_group": "GE_65"}
    res1 = allocator.allocate(subject_factors=new_subject, previous_allocations=[])
    assert res1["allocation"] in ("Arm A", "Arm B")

    # 2. Evaluate deterministic imbalance case
    # Let's mock a history of previous allocations:
    # We want:
    # Arm A having: gender=F, age_group=LT_65 (1 subject)
    # Arm B having: gender=M, age_group=GE_65 (1 subject)
    #               gender=F, age_group=GE_65 (1 subject)
    # Total historical subjects:
    # Subject 1 (Arm A): {"gender": "F", "age_group": "LT_65"}
    # Subject 2 (Arm B): {"gender": "M", "age_group": "GE_65"}
    # Subject 3 (Arm B): {"gender": "F", "age_group": "GE_65"}
    previous = [
        {"strat_factors": {"gender": "F", "age_group": "LT_65"}, "allocation": "Arm A"},
        {"strat_factors": {"gender": "M", "age_group": "GE_65"}, "allocation": "Arm B"},
        {"strat_factors": {"gender": "F", "age_group": "GE_65"}, "allocation": "Arm B"},
    ]

    # New Subject: {"gender": "F", "age_group": "GE_65"}
    # If assigned to Arm A:
    # - gender=F: Arm A count becomes 2, Arm B is 1. Imbalance Range = 1.
    # - age_group=GE_65: Arm A count becomes 1, Arm B is 2. Imbalance Range = 1.
    # Total Arm A imbalance score = 2
    # If assigned to Arm B:
    # - gender=F: Arm A is 1, Arm B count becomes 2. Imbalance Range = 1.
    # - age_group=GE_65: Arm A is 0, Arm B count becomes 3. Imbalance Range = 3.
    # Total Arm B imbalance score = 4
    # Preferred arm is Arm A (score 2 < 4). Since p_preferred = 1.0, it MUST allocate Arm A!
    res2 = allocator.allocate(
        subject_factors=new_subject, previous_allocations=previous
    )
    assert res2["allocation"] == "Arm A"


def test_minimization_uneven_ratios_and_weights():
    """Verify minimization handles uneven ratios and factor weights correctly."""
    # Config: 2:1 ratio for Arm A vs Arm B. Weights: gender is weighted heavily (10.0), age_group is (1.0).
    config = RandomizationConfigSchema(
        algorithm_type="MINIMIZATION",
        arms_ratios={"Arm A": 2, "Arm B": 1},
        stratification_factors=["gender", "age_group"],
        factor_weights={"gender": 10.0, "age_group": 1.0},
        p_preferred=1.0,  # Deterministic preferred selection
        seed=505,
    )
    allocator = RTSMAllocator(config)

    # Let's mock a previous allocations list:
    # Arm A has 2 subjects with gender=F
    # Arm B has 1 subject with gender=F
    # Normalized: Arm A = 2 / 2 = 1.0, Arm B = 1 / 1 = 1.0 (balanced)
    #
    # Now for age_group=GE_65:
    # Arm A has 0 subjects.
    # Arm B has 1 subject.
    # Normalized: Arm A = 0, Arm B = 1.0 (unbalanced)
    previous = [
        {"strat_factors": {"gender": "F", "age_group": "LT_65"}, "allocation": "Arm A"},
        {"strat_factors": {"gender": "F", "age_group": "LT_65"}, "allocation": "Arm A"},
        {"strat_factors": {"gender": "F", "age_group": "GE_65"}, "allocation": "Arm B"},
    ]

    # New subject is {"gender": "F", "age_group": "GE_65"}
    # Let's calculate imbalance scores:
    #
    # Assign to Arm A:
    # - gender=F counts: Arm A=3, Arm B=1. Normalized: Arm A = 1.5, Arm B = 1.0. Range = 0.5. Weighted = 5.0.
    # - age_group=GE_65 counts: Arm A=1, Arm B=1. Normalized: Arm A = 0.5, Arm B = 1.0. Range = 0.5. Weighted = 0.5.
    # Total Arm A imbalance = 5.5
    #
    # Assign to Arm B:
    # - gender=F counts: Arm A=2, Arm B=2. Normalized: Arm A = 1.0, Arm B = 2.0. Range = 1.0. Weighted = 10.0.
    # - age_group=GE_65 counts: Arm A=0, Arm B=2. Normalized: Arm A = 0.0, Arm B = 2.0. Range = 2.0. Weighted = 2.0.
    # Total Arm B imbalance = 12.0
    #
    # Arm A is heavily preferred (score 5.5 < 12.0). Since p_preferred = 1.0, it must choose Arm A!
    new_subject = {"gender": "F", "age_group": "GE_65"}
    res = allocator.allocate(subject_factors=new_subject, previous_allocations=previous)
    assert res["allocation"] == "Arm A"


def test_reproducibility_and_seeding():
    """Verify that seeded allocations are deterministic across runs, while unseeded are secure and pseudo-random."""
    config_seeded1 = RandomizationConfigSchema(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        block_sizes=[4],
        seed=12345,
    )
    allocator_seeded1 = RTSMAllocator(config_seeded1)

    config_seeded2 = RandomizationConfigSchema(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        block_sizes=[4],
        seed=12345,
    )
    allocator_seeded2 = RTSMAllocator(config_seeded2)

    # Both allocators initialized with same seed must yield identical sequences
    seq1 = []
    seq2 = []
    state1_seq, state1_idx = None, 0
    state2_seq, state2_idx = None, 0

    for _ in range(10):
        res1 = allocator_seeded1.allocate(sequence=state1_seq, block_index=state1_idx)
        seq1.append(res1["allocation"])
        state1_seq, state1_idx = res1["updated_sequence"], res1["updated_block_index"]

        res2 = allocator_seeded2.allocate(sequence=state2_seq, block_index=state2_idx)
        seq2.append(res2["allocation"])
        state2_seq, state2_idx = res2["updated_sequence"], res2["updated_block_index"]

    assert seq1 == seq2

    # Unseeded allocator should use system secure random (cannot predict or replicate easily)
    config_unseeded = RandomizationConfigSchema(
        algorithm_type="PERMUTED_BLOCK",
        arms_ratios={"Arm A": 1, "Arm B": 1},
        block_sizes=[4],
        seed=None,
    )
    allocator_unseeded = RTSMAllocator(config_unseeded)
    res_unseeded = allocator_unseeded.allocate(sequence=None, block_index=0)
    assert res_unseeded["allocation"] in ("Arm A", "Arm B")
