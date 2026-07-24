"""
Unit tests for EDC-to-SDTM Mapper.

Tests the stateless rule-based mappings without any database I/O.
"""

from datetime import date, datetime, timezone

import pytest
from sdtm.enums import (
    AEOutcome,
    AERelationship,
    AESeriousness,
    AESeverity,
    Race,
    Sex,
)

from apps.execution.demographics import encrypt_demographics
from apps.execution.sdtm_mapper import (
    compute_age,
    get_demographics,
    map_ae,
    map_cm,
    map_dm,
    map_lb,
    map_to_sdtm,
    map_vs,
    to_dtc,
)


class MockObject:
    """Mock database ORM object helper."""

    def __init__(self, **kwargs):
        for k, v in kwargs.items():
            setattr(self, k, v)


def test_to_dtc():
    """Verify DTC normalization for dates, datetimes, and strings."""
    assert to_dtc(None) is None
    assert to_dtc("  ") is None
    assert to_dtc("2026/08/02") == "2026-08-02"
    assert to_dtc("2026-08-02") == "2026-08-02"

    dt = datetime(2026, 8, 2, 12, 0, 0, tzinfo=timezone.utc)
    assert to_dtc(dt) == "2026-08-02T12:00:00Z"

    d = date(2026, 8, 2)
    assert to_dtc(d) == "2026-08-02"


def test_get_demographics():
    """Verify decryption of demographic information."""
    raw = {"gender": "Male", "race": "White", "birthdate": "1990-05-15"}
    enc = encrypt_demographics(raw)

    # 1. From dict (encrypted)
    res = get_demographics({"encrypted_demographics": enc})
    assert res["gender"] == "Male"

    # 2. From dict (unencrypted)
    res = get_demographics({"demographics": raw})
    assert res["gender"] == "Male"

    # 3. From object (encrypted)
    obj_enc = MockObject(encrypted_demographics=enc)
    res = get_demographics(obj_enc)
    assert res["gender"] == "Male"

    # 4. From object (unencrypted)
    obj_raw = MockObject(demographics=raw)
    res = get_demographics(obj_raw)
    assert res["gender"] == "Male"


def test_compute_age():
    """Verify age computation logic under various precisions."""
    # Complete precision (exact completed years)
    assert compute_age("2026-08-02", "1990-05-15") == 36
    assert compute_age("2026-05-14", "1990-05-15") == 35  # Birthday not yet occurred
    assert compute_age("2026-05-15", "1990-05-15") == 36  # Exactly birthday

    # Partial year precision
    assert compute_age("2026", "1990") == 36
    assert compute_age("2026-08", "1990") == 36

    # Edge cases
    assert compute_age(None, "1990-05-15") is None
    assert compute_age("2026-08-02", None) is None
    assert compute_age("1990-05-15", "2026-08-02") is None  # negative age


def test_map_dm_happy_path():
    """Verify demographics mapping with all core derivations."""
    raw_demo = {
        "gender": "Female",
        "race": "ASIAN",
        "birthdate": "1995-10-20",
        "arm": "Active Treatment",
        "site_id": "SITE-12",
    }
    enc = encrypt_demographics(raw_demo)

    subject = MockObject(
        subject_id="SUBJ-001",
        study_id="STUDY-XYZ",
        site_id="SITE-12",
        encrypted_demographics=enc,
    )

    # Mock observation for exposure
    ex_obs = MockObject(
        subject_id="SUBJ-001",
        domain="EX",
        test_code="EXSTDTC",
        value_string="2026-01-15",
    )

    # Mock observation for disposition
    ds_obs = MockObject(
        subject_id="SUBJ-001",
        domain="DS",
        test_code="DSSTDTC",
        value_string="2026-07-30",
    )

    dm_list = map_dm(
        subjects=[subject],
        visits=[],
        observations=[ex_obs, ds_obs],
        created_by="pi_user",
        reason_for_change="Initial mapping verification",
    )

    assert len(dm_list) == 1
    dm = dm_list[0]

    assert dm.STUDYID == "STUDY-XYZ"
    assert dm.DOMAIN == "DM"
    assert dm.USUBJID == "STUDY-XYZ-SITE-12-SUBJ-001"
    assert dm.SUBJID == "SUBJ-001"
    assert dm.RFSTDTC == "2026-01-15"
    assert dm.RFENDTC == "2026-07-30"
    assert dm.BRTHDTC == "1995-10-20"
    assert dm.AGE == 30  # 2026-01-15 minus 1995-10-20 is 30 years
    assert dm.AGEU == "YEARS"
    assert dm.SEX == Sex.F
    assert dm.RACE == Race.ASIAN
    assert dm.ARM == "Active Treatment"
    assert dm.created_by == "pi_user"
    assert dm.reason_for_change == "Initial mapping verification"


def test_map_dm_defaults_and_fallbacks():
    """Verify fallbacks for missing arm, multi-race, and fallback observations."""
    raw_demo = {
        "gender": "male",
        "race": "White, Asian",  # Multi-race
        # birthdate is missing
    }
    enc = encrypt_demographics(raw_demo)
    subject = MockObject(
        subject_id="SUBJ-002",
        study_id="STUDY-XYZ",
        site_id=None,  # missing site_id
        encrypted_demographics=enc,
    )

    # Fallback observations for SEX, RACE, and birthdate
    brth_obs = MockObject(
        subject_id="SUBJ-002",
        domain="DM",
        test_code="BRTHDTC",
        value_string="1980-01-01",
    )

    dm_list = map_dm(
        subjects=[subject],
        visits=[],
        observations=[brth_obs],
        created_by="system",
        reason_for_change="Fallback verification",
    )

    assert len(dm_list) == 1
    dm = dm_list[0]

    # Check default site_id is '001'
    assert dm.USUBJID == "STUDY-XYZ-001-SUBJ-002"

    # Check screen failure arm default
    assert dm.ARM == "SCREEN FAILURE"

    # Check multi-race normalization
    assert dm.RACE == Race.MULTIPLE

    # Check BRTHDTC observation fallback
    assert dm.BRTHDTC == "1980-01-01"


def test_map_vs():
    """Verify VS mapping with sequence numbers and preserved/normalized findings."""
    subject = MockObject(subject_id="SUBJ-101", study_id="STUDY-ABC", site_id="S01")

    # 3 vital signs observations (unsorted)
    obs3 = MockObject(
        subject_id="SUBJ-101",
        domain="VS",
        test_code="SYSBP",
        test_name="Systolic Blood Pressure",
        observation_date=datetime(2026, 8, 3, 10, 0, tzinfo=timezone.utc),
        value=130.0,
        unit="mmHg",
        normalized_value=130.0,
        normalized_unit="mmHg",
    )
    obs1 = MockObject(
        subject_id="SUBJ-101",
        domain="VS",
        test_code="TEMP",
        test_name="Temperature",
        observation_date=datetime(2026, 8, 2, 8, 0, tzinfo=timezone.utc),
        value=98.6,
        unit="[degF]",
        normalized_value=37.0,
        normalized_unit="Cel",
    )
    obs2 = MockObject(
        subject_id="SUBJ-101",
        domain="VS",
        test_code="SYSBP",
        test_name="Systolic Blood Pressure",
        observation_date=datetime(2026, 8, 2, 9, 0, tzinfo=timezone.utc),
        value=120.0,
        unit="mmHg",
        normalized_value=120.0,
        normalized_unit="mmHg",
    )

    vs_list = map_vs(
        subjects=[subject],
        visits=[],
        observations=[obs3, obs1, obs2],
    )

    assert len(vs_list) == 3

    # Sorted by timing variable (VSDTC or observation_date)
    # 1st: TEMP at 2026-08-02T08:00:00Z
    # 2nd: SYSBP at 2026-08-02T09:00:00Z
    # 3rd: SYSBP at 2026-08-03T10:00:00Z

    v1 = vs_list[0]
    assert v1.VSSEQ == 1
    assert v1.VSTESTCD == "TEMP"
    assert v1.VSORRES == 98.6
    assert v1.VSORRESU == "[degF]"
    assert v1.VSSTRESN == 37.0
    assert v1.VSSTRESU == "Cel"
    assert v1.VSSTRESC == "37.0"  # Sourced from normalized_value

    v2 = vs_list[1]
    assert v2.VSSEQ == 2
    assert v2.VSTESTCD == "SYSBP"
    assert v2.VSORRES == 120.0
    assert v2.VSSTRESN == 120.0

    v3 = vs_list[2]
    assert v3.VSSEQ == 3
    assert v3.VSTESTCD == "SYSBP"
    assert v3.VSORRES == 130.0


def test_map_lb():
    """Verify LB mapping with sequential sorting and lab indicator."""
    subject = MockObject(subject_id="SUBJ-201", study_id="STUDY-ABC", site_id="S01")

    # 2 laboratory observations
    obs1 = MockObject(
        subject_id="SUBJ-201",
        domain="LB",
        test_code="ALT",
        test_name="Alanine Aminotransferase",
        observation_date=datetime(2026, 8, 2, 8, 0, tzinfo=timezone.utc),
        value_string="45",
        unit="U/L",
        normalized_value=45.0,
        normalized_unit="U/L",
        lab_indicator="HIGH",
        lbloinc="26464-8",
    )
    obs2 = MockObject(
        subject_id="SUBJ-201",
        domain="LB",
        test_code="AST",
        test_name="Aspartate Aminotransferase",
        observation_date=datetime(2026, 8, 2, 8, 1, tzinfo=timezone.utc),
        value_string="30",
        unit="U/L",
        normalized_value=30.0,
        normalized_unit="U/L",
        lab_indicator="NORMAL",
    )

    lb_list = map_lb(
        subjects=[subject],
        visits=[],
        observations=[obs1, obs2],
    )

    assert len(lb_list) == 2

    lb1 = lb_list[0]
    assert lb1.LBSEQ == 1
    assert lb1.LBTESTCD == "ALT"
    assert lb1.LBORRES == "45"
    assert lb1.LBSTRESN == 45.0
    assert lb1.LBNRIND == "HIGH"
    assert lb1.LBLOINC == "26464-8"

    lb2 = lb_list[1]
    assert lb2.LBSEQ == 2
    assert lb2.LBTESTCD == "AST"
    assert lb2.LBNRIND == "NORMAL"


def test_map_ae_grouped_structure():
    """Verify AE mapping from CDASH grouped observation fields."""
    subject = MockObject(subject_id="SUBJ-301", study_id="STUDY-ABC", site_id="S01")

    # Group of observations for a single AE (with page_id = "AE_FORM_1")
    o1 = MockObject(
        subject_id="SUBJ-301",
        domain="AE",
        page_id="AE_FORM_1",
        test_code="AETERM",
        value_string="Mild Headache",
    )
    o2 = MockObject(
        subject_id="SUBJ-301",
        domain="AE",
        page_id="AE_FORM_1",
        test_code="AESEV",
        value_string="MILD",
    )
    o3 = MockObject(
        subject_id="SUBJ-301",
        domain="AE",
        page_id="AE_FORM_1",
        test_code="AESER",
        value_string="NO",
    )
    o4 = MockObject(
        subject_id="SUBJ-301",
        domain="AE",
        page_id="AE_FORM_1",
        test_code="AESTDTC",
        value_string="2026-08-01",
    )
    o5 = MockObject(
        subject_id="SUBJ-301",
        domain="AE",
        page_id="AE_FORM_1",
        test_code="AEENDTC",
        value_string="2026-08-02",
    )
    o6 = MockObject(
        subject_id="SUBJ-301",
        domain="AE",
        page_id="AE_FORM_1",
        test_code="AEREL",
        value_string="POSSIBLY_RELATED",
    )
    o7 = MockObject(
        subject_id="SUBJ-301",
        domain="AE",
        page_id="AE_FORM_1",
        test_code="AEOUT",
        value_string="RECOVERED_RESOLVED",
    )

    ae_list = map_ae(
        subjects=[subject],
        visits=[],
        observations=[o1, o2, o3, o4, o5, o6, o7],
    )

    assert len(ae_list) == 1
    ae = ae_list[0]

    assert ae.AESEQ == 1
    assert ae.AETERM == "Mild Headache"
    assert ae.AESEV == AESeverity.MILD
    assert ae.AESER == AESeriousness.N
    assert ae.AESTDTC == "2026-08-01"
    assert ae.AEENDTC == "2026-08-02"
    assert ae.AEREL == AERelationship.POSSIBLY_RELATED
    assert ae.AEOUT == AEOutcome.RECOVERED_RESOLVED


def test_map_ae_flat_structure():
    """Verify AE mapping from flat observation objects."""
    subject = MockObject(subject_id="SUBJ-302", study_id="STUDY-ABC", site_id="S01")

    flat_ae = MockObject(
        id=999,
        subject_id="SUBJ-302",
        domain="AE",
        aeterm="Nausea",
        aesev="MODERATE",
        aeser="YES",
        aestdtc="2026-08-03",
        aeendtc="2026-08-04",
        aerel="RELATED",
        aeout="RECOVERING_RESOLVING",
    )

    ae_list = map_ae(
        subjects=[subject],
        visits=[],
        observations=[flat_ae],
    )

    assert len(ae_list) == 1
    ae = ae_list[0]

    assert ae.AESEQ == 1
    assert ae.AETERM == "Nausea"
    assert ae.AESEV == AESeverity.MODERATE
    assert ae.AESER == AESeriousness.Y
    assert ae.AESTDTC == "2026-08-03"
    assert ae.AEENDTC == "2026-08-04"
    assert ae.AEREL == AERelationship.RELATED
    assert ae.AEOUT == AEOutcome.RECOVERING_RESOLVING


def test_map_cm_grouped_structure():
    """Verify CM mapping from CDASH grouped medication fields."""
    subject = MockObject(subject_id="SUBJ-401", study_id="STUDY-ABC", site_id="S01")

    o1 = MockObject(
        subject_id="SUBJ-401",
        domain="CM",
        page_id="CM_FORM_1",
        test_code="CMTRT",
        value_string="Aspirin",
    )
    o2 = MockObject(
        subject_id="SUBJ-401",
        domain="CM",
        page_id="CM_FORM_1",
        test_code="CMDOSE",
        value=100.0,
    )
    o3 = MockObject(
        subject_id="SUBJ-401",
        domain="CM",
        page_id="CM_FORM_1",
        test_code="CMDOSEU",
        value_string="mg",
    )
    o4 = MockObject(
        subject_id="SUBJ-401",
        domain="CM",
        page_id="CM_FORM_1",
        test_code="CMSTDTC",
        value_string="2026-07-29",
    )

    cm_list = map_cm(
        subjects=[subject],
        visits=[],
        observations=[o1, o2, o3, o4],
    )

    assert len(cm_list) == 1
    cm = cm_list[0]

    assert cm.CMSEQ == 1
    assert cm.CMTRT == "Aspirin"
    assert cm.CMDOSE == 100.0
    assert cm.CMDOSEU == "mg"
    assert cm.CMSTDTC == "2026-07-29"


def test_map_cm_flat_structure():
    """Verify CM mapping from flat observation objects."""
    subject = MockObject(subject_id="SUBJ-402", study_id="STUDY-ABC", site_id="S01")

    flat_cm = MockObject(
        id=888,
        subject_id="SUBJ-402",
        domain="CM",
        cmtrt="Paracetamol",
        cmdecod="PARACETAMOL",
        cmdose=500.0,
        cmdoseu="mg",
        cmstdtc="2026-07-30",
        cmendtc="2026-08-01",
    )

    cm_list = map_cm(
        subjects=[subject],
        visits=[],
        observations=[flat_cm],
    )

    assert len(cm_list) == 1
    cm = cm_list[0]

    assert cm.CMSEQ == 1
    assert cm.CMTRT == "Paracetamol"
    assert cm.CMDECOD == "PARACETAMOL"
    assert cm.CMDOSE == 500.0
    assert cm.CMDOSEU == "mg"
    assert cm.CMSTDTC == "2026-07-30"
    assert cm.CMENDTC == "2026-08-01"


def test_map_to_sdtm_orchestrator():
    """Verify orchestrator dispatcher works case-insensitively and raises errors on unknown domains."""
    subject = MockObject(subject_id="SUBJ-999", study_id="STUDY-ABC", site_id="S01")

    # Test DM dispatch
    dm_res = map_to_sdtm(
        domain="dm",
        subjects=[subject],
        visits=[],
        observations=[],
    )
    assert len(dm_res) == 1
    assert dm_res[0].DOMAIN == "DM"

    # Test unknown domain error
    with pytest.raises(ValueError, match="is not supported"):
        map_to_sdtm(
            domain="UNKNOWN",
            subjects=[subject],
            visits=[],
            observations=[],
        )
