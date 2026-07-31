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
from sdtm.sdtm_models import (
    SDTMRecordAE,
    SDTMRecordVS,
)
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
    ClinicalSubject,
    SDTMDomainRecord,
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
from apps.execution.services.sdtm_mapper import (
    CDASHToSDTMMapper,
    map_cdash_to_sdtm,
    persist_sdtm_records,
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


def test_cdash_ae_mapping():
    """
    Verify transforming raw Adverse Event eCRF payload produces valid SDTMRecordAE with derived fields.

    Requirements: PRD-SYS-001
    """
    mapper = CDASHToSDTMMapper()
    raw_ae = [
        {
            "ae_term": "Headache",
            "ae_severity": "MILD",
            "ae_serious": True,
            "ae_start_date": "2026/08/01",
            "ae_end_date": "2026-08-02T12:00:00",
        },
        {
            "ae_term": "Nausea",
            "ae_severity": "MODERATE",
            "ae_serious": False,
            "ae_start_date": "03 Aug 2026",
            "ae_end_date": "",
        },
    ]

    mapped = mapper.map_adverse_events("STUDY-001", "SUBJ-101", raw_ae)
    assert len(mapped) == 2

    # Verify first AE
    ae1 = SDTMRecordAE(
        STUDYID=mapped[0]["STUDYID"],
        DOMAIN=mapped[0]["DOMAIN"],
        USUBJID=mapped[0]["USUBJID"],
        AESEQ=mapped[0]["AESEQ"],
        AETERM=mapped[0]["AETERM"],
        AEDECOD=mapped[0]["AEDECOD"],
        AESEV=mapped[0]["AESEV"],
        AESER=mapped[0]["AESER"],
        AESTDTC=mapped[0]["AESTDTC"],
        AEENDTC=mapped[0]["AEENDTC"],
        created_by="system",
        reason_for_change="UnitTest",
    )
    assert ae1.STUDYID == "STUDY-001"
    assert ae1.AESEQ == 1
    assert ae1.AETERM == "HEADACHE"
    assert ae1.AESEV == "C49487"  # MILD NCI code
    assert ae1.AESER == "C48450"  # YES/Serious NCI code
    assert ae1.AESTDTC == "2026-08-01"
    assert ae1.AEENDTC == "2026-08-02T12:00:00"

    # Verify second AE
    ae2 = SDTMRecordAE(
        STUDYID=mapped[1]["STUDYID"],
        DOMAIN=mapped[1]["DOMAIN"],
        USUBJID=mapped[1]["USUBJID"],
        AESEQ=mapped[1]["AESEQ"],
        AETERM=mapped[1]["AETERM"],
        AEDECOD=mapped[1]["AEDECOD"],
        AESEV=mapped[1]["AESEV"],
        AESER=mapped[1]["AESER"],
        AESTDTC=mapped[1]["AESTDTC"],
        AEENDTC=mapped[1]["AEENDTC"],
        created_by="system",
        reason_for_change="UnitTest",
    )
    assert ae2.AESEQ == 2
    assert ae2.AETERM == "NAUSEA"
    assert ae2.AESEV == "C49488"  # MODERATE NCI code
    assert ae2.AESER == "C48451"  # NO/Non-serious NCI code
    assert ae2.AESTDTC == "2026-08-03"
    assert ae2.AEENDTC is None


def test_cdash_vs_unit_conversion_and_study_day():
    """
    Verify transforming Vital Signs eCRF payload handles unit conversions (LB to KG, IN to CM) and Study Days.

    Requirements: PRD-SYS-001
    """
    mapper = CDASHToSDTMMapper()
    raw_vs = [
        {
            "test_code": "WEIGHT",
            "test_name": "Weight",
            "value": 150.0,
            "unit": "lb",
            "observation_date": "2026-08-02",
        },
        {
            "test_code": "HEIGHT",
            "test_name": "Height",
            "value": 70.0,
            "unit": "in",
            "observation_date": "2026-08-03",
        },
        {
            "test_code": "TEMP",
            "test_name": "Temperature",
            "value": 37.0,
            "unit": "Cel",
            "observation_date": "2026-08-01",  # Before reference start date
        },
    ]

    rfstdtc = "2026-08-02"
    mapped = mapper.map_vital_signs("STUDY-001", "SUBJ-101", raw_vs, rfstdtc=rfstdtc)
    assert len(mapped) == 3

    vs1 = SDTMRecordVS(created_by="system", reason_for_change="UnitTest", **mapped[0])
    # 150 lb * 0.45359237 = 68.04 kg
    assert vs1.VSSEQ == 1
    assert vs1.VSORRES == 150.0
    assert vs1.VSORRESU == "lb"
    assert vs1.VSSTRESN == 68.04
    assert vs1.VSSTRESU == "KG"
    assert vs1.VSSTRESC == "68.04"
    assert vs1.VSDY == 1  # Same as rfstdtc -> 1

    vs2 = SDTMRecordVS(created_by="system", reason_for_change="UnitTest", **mapped[1])
    # 70 in * 2.54 = 177.8 cm
    assert vs2.VSSEQ == 2
    assert vs2.VSORRES == 70.0
    assert vs2.VSORRESU == "in"
    assert vs2.VSSTRESN == 177.80
    assert vs2.VSSTRESU == "CM"
    assert vs2.VSDY == 2  # One day after rfstdtc -> 2

    vs3 = SDTMRecordVS(created_by="system", reason_for_change="UnitTest", **mapped[2])
    assert vs3.VSSEQ == 3
    assert vs3.VSORRES == 37.0
    assert vs3.VSORRESU == "Cel"
    assert vs3.VSSTRESN == 37.0
    assert vs3.VSSTRESU == "Cel"
    assert vs3.VSDY == -1  # One day before rfstdtc -> -1


def test_cdash_generic_orchestrator():
    """
    Verify map_cdash_to_sdtm general orchestrator functions correctly across supported domains.

    Requirements: PRD-SYS-001
    """
    ecrf_ae = [
        {
            "study_id": "STUDY-001",
            "subject_id": "SUBJ-101",
            "ae_term": "Headache",
            "ae_severity": "MILD",
            "ae_serious": True,
            "ae_start_date": "2026-08-01",
            "ae_end_date": "2026-08-02",
        }
    ]

    mapped = map_cdash_to_sdtm("AE", ecrf_ae)
    assert len(mapped) == 1
    assert mapped[0]["AETERM"] == "HEADACHE"
    assert mapped[0]["AESEV"] == "C49487"


@pytest.mark.asyncio
async def test_persist_sdtm_records_pipeline():
    """
    Verify the database pipeline integration of persist_sdtm_records.

    Ensures we can read raw eCRF observations, transform them to strongly-typed SDTMRecord Pydantic models,
    and save them into the sdtm_domain_records table with versioning and GxP compliance.

    Requirements: PRD-SYS-001
    """
    # Initialize in-memory SQLite DB
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    async with db_manager.get_session_maker()() as session:
        # Create ClinicalSubject
        subj = ClinicalSubject(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            site_id="SITE-01",
        )
        session.add(subj)

        # Create Adverse Event ClinicalObservations representing CDASH fields
        o1 = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            page_id="ae_page_1",
            test_code="AETERM",
            test_name="Reported Adverse Event Term",
            value_string="Headache",
        )
        o2 = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            page_id="ae_page_1",
            test_code="AESEV",
            test_name="Severity",
            value_string="MILD",
        )
        o3 = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            page_id="ae_page_1",
            test_code="AESER",
            test_name="Serious Event",
            value_string="YES",
        )
        session.add_all([o1, o2, o3])
        await session.commit()

        # Run persist_sdtm_records
        persisted = await persist_sdtm_records(
            session, "STUDY-001", "AE", created_by="test_user"
        )
        assert len(persisted) == 1
        assert persisted[0].domain == "AE"
        assert persisted[0].study_id == "STUDY-001"
        assert persisted[0].record_data["AETERM"] == "HEADACHE"

        # Check they exist in DB
        stmt = select(SDTMDomainRecord).where(SDTMDomainRecord.study_id == "STUDY-001")
        res = await session.execute(stmt)
        records = res.scalars().all()
        assert len(records) == 1
        assert records[0].domain == "AE"
        assert records[0].record_data["AESEV"] == "C49487"

    await db_manager.close()
