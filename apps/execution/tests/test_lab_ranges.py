import json

import pytest

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

    @req:PRD-LAB-001
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

    @req:PRD-LAB-001
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

    @req:PRD-LAB-001
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
    """Verify that ranges are strictly filtered by the exact normalized unit.

    @req:PRD-LAB-001
    """
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

    @req:PRD-LAB-001
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

    @req:PRD-LAB-001
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
    """Verify behavior when some normal or critical bounds are absent/None.

    @req:PRD-LAB-001
    """
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
    """Verify that when no matching rule/range exists, results are safe and clean.

    @req:PRD-LAB-001
    """
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

    @req:PRD-LAB-001
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

    @req:PRD-LAB-001
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
    """Verify that soft-deleted ranges (is_deleted=True) are excluded from matching.

    @req:PRD-LAB-001
    """
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


@pytest.mark.asyncio
async def test_convert_lab_unit_db_and_fallback():
    """Verify test-code-aware unit conversion helper with database and fallback behavior.

    @req:PRD-LAB-001
    """
    from apps.execution.database.core import db_manager
    from apps.execution.database.models import Base, LabUnitConversion
    from apps.execution.lab_ranges import convert_lab_unit

    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    try:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

        session_maker = db_manager.get_session_maker()
        async with session_maker() as session:
            # 1. Test static ucum fallback (no DB conversion row exists)
            # Mass: 1.5 kg to g -> should be 1500.0
            val_fallback = await convert_lab_unit(session, "WBC", "kg", "g", 1.5)
            assert abs(val_fallback - 1500.0) < 1e-9

            # Temperature: 100 [Fahr] to Cel -> should be 37.777777778
            val_temp = await convert_lab_unit(session, "TEMP", "F", "C", 100.0)
            assert abs(val_temp - 37.777777778) < 1e-5

            # 2. Insert custom database-driven conversion row for HEMOGLOBIN (g/dL to g/L)
            # Custom factor = 10.0, offset = 0.5
            conv = LabUnitConversion(
                study_id="STUDY-123",
                test_code="HEMOGLOBIN",
                from_unit="g/dL",
                to_unit="g/L",
                factor=10.0,
                offset=0.5,
                created_by="test_user",
                reason_for_change="Custom conversion formula",
                version_index=1,
            )
            session.add(conv)
            await session.commit()

        # Re-open session to fetch from DB
        async with session_maker() as session:
            # Test custom database conversion
            # 12.0 * 10.0 + 0.5 = 120.5
            val_custom = await convert_lab_unit(
                session, "HEMOGLOBIN", "g/dL", "g/L", 12.0
            )
            assert abs(val_custom - 120.5) < 1e-9

            # 3. Test that other test code (e.g. "OTHER") or different units fallback properly
            # even if "HEMOGLOBIN" conversion exists
            val_custom_fallback = await convert_lab_unit(
                session, "OTHER", "kg", "g", 2.0
            )
            assert abs(val_custom_fallback - 2000.0) < 1e-9

    finally:
        await db_manager.close()


@pytest.mark.asyncio
async def test_lab_reference_range_synonyms_update_and_audit():
    """Verify that updating synonym columns works fine and does not interfere with audit fields."""
    from sqlalchemy import select

    from apps.execution.database.core import db_manager
    from apps.execution.database.models import Base, LabReferenceRange

    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    try:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

        session_maker = db_manager.get_session_maker()
        async with session_maker() as session:
            lab_range = LabReferenceRange(
                study_id="STUDY-SYN-UP",
                test_code="WBC",
                test_name="White Blood Cells",
                source="LOCAL",
                sex_applicability="ALL",
                low_bound=4.0,
                high_bound=11.0,
                created_by="synonym_user",
                reason_for_change="Initial synonyms",
                version_index=1,
            )
            session.add(lab_range)
            await session.commit()

        # Update synonym columns
        async with session_maker() as session:
            result = await session.execute(
                select(LabReferenceRange).where(
                    LabReferenceRange.study_id == "STUDY-SYN-UP"
                )
            )
            saved = result.scalar_one()
            saved.source = "CENTRAL"
            saved.sex_applicability = "M"
            saved.low_bound = 4.5
            saved.high_bound = 11.5
            saved.reason_for_change = "Updated via synonyms"
            saved.version_index = 2
            await session.commit()

        # Assert physical columns mapped and audit fields updated
        async with session_maker() as session:
            result = await session.execute(
                select(LabReferenceRange).where(
                    LabReferenceRange.study_id == "STUDY-SYN-UP"
                )
            )
            saved = result.scalar_one()
            assert saved.lab_source == "CENTRAL"
            assert saved.source == "CENTRAL"
            assert saved.sex == "M"
            assert saved.sex_applicability == "M"
            assert saved.range_low == 4.5
            assert saved.low_bound == 4.5
            assert saved.range_high == 11.5
            assert saved.high_bound == 11.5

            assert saved.created_at is not None
            assert saved.created_by == "synonym_user"
            assert saved.reason_for_change == "Updated via synonyms"
            assert saved.version_index == 2

    finally:
        await db_manager.close()


def test_evaluate_lab_value_all_indicators():
    """Verify that evaluate_lab_value returns the expected lab_indicator values
    (NORMAL, LOW, HIGH, LOW LOW, HIGH HIGH, None) and lab_out_of_range boolean.

    @req:PRD-LAB-001
    """
    # Create reference range with normal limits [10.0, 20.0] and critical limits [5.0, 25.0]
    r_range = create_mock_range(
        low_bound=10.0,
        high_bound=20.0,
        critical_low=5.0,
        critical_high=25.0,
    )

    # 1. Test None value (should be None, False)
    ind, out_of_range, bounds = evaluate_lab_value(None, r_range)
    assert ind is None
    assert out_of_range is False
    assert json.loads(bounds) == {"low": 10.0, "high": 20.0}

    # 2. Test None reference_range (should be None, False)
    ind, out_of_range, bounds = evaluate_lab_value(15.0, None)
    assert ind is None
    assert out_of_range is False
    assert bounds is None

    # 3. Test NORMAL (inclusive: low_bound <= value <= high_bound)
    for v in [10.0, 15.0, 20.0]:
        ind, out_of_range, _ = evaluate_lab_value(v, r_range)
        assert ind == "NORMAL"
        assert out_of_range is False

    # 4. Test LOW (low_bound_val is present and value < low_bound_val, and not LOW LOW)
    # critical_low is 5.0, so 5.0 <= value < 10.0
    for v in [5.0, 7.5, 9.9]:
        ind, out_of_range, _ = evaluate_lab_value(v, r_range)
        assert ind == "LOW"
        assert out_of_range is True

    # 5. Test HIGH (high_bound_val is present and value > high_bound_val, and not HIGH HIGH)
    # critical_high is 25.0, so 20.0 < value <= 25.0
    for v in [20.1, 22.5, 25.0]:
        ind, out_of_range, _ = evaluate_lab_value(v, r_range)
        assert ind == "HIGH"
        assert out_of_range is True

    # 6. Test LOW LOW (exclusive: value < critical_low)
    for v in [4.9, 2.0]:
        ind, out_of_range, _ = evaluate_lab_value(v, r_range)
        assert ind == "LOW LOW"
        assert out_of_range is True

    # 7. Test HIGH HIGH (exclusive: value > critical_high)
    for v in [25.1, 30.0]:
        ind, out_of_range, _ = evaluate_lab_value(v, r_range)
        assert ind == "HIGH HIGH"
        assert out_of_range is True


def test_task1_sex_u_matching():
    """Add tests that pass literal sex="U" and confirm only ranges with sex_applicability in ALL/U/None/empty match.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Define ranges with various sex applicabilities
    r_all = create_mock_range(
        id="sex_all", test_code=tcode, normalized_unit=unit, sex_applicability="ALL"
    )
    r_u = create_mock_range(
        id="sex_u", test_code=tcode, normalized_unit=unit, sex_applicability="U"
    )
    r_none = create_mock_range(
        id="sex_none", test_code=tcode, normalized_unit=unit, sex_applicability=None
    )
    r_empty = create_mock_range(
        id="sex_empty", test_code=tcode, normalized_unit=unit, sex_applicability=""
    )
    r_m = create_mock_range(
        id="sex_m", test_code=tcode, normalized_unit=unit, sex_applicability="M"
    )
    r_f = create_mock_range(
        id="sex_f", test_code=tcode, normalized_unit=unit, sex_applicability="F"
    )

    # When sex="U", r_all, r_u, r_none, r_empty should match (with sex_score 1), and r_m, r_f should NOT match.
    # We can test individual ranges separately to see if they match or not (return None).
    for r in [r_all, r_u, r_none, r_empty]:
        matched = select_reference_range(
            [r], study, tcode, unit, "CENTRAL", sex="U", age=30.0
        )
        assert matched is not None, (
            f"Range with sex_applicability={r['sex_applicability']} should match when sex='U'"
        )
        assert matched["id"] == r["id"]

    for r in [r_m, r_f]:
        matched = select_reference_range(
            [r], study, tcode, unit, "CENTRAL", sex="U", age=30.0
        )
        assert matched is None, (
            f"Range with sex_applicability={r['sex_applicability']} should NOT match when sex='U'"
        )


def test_task1_sex_alias_strings():
    """Add tests that pass sex alias strings ("Male", "Female", "Boy", "Girl", "Woman", "Man", "Unknown")
    into select_reference_range and assert the expected M/F/U matching outcome.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    r_m = create_mock_range(
        id="sex_m", test_code=tcode, normalized_unit=unit, sex_applicability="M"
    )
    r_f = create_mock_range(
        id="sex_f", test_code=tcode, normalized_unit=unit, sex_applicability="F"
    )
    r_all = create_mock_range(
        id="sex_all", test_code=tcode, normalized_unit=unit, sex_applicability="ALL"
    )

    # M aliases
    for m_alias in ["Male", "Boy", "Man"]:
        matched = select_reference_range(
            [r_m, r_f], study, tcode, unit, "CENTRAL", sex=m_alias, age=30.0
        )
        assert matched is not None
        assert matched["id"] == "sex_m"

    # F aliases
    for f_alias in ["Female", "Girl", "Woman"]:
        matched = select_reference_range(
            [r_m, r_f], study, tcode, unit, "CENTRAL", sex=f_alias, age=30.0
        )
        assert matched is not None
        assert matched["id"] == "sex_f"

    # U aliases
    for u_alias in ["Unknown"]:
        # Should not match r_m or r_f, but matches r_all
        matched_specific = select_reference_range(
            [r_m, r_f], study, tcode, unit, "CENTRAL", sex=u_alias, age=30.0
        )
        assert matched_specific is None

        matched_all = select_reference_range(
            [r_all], study, tcode, unit, "CENTRAL", sex=u_alias, age=30.0
        )
        assert matched_all is not None
        assert matched_all["id"] == "sex_all"


def test_task1_exact_m_rejected_against_f_only_range():
    """Add a test that confirms an exact M observation is rejected against an F-only range (sex score 0 discard).

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    r_f = create_mock_range(
        id="sex_f", test_code=tcode, normalized_unit=unit, sex_applicability="F"
    )

    matched = select_reference_range(
        [r_f], study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is None


def test_task1_divergence_select_reference_range_vs_normalize_gender():
    """Verify that divergence is resolved: the unified helper is used with preserve_custom=True,
    allowing custom biological sex exact reference ranges to match with high specificity (score 2).

    @req:PRD-LAB-001
    """
    from apps.execution.demographics import normalize_gender

    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # 1. demographics.normalize_gender without preserve_custom still defaults to "U"
    assert normalize_gender("OTHER", preserve_custom=False) == "U"
    assert normalize_gender("X", preserve_custom=False) == "U"

    # With preserve_custom=True, it returns the custom string
    assert normalize_gender("OTHER", preserve_custom=True) == "OTHER"
    assert normalize_gender("X", preserve_custom=True) == "X"

    r_other = create_mock_range(
        id="sex_other", test_code=tcode, normalized_unit=unit, sex_applicability="OTHER"
    )
    r_x = create_mock_range(
        id="sex_x", test_code=tcode, normalized_unit=unit, sex_applicability="X"
    )
    r_all = create_mock_range(
        id="sex_all", test_code=tcode, normalized_unit=unit, sex_applicability="ALL"
    )

    # Passing sex="OTHER" or "X" will now match the exact "OTHER" or "X" range
    matched_other = select_reference_range(
        [r_other], study, tcode, unit, "CENTRAL", sex="OTHER", age=30.0
    )
    assert matched_other is not None
    assert matched_other["id"] == "sex_other"

    matched_x = select_reference_range(
        [r_x], study, tcode, unit, "CENTRAL", sex="X", age=30.0
    )
    assert matched_x is not None
    assert matched_x["id"] == "sex_x"

    # Passing sex="OTHER" or "X" will fall back to "ALL" range if specific custom range is not in candidate list
    matched_all_other = select_reference_range(
        [r_all], study, tcode, unit, "CENTRAL", sex="OTHER", age=30.0
    )
    assert matched_all_other is not None
    assert matched_all_other["id"] == "sex_all"

    # Verify specificity scoring priority: exact custom sex range match wins over "ALL" fallback range
    matched_priority = select_reference_range(
        [r_other, r_all], study, tcode, unit, "CENTRAL", sex="OTHER", age=30.0
    )
    assert matched_priority is not None
    assert (
        matched_priority["id"] == "sex_other"
    )  # Wins because of sex_score = 2 vs sex_score = 1


def test_task2_age_inclusive_boundaries():
    """Add tests that assert inclusive boundary behavior when age == age_low and age == age_high.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Define range with age bounds [18.0, 65.0]
    r_bounded = create_mock_range(
        id="age_bounded",
        test_code=tcode,
        normalized_unit=unit,
        age_low=18.0,
        age_high=65.0,
    )

    # 1. Test age == age_low (18.0)
    matched_low = select_reference_range(
        [r_bounded], study, tcode, unit, "CENTRAL", sex="M", age=18.0
    )
    assert matched_low is not None
    assert matched_low["id"] == "age_bounded"

    # 2. Test age == age_high (65.0)
    matched_high = select_reference_range(
        [r_bounded], study, tcode, unit, "CENTRAL", sex="M", age=65.0
    )
    assert matched_high is not None
    assert matched_high["id"] == "age_bounded"

    # 3. Test age just inside bounds (18.1, 64.9)
    assert (
        select_reference_range(
            [r_bounded], study, tcode, unit, "CENTRAL", sex="M", age=18.1
        )
        is not None
    )
    assert (
        select_reference_range(
            [r_bounded], study, tcode, unit, "CENTRAL", sex="M", age=64.9
        )
        is not None
    )

    # 4. Test age outside bounds (17.9, 65.1)
    assert (
        select_reference_range(
            [r_bounded], study, tcode, unit, "CENTRAL", sex="M", age=17.9
        )
        is None
    )
    assert (
        select_reference_range(
            [r_bounded], study, tcode, unit, "CENTRAL", sex="M", age=65.1
        )
        is None
    )


def test_task2_age_none_matching():
    """Add a test that confirms age=None matches only unbounded ranges (score 1) and is discarded against bounded ranges.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    r_unbounded = create_mock_range(
        id="age_unbounded",
        test_code=tcode,
        normalized_unit=unit,
        age_low=None,
        age_high=None,
    )
    r_bounded_low = create_mock_range(
        id="age_bounded_low",
        test_code=tcode,
        normalized_unit=unit,
        age_low=18.0,
        age_high=None,
    )
    r_bounded_high = create_mock_range(
        id="age_bounded_high",
        test_code=tcode,
        normalized_unit=unit,
        age_low=None,
        age_high=65.0,
    )
    r_bounded_both = create_mock_range(
        id="age_bounded_both",
        test_code=tcode,
        normalized_unit=unit,
        age_low=18.0,
        age_high=65.0,
    )

    all_ranges = [r_unbounded, r_bounded_low, r_bounded_high, r_bounded_both]

    # Passing age=None should strictly select r_unbounded (bounded ranges should be discarded)
    matched = select_reference_range(
        all_ranges, study, tcode, unit, "CENTRAL", sex="M", age=None
    )
    assert matched is not None
    assert matched["id"] == "age_unbounded"


def test_task2_zero_and_negative_age_evaluation():
    """Add tests for zero age and negative age values at evaluation time.
    - Pass age=0. Assert expected matching outcome against ranges with and without lower bounds.
    - Pass negative age (e.g., age=-1). Assert evaluation-time behavior (distinct from create-time validation).

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Range with lower bound (age_low=0.0, age_high=1.0)
    r_infant = create_mock_range(
        id="infant", test_code=tcode, normalized_unit=unit, age_low=0.0, age_high=1.0
    )
    # Range with age_low=5.0 (does not match age=0)
    r_child = create_mock_range(
        id="child", test_code=tcode, normalized_unit=unit, age_low=5.0, age_high=12.0
    )
    # Range without lower bound (age_low=None, age_high=5.0)
    r_young = create_mock_range(
        id="young", test_code=tcode, normalized_unit=unit, age_low=None, age_high=5.0
    )
    # Range designed for prenatal/gestational (negative age, e.g. -2.0 to 0.0)
    r_prenatal = create_mock_range(
        id="prenatal", test_code=tcode, normalized_unit=unit, age_low=-2.0, age_high=0.0
    )

    # 1. Test age = 0 at evaluation time against ranges with/without lower bounds
    # Under infant (matches because age_low=0.0 <= age=0.0 <= age_high=1.0)
    matched_infant = select_reference_range(
        [r_infant], study, tcode, unit, "CENTRAL", sex="M", age=0.0
    )
    assert matched_infant is not None
    assert matched_infant["id"] == "infant"

    # Under young (matches because age_low=None <= age=0.0 <= age_high=5.0)
    matched_young = select_reference_range(
        [r_young], study, tcode, unit, "CENTRAL", sex="M", age=0.0
    )
    assert matched_young is not None
    assert matched_young["id"] == "young"

    # Under prenatal (matches because age_low=-2.0 <= age=0.0 <= age_high=0.0)
    matched_prenatal = select_reference_range(
        [r_prenatal], study, tcode, unit, "CENTRAL", sex="M", age=0.0
    )
    assert matched_prenatal is not None
    assert matched_prenatal["id"] == "prenatal"

    # Under child (does NOT match because age_low=5.0 > age=0.0)
    matched_child = select_reference_range(
        [r_child], study, tcode, unit, "CENTRAL", sex="M", age=0.0
    )
    assert matched_child is None

    # 2. Test negative age (e.g. age=-1) at evaluation time
    # (Note: This is distinct from the create-time age_low < 0 validation)
    # age=-1 should match r_prenatal (age_low=-2.0 <= -1 <= age_high=0.0) and r_young (age_low=None <= -1 <= age_high=5.0)
    # but NOT r_infant (age_low=0.0)
    matched_neg_prenatal = select_reference_range(
        [r_prenatal], study, tcode, unit, "CENTRAL", sex="M", age=-1.0
    )
    assert matched_neg_prenatal is not None
    assert matched_neg_prenatal["id"] == "prenatal"

    matched_neg_young = select_reference_range(
        [r_young], study, tcode, unit, "CENTRAL", sex="M", age=-1.0
    )
    assert matched_neg_young is not None
    assert matched_neg_young["id"] == "young"

    matched_neg_infant = select_reference_range(
        [r_infant], study, tcode, unit, "CENTRAL", sex="M", age=-1.0
    )
    assert matched_neg_infant is None


def test_task2_age_span_tie_breaking():
    """Add a test that confirms age-span tie-breaking selects the narrower range when site and sex scores tie.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Both ranges have exact sex match (score 2) and central site match (score 1) and both age bounds (score 3)
    # Range 1: age span 18.0 to 65.0 (span = 47.0)
    # Range 2: age span 20.0 to 40.0 (span = 20.0)
    r_wide = create_mock_range(
        id="wide_range",
        test_code=tcode,
        normalized_unit=unit,
        age_low=18.0,
        age_high=65.0,
    )
    r_narrow = create_mock_range(
        id="narrow_range",
        test_code=tcode,
        normalized_unit=unit,
        age_low=20.0,
        age_high=40.0,
    )

    # For an observation with age = 30.0, both match. But r_narrow has narrower age span (20 vs 47), so it should win.
    matched = select_reference_range(
        [r_wide, r_narrow], study, tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is not None
    assert matched["id"] == "narrow_range"


def test_task3_study_id_isolation():
    """Add a test that passes a study_id that does not match any candidate and asserts select_reference_range returns None.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    r_candidate = create_mock_range(
        id="r_cand", study_id=study, test_code=tcode, normalized_unit=unit
    )

    # passing mismatched study_id "STUDY-999" must return None
    matched = select_reference_range(
        [r_candidate], "STUDY-999", tcode, unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is None


def test_task3_test_code_isolation():
    """Add a test that passes a mismatched test_code and asserts no candidate matches.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    r_candidate = create_mock_range(
        id="r_cand", study_id=study, test_code=tcode, normalized_unit=unit
    )

    # passing mismatched test_code "RBC" must return None
    matched = select_reference_range(
        [r_candidate], study, "RBC", unit, "CENTRAL", sex="M", age=30.0
    )
    assert matched is None


def test_task3_unknown_lab_source_fallback():
    """Add a test with a lab_source value outside LOCAL and CENTRAL (e.g. "REGIONAL")
    and assert the else branch falls back to CENTRAL-only matching.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    r_central = create_mock_range(
        id="r_central",
        study_id=study,
        test_code=tcode,
        normalized_unit=unit,
        source="CENTRAL",
    )
    r_local = create_mock_range(
        id="r_local",
        study_id=study,
        test_code=tcode,
        normalized_unit=unit,
        source="LOCAL",
    )

    # If we pass lab_source="REGIONAL" (outside LOCAL and CENTRAL), only the r_central range should match
    # (the r_local range should be discarded, i.e., site_score = 0)
    matched = select_reference_range(
        [r_central, r_local], study, tcode, unit, "REGIONAL", sex="M", age=30.0
    )
    assert matched is not None
    assert matched["id"] == "r_central"

    # Verify that if only the r_local range is present, passing "REGIONAL" returns None
    matched_local_only = select_reference_range(
        [r_local], study, tcode, unit, "REGIONAL", sex="M", age=30.0
    )
    assert matched_local_only is None


def test_task3_site_id_combinations():
    """Add a test with a range that has a site_id while the observation has none (and the reverse)
    and assert the expected site score outcome.

    @req:PRD-LAB-001
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Case 1: Range has site_id, but Observation has no site_id (lab_source="LOCAL")
    # This should yield site_score = 0 and be discarded.
    r_specific_site = create_mock_range(
        id="specific_site",
        study_id=study,
        test_code=tcode,
        normalized_unit=unit,
        source="LOCAL",
        site_id="SITE-A",
    )
    matched_case1 = select_reference_range(
        [r_specific_site], study, tcode, unit, "LOCAL", sex="M", age=30.0, site_id=None
    )
    assert matched_case1 is None

    # Case 2: Range has no site_id, but Observation has site_id="SITE-A" (lab_source="LOCAL")
    # This matches generic local (site_score = 2).
    r_generic_site = create_mock_range(
        id="generic_site",
        study_id=study,
        test_code=tcode,
        normalized_unit=unit,
        source="LOCAL",
        site_id=None,
    )
    matched_case2 = select_reference_range(
        [r_generic_site],
        study,
        tcode,
        unit,
        "LOCAL",
        sex="M",
        age=30.0,
        site_id="SITE-A",
    )
    assert matched_case2 is not None
    assert matched_case2["id"] == "generic_site"


@pytest.mark.asyncio
async def test_task4_convert_lab_unit_edge_cases():
    """Add tests for convert_lab_unit covering:
    - DB conversion row with offset=None.
    - DB conversion row with a non-null offset.
    - from_unit == to_unit no-op via UCUM.
    - incompatible-unit conversion that propagates ValueError from convert_unit.
    - confirms is_deleted LabUnitConversion rows are ignored and the UCUM fallback is used.

    @req:PRD-LAB-001
    """
    from apps.execution.database.core import db_manager
    from apps.execution.database.models import Base, LabUnitConversion
    from apps.execution.lab_ranges import convert_lab_unit

    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    try:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

        session_maker = db_manager.get_session_maker()
        async with session_maker() as session:
            # 1. DB conversion row with offset=None
            conv_none_offset = LabUnitConversion(
                study_id="STUDY-123",
                test_code="TEST-A",
                from_unit="u_from",
                to_unit="u_to",
                factor=2.5,
                offset=None,
                created_by="test_user",
                reason_for_change="None offset check",
                version_index=1,
            )
            session.add(conv_none_offset)

            # 2. DB conversion row with non-null offset
            conv_with_offset = LabUnitConversion(
                study_id="STUDY-123",
                test_code="TEST-B",
                from_unit="u_from",
                to_unit="u_to",
                factor=2.0,
                offset=1.5,
                created_by="test_user",
                reason_for_change="Non-null offset check",
                version_index=1,
            )
            session.add(conv_with_offset)

            # 3. is_deleted LabUnitConversion row (should be ignored and UCUM fallback used)
            # In UCUM: kg to g is factor of 1000. Let's add a deleted conversion row with factor 5.0
            conv_deleted = LabUnitConversion(
                study_id="STUDY-123",
                test_code="WBC",
                from_unit="kg",
                to_unit="g",
                factor=5.0,
                offset=0.0,
                is_deleted=True,
                created_by="test_user",
                reason_for_change="Deleted conversion check",
                version_index=1,
            )
            session.add(conv_deleted)

            await session.commit()

        # Re-open session to run test operations
        async with session_maker() as session:
            # 1. DB conversion row with offset=None: value=10.0 * 2.5 + 0.0 = 25.0
            val_none_offset = await convert_lab_unit(
                session, "TEST-A", "u_from", "u_to", 10.0
            )
            assert abs(val_none_offset - 25.0) < 1e-9

            # 2. DB conversion row with non-null offset: value=10.0 * 2.0 + 1.5 = 21.5
            val_with_offset = await convert_lab_unit(
                session, "TEST-B", "u_from", "u_to", 10.0
            )
            assert abs(val_with_offset - 21.5) < 1e-9

            # 3. from_unit == to_unit no-op via UCUM (using units not in DB, e.g. "kg" to "kg" or unrecognized)
            # Both recognized "kg" to "kg" and unrecognized "xyz" to "xyz" should return the input value as a no-op
            val_noop_kg = await convert_lab_unit(
                session, "SOME-TEST", "kg", "kg", 12.34
            )
            assert val_noop_kg == 12.34

            val_noop_unrecognized = await convert_lab_unit(
                session, "SOME-TEST", "xyz", "xyz", 56.78
            )
            assert val_noop_unrecognized == 56.78

            # 4. incompatible-unit conversion that propagates ValueError from convert_unit
            # Converting mass "kg" to length "m"
            with pytest.raises(ValueError, match="Incompatible unit conversion"):
                await convert_lab_unit(session, "SOME-TEST", "kg", "m", 1.0)

            # 5. confirms is_deleted LabUnitConversion rows are ignored and the UCUM fallback is used
            # We added a deleted custom conversion row for WBC kg -> g with factor 5.0.
            # Since it is ignored, the standard UCUM fallback conversion (factor 1000) should be used.
            # value=2.0 kg should convert to 2000.0 g instead of 10.0 g.
            val_deleted_ignored = await convert_lab_unit(session, "WBC", "kg", "g", 2.0)
            assert abs(val_deleted_ignored - 2000.0) < 1e-9

    finally:
        await db_manager.close()


def test_task4_evaluate_lab_value_edge_cases():
    """Add tests for evaluate_lab_value covering:
    - only critical bounds set (no normal bounds)
    - only normal bounds set (no critical bounds)
    - negative and zero lab values
    - values exactly equal to a critical boundary (inclusive normal boundary vs exclusive critical boundary)
    - value exactly equal to normal bound stays NORMAL

    @req:PRD-LAB-001
    """
    # 1. Only critical bounds set (no normal bounds)
    # low_bound=None, high_bound=None, critical_low=5.0, critical_high=25.0
    r_only_critical = create_mock_range(
        low_bound=None, high_bound=None, critical_low=5.0, critical_high=25.0
    )

    # Within critical limits -> NORMAL (since there are no normal bounds to trigger LOW/HIGH)
    ind, out_of_range, bounds = evaluate_lab_value(15.0, r_only_critical)
    assert ind == "NORMAL"
    assert out_of_range is False
    assert json.loads(bounds) == {"low": None, "high": None}

    # Below critical_low -> LOW LOW
    ind, out_of_range, _ = evaluate_lab_value(4.9, r_only_critical)
    assert ind == "LOW LOW"
    assert out_of_range is True

    # Above critical_high -> HIGH HIGH
    ind, out_of_range, _ = evaluate_lab_value(25.1, r_only_critical)
    assert ind == "HIGH HIGH"
    assert out_of_range is True

    # 2. Only normal bounds set (no critical bounds)
    # low_bound=10.0, high_bound=20.0, critical_low=None, critical_high=None
    r_only_normal = create_mock_range(
        low_bound=10.0, high_bound=20.0, critical_low=None, critical_high=None
    )

    # Within limits -> NORMAL
    ind, out_of_range, bounds = evaluate_lab_value(15.0, r_only_normal)
    assert ind == "NORMAL"
    assert out_of_range is False
    assert json.loads(bounds) == {"low": 10.0, "high": 20.0}

    # Below low_bound -> LOW (no critical bounds to trigger LOW LOW, so even very low values are just LOW)
    ind, out_of_range, _ = evaluate_lab_value(1.0, r_only_normal)
    assert ind == "LOW"
    assert out_of_range is True

    # Above high_bound -> HIGH (no critical bounds to trigger HIGH HIGH, so even very high values are just HIGH)
    ind, out_of_range, _ = evaluate_lab_value(100.0, r_only_normal)
    assert ind == "HIGH"
    assert out_of_range is True

    # 3. Negative and zero lab values
    # Let's say we have normal bounds [-1.0, 1.0] and critical bounds [-2.0, 2.0]
    r_neg_zero = create_mock_range(
        low_bound=-1.0, high_bound=1.0, critical_low=-2.0, critical_high=2.0
    )

    # Zero value (0.0) -> NORMAL
    ind, out_of_range, _ = evaluate_lab_value(0.0, r_neg_zero)
    assert ind == "NORMAL"
    assert out_of_range is False

    # Negative value within normal (-0.5) -> NORMAL
    ind, out_of_range, _ = evaluate_lab_value(-0.5, r_neg_zero)
    assert ind == "NORMAL"
    assert out_of_range is False

    # Negative value out of normal but within critical (-1.5) -> LOW
    ind, out_of_range, _ = evaluate_lab_value(-1.5, r_neg_zero)
    assert ind == "LOW"
    assert out_of_range is True

    # Negative value below critical (-2.5) -> LOW LOW
    ind, out_of_range, _ = evaluate_lab_value(-2.5, r_neg_zero)
    assert ind == "LOW LOW"
    assert out_of_range is True

    # 4. Values exactly equal to a critical boundary
    # Boundary Inclusion Policy:
    # - Normal boundaries (low_bound, high_bound) are inclusive.
    # - Critical boundaries (critical_low, critical_high) are exclusive.
    # This means:
    # - "LOW LOW" is value < critical_low (value == critical_low is NOT "LOW LOW").
    # - "HIGH HIGH" is value > critical_high (value == critical_high is NOT "HIGH HIGH").
    # If we have low_bound=10.0, critical_low=5.0:
    # - value = 5.0 is exactly equal to critical_low. Since it is NOT < critical_low, it is NOT LOW LOW.
    # - But value = 5.0 is < low_bound (10.0), so it is "LOW".
    # If we have high_bound=20.0, critical_high=25.0:
    # - value = 25.0 is exactly equal to critical_high. Since it is NOT > critical_high, it is NOT HIGH HIGH.
    # - But value = 25.0 is > high_bound (20.0), so it is "HIGH".

    r_exact_bounds = create_mock_range(
        low_bound=10.0, high_bound=20.0, critical_low=5.0, critical_high=25.0
    )

    # Exactly equal to critical_low (5.0) -> LOW (NOT LOW LOW)
    ind, out_of_range, _ = evaluate_lab_value(5.0, r_exact_bounds)
    assert ind == "LOW"
    assert out_of_range is True

    # Exactly equal to critical_high (25.0) -> HIGH (NOT HIGH HIGH)
    ind, out_of_range, _ = evaluate_lab_value(25.0, r_exact_bounds)
    assert ind == "HIGH"
    assert out_of_range is True

    # Exactly equal to low_bound (10.0) -> NORMAL
    ind, out_of_range, _ = evaluate_lab_value(10.0, r_exact_bounds)
    assert ind == "NORMAL"
    assert out_of_range is False

    # Exactly equal to high_bound (20.0) -> NORMAL
    ind, out_of_range, _ = evaluate_lab_value(20.0, r_exact_bounds)
    assert ind == "NORMAL"
    assert out_of_range is False

    # Slightly below critical_low (4.99) -> LOW LOW
    ind, out_of_range, _ = evaluate_lab_value(4.99, r_exact_bounds)
    assert ind == "LOW LOW"
    assert out_of_range is True

    # Slightly above critical_high (25.01) -> HIGH HIGH
    ind, out_of_range, _ = evaluate_lab_value(25.01, r_exact_bounds)
    assert ind == "HIGH HIGH"
    assert out_of_range is True


@pytest.mark.asyncio
async def test_lab_reference_range_synonyms_and_audit():
    """Verify synonym columns map to physical columns without affecting audit-field behavior.

    @req:PRD-LAB-001
    """
    from sqlalchemy import select

    from apps.execution.database.core import db_manager
    from apps.execution.database.models import Base, LabReferenceRange

    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    try:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

        session_maker = db_manager.get_session_maker()
        async with session_maker() as session:
            # Create range using synonyms
            lab_range = LabReferenceRange(
                study_id="STUDY-SYN",
                test_code="WBC",
                test_name="White Blood Cells",
                source="LOCAL",  # synonym for lab_source
                sex_applicability="ALL",  # synonym for sex
                low_bound=4.0,  # synonym for range_low
                high_bound=11.0,  # synonym for range_high
                created_by="synonym_test",
                reason_for_change="Testing synonym mapping",
                version_index=1,
            )
            session.add(lab_range)
            await session.commit()

        # Re-open and assert
        async with session_maker() as session:
            result = await session.execute(
                select(LabReferenceRange).where(
                    LabReferenceRange.study_id == "STUDY-SYN"
                )
            )
            saved = result.scalar_one()

            # Assert they map to physical columns
            assert saved.lab_source == "LOCAL"
            assert saved.source == "LOCAL"
            assert saved.sex == "ALL"
            assert saved.sex_applicability == "ALL"
            assert saved.range_low == 4.0
            assert saved.low_bound == 4.0
            assert saved.range_high == 11.0
            assert saved.high_bound == 11.0

            # Assert audit fields persist
            assert saved.created_at is not None
            assert saved.created_by == "synonym_test"
            assert saved.reason_for_change == "Testing synonym mapping"
            assert saved.version_index == 1

    finally:
        await db_manager.close()


def test_negative_age_matching():
    """Verify that a negative decimal age (e.g. -0.25) matches correctly against reference ranges with negative bounds.

    @req:PRD-LAB-005
    """
    study = "STUDY-123"
    tcode = "WBC"
    unit = "10^9/L"

    # Define reference ranges:
    # 1. Prenatal range: -0.5 to 0.0 years
    r_prenatal = create_mock_range(
        id="prenatal",
        test_code=tcode,
        normalized_unit=unit,
        source="CENTRAL",
        age_low=-0.5,
        age_high=0.0,
    )
    # 2. Infant/neonatal range: 0.0 to 1.0 years
    r_infant = create_mock_range(
        id="infant",
        test_code=tcode,
        normalized_unit=unit,
        source="CENTRAL",
        age_low=0.0,
        age_high=1.0,
    )
    # 3. Adult range: 18.0 to 100.0 years
    r_adult = create_mock_range(
        id="adult",
        test_code=tcode,
        normalized_unit=unit,
        source="CENTRAL",
        age_low=18.0,
        age_high=100.0,
    )

    ranges = [r_prenatal, r_infant, r_adult]

    # Matching for a prenatal subject with decimal age -0.25 years
    matched = select_reference_range(
        ranges, study, tcode, unit, "CENTRAL", sex="F", age=-0.25, site_id=None
    )
    assert matched is not None
    assert matched["id"] == "prenatal"
