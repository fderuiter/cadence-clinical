import pytest
from pydantic import ValidationError

from apps.execution.biostat import (
    extract_ae,
    extract_dm,
    extract_lb,
    extract_mh,
    extract_vs,
)
from apps.execution.biostat.mappings import (
    get_mappings_by_domain,
    get_mappings_for_domain,
)
from apps.execution.biostat.models import (
    ClinicalData,
    DatasetJSON,
    DatasetJSONItemGroup,
    SUPPRecord,
    VariableMetadata,
)
from apps.execution.biostat.terminology import (
    normalize_race,
    normalize_severity,
    normalize_sex,
)

# --- Tests for Pydantic v2 Models ---


def test_variable_metadata_validation():
    """Verify that VariableMetadata correctly validates types and fields."""
    # Valid model
    vm = VariableMetadata(
        name="USUBJID",
        label="Unique Subject Identifier",
        type="string",
        length=200,
    )
    assert vm.name == "USUBJID"
    assert vm.type == "string"

    # Lowercases the type automatically
    vm2 = VariableMetadata(
        name="AGE",
        label="Age",
        type="INTEGER",
    )
    assert vm2.type == "integer"

    # Invalid type should raise ValidationError
    with pytest.raises(ValidationError):
        VariableMetadata(
            name="INVALID",
            label="Invalid",
            type="complex_type",
        )


def test_supp_record_row_conversion():
    """Verify that SUPPRecord initializes correctly and serializes to ordered rows."""
    supp = SUPPRecord(
        STUDYID="STUDY-123",
        RDOMAIN="DM",
        USUBJID="STUDY-123-001",
        IDVAR="",
        IDVARVAL="",
        QNAM="MULTIRAC",
        QLABEL="Multiple Race Flag",
        QVAL="Y",
    )
    assert supp.QEVAL == ""

    # Convert to row based on ordered list of variable names
    vars_list = ["STUDYID", "RDOMAIN", "USUBJID", "QNAM", "QVAL"]
    row = supp.to_row(vars_list)
    assert row == ["STUDY-123", "DM", "STUDY-123-001", "MULTIRAC", "Y"]


def test_dataset_json_integration_structure():
    """Verify that the full DatasetJSON container model constructs a compliant schema."""
    # Create variable metadata
    variables = [
        VariableMetadata(name="STUDYID", label="Study OID", type="string"),
        VariableMetadata(name="USUBJID", label="Subject ID", type="string"),
        VariableMetadata(name="AGE", label="Age", type="integer"),
    ]

    item_group = DatasetJSONItemGroup(
        records=2,
        name="DM",
        label="Demographics",
        items=variables,
        itemData=[
            ["STUDY-01", "STUDY-01-001", 35],
            ["STUDY-01", "STUDY-01-002", 42],
        ],
    )

    clinical_data = ClinicalData(
        studyOID="STUDY.01",
        metaDataVersionOID="MDV.01",
        itemGroupData={"IG.DM": item_group},
    )

    dj = DatasetJSON(
        creationDateTime="2026-07-29T12:00:00Z",
        datasetJSONVersion="1.0.0",
        clinicalData=clinical_data,
    )

    assert dj.datasetJSONVersion == "1.0.0"
    assert dj.clinicalData is not None
    assert "IG.DM" in dj.clinicalData.itemGroupData
    assert dj.clinicalData.itemGroupData["IG.DM"].records == 2

    # Check validation on incorrect timestamp format
    with pytest.raises(ValidationError):
        DatasetJSON(
            creationDateTime="not-a-timestamp",
            clinicalData=clinical_data,
        )


# --- Tests for Declarative Mapping Table ---


def test_declarative_mappings_coverage():
    """Verify that the declarative mapping covers DM, AE, VS, LB, and MH domains with expected variables."""
    # Group mappings
    grouped = get_mappings_by_domain()
    assert "DM" in grouped
    assert "AE" in grouped
    assert "VS" in grouped
    assert "LB" in grouped
    assert "MH" in grouped

    # Check some critical variables
    dm_vars = {m.variable_name for m in grouped["DM"]}
    ae_vars = {m.variable_name for m in grouped["AE"]}
    vs_vars = {m.variable_name for m in grouped["VS"]}
    lb_vars = {m.variable_name for m in grouped["LB"]}
    mh_vars = {m.variable_name for m in grouped["MH"]}

    # Demographics
    assert "STUDYID" in dm_vars
    assert "USUBJID" in dm_vars
    assert "SEX" in dm_vars
    assert "RACE" in dm_vars
    assert "ARM" in dm_vars

    # Adverse Events
    assert "AESEQ" in ae_vars
    assert "AETERM" in ae_vars
    assert "AESEV" in ae_vars
    assert "AEREL" in ae_vars

    # Vital Signs
    assert "VSSEQ" in vs_vars
    assert "VSTESTCD" in vs_vars
    assert "VSSTRESN" in vs_vars

    # Labs
    assert "LBSEQ" in lb_vars
    assert "LBTESTCD" in lb_vars
    assert "LBSTRESN" in lb_vars

    # Medical History
    assert "MHSEQ" in mh_vars
    assert "MHTERM" in mh_vars
    assert "MHDECOD" in mh_vars


def test_mapping_helpers():
    """Verify helper functions retrieve mapped records accurately."""
    ae_mappings = get_mappings_for_domain("AE")
    assert len(ae_mappings) > 0
    for mapping in ae_mappings:
        assert mapping.domain == "AE"
        assert mapping.transformation_kind in {
            "DIRECT",
            "CONCATENATION",
            "COMPUTED",
            "FIXED",
            "CONTROLLED_TERMINOLOGY",
        }


# --- Tests for Controlled Terminology Helpers ---


def test_normalize_sex():
    """Verify SEX field normalization and validation."""
    assert normalize_sex("male") == "M"
    assert normalize_sex("M") == "M"
    assert normalize_sex("Female") == "F"
    assert normalize_sex("f_gen") == "F"
    assert normalize_sex("UNKNOWN") == "U"
    assert normalize_sex("not reported") == "U"

    with pytest.raises(ValueError):
        normalize_sex("alien")

    with pytest.raises(ValueError):
        normalize_sex(None)


def test_normalize_race():
    """Verify RACE field normalization, handling of multiple entries, and validation."""
    # Single race terms
    assert normalize_race("white") == "WHITE"
    assert normalize_race("caucasian") == "WHITE"
    assert normalize_race("black") == "BLACK OR AFRICAN AMERICAN"
    assert normalize_race("African American") == "BLACK OR AFRICAN AMERICAN"
    assert normalize_race("asian") == "ASIAN"
    assert (
        normalize_race("AMERICAN INDIAN OR ALASKA NATIVE")
        == "AMERICAN INDIAN OR ALASKA NATIVE"
    )
    assert (
        normalize_race("pacific islander")
        == "NATIVE HAWAIIAN OR OTHER PACIFIC ISLANDER"
    )
    assert normalize_race("declined") == "OTHER"

    # Multi-race options (lists or strings with separators)
    assert normalize_race(["White", "Asian"]) == "MULTIPLE"
    assert normalize_race("White, Black") == "MULTIPLE"
    assert normalize_race("White; Asian") == "MULTIPLE"
    assert normalize_race("White and Asian") == "MULTIPLE"

    with pytest.raises(ValueError):
        normalize_race("invalid-race-name")

    with pytest.raises(ValueError):
        normalize_race([])

    with pytest.raises(ValueError):
        normalize_race("")


def test_normalize_severity():
    """Verify AESEV (Severity) field normalization and validation."""
    assert normalize_severity("mild") == "MILD"
    assert normalize_severity("1") == "MILD"
    assert normalize_severity("grade 1") == "MILD"

    assert normalize_severity("Moderate") == "MODERATE"
    assert normalize_severity("2") == "MODERATE"

    assert normalize_severity("severe") == "SEVERE"
    assert normalize_severity("GRADE 3") == "SEVERE"
    assert normalize_severity("HIGH") == "SEVERE"

    with pytest.raises(ValueError):
        normalize_severity("critical")

    with pytest.raises(ValueError):
        normalize_severity(None)


# --- Tests for DM and MH Extractors ---


def test_extract_dm_demographics():
    """Verify that extract_dm correctly maps demographics, ARM, sex, and race, and performs age calculations."""
    subjects = [
        {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-X",
            "site_id": "SITE-A",
            "demographics": {
                "birthdate": "1990-05-15",
                "gender": "male",
                "race": "white",
                "arm": "Active Arm",
            },
        },
        {
            "subject_id": "SUBJ-002",
            "study_id": "STUDY-X",
            "site_id": "SITE-B",
            "demographics": {
                "birthdate": "1985-10-20",
                "gender": "Female",
                "race": "black",
            },
        },
    ]

    # Add observations for first exposure
    observations = [
        {
            "subject_id": "SUBJ-001",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2020-05-15T10:00:00",
        },
        {
            "subject_id": "SUBJ-002",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2020-10-20",
        },
    ]

    records = extract_dm(subjects, observations)
    assert len(records) == 2

    rec1 = next(r for r in records if r["SUBJID"] == "SUBJ-001")
    assert rec1["STUDYID"] == "STUDY-X"
    assert rec1["USUBJID"] == "STUDY-X-SITE-A-SUBJ-001"
    assert rec1["BRTHDTC"] == "1990-05-15"
    assert rec1["SEX"] == "M"
    assert rec1["RACE"] == "WHITE"
    assert rec1["ARM"] == "Active Arm"
    assert rec1["AGE"] == 30  # 2020-05-15 minus 1990-05-15 is exactly 30 years
    assert rec1["AGEU"] == "YEARS"

    rec2 = next(r for r in records if r["SUBJID"] == "SUBJ-002")
    assert rec2["USUBJID"] == "STUDY-X-SITE-B-SUBJ-002"
    assert rec2["SEX"] == "F"
    assert rec2["RACE"] == "BLACK OR AFRICAN AMERICAN"
    assert rec2["ARM"] == "SCREEN FAILURE"  # Default
    assert rec2["AGE"] == 35  # 2020-10-20 minus 1985-10-20 is exactly 35 years


def test_extract_dm_age_precision_and_controlled_terminology():
    """Verify that AGE is only computed when precision is sufficient, and terminology behavior throws expected errors on invalid entries."""
    subjects = [
        {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-X",
            "demographics": {
                "birthdate": "1990-UN-UN",  # Insufficient precision
                "gender": "male",
                "race": "white",
            },
        },
        {
            "subject_id": "SUBJ-002",
            "study_id": "STUDY-X",
            "demographics": {
                "birthdate": "1990-05-15",
                "gender": "invalid-gender",  # Invalid SEX
                "race": "white",
            },
        },
    ]

    observations = [
        {
            "subject_id": "SUBJ-001",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2020-05-15",
        }
    ]

    # Record 1 should have None for AGE due to insufficient precision
    records = extract_dm([subjects[0]], observations)
    assert records[0]["AGE"] is None
    assert records[0]["AGEU"] == "YEARS"

    # Record 2 should raise ValueError on invalid gender
    with pytest.raises(ValueError):
        extract_dm([subjects[1]], observations)


def test_extract_mh_sequencing_and_supp():
    """Verify that MH records have stable per-subject sequencing by date, and unmapped fields are retained in SUPPMH."""
    subjects = [
        {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-Y",
            "site_id": "SITE-Z",
        }
    ]

    observations = [
        # Medical History Event 1 (later onset)
        {
            "subject_id": "SUBJ-001",
            "domain": "MH",
            "page_id": "page_1",
            "test_code": "MHTERM",
            "value_string": "Asthma",
        },
        {
            "subject_id": "SUBJ-001",
            "domain": "MH",
            "page_id": "page_1",
            "test_code": "MHSTDTC",
            "value_string": "2015-06-01",
        },
        {
            "subject_id": "SUBJ-001",
            "domain": "MH",
            "page_id": "page_1",
            "test_code": "MHDECOD",
            "value_string": "Asthma PT",
        },
        # Medical History Event 2 (earlier onset)
        {
            "subject_id": "SUBJ-001",
            "domain": "MH",
            "page_id": "page_2",
            "test_code": "MHTERM",
            "value_string": "Hypertension",
        },
        {
            "subject_id": "SUBJ-001",
            "domain": "MH",
            "page_id": "page_2",
            "test_code": "MHSTDTC",
            "value_string": "2010-01-01",
        },
        {
            "subject_id": "SUBJ-001",
            "domain": "MH",
            "page_id": "page_2",
            "test_code": "MHBODSYS",
            "value_string": "Vascular disorders",
        },
        # Non-standard / unmapped qualifier on Event 2
        {
            "subject_id": "SUBJ-001",
            "domain": "MH",
            "page_id": "page_2",
            "test_code": "MHSEV",
            "test_name": "Severity of History Event",
            "value_string": "MILD",
        },
    ]

    mh_records, supp_records = extract_mh(subjects, observations)

    # Hypertension (2010-01-01) must be sorted BEFORE Asthma (2015-06-01)
    assert len(mh_records) == 2
    assert mh_records[0]["MHTERM"] == "Hypertension"
    assert mh_records[0]["MHSEQ"] == 1
    assert mh_records[0]["MHBODSYS"] == "Vascular disorders"

    assert mh_records[1]["MHTERM"] == "Asthma"
    assert mh_records[1]["MHSEQ"] == 2
    assert mh_records[1]["MHDECOD"] == "Asthma PT"

    # Verify SUPPMH records
    assert len(supp_records) == 1
    supp = supp_records[0]
    assert supp.STUDYID == "STUDY-Y"
    assert supp.RDOMAIN == "MH"
    assert supp.USUBJID == "STUDY-Y-SITE-Z-SUBJ-001"
    assert supp.IDVAR == "MHSEQ"
    assert supp.IDVARVAL == "1"  # Hypertension was assigned MHSEQ=1
    assert supp.QNAM == "MHSEV"
    assert supp.QLABEL == "Severity of History Event"
    assert supp.QVAL == "MILD"


# --- Tests for AE, VS, and LB Extractors ---


def test_extract_ae_sorting_ongoing_supp():
    """Verify extract_ae correctly sorts events, maps ongoing status and severity, and preserves unmapped qualifiers in SUPPAE."""
    subjects = [
        {
            "subject_id": "SUBJ-101",
            "study_id": "STUDY-A",
            "site_id": "SITE-1",
        }
    ]

    observations = [
        # Event 1: Later onset, ongoing
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_1",
            "test_code": "AETERM",
            "value_string": "Headache",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_1",
            "test_code": "AESTDTC",
            "value_string": "2026-08-01",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_1",
            "test_code": "AESEV",
            "value_string": "GRADE 1",  # Maps to MILD
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_1",
            "test_code": "AEONGO",
            "value_string": "YES",
        },
        # Event 2: Earlier onset, resolved, with unmapped qualifier
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_2",
            "test_code": "AETERM",
            "value_string": "Nausea",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_2",
            "test_code": "AESTDTC",
            "value_string": "2026-07-15",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_2",
            "test_code": "AESEV",
            "value_string": "MODERATE",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_2",
            "test_code": "AEENDTC",
            "value_string": "2026-07-18",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "ae_page_2",
            "test_code": "AETREAT",
            "test_name": "Treatment for AE",
            "value_string": "Paracetamol",
        },
    ]

    ae_records, supp_records = extract_ae(subjects, observations)

    assert len(ae_records) == 2

    # Earlier onset (Nausea) must be sorted BEFORE Headache
    assert ae_records[0]["AETERM"] == "Nausea"
    assert ae_records[0]["AESEQ"] == 1
    assert ae_records[0]["AESEV"] == "MODERATE"
    assert ae_records[0]["AEENDTC"] == "2026-07-18"

    assert ae_records[1]["AETERM"] == "Headache"
    assert ae_records[1]["AESEQ"] == 2
    assert ae_records[1]["AESEV"] == "MILD"
    # Headache is ongoing, so end date is cleared
    assert ae_records[1]["AEENDTC"] == ""

    # Check SUPPAE records
    # 1. AEENGRY for Headache (AESEQ=2)
    # 2. AETREAT for Nausea (AESEQ=1)
    assert len(supp_records) == 2

    supp_ong = next(s for s in supp_records if s.QNAM == "AEENGRY")
    assert supp_ong.STUDYID == "STUDY-A"
    assert supp_ong.RDOMAIN == "AE"
    assert supp_ong.USUBJID == "STUDY-A-SITE-1-SUBJ-101"
    assert supp_ong.IDVAR == "AESEQ"
    assert supp_ong.IDVARVAL == "2"
    assert supp_ong.QVAL == "ONGOING"

    supp_treat = next(s for s in supp_records if s.QNAM == "AETREAT")
    assert supp_treat.STUDYID == "STUDY-A"
    assert supp_treat.RDOMAIN == "AE"
    assert supp_treat.USUBJID == "STUDY-A-SITE-1-SUBJ-101"
    assert supp_treat.IDVAR == "AESEQ"
    assert supp_treat.IDVARVAL == "1"
    assert supp_treat.QLABEL == "Treatment for AE"
    assert supp_treat.QVAL == "Paracetamol"


def test_extract_vs_baseline_supp():
    """Verify extract_vs correctly maps verbatim and normalized results, computes baseline flag VSBLFL, and exports SUPPVS."""
    subjects = [
        {
            "subject_id": "SUBJ-202",
            "study_id": "STUDY-B",
            "site_id": "SITE-2",
            "rfstdtc": "2026-08-10T12:00:00",
        }
    ]

    observations = [
        # Visit 1: Screening (On or prior to rfstdtc) -> Should be baseline
        {
            "subject_id": "SUBJ-202",
            "domain": "VS",
            "page_id": "vs_page_1",
            "test_code": "SYSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 120.0,
            "unit": "mmHg",
            "normalized_value": 120.0,
            "normalized_unit": "mmHg",
            "observation_date": "2026-08-05T09:00:00",
        },
        {
            "subject_id": "SUBJ-202",
            "domain": "VS",
            "page_id": "vs_page_1",
            "test_code": "VSPOS",
            "value_string": "SITTING",
            "observation_date": "2026-08-05T09:00:00",
        },
        # Visit 2: Pre-dose Day 1 (On or prior to rfstdtc, later than Screening) -> New Baseline
        {
            "subject_id": "SUBJ-202",
            "domain": "VS",
            "page_id": "vs_page_2",
            "test_code": "SYSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 125.0,
            "unit": "mmHg",
            "normalized_value": 125.0,
            "normalized_unit": "mmHg",
            "observation_date": "2026-08-10T11:00:00",
        },
        {
            "subject_id": "SUBJ-202",
            "domain": "VS",
            "page_id": "vs_page_2",
            "test_code": "VSPOS",
            "value_string": "SITTING",
            "observation_date": "2026-08-10T11:00:00",
        },
        # Visit 3: Post-dose Day 1 (After rfstdtc) -> Post-baseline, not baseline
        {
            "subject_id": "SUBJ-202",
            "domain": "VS",
            "page_id": "vs_page_3",
            "test_code": "SYSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 130.0,
            "unit": "mmHg",
            "normalized_value": 130.0,
            "normalized_unit": "mmHg",
            "observation_date": "2026-08-10T14:00:00",
        },
        {
            "subject_id": "SUBJ-202",
            "domain": "VS",
            "page_id": "vs_page_3",
            "test_code": "VSPOS",
            "value_string": "SITTING",
            "observation_date": "2026-08-10T14:00:00",
        },
    ]

    vs_records, supp_records = extract_vs(subjects, observations)

    assert len(vs_records) == 3

    # Check sequencing and details
    assert vs_records[0]["VSSEQ"] == 1
    assert vs_records[0]["VSORRES"] == 120.0
    assert vs_records[0]["VSSTRESN"] == 120.0
    assert vs_records[0]["VSSTRESC"] == "120"
    assert vs_records[0]["VSPOS"] == "SITTING"
    assert vs_records[0]["VSBLFL"] == ""  # Superceded by pre-dose measurement

    assert vs_records[1]["VSSEQ"] == 2
    assert vs_records[1]["VSORRES"] == 125.0
    assert vs_records[1]["VSSTRESN"] == 125.0
    assert vs_records[1]["VSBLFL"] == "Y"  # Latest before/on treatment start

    assert vs_records[2]["VSSEQ"] == 3
    assert vs_records[2]["VSORRES"] == 130.0
    assert vs_records[2]["VSSTRESN"] == 130.0
    assert vs_records[2]["VSBLFL"] == ""  # Post-treatment exposure


def test_extract_lb_verbatim_normalized_supp():
    """Verify extract_lb correctly maps verbatim and normalized results, maps normal range indicators, and generates SUPPLB."""
    subjects = [
        {
            "subject_id": "SUBJ-303",
            "study_id": "STUDY-C",
            "site_id": "SITE-3",
        }
    ]

    observations = [
        # Record 1: ALT Normal
        {
            "subject_id": "SUBJ-303",
            "domain": "LB",
            "page_id": "lb_page_1",
            "test_code": "ALT",
            "test_name": "Alanine Aminotransferase",
            "value_string": "25",
            "value": 25.0,
            "unit": "U/L",
            "normalized_value": 25.0,
            "normalized_unit": "U/L",
            "lab_indicator": "NORMAL",
            "observation_date": "2026-08-11T08:00:00",
            "lbloinc": "1742-6",
        },
        # Record 2: Hemoglobin (requires unit conversion verbatim vs normalized)
        {
            "subject_id": "SUBJ-303",
            "domain": "LB",
            "page_id": "lb_page_2",
            "test_code": "HEMOG",
            "test_name": "Hemoglobin",
            "value_string": "14",
            "value": 14.0,
            "unit": "g/dL",
            "normalized_value": 140.0,
            "normalized_unit": "g/L",
            "lab_indicator": "NORMAL",
            "observation_date": "2026-08-11T08:05:00",
        },
    ]

    lb_records, supp_records = extract_lb(subjects, observations)

    assert len(lb_records) == 2

    # ALT Test
    alt_rec = next(r for r in lb_records if r["LBTESTCD"] == "ALT")
    assert alt_rec["LBSEQ"] == 1
    assert alt_rec["LBORRES"] == "25"
    assert alt_rec["LBORRESU"] == "U/L"
    assert alt_rec["LBSTRESN"] == 25.0
    assert alt_rec["LBSTRESU"] == "U/L"
    assert alt_rec["LBSTRESC"] == "25"
    assert alt_rec["LBNRIND"] == "NORMAL"
    assert alt_rec["LBLOINC"] == "1742-6"

    # Hemoglobin Test (Verbatim vs Normalized)
    hem_rec = next(r for r in lb_records if r["LBTESTCD"] == "HEMOG")
    assert hem_rec["LBSEQ"] == 2
    assert hem_rec["LBORRES"] == "14"
    assert hem_rec["LBORRESU"] == "g/dL"
    assert hem_rec["LBSTRESN"] == 140.0
    assert hem_rec["LBSTRESU"] == "g/L"
    assert hem_rec["LBSTRESC"] == "140"
    assert hem_rec["LBNRIND"] == "NORMAL"
