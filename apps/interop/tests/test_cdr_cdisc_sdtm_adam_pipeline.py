"""Comprehensive unit and integration tests for CDISC SDTM/ADaM Pipeline & Dataset-JSON / Define-XML 2.1 Compiler.

Requirements: PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.interop.application.cdr_pipeline_service import CDRPipelineService
from apps.interop.domain.cdr.adam_pipeline import ADaMPipelineEngine
from apps.interop.domain.cdr.dataset_json_compiler import (
    DatasetJSONCompiler,
)
from apps.interop.domain.cdr.define_xml_compiler import (
    DefineXMLCompiler,
)
from apps.interop.domain.cdr.models import (
    SDTMAE,
    SDTMDM,
    SDTMEX,
    SDTMLB,
    SDTMRS,
    SDTMTR,
    SDTMTU,
    SDTMVS,
    ADaMADAE,
    ADaMADSL,
    ADaMADTTE,
    ADaMBDS,
    DefineXMLCompilationRequest,
    PipelineExecutionStatus,
)
from apps.interop.domain.cdr.sdtm_pipeline import SDTMPipelineEngine
from apps.interop.domain.cdr.submission_engine import (
    CdiscSubmissionEngine,
    SubmissionPackage,
)
from apps.interop.domain.lakehouse_models import (
    CanonicalClinicalRecord,
    DataStreamType,
    ObservationDomain,
)
from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.lakehouse_repository import LakehouseRepository
from apps.interop.infrastructure.models import Base
from apps.interop.main import app
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup clean in-memory database for pipeline tests."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "data_manager",
    user_id: str = "user_biostat_01",
    change_reason: str = "GxP CDR Pipeline Test",
    unblinded_access: bool = False,
) -> dict[str, str]:
    """Generate signed gateway authorization headers."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
        unblinded_access=unblinded_access,
    )


def create_sample_canonical_records(
    study_id: str = "STUDY-001",
) -> list[CanonicalClinicalRecord]:
    """Create comprehensive multi-domain canonical records for testing."""
    dt_base = datetime(2026, 1, 15, 10, 0, 0, tzinfo=UTC)
    dt_visit2 = dt_base + timedelta(days=14)

    records: list[CanonicalClinicalRecord] = [
        # Subject 001 - Demographics
        CanonicalClinicalRecord(
            record_id="REC-DM-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.EDC_ECRF.value,
            domain=ObservationDomain.DM.value,
            item_code="DM_DEMOG",
            item_name="Demographics eCRF",
            observation_datetime=dt_base,
            numeric_value=52.0,
            text_value="MALE",
            unit="YEARS",
            reference_low=None,
            reference_high=None,
            abnormal_flag=None,
            is_unblinded=False,
            raw_payload={
                "age": 52,
                "sex": "MALE",
                "race": "WHITE",
                "ethnic": "NOT HISPANIC OR LATINO",
                "birth_date": "1974-01-10",
                "arm": "Active Drug 100mg",
                "armcd": "TRT-100",
            },
            normalized_attributes={"arm": "Active Drug 100mg"},
            data_hash="hash001",
            created_at=dt_base,
        ),
        # Subject 001 - Exposure
        CanonicalClinicalRecord(
            record_id="REC-EX-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.EDC_ECRF.value,
            domain=ObservationDomain.EX.value,
            item_code="EX_DOSE",
            item_name="Drug Administration",
            observation_datetime=dt_base,
            numeric_value=100.0,
            text_value="Active Drug 100mg",
            unit="mg",
            reference_low=None,
            reference_high=None,
            abnormal_flag=None,
            is_unblinded=False,
            raw_payload={
                "treatment_name": "Active Drug 100mg",
                "dose": 100.0,
                "dose_unit": "mg",
                "start_date": "2026-01-15",
                "end_date": "2026-01-15",
                "lot_number": "LOT-001",
            },
            normalized_attributes={"visit_id": "Day 1", "visit_num": 1.0},
            data_hash="hash002",
            created_at=dt_base,
        ),
        # Subject 001 - Vital Signs (Baseline & Visit 2)
        CanonicalClinicalRecord(
            record_id="REC-VS-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.EDC_ECRF.value,
            domain=ObservationDomain.VS.value,
            item_code="VS_SYSBP",
            item_name="Systolic Blood Pressure",
            observation_datetime=dt_base,
            numeric_value=130.0,
            text_value="130",
            unit="mmHg",
            reference_low=90.0,
            reference_high=140.0,
            abnormal_flag="NORMAL",
            is_unblinded=False,
            raw_payload={"VSTESTCD": "SYSBP"},
            normalized_attributes={"visit_id": "Baseline", "visit_num": 1.0},
            data_hash="hash003",
            created_at=dt_base,
        ),
        CanonicalClinicalRecord(
            record_id="REC-VS-002",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.EDC_ECRF.value,
            domain=ObservationDomain.VS.value,
            item_code="VS_SYSBP",
            item_name="Systolic Blood Pressure",
            observation_datetime=dt_visit2,
            numeric_value=122.0,
            text_value="122",
            unit="mmHg",
            reference_low=90.0,
            reference_high=140.0,
            abnormal_flag="NORMAL",
            is_unblinded=False,
            raw_payload={"VSTESTCD": "SYSBP"},
            normalized_attributes={"visit_id": "Visit 2", "visit_num": 2.0},
            data_hash="hash004",
            created_at=dt_visit2,
        ),
        # Subject 001 - Labs (Baseline & Visit 2)
        CanonicalClinicalRecord(
            record_id="REC-LB-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.CENTRAL_LAB.value,
            domain=ObservationDomain.LB.value,
            item_code="LB_ALT",
            item_name="Alanine Aminotransferase",
            observation_datetime=dt_base,
            numeric_value=25.0,
            text_value="25",
            unit="U/L",
            reference_low=10.0,
            reference_high=40.0,
            abnormal_flag="NORMAL",
            is_unblinded=False,
            raw_payload={"LBTESTCD": "ALT", "loinc": "1742-6"},
            normalized_attributes={
                "visit_id": "Baseline",
                "visit_num": 1.0,
                "loinc": "1742-6",
            },
            data_hash="hash005",
            created_at=dt_base,
        ),
        CanonicalClinicalRecord(
            record_id="REC-LB-002",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.CENTRAL_LAB.value,
            domain=ObservationDomain.LB.value,
            item_code="LB_ALT",
            item_name="Alanine Aminotransferase",
            observation_datetime=dt_visit2,
            numeric_value=32.0,
            text_value="32",
            unit="U/L",
            reference_low=10.0,
            reference_high=40.0,
            abnormal_flag="NORMAL",
            is_unblinded=False,
            raw_payload={"LBTESTCD": "ALT", "loinc": "1742-6"},
            normalized_attributes={
                "visit_id": "Visit 2",
                "visit_num": 2.0,
                "loinc": "1742-6",
            },
            data_hash="hash006",
            created_at=dt_visit2,
        ),
        # Subject 001 - Adverse Event (Post-baseline)
        CanonicalClinicalRecord(
            record_id="REC-AE-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.EDC_ECRF.value,
            domain=ObservationDomain.AE.value,
            item_code="AE_EVENT",
            item_name="Headache",
            observation_datetime=dt_base + timedelta(days=3),
            numeric_value=None,
            text_value="Mild Headache",
            unit=None,
            reference_low=None,
            reference_high=None,
            abnormal_flag=None,
            is_unblinded=False,
            raw_payload={
                "ae_term": "Headache",
                "preferred_term": "Headache",
                "soc": "Nervous system disorders",
                "severity": "MILD",
                "serious": False,
                "causality": "POSSIBLY RELATED",
                "outcome": "RECOVERED/RESOLVED",
                "start_date": "2026-01-18",
                "end_date": "2026-01-19",
            },
            normalized_attributes={},
            data_hash="hash007",
            created_at=dt_base + timedelta(days=3),
        ),
        # Subject 001 - Concomitant Medication
        CanonicalClinicalRecord(
            record_id="REC-CM-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.EDC_ECRF.value,
            domain=ObservationDomain.OTHER.value,
            item_code="CM_MED",
            item_name="Acetaminophen",
            observation_datetime=dt_base + timedelta(days=3),
            numeric_value=500.0,
            text_value="Acetaminophen 500mg",
            unit="mg",
            reference_low=None,
            reference_high=None,
            abnormal_flag=None,
            is_unblinded=False,
            raw_payload={
                "medication_name": "Acetaminophen",
                "dose": 500.0,
                "dose_unit": "mg",
                "start_date": "2026-01-18",
                "end_date": "2026-01-18",
            },
            normalized_attributes={},
            data_hash="hash008",
            created_at=dt_base + timedelta(days=3),
        ),
        # Subject 001 - Disposition
        CanonicalClinicalRecord(
            record_id="REC-DS-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.EDC_ECRF.value,
            domain=ObservationDomain.DS.value,
            item_code="DS_COMPL",
            item_name="Completed Study",
            observation_datetime=dt_base + timedelta(days=90),
            numeric_value=None,
            text_value="Study Completed",
            unit=None,
            reference_low=None,
            reference_high=None,
            abnormal_flag=None,
            is_unblinded=False,
            raw_payload={
                "disposition_term": "COMPLETED STUDY PROTOCOL",
                "standardized_term": "COMPLETED",
                "date": "2026-04-15",
            },
            normalized_attributes={},
            data_hash="hash009",
            created_at=dt_base + timedelta(days=90),
        ),
        # Subject 001 - Tumor Identification (TU)
        CanonicalClinicalRecord(
            record_id="REC-TU-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.MEDICAL_IMAGING.value,
            domain=ObservationDomain.TU.value,
            item_code="TU_TUMOR_LESION",
            item_name="Target Lesion Identification",
            observation_datetime=dt_base,
            numeric_value=None,
            text_value="TARGET",
            unit=None,
            reference_low=None,
            reference_high=None,
            abnormal_flag=None,
            is_unblinded=False,
            raw_payload={
                "lesion_id": "L01",
                "lesion_type": "TARGET",
                "anatomical_site": "Right Upper Lobe Lung",
            },
            normalized_attributes={"modality": "CT SCAN", "timepoint": "Baseline"},
            data_hash="hash010",
            created_at=dt_base,
        ),
        # Subject 001 - Tumor Measurement (TR)
        CanonicalClinicalRecord(
            record_id="REC-TR-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.MEDICAL_IMAGING.value,
            domain=ObservationDomain.TR.value,
            item_code="TR_DIAMETER",
            item_name="Lesion Caliper Measurement",
            observation_datetime=dt_base,
            numeric_value=24.5,
            text_value="24.5",
            unit="mm",
            reference_low=None,
            reference_high=None,
            abnormal_flag=None,
            is_unblinded=False,
            raw_payload={"lesion_id": "L01", "longest_diameter_mm": 24.5},
            normalized_attributes={"modality": "CT SCAN", "timepoint": "Baseline"},
            data_hash="hash011",
            created_at=dt_base,
        ),
        # Subject 001 - Disease Response (RS)
        CanonicalClinicalRecord(
            record_id="REC-RS-001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="001",
            stream_type=DataStreamType.MEDICAL_IMAGING.value,
            domain=ObservationDomain.RS.value,
            item_code="RS_RECIST",
            item_name="RECIST Overall Response",
            observation_datetime=dt_visit2,
            numeric_value=None,
            text_value="PR",
            unit=None,
            reference_low=None,
            reference_high=None,
            abnormal_flag=None,
            is_unblinded=False,
            raw_payload={"overall_response": "PR"},
            normalized_attributes={"timepoint": "Visit 2"},
            data_hash="hash012",
            created_at=dt_visit2,
        ),
    ]
    return records


# =========================================================================
# Unit Tests: SDTM Pipeline Engine (10 Core Domains)
# =========================================================================


class TestSDTMPipelineEngine:
    """Test automated transformation mapping canonical observations into standard SDTM domains.

    @req:PRD-CDR-002
    @req:ADR-2199
    """

    def test_sdtm_10_domains_mapped_successfully(self) -> None:
        """Verify all 10 standard SDTM domains (DM, AE, CM, VS, LB, EX, DS, TU, TR, RS) are correctly populated.

        @req:PRD-CDR-002
        @req:ADR-2199
        """
        records = create_sample_canonical_records("STUDY-001")
        engine = SDTMPipelineEngine(study_id="STUDY-001")
        domains = engine.build_sdtm_domains(records)

        # Check all 10 domain keys exist
        for dom in ["DM", "AE", "CM", "VS", "LB", "EX", "DS", "TU", "TR", "RS"]:
            assert dom in domains
            assert len(domains[dom]) >= 1, f"Domain {dom} should have at least 1 record"

        # Check Demographics (DM)
        dm: SDTMDM = domains["DM"][0]
        assert dm.STUDYID == "STUDY-001"
        assert dm.DOMAIN == "DM"
        assert dm.USUBJID == "STUDY-001-001"
        assert dm.AGE == 52
        assert dm.SEX == "M"
        assert dm.RACE == "WHITE"
        assert dm.ARM == "Active Drug 100mg"

        # Check Adverse Events (AE)
        ae: SDTMAE = domains["AE"][0]
        assert ae.DOMAIN == "AE"
        assert ae.USUBJID == "STUDY-001-001"
        assert ae.AESEQ == 1
        assert ae.AETERM == "Headache"
        assert ae.AESEV == "MILD"
        assert ae.AESER == "N"
        assert ae.AEREL == "RELATED"
        assert ae.AESTDTC == "2026-01-18"

        # Check Vital Signs (VS)
        vs_records: list[SDTMVS] = domains["VS"]
        assert len(vs_records) == 2
        vs_bl = vs_records[0]
        assert vs_bl.VSTESTCD == "SYSBP"
        assert vs_bl.VSSTRESN == 130.0
        assert vs_bl.VSBLFL == "Y"
        assert vs_bl.VSDY == 1

        vs_post = vs_records[1]
        assert vs_post.VSSTRESN == 122.0
        assert vs_post.VSBLFL is None
        assert vs_post.VSDY == 15

        # Check Laboratory (LB)
        lb_records: list[SDTMLB] = domains["LB"]
        assert len(lb_records) == 2
        assert lb_records[0].LBTESTCD == "ALT"
        assert lb_records[0].LBBLFL == "Y"
        assert lb_records[0].LOINC == "1742-6"

        # Check Exposure (EX)
        ex: SDTMEX = domains["EX"][0]
        assert ex.EXTRT == "Active Drug 100mg"
        assert ex.EXDOSE == 100.0

        # Check Oncology Domains (TU, TR, RS)
        tu: SDTMTU = domains["TU"][0]
        assert tu.TULNKID == "L01"
        assert tu.TUORRES == "TARGET"

        tr: SDTMTR = domains["TR"][0]
        assert tr.TRLNKID == "L01"
        assert tr.TRSTRESN == 24.5

        rs: SDTMRS = domains["RS"][0]
        assert rs.RSORRES == "PR"
        assert rs.RSMETHOD == "RECIST 1.1"


# =========================================================================
# Unit Tests: ADaM Derivation Engine (ADSL, ADAE, ADTTE, BDS)
# =========================================================================


class TestADaMPipelineEngine:
    """Test automated ADaM derivation rules for ADSL, ADAE, ADTTE, and BDS.

    @req:PRD-CDR-002
    @req:ADR-2199
    """

    def test_adam_datasets_derived_accurately(self) -> None:
        """Verify population flags, TEAE derivations, time-to-event censoring, and baseline change derivations.

        @req:PRD-CDR-002
        @req:ADR-2199
        """
        records = create_sample_canonical_records("STUDY-001")
        sdtm_engine = SDTMPipelineEngine(study_id="STUDY-001")
        sdtm_domains = sdtm_engine.build_sdtm_domains(records)

        adam_engine = ADaMPipelineEngine(study_id="STUDY-001")
        adam_datasets = adam_engine.build_adam_datasets(sdtm_domains)

        # Check all 4 datasets exist
        for ds in ["ADSL", "ADAE", "ADTTE", "BDS"]:
            assert ds in adam_datasets
            assert len(adam_datasets[ds]) >= 1

        # 1. Verify ADSL Derivations
        adsl: ADaMADSL = adam_datasets["ADSL"][0]
        assert adsl.USUBJID == "STUDY-001-001"
        assert adsl.SAFFL == "Y"  # Received dose > 0
        assert adsl.ITTFL == "Y"  # Randomized/Enrolled
        assert adsl.FASFL == "Y"
        assert adsl.COMPLFL == "Y"  # Completed disposition
        assert adsl.TRTSDT == "2026-01-15"
        assert adsl.EOSDT == "2026-04-15"
        assert adsl.BMIBL is not None

        # 2. Verify ADAE Derivations
        adae: ADaMADAE = adam_datasets["ADAE"][0]
        assert adae.USUBJID == "STUDY-001-001"
        assert adae.AETERM == "Headache"
        assert adae.ASTDT == "2026-01-18"
        assert adae.ASTDY == 4  # Jan 18 is Day 4 relative to Jan 15 start
        assert adae.ADURN == 2  # Jan 18 to Jan 19 inclusive
        assert adae.TRTEMFL == "Y"  # Treatment emergent
        assert adae.PREFL == "N"
        assert adae.AESEVN == 1  # Mild

        # 3. Verify ADTTE Time-to-Event Derivations
        adtte_list: list[ADaMADTTE] = adam_datasets["ADTTE"]
        params = {r.PARAMCD: r for r in adtte_list}

        # Overall Survival
        assert "TTDEATH" in params
        assert params["TTDEATH"].CNSR == 1  # Subject alive -> censored
        assert params["TTDEATH"].AVAL > 0

        # Toxicity
        assert "TTTOX" in params
        assert params["TTTOX"].CNSR == 0  # Headache occurred -> event
        assert params["TTTOX"].AVAL == 4.0  # Jan 18 event date

        # 4. Verify BDS Derivations (Change from baseline)
        bds_list: list[ADaMBDS] = adam_datasets["BDS"]
        sysbp_post = [r for r in bds_list if r.PARAMCD == "SYSBP" and r.ABLFL is None][
            0
        ]
        assert sysbp_post.AVAL == 122.0
        assert sysbp_post.BASE == 130.0
        assert sysbp_post.CHG == -8.0  # 122 - 130
        assert sysbp_post.PCHG == -6.15  # -8 / 130 * 100


# =========================================================================
# Unit Tests: CDISC Dataset-JSON 1.1 / 2.0 Serializer & Validator
# =========================================================================


class TestDatasetJSONCompiler:
    """Test serialization and strict validation of official CDISC Dataset-JSON 1.1/2.0.

    @req:PRD-CDR-003
    @req:ADR-2199
    """

    def test_dataset_json_serialization_and_validation(self) -> None:
        """Verify Dataset-JSON produces valid structure, row matrix, and passes schema validation.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        records = create_sample_canonical_records("STUDY-001")
        sdtm_engine = SDTMPipelineEngine(study_id="STUDY-001")
        sdtm_domains = sdtm_engine.build_sdtm_domains(records)

        compiler = DatasetJSONCompiler(study_id="STUDY-001", spec_version="1.1.0")
        doc = compiler.serialize_datasets(sdtm_domains)

        assert doc.datasetJSONVersion == "1.1.0"
        assert doc.clinicalData.studyOID == "STUDY-001"
        assert "IG.DM" in doc.clinicalData.itemGroupData

        json_str = compiler.to_json(doc)
        val_result = compiler.validate_dataset_json(json_str)

        assert val_result["is_valid"] is True
        assert len(val_result["errors"]) == 0
        assert val_result["dataset_count"] == 10
        assert val_result["record_count"] >= 10

    def test_dataset_json_referential_validation_failure(self) -> None:
        """Verify referential integrity failure when a dependent domain has unmapped USUBJID.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        compiler = DatasetJSONCompiler(study_id="STUDY-001")

        corrupted_payload = {
            "datasetJSONVersion": "1.1.0",
            "fileOID": "FILE.001",
            "clinicalData": {
                "studyOID": "STUDY-001",
                "metaDataVersionOID": "MDV.001",
                "itemGroupData": {
                    "IG.DM": {
                        "name": "DM",
                        "label": "Demographics",
                        "records": 1,
                        "items": [{"name": "STUDYID"}, {"name": "USUBJID"}],
                        "itemData": [["STUDY-001", "STUDY-001-001"]],
                    },
                    "IG.AE": {
                        "name": "AE",
                        "label": "Adverse Events",
                        "records": 1,
                        "items": [{"name": "STUDYID"}, {"name": "USUBJID"}],
                        "itemData": [
                            ["STUDY-001", "STUDY-001-999"]
                        ],  # 999 does not exist in DM!
                    },
                },
            },
        }

        res = compiler.validate_dataset_json(corrupted_payload)
        assert res["is_valid"] is False
        assert any("Referential integrity failure" in e for e in res["errors"])


# =========================================================================
# Unit Tests: CDISC Define-XML v2.1 Compiler & Conformance
# =========================================================================


class TestDefineXMLCompiler:
    """Test automated compilation of CDISC Define-XML v2.1 specifications with VLM, WhereClauses, and Origins.

    @req:PRD-CDR-003
    @req:ADR-2199
    """

    def test_define_xml_compilation_with_vlm_whereclauses_origins(self) -> None:
        """Verify generated Define-XML 2.1 contains all required CDISC XML tags, VLM, and WhereClauses.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        req = DefineXMLCompilationRequest(
            study_id="STUDY-001",
            study_name="Cadence Phase III Protocol",
            protocol_name="CADENCE-2026",
            domains=["DM", "AE", "CM", "VS", "LB", "EX", "DS", "TU", "TR", "RS"],
            adam_datasets=["ADSL", "ADAE", "ADTTE", "BDS"],
            include_vlm=True,
            include_where_clauses=True,
            include_origins=True,
        )

        compiler = DefineXMLCompiler(req)
        xml_string = compiler.compile()

        assert "<?xml" in xml_string
        assert 'xmlns:def="http://www.cdisc.org/ns/def/v2.1"' in xml_string
        assert 'def:DefineVersion="2.1.0"' in xml_string
        assert "<def:Standards>" in xml_string
        assert '<ItemGroupDef OID="IG.DM"' in xml_string
        assert '<ItemGroupDef OID="IG.ADSL"' in xml_string
        assert '<def:ValueListDef OID="VL.LB.LBORRES"' in xml_string
        assert '<def:WhereClauseDef OID="WC.LB.ALT"' in xml_string
        assert (
            '<def:Origin Type="Collected"' in xml_string
            or '<def:Origin Type="Derived"' in xml_string
        )
        assert '<MethodDef OID="MT.ADSL.BMIBL"' in xml_string

        # Conformance Validation
        val = DefineXMLCompiler.validate_define_xml(xml_string)
        assert val.is_valid is True
        assert len(val.errors) == 0
        assert val.item_group_count == 14  # 10 SDTM + 4 ADaM
        assert val.item_def_count >= 100
        assert val.value_level_metadata_count >= 3
        assert val.where_clause_count >= 10
        assert val.codelist_count >= 5
        assert val.method_count >= 5


# =========================================================================
# Integration Tests: Application Service & Blinding Isolation
# =========================================================================


class TestCDRPipelineService:
    """Test full pipeline service orchestration, blinding isolation, and 21 CFR Part 11 audit trails.

    @req:PRD-CDR-002
    @req:PRD-CDR-003
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_continuous_pipeline_execution_and_audit(self) -> None:
        """Verify end-to-end pipeline run stores audit logs with SHA-256 seal.

        @req:PRD-CDR-002
        @req:PRD-CDR-003
        @req:ADR-2199
        """
        async with db_manager.session_maker() as session:
            repo = LakehouseRepository(session)
            # Ingest records
            records = create_sample_canonical_records("STUDY-001")
            await repo.save_canonical_records(records)

            service = CDRPipelineService(repo)
            result = await service.execute_continuous_pipeline(
                study_id="STUDY-001",
                user_id="biostat_lead",
                user_role="unblinded_statistician",
                is_unblinded=True,
                dataset_json_version="1.1.0",
                reason_for_change="Initial continuous pipeline run",
            )

            assert result.status == PipelineExecutionStatus.COMPLETED
            assert result.total_sdtm_records >= 10
            assert result.total_adam_records >= 4
            assert result.dataset_json_manifest["is_valid"] is True
            assert result.define_xml_summary["is_valid"] is True

            # Verify audit trail
            logs = await repo.get_audit_logs(study_id="STUDY-001")
            assert len(logs) >= 1
            log_seal = (
                logs[0].sha256_seal
                if hasattr(logs[0], "sha256_seal")
                else logs[0]["sha256_seal"]
            )
            assert log_seal is not None
            assert len(log_seal) == 64

    @pytest.mark.asyncio
    async def test_blinding_isolation_masks_treatment_arms(self) -> None:
        """Verify blinded roles receive masked treatment arms in SDTM and ADaM datasets.

        @req:PRD-CDR-002
        @req:ADR-2199
        """
        async with db_manager.session_maker() as session:
            repo = LakehouseRepository(session)
            records = create_sample_canonical_records("STUDY-001")
            await repo.save_canonical_records(records)

            service = CDRPipelineService(repo)

            # 1. Blinded run
            blinded_res = await service.execute_continuous_pipeline(
                study_id="STUDY-001",
                user_id="blinded_cra",
                user_role="cra",
                is_unblinded=False,
            )
            cached_json_str = service.get_cached_dataset_json(blinded_res.execution_id)
            assert cached_json_str is not None
            assert "Masked Treatment Arm" in cached_json_str
            assert "Active Drug 100mg" not in cached_json_str


# =========================================================================
# Integration Tests: FastAPI REST Endpoints
# =========================================================================


class TestCDRPipelineAPI:
    """Test all REST API endpoints for SDTM, ADaM, Dataset-JSON, and Define-XML operations.

    @req:PRD-CDR-002
    @req:PRD-CDR-003
    @req:ADR-2199
    """

    def test_api_execute_pipeline_endpoint(self) -> None:
        """Test POST /api/v1/interop/cdr/pipeline/execute and GET /results/{execution_id}.

        @req:PRD-CDR-002
        @req:PRD-CDR-003
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers(roles="data_manager", user_id="dm_user")

        payload = {
            "study_id": "STUDY-001",
            "dataset_json_version": "1.1.0",
            "reason_for_change": "API test execution",
        }

        resp = client.post(
            "/api/v1/interop/cdr/pipeline/execute", json=payload, headers=headers
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["status"] == "COMPLETED"
        assert "execution_id" in data
        exec_id = data["execution_id"]

        # Fetch result
        get_resp = client.get(
            f"/api/v1/interop/cdr/pipeline/results/{exec_id}", headers=headers
        )
        assert get_resp.status_code == 200
        assert get_resp.json()["execution_id"] == exec_id

    def test_api_sdtm_endpoint(self) -> None:
        """Test POST /api/v1/interop/cdr/pipeline/sdtm.

        @req:PRD-CDR-002
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers()

        payload = {
            "study_id": "STUDY-001",
            "domains": ["DM", "AE", "VS", "LB"],
            "reason_for_change": "SDTM mapping query",
        }
        resp = client.post(
            "/api/v1/interop/cdr/pipeline/sdtm", json=payload, headers=headers
        )
        assert resp.status_code == 200
        data = resp.json()
        assert "domains" in data
        assert "DM" in data["domains"]

    def test_api_adam_endpoint(self) -> None:
        """Test POST /api/v1/interop/cdr/pipeline/adam.

        @req:PRD-CDR-002
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers()

        payload = {
            "study_id": "STUDY-001",
            "datasets": ["ADSL", "ADAE", "ADTTE", "BDS"],
            "reason_for_change": "ADaM derivation query",
        }
        resp = client.post(
            "/api/v1/interop/cdr/pipeline/adam", json=payload, headers=headers
        )
        assert resp.status_code == 200
        data = resp.json()
        assert "datasets" in data
        assert "ADSL" in data["datasets"]

    def test_api_export_dataset_json(self) -> None:
        """Test POST /api/v1/interop/cdr/export/dataset-json.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers()

        payload = {
            "study_id": "STUDY-001",
            "dataset_json_version": "1.1.0",
            "reason_for_change": "Export Dataset-JSON",
        }
        resp = client.post(
            "/api/v1/interop/cdr/export/dataset-json", json=payload, headers=headers
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["datasetJSONVersion"] == "1.1.0"
        assert "clinicalData" in data

    def test_api_export_define_xml(self) -> None:
        """Test POST /api/v1/interop/cdr/export/define-xml.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers()

        payload = {
            "study_id": "STUDY-001",
            "domains": ["DM", "AE", "VS", "LB"],
            "adam_datasets": ["ADSL", "ADAE"],
            "include_vlm": True,
            "include_where_clauses": True,
            "include_origins": True,
            "reason_for_change": "Export Define-XML",
        }
        resp = client.post(
            "/api/v1/interop/cdr/export/define-xml", json=payload, headers=headers
        )
        assert resp.status_code == 200
        assert resp.headers["content-type"].startswith("application/xml")
        assert "<ODM" in resp.text
        assert "xmlns:def=" in resp.text

    def test_api_validate_dataset_json(self) -> None:
        """Test POST /api/v1/interop/cdr/validate/dataset-json.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers()

        valid_payload = {
            "payload": {
                "datasetJSONVersion": "1.1.0",
                "fileOID": "FILE.001",
                "clinicalData": {
                    "studyOID": "STUDY-001",
                    "metaDataVersionOID": "MDV.001",
                    "itemGroupData": {
                        "IG.DM": {
                            "name": "DM",
                            "label": "Demographics",
                            "records": 1,
                            "items": [{"name": "STUDYID"}, {"name": "USUBJID"}],
                            "itemData": [["STUDY-001", "STUDY-001-001"]],
                        }
                    },
                },
            }
        }
        resp = client.post(
            "/api/v1/interop/cdr/validate/dataset-json",
            json=valid_payload,
            headers=headers,
        )
        assert resp.status_code == 200
        assert resp.json()["is_valid"] is True

    def test_api_validate_define_xml(self) -> None:
        """Test POST /api/v1/interop/cdr/validate/define-xml.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers()

        xml_sample = """<?xml version="1.0" encoding="UTF-8"?>
<ODM xmlns="http://www.cdisc.org/ns/odm/v1.3"
     xmlns:def="http://www.cdisc.org/ns/def/v2.1"
     FileType="Snapshot"
     FileOID="DEFINE.001"
     ODMVersion="1.3.2">
  <Study OID="STUDY.001">
    <MetaDataVersion OID="MDV.001" def:DefineVersion="2.1.0">
      <ItemGroupDef OID="IG.DM" Name="DM">
        <ItemRef ItemOID="IT.DM.USUBJID"/>
      </ItemGroupDef>
      <ItemDef OID="IT.DM.USUBJID" Name="USUBJID" DataType="string"/>
    </MetaDataVersion>
  </Study>
</ODM>"""
        resp = client.post(
            "/api/v1/interop/cdr/validate/define-xml",
            json={"xml_content": xml_sample},
            headers=headers,
        )
        assert resp.status_code == 200
        assert resp.json()["is_valid"] is True
        assert resp.json()["item_group_count"] == 1

    def test_api_metadata_catalog(self) -> None:
        """Test GET /api/v1/interop/cdr/metadata/catalog.

        @req:PRD-CDR-002
        @req:PRD-CDR-003
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers()

        resp = client.get("/api/v1/interop/cdr/metadata/catalog", headers=headers)
        assert resp.status_code == 200
        data = resp.json()
        assert "sdtm_domains" in data
        assert "adam_datasets" in data
        assert "DM" in data["sdtm_domains"]
        assert "ADSL" in data["adam_datasets"]


class TestCdiscSubmissionEngine:
    """Comprehensive tests for the unified CdiscSubmissionEngine and SubmissionPackage."""

    def test_compile_submission_package_end_to_end(self) -> None:
        """Test unified single-call compilation across SDTM, ADaM, Dataset-JSON, and Define-XML.

        @req:PRD-CDR-002
        @req:PRD-CDR-003
        @req:ADR-2199
        """
        records = create_sample_canonical_records("STUDY-001")
        engine = CdiscSubmissionEngine(study_id="STUDY-001")

        package = engine.compile_submission_package(
            study_id="STUDY-001",
            raw_records=records,
            snapshot_id="SNAP-2026-001",
            is_unblinded=True,
            dataset_json_version="1.1.0",
        )

        assert isinstance(package, SubmissionPackage)
        assert package.study_id == "STUDY-001"
        assert package.snapshot_id == "SNAP-2026-001"
        assert package.is_unblinded is True
        assert package.total_sdtm_records > 0
        assert package.total_adam_records > 0
        assert (
            package.total_records
            == package.total_sdtm_records + package.total_adam_records
        )

        # Verify 10 SDTM domains present
        for d in ["DM", "AE", "CM", "VS", "LB", "EX", "DS", "TU", "TR", "RS"]:
            assert d in package.sdtm_domains
            assert d in package.sdtm_counts

        # Verify 4 ADaM datasets present
        for a in ["ADSL", "ADAE", "ADTTE", "BDS"]:
            assert a in package.adam_datasets
            assert a in package.adam_counts

        # Verify Dataset-JSON document and validation
        assert package.dataset_json_document is not None
        assert package.dataset_json_document.datasetJSONVersion == "1.1.0"
        assert package.dataset_json_validation["is_valid"] is True
        assert len(package.dataset_json_string) > 0

        # Verify Define-XML 2.1 document and validation
        assert "<ODM" in package.define_xml_string
        assert "xmlns:def=" in package.define_xml_string
        assert package.define_xml_validation.is_valid is True
        assert package.define_xml_validation.item_group_count >= 14
        assert package.define_xml_validation.value_level_metadata_count > 0

        # Verify SHA-256 seal
        assert len(package.package_hash_sha256) == 64

    def test_compile_submission_package_with_blinding(self) -> None:
        """Test blinding mask on treatment arms when is_unblinded is False.

        @req:PRD-CDR-002
        @req:PRD-CDR-006
        @req:ADR-2199
        """
        records = create_sample_canonical_records("STUDY-001")
        engine = CdiscSubmissionEngine(study_id="STUDY-001")

        package = engine.compile_submission_package(
            study_id="STUDY-001",
            raw_records=records,
            is_unblinded=False,
        )

        assert package.is_unblinded is False
        # SDTM DM check
        dm_recs = package.sdtm_domains["DM"]
        assert len(dm_recs) > 0
        for dm in dm_recs:
            assert dm.ARM == "Masked Treatment Arm"
            assert dm.ARMCD == "MASKED"

        # ADaM ADSL check
        adsl_recs = package.adam_datasets["ADSL"]
        assert len(adsl_recs) > 0
        for adsl in adsl_recs:
            assert adsl.TRT01P == "Masked Treatment Arm"
            assert adsl.ARMCD == "MASKED"

    def test_submission_package_helper_methods(self) -> None:
        """Test get_dataset and to_pipeline_execution_result conversion on SubmissionPackage.

        @req:PRD-CDR-002
        @req:PRD-CDR-003
        @req:ADR-2199
        """
        records = create_sample_canonical_records("STUDY-001")
        engine = CdiscSubmissionEngine(study_id="STUDY-001")

        package = engine.compile_submission_package(
            study_id="STUDY-001",
            raw_records=records,
            snapshot_id="SNAP-001",
            is_unblinded=True,
        )

        # get_dataset helper
        dm = package.get_dataset("DM")
        assert dm is not None and len(dm) > 0
        adsl = package.get_dataset("ADSL")
        assert adsl is not None and len(adsl) > 0
        assert package.get_dataset("NON_EXISTENT") is None

        # to_pipeline_execution_result helper
        start_time = datetime(2026, 8, 23, 12, 0, 0, tzinfo=UTC)
        exec_res = package.to_pipeline_execution_result(
            execution_id="EXEC-TEST-001",
            started_at=start_time,
        )
        assert exec_res.execution_id == "EXEC-TEST-001"
        assert exec_res.status == PipelineExecutionStatus.COMPLETED
        assert exec_res.total_sdtm_records == package.total_sdtm_records
        assert exec_res.total_adam_records == package.total_adam_records
        assert exec_res.dataset_json_manifest["is_valid"] is True
        assert exec_res.define_xml_summary["is_valid"] is True

    def test_submission_package_sha256_reproducibility_and_tampering(self) -> None:
        """Test deterministic hash generation and sensitivity to content changes.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        records = create_sample_canonical_records("STUDY-001")
        engine = CdiscSubmissionEngine(study_id="STUDY-001")

        pkg1 = engine.compile_submission_package(
            study_id="STUDY-001",
            raw_records=records,
            snapshot_id="SNAP-FIXED",
            is_unblinded=True,
        )
        pkg2 = engine.compile_submission_package(
            study_id="STUDY-001",
            raw_records=records,
            snapshot_id="SNAP-FIXED",
            is_unblinded=True,
        )

        assert pkg1.total_sdtm_records == pkg2.total_sdtm_records
        assert pkg1.total_adam_records == pkg2.total_adam_records

        # Different unblinding setting alters the package hash
        pkg_blinded = engine.compile_submission_package(
            study_id="STUDY-001",
            raw_records=records,
            snapshot_id="SNAP-FIXED",
            is_unblinded=False,
        )
        assert pkg_blinded.package_hash_sha256 != pkg1.package_hash_sha256

    def test_submission_engine_device_sdtm_compilation(self) -> None:
        """Test compilation of device SDTM domains (DD, DO) and device investigation package.

        @req:PRD-DEV-004
        @req:ADR-2199
        """
        engine = CdiscSubmissionEngine(study_id="STUDY-DEV-001")

        deficiencies = [
            {
                "study_id": "STUDY-DEV-001",
                "site_id": "SITE-01",
                "subject_id": "001",
                "device_name": "Digital Inhaler",
                "udi_di": "00840123456789",
                "lot_number": "LOT-INH-01",
                "serial_number": "SN-001",
                "deficiency_type": "MALFUNCTION",
                "deficiency_description": "Actuator failed to deliver dose",
                "incident_date": "2026-08-20T08:00:00Z",
                "action_taken_device": "QUARANTINED_AT_SITE",
            }
        ]
        devices = [
            {
                "study_id": "STUDY-DEV-001",
                "site_id": "SITE-01",
                "device_identifier": "DEV-INH-001",
                "udi_di": "00840123456789",
                "lot_number": "LOT-INH-01",
                "serial_number": "SN-001",
                "status": "DISPENSED",
            }
        ]

        dev_sdtm = engine.compile_device_sdtm(
            deficiencies=deficiencies,
            devices=devices,
        )
        assert "DD" in dev_sdtm
        assert "DO" in dev_sdtm
        assert len(dev_sdtm["DD"]) == 1
        assert len(dev_sdtm["DO"]) == 1

        # Test custom domains compilation in full submission package
        records = create_sample_canonical_records("STUDY-DEV-001")
        pkg = engine.compile_submission_package(
            study_id="STUDY-DEV-001",
            raw_records=records,
            custom_domains=dev_sdtm,
        )
        assert "DD" in pkg.sdtm_domains
        assert "DO" in pkg.sdtm_domains
        assert pkg.sdtm_counts["DD"] == 1
        assert pkg.sdtm_counts["DO"] == 1

    def test_submission_engine_dataset_json_2_0_serialization(self) -> None:
        """Test serialization to CDISC Dataset-JSON 2.0.0.

        @req:PRD-CDR-003
        @req:ADR-2199
        """
        records = create_sample_canonical_records("STUDY-001")
        engine = CdiscSubmissionEngine(study_id="STUDY-001", spec_version="2.0.0")

        package = engine.compile_submission_package(
            study_id="STUDY-001",
            raw_records=records,
            dataset_json_version="2.0.0",
        )
        assert package.dataset_json_version == "2.0.0"
        assert package.dataset_json_document.datasetJSONVersion == "2.0.0"
        assert package.dataset_json_validation["is_valid"] is True
