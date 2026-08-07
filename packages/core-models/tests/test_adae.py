from datetime import date

from apps.execution.biostat import derive_adae
from apps.execution.biostat.adae import from_sas_date
from apps.execution.biostat.dates import to_sas_date


def test_from_sas_date():
    assert from_sas_date(None) is None
    assert from_sas_date(0) == "1960-01-01"
    assert from_sas_date(1) == "1960-01-02"
    assert from_sas_date(-1) == "1959-12-31"
    # test 2026-08-05
    sas_day = (date(2026, 8, 5) - date(1960, 1, 1)).days
    assert from_sas_date(sas_day) == "2026-08-05"


def test_derive_adae_basic_join():
    adsl = [
        {
            "USUBJID": "STUDY-01-SUBJ-001",
            "STUDYID": "STUDY-01",
            "SUBJID": "SUBJ-001",
            "SITEID": "SITE-A",
            "ARM": "Active",
            "ACTARM": "Active",
            "TRT01P": "Active",
            "TRT01A": "Active",
            "TRTSDT": to_sas_date("2026-08-01"),
            "TRTEDT": to_sas_date("2026-08-15"),
            "EOSDT": to_sas_date("2026-08-20"),
            "SAFFL": "Y",
            "ITTFL": "Y",
        }
    ]

    ae = [
        {
            "USUBJID": "STUDY-01-SUBJ-001",
            "AESEQ": 1,
            "AETERM": "HEADACHE",
            "AESTDTC": "2026-08-05",
            "AEENDTC": "2026-08-07",
            "AESEV": "MILD",
        }
    ]

    adae = derive_adae(adsl, ae)
    assert len(adae) == 1
    rec = adae[0]

    # Check join fields from ADSL are preserved
    assert rec["USUBJID"] == "STUDY-01-SUBJ-001"
    assert rec["SITEID"] == "SITE-A"
    assert rec["ARM"] == "Active"

    # Check AE fields are copied
    assert rec["AESEQ"] == 1
    assert rec["AETERM"] == "HEADACHE"

    # Check analysis dates
    assert rec["ASTDT"] == to_sas_date("2026-08-05")
    assert rec["AENDT"] == to_sas_date("2026-08-07")

    # Check relative day calculation
    # ASTDT = 2026-08-05, TRTSDT = 2026-08-01. Diff is 4. Since ASTDT >= TRTSDT, ASTDY = 4 + 1 = 5.
    assert rec["ASTDY"] == 5
    assert rec["AENDY"] == 7

    # Check treatment emergent flag (starts after TRTSDT and before TRTEDT + 30)
    assert rec["TRTEMFL"] == "Y"

    # Check severity mapping
    assert rec["AESEVN"] == 1


def test_derive_adae_partial_dates_imputation():
    adsl = [
        {
            "USUBJID": "STUDY-01-SUBJ-001",
            "TRTSDT": to_sas_date("2026-08-15"),
            "TRTEDT": to_sas_date("2026-08-30"),
            "EOSDT": to_sas_date("2026-09-10"),
        }
    ]

    ae = [
        # Start date partial: Year and Month match TRTSDT month, imputes to TRTSDT
        {
            "USUBJID": "STUDY-01-SUBJ-001",
            "AESTDTC": "2026-08-UN",
            "AEENDTC": "2026-09-UN",  # End date partial: imputes to end of Sept month (30) or EOSDT
            "AESEV": "MODERATE",
        }
    ]

    adae = derive_adae(adsl, ae)
    assert len(adae) == 1
    rec = adae[0]

    assert rec["ASTDT"] == to_sas_date("2026-08-15")  # Imputed to treatment start
    # AEENDTC is 2026-09-UN, which usually imputes to 2026-09-30.
    # But EOSDT is 2026-09-10. Wait, let's verify if end_of_study_date is cap or if it is only used for year-only.
    # Let's check dates.py logic: for "Year and Month are known, Day is missing":
    # calendar.monthrange(y, m)[1] -> date(y, m, last_day). It does not cap with EOSDT for month-only.
    # So it should be 2026-09-30. Let's verify!
    assert rec["AENDT"] == to_sas_date("2026-09-30")
    assert rec["AESEVN"] == 2


def test_derive_adae_relative_day_formula():
    trtsdt_str = "2026-08-10"
    trtsdt = to_sas_date(trtsdt_str)

    adsl = [{"USUBJID": "SUBJ-01", "TRTSDT": trtsdt}]

    ae = [
        # ASTDT == TRTSDT (Should be +1)
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-10"},
        # ASTDT > TRTSDT (Should be +1)
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-11"},
        # ASTDT < TRTSDT (Should be negative, no +1)
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-09"},
    ]

    adae = derive_adae(adsl, ae)
    assert len(adae) == 3

    assert adae[0]["ASTDT"] == trtsdt
    assert adae[0]["ASTDY"] == 1

    assert adae[1]["ASTDT"] == trtsdt + 1
    assert adae[1]["ASTDY"] == 2

    assert adae[2]["ASTDT"] == trtsdt - 1
    assert adae[2]["ASTDY"] == -1


def test_derive_adae_treatment_emergent_safety_window():
    trtsdt = to_sas_date("2026-08-10")
    trtedt = to_sas_date("2026-08-20")

    adsl = [{"USUBJID": "SUBJ-01", "TRTSDT": trtsdt, "TRTEDT": trtedt}]

    ae = [
        # Onset before treatment start -> N
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-09", "AETERM": "Pre-trt"},
        # Onset exactly on treatment start -> Y
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-10", "AETERM": "At trt start"},
        # Onset on treatment end -> Y
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-20", "AETERM": "At trt end"},
        # Onset exactly on treatment end + 30 days -> Y
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-09-19", "AETERM": "At 30 days post"},
        # Onset on treatment end + 31 days -> N
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-09-20", "AETERM": "At 31 days post"},
    ]

    adae = derive_adae(adsl, ae)
    assert len(adae) == 5

    assert adae[0]["TRTEMFL"] == "N"
    assert adae[1]["TRTEMFL"] == "Y"
    assert adae[2]["TRTEMFL"] == "Y"
    assert adae[3]["TRTEMFL"] == "Y"
    assert adae[4]["TRTEMFL"] == "N"


def test_derive_adae_missing_dates_and_ongoing():
    trtsdt = to_sas_date("2026-08-10")

    # TRTEDT is None (e.g. ongoing treatment)
    adsl = [{"USUBJID": "SUBJ-01", "TRTSDT": trtsdt, "TRTEDT": None}]

    ae = [
        # ASTDT is None / invalid -> TRTEMFL is N, ASTDY is None
        {"USUBJID": "SUBJ-01", "AESTDTC": "INVALID-DATE", "AETERM": "Invalid Date"},
        # Missing AESTDTC -> TRTEMFL is N, ASTDY is None
        {"USUBJID": "SUBJ-01", "AESTDTC": "", "AETERM": "Missing Date"},
        # Onset on/after TRTSDT -> Y (since ongoing treatment, no upper limit)
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-09-30", "AETERM": "Late event"},
    ]

    adae = derive_adae(adsl, ae)
    assert len(adae) == 3

    # Invalid Date
    assert adae[0]["ASTDT"] is None
    assert adae[0]["ASTDY"] is None
    assert adae[0]["TRTEMFL"] == "N"

    # Missing Date
    assert adae[1]["ASTDT"] is None
    assert adae[1]["ASTDY"] is None
    assert adae[1]["TRTEMFL"] == "N"

    # Late event
    assert adae[2]["ASTDT"] == to_sas_date("2026-09-30")
    assert adae[2]["ASTDY"] == (to_sas_date("2026-09-30") - trtsdt + 1)
    assert adae[2]["TRTEMFL"] == "Y"


def test_derive_adae_severity_mappings():
    adsl = [{"USUBJID": "SUBJ-01", "TRTSDT": to_sas_date("2026-08-10")}]

    ae = [
        {"USUBJID": "SUBJ-01", "AESEV": "MILD"},
        {"USUBJID": "SUBJ-01", "AESEV": "moderate"},
        {"USUBJID": "SUBJ-01", "AESEV": "severe"},
        {"USUBJID": "SUBJ-01", "AESEV": "GRADE 1"},
        {"USUBJID": "SUBJ-01", "AESEV": "GRADE 3"},
        {"USUBJID": "SUBJ-01", "AESEV": "UNKNOWN_SEV"},
        {"USUBJID": "SUBJ-01", "AESEV": None},
    ]

    adae = derive_adae(adsl, ae)
    assert len(adae) == 7

    assert adae[0]["AESEVN"] == 1
    assert adae[1]["AESEVN"] == 2
    assert adae[2]["AESEVN"] == 3
    assert adae[3]["AESEVN"] == 1
    assert adae[4]["AESEVN"] == 3
    assert adae[5]["AESEVN"] is None
    assert adae[6]["AESEVN"] is None


def test_derive_adae_unmatched_subject_skipped():
    adsl = [{"USUBJID": "SUBJ-01", "TRTSDT": to_sas_date("2026-08-10")}]

    ae = [
        # Match
        {"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-12"},
        # No Match
        {"USUBJID": "SUBJ-02", "AESTDTC": "2026-08-12"},
    ]

    adae = derive_adae(adsl, ae)
    assert len(adae) == 1
    assert adae[0]["USUBJID"] == "SUBJ-01"
