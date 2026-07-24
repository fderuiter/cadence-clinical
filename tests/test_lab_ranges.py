import json

from apps.execution.lab_ranges import evaluate_lab_value, select_reference_range


# Mock/dictionary helper representing LabReferenceRange objects
def create_mock_range(
    id="range-01",
    study_id="STUDY-123",
    test_code="WBC",
    source="CENTRAL",
    site_id=None,
    unit="10^9/L",
    normalized_unit="10^9/L",
    sex_applicability="ALL",
    age_low=None,
    age_high=None,
    low_bound=4.0,
    high_bound=11.0,
    critical_low=None,
    critical_high=None,
    is_deleted=False,
):
    return {
        "id": id,
        "study_id": study_id,
        "test_code": test_code,
        "source": source,
        "site_id": site_id,
        "unit": unit,
        "normalized_unit": normalized_unit,
        "sex_applicability": sex_applicability,
        "age_low": age_low,
        "age_high": age_high,
        "low_bound": low_bound,
        "high_bound": high_bound,
        "critical_low": critical_low,
        "critical_high": critical_high,
        "is_deleted": is_deleted,
    }


def test_site_and_source_precedence():
    """Verify source/site precedence:
    - If lab_source is LOCAL, an exact site match (score 3) beats a generic local match (score 2),
      which beats a CENTRAL fallback match (score 1).
    - If lab_source is CENTRAL, only CENTRAL ranges are matched.
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Define ranges for the test
    r_central = create_mock_range(
        id="central",
        test_code=tcode,
        normalized_unit=unit,
        source="CENTRAL",
        site_id=None,
    )
    r_local_generic = create_mock_range(
        id="local_generic",
        test_code=tcode,
        normalized_unit=unit,
        source="LOCAL",
        site_id=None,
    )
    r_local_exact = create_mock_range(
        id="local_exact",
        test_code=tcode,
        normalized_unit=unit,
        source="LOCAL",
        site_id="SITE-A",
    )

    ranges = [r_central, r_local_generic, r_local_exact]

    # Scenario 1: lab_source="LOCAL", site_id="SITE-A"
    # Should pick local_exact (score 3)
    matched = select_reference_range(
        ranges, study, tcode, unit, "LOCAL", sex="M", age=30.0, site_id="SITE-A"
    )
    assert matched is not None
    assert matched["id"] == "local_exact"

    # Scenario 2: lab_source="LOCAL", site_id="SITE-B" (no exact match for SITE-B)
    # Should pick local_generic (score 2)
    matched = select_reference_range(
        ranges, study, tcode, unit, "LOCAL", sex="M", age=30.0, site_id="SITE-B"
    )
    assert matched is not None
    assert matched["id"] == "local_generic"

    # Scenario 3: lab_source="LOCAL", site_id=None (no site provided)
    # Should pick local_generic (score 2)
    matched = select_reference_range(
        ranges, study, tcode, unit, "LOCAL", sex="M", age=30.0, site_id=None
    )
    assert matched is not None
    assert matched["id"] == "local_generic"

    # Scenario 4: Only CENTRAL range exists, lab_source="LOCAL", site_id="SITE-A"
    # Should fall back to r_central (score 1)
    matched = select_reference_range(
        [r_central], study, tcode, unit, "LOCAL", sex="M", age=30.0, site_id="SITE-A"
    )
    assert matched is not None
    assert matched["id"] == "central"

    # Scenario 5: lab_source="CENTRAL"
    # Should only match CENTRAL (LOCAL range is incompatible/score 0)
    matched = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex="M", age=30.0, site_id="SITE-A"
    )
    assert matched is not None
    assert matched["id"] == "central"


def test_age_boundaries():
    """Verify age boundaries and specificity:
    - Match rules where subject age is between age_low and age_high.
    - Specificity: both bounds (3) > single bound (2) > no bounds (1).
    """
    study = "STUDY-123"
    tcode = "ALT"
    unit = "U/L"

    r_no_age = create_mock_range(
        id="no_age", test_code=tcode, normalized_unit=unit, age_low=None, age_high=None
    )
    r_single_bound = create_mock_range(
        id="single_bound",
        test_code=tcode,
        normalized_unit=unit,
        age_low=18.0,
        age_high=None,
    )
    r_both_bounds = create_mock_range(
        id="both_bounds",
        test_code=tcode,
        normalized_unit=unit,
        age_low=18.0,
        age_high=65.0,
    )

    ranges = [r_no_age, r_single_bound, r_both_bounds]

    # Scenario 1: age = 25 (matches all, but both_bounds has higher score 3)
    matched = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex="M", age=25.0
    )
    assert matched is not None
    assert matched["id"] == "both_bounds"

    # Scenario 2: age = 70 (matches no_age [score 1] and single_bound [score 2], single wins)
    matched = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex="M", age=70.0
    )
    assert matched is not None
    assert matched["id"] == "single_bound"

    # Scenario 3: age = 10 (matches only no_age [score 1])
    matched = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex="M", age=10.0
    )
    assert matched is not None
    assert matched["id"] == "no_age"

    # Scenario 4: age = None (should only match no_age)
    matched = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex="M", age=None
    )
    assert matched is not None
    assert matched["id"] == "no_age"


def test_sex_and_all_fallback():
    """Verify sex applicability and fallback:
    - Subject sex 'M' matches 'M' (score 2) and fallback 'ALL' (score 1).
    - Subject sex 'F' matches 'F' (score 2) and fallback 'ALL' (score 1).
    - Subject sex 'U' or None matches only fallback 'ALL' (score 1).
    """
    study = "STUDY-123"
    tcode = "HEMOGLOBIN"
    unit = "g/dL"

    r_all = create_mock_range(
        id="sex_all", test_code=tcode, normalized_unit=unit, sex_applicability="ALL"
    )
    r_m = create_mock_range(
        id="sex_m", test_code=tcode, normalized_unit=unit, sex_applicability="M"
    )
    r_f = create_mock_range(
        id="sex_f", test_code=tcode, normalized_unit=unit, sex_applicability="F"
    )

    ranges = [r_all, r_m, r_f]

    # Subject Male
    matched_m = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched_m is not None
    assert matched_m["id"] == "sex_m"

    # Subject Female
    matched_f = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex="F", age=30.0
    )
    assert matched_f is not None
    assert matched_f["id"] == "sex_f"

    # Subject Unknown/None
    matched_u = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex=None, age=30.0
    )
    assert matched_u is not None
    assert matched_u["id"] == "sex_all"


def test_unit_matching():
    """Verify that ranges are strictly filtered by the exact normalized unit."""
    study = "STUDY-123"
    tcode = "CREATININE"

    r_mg = create_mock_range(id="mg_dl", test_code=tcode, normalized_unit="mg/dL")
    r_umol = create_mock_range(id="umol_l", test_code=tcode, normalized_unit="umol/L")

    ranges = [r_mg, r_umol]

    matched_mg = select_reference_range(
        ranges, study, tcode, "mg/dL", "CENTRAL", sex="M", age=30.0
    )
    assert matched_mg is not None
    assert matched_mg["id"] == "mg_dl"

    matched_umol = select_reference_range(
        ranges, study, tcode, "umol/L", "CENTRAL", sex="M", age=30.0
    )
    assert matched_umol is not None
    assert matched_umol["id"] == "umol_l"

    matched_none = select_reference_range(
        ranges, study, tcode, "g/L", "CENTRAL", sex="M", age=30.0
    )
    assert matched_none is None


def test_normal_boundaries_and_inclusion():
    """Verify normal boundary comparison and inclusion:
    - Normal boundaries low_bound and high_bound are inclusive.
    - If low_bound <= value <= high_bound, indicators must be "NORMAL".
    - If value < low_bound, indicator must be "LOW".
    - If value > high_bound, indicator must be "HIGH".
    """
    r_normal = create_mock_range(low_bound=10.0, high_bound=20.0)

    # Inclusive lower bound
    indicator, out_of_range, bounds = evaluate_lab_value(10.0, r_normal)
    assert indicator == "NORMAL"
    assert out_of_range is False
    assert json.loads(bounds) == {"low": 10.0, "high": 20.0}

    # Inclusive upper bound
    indicator, out_of_range, _ = evaluate_lab_value(20.0, r_normal)
    assert indicator == "NORMAL"
    assert out_of_range is False

    # Within bounds
    indicator, out_of_range, _ = evaluate_lab_value(15.0, r_normal)
    assert indicator == "NORMAL"
    assert out_of_range is False

    # Below lower bound
    indicator, out_of_range, _ = evaluate_lab_value(9.9, r_normal)
    assert indicator == "LOW"
    assert out_of_range is True

    # Above upper bound
    indicator, out_of_range, _ = evaluate_lab_value(20.1, r_normal)
    assert indicator == "HIGH"
    assert out_of_range is True

    # Check None value
    indicator, out_of_range, bounds = evaluate_lab_value(None, r_normal)
    assert indicator is None
    assert out_of_range is False
    assert json.loads(bounds) == {"low": 10.0, "high": 20.0}


def test_critical_boundaries_and_exclusion():
    """Verify critical boundaries and exclusive bounds behavior:
    - Critical boundaries critical_low and critical_high are exclusive.
    - value < critical_low triggers "LOW LOW".
    - value > critical_high triggers "HIGH HIGH".
    """
    r_critical = create_mock_range(
        low_bound=10.0, high_bound=20.0, critical_low=5.0, critical_high=25.0
    )

    # Inside normal range
    indicator, out_of_range, _ = evaluate_lab_value(15.0, r_critical)
    assert indicator == "NORMAL"
    assert out_of_range is False

    # Below low_bound but >= critical_low
    indicator, out_of_range, _ = evaluate_lab_value(5.0, r_critical)
    assert indicator == "LOW"
    assert out_of_range is True

    # Below critical_low (exclusive boundary check: value < critical_low)
    indicator, out_of_range, _ = evaluate_lab_value(4.9, r_critical)
    assert indicator == "LOW LOW"
    assert out_of_range is True

    # Above high_bound but <= critical_high
    indicator, out_of_range, _ = evaluate_lab_value(25.0, r_critical)
    assert indicator == "HIGH"
    assert out_of_range is True

    # Above critical_high (exclusive boundary check: value > critical_high)
    indicator, out_of_range, _ = evaluate_lab_value(25.1, r_critical)
    assert indicator == "HIGH HIGH"
    assert out_of_range is True


def test_absent_boundaries():
    """Verify behavior when some normal or critical bounds are absent/None."""
    # Scenario 1: Only low_bound and critical_low present
    r_only_low = create_mock_range(
        low_bound=10.0, high_bound=None, critical_low=5.0, critical_high=None
    )
    indicator, out_of_range, bounds = evaluate_lab_value(100.0, r_only_low)
    assert indicator == "NORMAL"
    assert out_of_range is False
    assert json.loads(bounds) == {"low": 10.0, "high": None}

    indicator, out_of_range, _ = evaluate_lab_value(8.0, r_only_low)
    assert indicator == "LOW"
    assert out_of_range is True

    indicator, out_of_range, _ = evaluate_lab_value(4.0, r_only_low)
    assert indicator == "LOW LOW"
    assert out_of_range is True

    # Scenario 2: All bounds None
    r_none = create_mock_range(
        low_bound=None, high_bound=None, critical_low=None, critical_high=None
    )
    indicator, out_of_range, bounds = evaluate_lab_value(42.0, r_none)
    assert indicator == "NORMAL"
    assert out_of_range is False
    assert json.loads(bounds) == {"low": None, "high": None}


def test_no_matching_rule_behavior():
    """Verify that when no matching rule/range exists, results are safe and clean."""
    indicator, out_of_range, bounds = evaluate_lab_value(42.0, None)
    assert indicator is None
    assert out_of_range is False
    assert bounds is None


def test_deterministic_ties():
    """Verify that equal-specificity rules are resolved deterministically.
    - Two ranges with identical site, sex, and age scores are resolved by:
      - Narrower age span first.
      - If age span is same, higher age_low (more specific) first.
      - If age boundaries are same, lower low_bound first.
      - Alphabetically by range ID string.
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Create ranges with same site, sex, and age specificity
    # Both have both age bounds (score 3)
    r1 = create_mock_range(id="r1", age_low=10.0, age_high=50.0, low_bound=4.0)
    r2 = create_mock_range(
        id="r2", age_low=20.0, age_high=50.0, low_bound=4.0
    )  # narrower span than r1
    r3 = create_mock_range(
        id="r3", age_low=20.0, age_high=50.0, low_bound=3.0
    )  # same span as r2, lower low_bound

    # Scenario 1: r1 vs r2. r2 has narrower span (30 vs 40), so r2 wins.
    matched = select_reference_range(
        [r1, r2], study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is not None
    assert matched["id"] == "r2"

    # Scenario 2: r2 vs r3. Same span, r3 has lower low_bound (3.0 vs 4.0), so r3 wins.
    matched = select_reference_range(
        [r2, r3], study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is not None
    assert matched["id"] == "r3"

    # Scenario 3: r_a vs r_b with identical properties. Sort alphabetically by ID.
    r_a = create_mock_range(id="range-A", age_low=10.0, age_high=50.0)
    r_b = create_mock_range(id="range-B", age_low=10.0, age_high=50.0)

    matched = select_reference_range(
        [r_b, r_a], study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is not None
    assert matched["id"] == "range-A"


def test_tie_breaking_with_none_bounds():
    """Verify that tie-breaking handles ranges with missing age_high_val gracefully,
    and does not raise a TypeError (NoneType comparison).
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Two ranges with no age bounds (both have age_low=None, age_high=None).
    # This scenario is very common and would raise a TypeError in the sort key if None compares to None.
    r1 = create_mock_range(id="r1", age_low=None, age_high=None, low_bound=5.0)
    r2 = create_mock_range(id="r2", age_low=None, age_high=None, low_bound=4.0)

    # They should sort deterministically, and r2 should win because low_bound is 4.0 < 5.0
    matched = select_reference_range(
        [r1, r2], study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is not None
    assert matched["id"] == "r2"


def test_is_deleted_filtering():
    """Verify that soft-deleted ranges (is_deleted=True) are excluded from matching."""
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    r_deleted = create_mock_range(id="deleted", is_deleted=True)
    r_active = create_mock_range(id="active", is_deleted=False)

    matched = select_reference_range(
        [r_deleted, r_active], study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is not None
    assert matched["id"] == "active"

    matched_deleted_only = select_reference_range(
        [r_deleted], study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched_deleted_only is None
