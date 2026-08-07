from datetime import date, datetime

from apps.execution.biostat import derive_adsl
from apps.execution.biostat.dates import (
    impute_partial_date,
    parse_partial_date,
    to_date_obj,
    to_sas_date,
)


def test_parse_partial_date():
    assert parse_partial_date("2026-08-05") == (2026, 8, 5)
    assert parse_partial_date("2026-08-UN") == (2026, 8, None)
    assert parse_partial_date("2026-UN-UN") == (2026, None, None)
    assert parse_partial_date("2026-08-05T12:00:00") == (2026, 8, 5)
    assert parse_partial_date("2026") == (2026, None, None)
    assert parse_partial_date("") == (None, None, None)
    assert parse_partial_date(None) == (None, None, None)
    assert parse_partial_date("invalid-date-format") == (None, None, None)
    assert parse_partial_date("0000-00-00") == (None, None, None)
    assert parse_partial_date("2026-00-05") == (2026, None, 5)
    assert parse_partial_date("2026-abc-05") == (2026, None, 5)


def test_to_date_obj():
    assert to_date_obj("2026-08-05") == date(2026, 8, 5)
    assert to_date_obj(date(2026, 8, 5)) == date(2026, 8, 5)
    # Datetime object should also be resolved to date object
    dt = datetime(2026, 8, 5, 12, 0, 0)
    assert to_date_obj(dt) == date(2026, 8, 5)
    assert to_date_obj(None) is None
    assert to_date_obj("invalid") is None


def test_to_sas_date():
    assert to_sas_date("1960-01-01") == 0
    assert to_sas_date("1960-01-02") == 1
    assert to_sas_date("1959-12-31") == -1
    # 2026-08-05 is 24323 days since 1960-01-01
    assert to_sas_date("2026-08-05") == (date(2026, 8, 5) - date(1960, 1, 1)).days
    assert to_sas_date("invalid-date") is None


def test_impute_partial_date():
    # Complete date
    assert impute_partial_date("2026-08-05") == "2026-08-05"
    assert impute_partial_date("2026-02-30") is None  # Invalid day

    # Start direction, year & month only
    assert impute_partial_date("2026-08-UN", "START") == "2026-08-01"
    assert (
        impute_partial_date("2026-08-UN", "START", treatment_start_date="2026-08-15")
        == "2026-08-15"
    )
    assert (
        impute_partial_date("2026-08-UN", "START", treatment_start_date="2026-09-15")
        == "2026-08-01"
    )

    # Start direction, year only
    assert impute_partial_date("2026-UN-UN", "START") == "2026-01-01"
    assert (
        impute_partial_date("2026-UN-UN", "START", treatment_start_date="2026-08-15")
        == "2026-08-15"
    )
    assert (
        impute_partial_date("2026-UN-UN", "START", treatment_start_date="2025-08-15")
        == "2026-01-01"
    )

    # End direction, year & month only
    assert impute_partial_date("2026-02-UN", "END") == "2026-02-28"
    assert impute_partial_date("2024-02-UN", "END") == "2024-02-29"  # Leap year
    assert impute_partial_date("2026-13-UN", "END") is None  # Invalid month

    # End direction, year only
    assert impute_partial_date("2026-UN-UN", "END") == "2026-12-31"
    assert (
        impute_partial_date("2026-UN-UN", "END", end_of_study_date="2026-06-15")
        == "2026-06-15"
    )
    assert (
        impute_partial_date("2026-UN-UN", "END", end_of_study_date="2027-06-15")
        == "2026-12-31"
    )

    # Invalid options
    assert impute_partial_date("2026-08-UN", "INVALID") is None
    assert impute_partial_date("UN-UN-UN") is None


def test_derive_adsl_basic():
    subjects = [
        {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-Z",
            "site_id": "SITE-A",
            "demographics": {
                "arm": "Active Arm",
            },
        }
    ]

    observations = [
        # Exposure (Earliest start, latest end)
        {
            "subject_id": "SUBJ-001",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2026-08-01",
        },
        {
            "subject_id": "SUBJ-001",
            "domain": "EX",
            "test_code": "EXENDTC",
            "value_string": "2026-08-15",
        },
        # Disposition (Randomization Date)
        {
            "subject_id": "SUBJ-001",
            "domain": "DS",
            "page_id": "rand_page",
            "test_code": "DSDECOD",
            "value_string": "RANDOMIZED",
        },
        {
            "subject_id": "SUBJ-001",
            "domain": "DS",
            "page_id": "rand_page",
            "test_code": "DSSTDTC",
            "value_string": "2026-07-25",
        },
        # Disposition (End of Study)
        {
            "subject_id": "SUBJ-001",
            "domain": "DS",
            "page_id": "eos_page",
            "test_code": "DSCAT",
            "value_string": "DISPOSITION EVENT",
        },
        {
            "subject_id": "SUBJ-001",
            "domain": "DS",
            "page_id": "eos_page",
            "test_code": "DSSCAT",
            "value_string": "STUDY COMPLETION/WITHDRAWAL",
        },
        {
            "subject_id": "SUBJ-001",
            "domain": "DS",
            "page_id": "eos_page",
            "test_code": "DSSTDTC",
            "value_string": "2026-08-20",
        },
    ]

    adsl = derive_adsl(subjects, observations)
    assert len(adsl) == 1

    rec = adsl[0]
    assert rec["STUDYID"] == "STUDY-Z"
    assert rec["USUBJID"] == "STUDY-Z-SITE-A-SUBJ-001"
    assert rec["SUBJID"] == "SUBJ-001"
    assert rec["SITEID"] == "SITE-A"
    assert rec["ARM"] == "Active Arm"
    assert rec["ACTARM"] == "Active Arm"
    assert rec["TRT01P"] == "Active Arm"
    assert rec["TRT01A"] == "Active Arm"

    assert rec["TRTSDT"] == to_sas_date("2026-08-01")
    assert rec["TRTEDT"] == to_sas_date("2026-08-15")
    assert rec["RANDT"] == to_sas_date("2026-07-25")
    assert rec["EOSDT"] == to_sas_date("2026-08-20")
    assert rec["DTHDT"] is None

    assert rec["SAFFL"] == "Y"
    assert rec["ITTFL"] == "Y"


def test_derive_adsl_partial_dates_and_population_flags():
    subjects = [
        # Subject with partial dates and not randomized (No RANDT) -> ITTFL="N"
        {
            "subject_id": "SUBJ-002",
            "study_id": "STUDY-Z",
            "site_id": "SITE-B",
            "demographics": {
                "arm": "Placebo Arm",
            },
        },
        # Subject with no exposure -> SAFFL="N"
        {
            "subject_id": "SUBJ-003",
            "study_id": "STUDY-Z",
            "site_id": "SITE-B",
            "demographics": {
                "arm": "Active Arm",
            },
        },
    ]

    observations = [
        # SUBJ-002: exposure with partial dates
        {
            "subject_id": "SUBJ-002",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2026-08-UN",  # Imputes to 2026-08-01
        },
        {
            "subject_id": "SUBJ-002",
            "domain": "EX",
            "test_code": "EXENDTC",
            "value_string": "2026-08-UN",  # Imputes to 2026-08-31
        },
        # SUBJ-003: Randomized but never dosed
        {
            "subject_id": "SUBJ-003",
            "domain": "DS",
            "page_id": "rand_page_3",
            "test_code": "DSDECOD",
            "value_string": "RANDOMIZED",
        },
        {
            "subject_id": "SUBJ-003",
            "domain": "DS",
            "page_id": "rand_page_3",
            "test_code": "DSSTDTC",
            "value_string": "2026-08-01",
        },
    ]

    adsl = derive_adsl(subjects, observations)
    assert len(adsl) == 2

    # Verify SUBJ-002
    rec2 = next(r for r in adsl if r["SUBJID"] == "SUBJ-002")
    assert rec2["TRTSDT"] == to_sas_date("2026-08-01")
    assert rec2["TRTEDT"] == to_sas_date("2026-08-31")
    assert rec2["RANDT"] is None
    assert rec2["SAFFL"] == "Y"
    assert rec2["ITTFL"] == "N"

    # Verify SUBJ-003
    rec3 = next(r for r in adsl if r["SUBJID"] == "SUBJ-003")
    assert rec3["TRTSDT"] is None
    assert rec3["TRTEDT"] is None
    assert rec3["RANDT"] == to_sas_date("2026-08-01")
    assert rec3["SAFFL"] == "N"
    assert rec3["ITTFL"] == "Y"


def test_derive_adsl_edge_cases():
    subjects = [
        {
            "subject_id": "SUBJ-004",
            "study_id": "STUDY-Z",
            "site_id": "SITE-C",
            "actarm": "Actual Dose 10mg",
            "randomization_date": "2026-07-20",
            "end_of_study_date": "2026-09-01",
            "death_date": "2026-09-02",
            "demographics": {
                "arm": "Planned Dose 10mg",
            },
        }
    ]

    # No observations provided
    adsl = derive_adsl(subjects, [])
    assert len(adsl) == 1
    rec = adsl[0]
    assert rec["ACTARM"] == "Actual Dose 10mg"
    assert rec["ARM"] == "Planned Dose 10mg"
    assert rec["TRT01P"] == "Planned Dose 10mg"
    assert rec["TRT01A"] == "Actual Dose 10mg"
    assert rec["RANDT"] == to_sas_date("2026-07-20")
    assert rec["EOSDT"] == to_sas_date("2026-09-01")
    assert rec["DTHDT"] == to_sas_date("2026-09-02")


def test_derive_adsl_observation_based_death_and_actarm():
    subjects = [
        {
            "subject_id": "SUBJ-005",
            "study_id": "STUDY-Z",
            "site_id": "SITE-C",
            "demographics": {
                "arm": "Active",
            },
        }
    ]

    observations = [
        # EXTRT as active arm
        {
            "subject_id": "SUBJ-005",
            "domain": "EX",
            "test_code": "EXTRT",
            "value_string": "Actual High Dose",
        },
        # DTHDTC
        {
            "subject_id": "SUBJ-005",
            "domain": "DM",
            "test_code": "DTHDTC",
            "value_string": "2026-09-10",
        },
        # DSDECOD for EOS fallback
        {
            "subject_id": "SUBJ-005",
            "domain": "DS",
            "page_id": "ds_eos",
            "test_code": "DSDECOD",
            "value_string": "STUDY COMPLETION",
        },
        {
            "subject_id": "SUBJ-005",
            "domain": "DS",
            "page_id": "ds_eos",
            "test_code": "DSSTDTC",
            "value_string": "2026-09-11",
        },
    ]

    adsl = derive_adsl(subjects, observations)
    assert len(adsl) == 1
    rec = adsl[0]
    assert rec["ACTARM"] == "Actual High Dose"
    assert rec["DTHDT"] == to_sas_date("2026-09-10")
    assert rec["EOSDT"] == to_sas_date("2026-09-11")


def test_derive_adsl_additional_branches():
    # Test fallback Site ID options
    subjects_site_id = [
        {
            "subject_id": "SUBJ-SITE-1",
            "study_id": "S1",
            "demographics": {"site_id": "SITE-D", "ARM": "A1"},
        },
        {
            "subject_id": "SUBJ-SITE-2",
            "study_id": "S1",
            "demographics": {"siteID": "SITE-E", "ARM": "A1"},
        },
        {"subject_id": "SUBJ-SITE-3", "study_id": "S1", "demographics": {"ARM": "A1"}},
        # Subject without subject_id should be skipped
        {"study_id": "S1", "demographics": {"ARM": "A1"}},
    ]
    adsl = derive_adsl(subjects_site_id, [])
    assert len(adsl) == 3
    assert adsl[0]["SITEID"] == "SITE-D"
    assert adsl[1]["SITEID"] == "SITE-E"
    assert adsl[2]["SITEID"] == "001"

    # Test fallback arm from observations
    subjects_arm_obs = [
        {"subject_id": "SUBJ-ARM-OBS", "study_id": "S1", "demographics": {}}
    ]
    observations_arm_obs = [
        {
            "subject_id": "SUBJ-ARM-OBS",
            "domain": "DM",
            "test_code": "ARM",
            "value_string": "Observed Arm Value",
        }
    ]
    adsl_arm_obs = derive_adsl(subjects_arm_obs, observations_arm_obs)
    assert adsl_arm_obs[0]["ARM"] == "Observed Arm Value"

    # Test fallback USUBJID and various date properties
    subjects_props = [
        {
            "subject_id": "SUBJ-PROPS",
            "study_id": "S1",
            "USUBJID": "CUSTOM-USUBJID",
            "rfstdtc": "2026-08-01",
            "rfendtc": "2026-08-10",
            "eosdt": "2026-08-12",
            "randt": "2026-07-20",
            "DTHDTC": "2026-08-15",
            "demographics": {},
        }
    ]
    adsl_props = derive_adsl(subjects_props, [])
    assert adsl_props[0]["USUBJID"] == "CUSTOM-USUBJID"
    assert adsl_props[0]["TRTSDT"] == to_sas_date("2026-08-01")
    assert adsl_props[0]["TRTEDT"] == to_sas_date("2026-08-10")
    assert adsl_props[0]["EOSDT"] == to_sas_date("2026-08-12")
    assert adsl_props[0]["RANDT"] == to_sas_date("2026-07-20")
    assert adsl_props[0]["DTHDT"] == to_sas_date("2026-08-15")

    # Test fallback date properties in demographics
    subjects_demo_props = [
        {
            "subject_id": "SUBJ-DEMO-PROPS",
            "study_id": "S1",
            "demographics": {
                "usubjid": "DEMO-USUBJID",
                "rfstdtc": "2026-08-01",
                "rfendtc": "2026-08-10",
                "eosdt": "2026-08-12",
                "randt": "2026-07-20",
                "DTHDTC": "2026-08-15",
            },
        }
    ]
    adsl_demo_props = derive_adsl(subjects_demo_props, [])
    assert adsl_demo_props[0]["USUBJID"] == "DEMO-USUBJID"
    assert adsl_demo_props[0]["TRTSDT"] == to_sas_date("2026-08-01")
    assert adsl_demo_props[0]["TRTEDT"] == to_sas_date("2026-08-10")
    assert adsl_demo_props[0]["EOSDT"] == to_sas_date("2026-08-12")
    assert adsl_demo_props[0]["RANDT"] == to_sas_date("2026-07-20")
    assert adsl_demo_props[0]["DTHDT"] == to_sas_date("2026-08-15")


def test_derive_adsl_with_datetime_objects():
    from datetime import datetime

    subjects = [
        {
            "subject_id": "SUBJ-DATETIMES",
            "study_id": "S1",
            "demographics": {
                "ARM": "Active",
            },
        }
    ]
    observations = [
        {
            "subject_id": "SUBJ-DATETIMES",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "observation_date": datetime(2026, 8, 1, 10, 0, 0),
        },
        {
            "subject_id": "SUBJ-DATETIMES",
            "domain": "EX",
            "test_code": "EXENDTC",
            "observation_date": datetime(2026, 8, 10, 15, 30, 0),
        },
        {
            "subject_id": "SUBJ-DATETIMES",
            "domain": "DS",
            "test_code": "DSDECOD",
            "value_string": "RANDOMIZED",
            "observation_date": datetime(2026, 7, 20, 9, 0, 0),
        },
    ]
    adsl = derive_adsl(subjects, observations)
    assert len(adsl) == 1
    assert adsl[0]["TRTSDT"] == to_sas_date("2026-08-01")
    assert adsl[0]["TRTEDT"] == to_sas_date("2026-08-10")
    assert adsl[0]["RANDT"] == to_sas_date("2026-07-20")


def test_derive_adsl_various_fallback_branches():
    # DS events without DSCAT/DSSCAT but matching DSDECOD fallback logic
    subjects = [
        {
            "subject_id": "SUBJ-DS-FALLBACK",
            "study_id": "S1",
            "demographics": {"ARM": "Placebo"},
        }
    ]
    observations = [
        # Match DSDECOD for "WITHDRAWAL" with page_id matching or missing
        {
            "subject_id": "SUBJ-DS-FALLBACK",
            "domain": "DS",
            "test_code": "DSDECOD",
            "value_string": "WITHDRAWAL",
            "observation_date": "2026-08-30T10:00:00",
        },
        # Randomization page with no explicit DSSTDTC/DSDTC variable but matched by observation_date
        {
            "subject_id": "SUBJ-DS-FALLBACK",
            "domain": "DS",
            "test_code": "DSDECOD",
            "value_string": "RANDOMIZED",
            "observation_date": "2026-08-01T09:00:00",
        },
    ]
    adsl = derive_adsl(subjects, observations)
    assert len(adsl) == 1
    assert adsl[0]["RANDT"] == to_sas_date("2026-08-01")
    assert adsl[0]["EOSDT"] == to_sas_date("2026-08-30")
