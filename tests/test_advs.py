"""Unit tests for ADVS dataset derivation."""

from apps.execution.biostat.advs import derive_advs


def test_advs_basic_derivation():
    """Verify ADVS basic variables mapping from VS and ADSL."""
    adsl_records = [
        {
            "USUBJID": "STUDY-101-001",
            "STUDYID": "STUDY-101",
            "SUBJID": "001",
            "TRTSDT": 24350,  # 2026-09-01
        }
    ]

    vs_records = [
        {
            "USUBJID": "STUDY-101-001",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 120,
            "VSSTRESC": "120",
            "VSSTRESU": "mmHg",
            "VSDTC": "2026-09-01",
            "VISIT": "Week 1",
            "VISITNUM": 2.0,
            "VSBLFL": "Y",
        }
    ]

    advs = derive_advs(adsl_records, vs_records)
    assert len(advs) == 1
    rec = advs[0]

    # Check mapped variables
    assert rec["PARAMCD"] == "SYSBP"
    assert rec["PARAM"] == "Systolic Blood Pressure (mmHg)"
    assert rec["AVAL"] == 120.0
    assert rec["AVALC"] == "120"
    assert rec["AVISIT"] == "Week 1"
    assert rec["AVISITN"] == 2.0
    assert rec["ADY"] == 1  # 2026-09-01 is Day 1 relative to 2026-09-01 (TRTSDT)


def test_advs_baseline_selection_and_flags():
    """Verify that exactly one baseline record per subject/parameter is selected and marked ABLFL='Y'."""
    adsl_records = [
        {
            "USUBJID": "SUBJ-A",
            "TRTSDT": 24350,  # 2026-09-01
        }
    ]

    # Two records marked VSBLFL == "Y" for SYSBP. The latest by date/seq should be chosen deterministically.
    vs_records = [
        {
            "USUBJID": "SUBJ-A",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 118,
            "VSSTRESC": "118",
            "VSSTRESU": "mmHg",
            "VSDTC": "2026-08-30",
            "VISIT": "Screening 1",
            "VISITNUM": 1.1,
            "VSBLFL": "Y",
            "VSSEQ": 1,
        },
        {
            "USUBJID": "SUBJ-A",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 122,
            "VSSTRESC": "122",
            "VSSTRESU": "mmHg",
            "VSDTC": "2026-08-31",
            "VISIT": "Screening 2",
            "VISITNUM": 1.2,
            "VSBLFL": "Y",
            "VSSEQ": 2,
        },
    ]

    advs = derive_advs(adsl_records, vs_records)
    assert len(advs) == 2

    # Check that second record (latest by date/seq) is selected as baseline
    rec1 = next(r for r in advs if r["VSSEQ"] == 1)
    rec2 = next(r for r in advs if r["VSSEQ"] == 2)

    assert rec1["ABLFL"] is None
    assert rec2["ABLFL"] == "Y"

    # BASE should be populated for both with the baseline value (122.0)
    assert rec1["BASE"] == 122.0
    assert rec2["BASE"] == 122.0


def test_advs_change_metrics_and_division_by_zero():
    """Verify CHG and PCHG computations, post-baseline conditions, and explicit division-by-zero handling."""
    adsl_records = [
        {
            "USUBJID": "SUBJ-B",
            "TRTSDT": 24350,  # 2026-09-01
        }
    ]

    vs_records = [
        # Baseline: SYSBP = 100
        {
            "USUBJID": "SUBJ-B",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 100,
            "VSSTRESC": "100",
            "VSSTRESU": "mmHg",
            "VSDTC": "2026-08-31",
            "VISIT": "Screening",
            "VISITNUM": 1.0,
            "VSBLFL": "Y",
            "VSSEQ": 1,
        },
        # Post-baseline 1: SYSBP = 110 (CHG = 10, PCHG = 10%)
        {
            "USUBJID": "SUBJ-B",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 110,
            "VSSTRESC": "110",
            "VSSTRESU": "mmHg",
            "VSDTC": "2026-09-02",
            "VISIT": "Week 1",
            "VISITNUM": 2.0,
            "VSSEQ": 2,
        },
        # Baseline for DIA_PULSE = 0 (to test division by zero)
        {
            "USUBJID": "SUBJ-B",
            "VSTESTCD": "PULSE",
            "VSTEST": "Pulse Rate",
            "VSSTRESN": 0,
            "VSSTRESC": "0",
            "VSSTRESU": "beats/min",
            "VSDTC": "2026-08-31",
            "VISIT": "Screening",
            "VISITNUM": 1.0,
            "VSBLFL": "Y",
            "VSSEQ": 3,
        },
        # Post-baseline for PULSE = 60 (CHG = 60, PCHG = None due to BASE = 0)
        {
            "USUBJID": "SUBJ-B",
            "VSTESTCD": "PULSE",
            "VSTEST": "Pulse Rate",
            "VSSTRESN": 60,
            "VSSTRESC": "60",
            "VSSTRESU": "beats/min",
            "VSDTC": "2026-09-02",
            "VISIT": "Week 1",
            "VISITNUM": 2.0,
            "VSSEQ": 4,
        },
    ]

    advs = derive_advs(adsl_records, vs_records)
    assert len(advs) == 4

    sysbp_post = next(r for r in advs if r["VSSEQ"] == 2)
    assert sysbp_post["BASE"] == 100.0
    assert sysbp_post["CHG"] == 10.0
    assert sysbp_post["PCHG"] == 10.0

    pulse_post = next(r for r in advs if r["VSSEQ"] == 4)
    assert pulse_post["BASE"] == 0.0
    assert pulse_post["CHG"] == 60.0
    assert pulse_post["PCHG"] is None  # division by zero handled explicitly


def test_advs_no_coercion_of_missing_numeric_values():
    """Verify that missing/unavailable numeric results are not coerced into misleading values (e.g. 0.0)."""
    adsl_records = [
        {
            "USUBJID": "SUBJ-C",
            "TRTSDT": 24350,  # 2026-09-01
        }
    ]

    vs_records = [
        {
            "USUBJID": "SUBJ-C",
            "VSTESTCD": "TEMP",
            "VSTEST": "Temperature",
            "VSSTRESN": None,  # Unavailable numeric result
            "VSSTRESC": "Measurement Failed",
            "VSSTRESU": "C",
            "VSDTC": "2026-08-31",
            "VISIT": "Screening",
            "VISITNUM": 1.0,
            "VSBLFL": "Y",
            "VSSEQ": 1,
        },
        {
            "USUBJID": "SUBJ-C",
            "VSTESTCD": "TEMP",
            "VSTEST": "Temperature",
            "VSSTRESN": 37.0,
            "VSSTRESC": "37",
            "VSSTRESU": "C",
            "VSDTC": "2026-09-02",
            "VISIT": "Week 1",
            "VISITNUM": 2.0,
            "VSSEQ": 2,
        },
    ]

    advs = derive_advs(adsl_records, vs_records)
    assert len(advs) == 2

    rec1 = next(r for r in advs if r["VSSEQ"] == 1)
    rec2 = next(r for r in advs if r["VSSEQ"] == 2)

    # Base should be None since baseline numeric value (VSSTRESN) was None
    assert rec1["AVAL"] is None
    assert rec1["AVALC"] == "Measurement Failed"
    assert rec1["BASE"] is None

    # Change metrics should be None because base is None
    assert rec2["BASE"] is None
    assert rec2["CHG"] is None
    assert rec2["PCHG"] is None


def test_advs_date_and_visit_fallback():
    """Verify that case-insensitive visit name and number lookups, and missing values, are handled correctly."""
    adsl_records = [
        {
            "USUBJID": "SUBJ-D",
            "TRTSDT": 24350,  # 2026-09-01
        }
    ]

    vs_records = [
        {
            "USUBJID": "SUBJ-D",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 120,
            "VSSTRESC": "120",
            "VSSTRESU": "mmHg",
            "VSDTC": "2026-09-02",
            "visit_name": "Follow-Up 1",  # lowercase key
            "visit_num": 3.0,  # lowercase key
            "VSSEQ": 1,
        }
    ]

    advs = derive_advs(adsl_records, vs_records)
    assert len(advs) == 1
    rec = advs[0]

    assert rec["AVISIT"] == "Follow-Up 1"
    assert rec["AVISITN"] == 3.0


def test_advs_missing_baseline_behavior():
    """Verify that post-baseline records handle a completely missing baseline parameter without crashing."""
    adsl_records = [
        {
            "USUBJID": "SUBJ-E",
            "TRTSDT": 24350,  # 2026-09-01
        }
    ]

    # No baseline record (no records with VSBLFL == 'Y' and no records with ADY <= 0)
    vs_records = [
        {
            "USUBJID": "SUBJ-E",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 125,
            "VSSTRESC": "125",
            "VSSTRESU": "mmHg",
            "VSDTC": "2026-09-05",  # Day 5
            "VISIT": "Week 1",
            "VISITNUM": 2.0,
            "VSSEQ": 1,
        }
    ]

    advs = derive_advs(adsl_records, vs_records)
    assert len(advs) == 1
    rec = advs[0]

    assert rec["ABLFL"] is None
    assert rec["BASE"] is None
    assert rec["CHG"] is None
    assert rec["PCHG"] is None
