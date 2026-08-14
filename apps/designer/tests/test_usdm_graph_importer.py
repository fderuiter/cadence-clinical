"""Test suite for CDISC USDM Protocol Ingestion & Neo4j Graph Importer.

Requirements: PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
"""

import pytest

from apps.designer.db import MOCK_STUDIES, MOCK_STUDY_VERSIONS
from apps.designer.delta import MOCK_SOA_DATA
from apps.designer.domain.cdisc.usdm_importer import (
    USDMGraphImporter,
    USDMImporter,
    USDMImportResult,
)
from apps.designer.domain.cdisc.usdm_models import (
    Activity,
    BiomedicalConcept,
    BiomedicalConceptProperty,
    EligibilityCriterion,
    Encounter,
    StudyArm,
    StudyDesign,
    StudyEpoch,
    StudyVersion,
    USDMStudy,
)
from packages.database.mock_graph import MockGraphDriver


@pytest.fixture
def sample_usdm_v4_payload() -> dict:
    """Fixture providing a rich CDISC USDM v4.0 protocol payload."""
    return {
        "id": "study_onc_401",
        "name": "CADENCE-ONC-401",
        "protocolTitle": "Phase III Trial in Advanced Solid Tumors",
        "protocolId": "PROT-401",
        "phase": "PHASE_III",
        "therapeuticArea": "Oncology",
        "usdmVersion": "4.0",
        "studyVersions": [
            {
                "id": "ver_401_1",
                "versionTag": "1.0",
                "status": "DRAFT",
                "versionIndex": 1,
            }
        ],
        "biomedicalConcepts": [
            {
                "id": "bc_vs_sbp",
                "name": "Systolic Blood Pressure",
                "conceptCode": "C25298",
                "displayName": "Systolic BP",
                "cdashDomain": "VS",
                "cdashVariable": "SYSBP",
                "dataType": "numeric",
                "allowableUnits": ["mmHg"],
                "properties": [
                    {
                        "id": "vlm_sbp_val",
                        "name": "SYSBP",
                        "cdashVariable": "SYSBP",
                        "dataType": "numeric",
                        "mandatory": True,
                        "range": "60-250",
                        "unit": "mmHg",
                    }
                ],
            },
            {
                "id": "bc_vs_dbp",
                "name": "Diastolic Blood Pressure",
                "conceptCode": "C25299",
                "displayName": "Diastolic BP",
                "cdashDomain": "VS",
                "cdashVariable": "DIABP",
                "dataType": "numeric",
                "allowableUnits": ["mmHg"],
                "properties": [
                    {
                        "id": "vlm_dbp_val",
                        "name": "DIABP",
                        "cdashVariable": "DIABP",
                        "dataType": "numeric",
                        "mandatory": True,
                        "range": "40-150",
                        "unit": "mmHg",
                    }
                ],
            },
            {
                "id": "bc_eg_qtc",
                "name": "Corrected QT Interval",
                "conceptCode": "C117761",
                "displayName": "QTcF",
                "cdashDomain": "EG",
                "cdashVariable": "QTCFR",
                "dataType": "numeric",
                "allowableUnits": ["msec"],
                "properties": [
                    {
                        "id": "vlm_qtcf_val",
                        "name": "QTCFR",
                        "cdashVariable": "QTCFR",
                        "dataType": "numeric",
                        "mandatory": True,
                        "range": "200-800",
                        "unit": "msec",
                    }
                ],
            },
        ],
        "studyDesigns": [
            {
                "id": "sd_401",
                "name": "Main Oncology Design",
                "designType": "Parallel",
                "arms": [
                    {
                        "id": "arm_investigational",
                        "name": "Investigational Arm A",
                        "armType": "Treatment",
                        "description": "Novel Compound 50mg BID",
                        "targetSampleSize": 150,
                    },
                    {
                        "id": "arm_control",
                        "name": "Control Arm B",
                        "armType": "Active Comparator",
                        "description": "Standard of Care SOC",
                        "targetSampleSize": 150,
                    },
                ],
                "epochs": [
                    {
                        "id": "ep_screening",
                        "name": "Screening",
                        "epochType": "Screening",
                        "sequenceNumber": 1,
                        "sequenceIndex": 1,
                    },
                    {
                        "id": "ep_treatment",
                        "name": "Treatment",
                        "epochType": "Treatment",
                        "sequenceNumber": 2,
                        "sequenceIndex": 2,
                    },
                    {
                        "id": "ep_followup",
                        "name": "Follow-Up",
                        "epochType": "Follow-up",
                        "sequenceNumber": 3,
                        "sequenceIndex": 3,
                    },
                ],
                "encounters": [
                    {
                        "id": "enc_scr_d1",
                        "name": "Screening Visit",
                        "encounterType": "Visit",
                        "epochId": "ep_screening",
                        "targetDay": -14,
                        "windowLower": 7,
                        "windowUpper": 0,
                        "isMandatory": True,
                    },
                    {
                        "id": "enc_c1d1",
                        "name": "Cycle 1 Day 1",
                        "encounterType": "Visit",
                        "epochId": "ep_treatment",
                        "targetDay": 1,
                        "windowLower": 0,
                        "windowUpper": 2,
                        "isMandatory": True,
                    },
                    {
                        "id": "enc_c1d15",
                        "name": "Cycle 1 Day 15",
                        "encounterType": "Visit",
                        "epochId": "ep_treatment",
                        "targetDay": 15,
                        "windowLower": 1,
                        "windowUpper": 1,
                        "isMandatory": True,
                    },
                    {
                        "id": "enc_eot",
                        "name": "End of Treatment",
                        "encounterType": "Visit",
                        "epochId": "ep_followup",
                        "targetDay": 90,
                        "windowLower": 3,
                        "windowUpper": 3,
                        "isMandatory": True,
                    },
                ],
                "activities": [
                    {
                        "id": "act_vital_signs",
                        "name": "Vital Signs Measurement",
                        "description": "BP, Pulse, Temperature, Resp Rate",
                        "cdashDomain": "VS",
                        "biomedicalConceptIds": ["bc_vs_sbp", "bc_vs_dbp"],
                        "assignedVisitNames": [
                            "Screening Visit",
                            "Cycle 1 Day 1",
                            "Cycle 1 Day 15",
                            "End of Treatment",
                        ],
                    },
                    {
                        "id": "act_ecg",
                        "name": "12-Lead Electrocardiogram",
                        "description": "Standard 12-lead triplicate ECG",
                        "cdashDomain": "EG",
                        "biomedicalConceptIds": ["bc_eg_qtc"],
                        "assignedVisitNames": [
                            "Screening Visit",
                            "Cycle 1 Day 1",
                        ],
                    },
                ],
                "eligibilityCriteria": [
                    {
                        "id": "crit_inc_1",
                        "name": "Adult Age",
                        "identifier": "INC-01",
                        "criterionType": "Inclusion",
                        "category": "Demographic",
                        "text": "Subject must be >= 18 years of age at screening.",
                    },
                    {
                        "id": "crit_exc_1",
                        "name": "Prior Therapy",
                        "identifier": "EXC-01",
                        "criterionType": "Exclusion",
                        "category": "Medical History",
                        "text": "Prior systemic therapy within 28 days of Day 1.",
                    },
                ],
            }
        ],
    }


@pytest.mark.asyncio
async def test_usdm_graph_importer_v4_ingestion(
    sample_usdm_v4_payload: dict,
) -> None:
    """Validate USDMGraphImporter parses USDM v4.0 structures and creates valid graph entities in Neo4j.

    @req:PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
    """
    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    result = await importer.import_usdm(
        payload=sample_usdm_v4_payload,
        user_id="user_mdr_001",
        change_reason="Zero-Click USDM Study Ingestion",
    )

    assert isinstance(result, USDMImportResult)
    assert result.study_id == "study_onc_401"
    assert result.protocol_title == "Phase III Trial in Advanced Solid Tumors"
    assert result.phase == "PHASE_III"
    assert result.therapeutic_area == "Oncology"
    assert result.status == "COMMITTED"
    assert len(result.validation_warnings) == 0

    # 1 Study + 1 Version + 1 Design + 2 Arms + 3 Epochs + 4 Encounters + 2 Activities + 3 Concepts + 2 Criteria = 19 nodes
    assert result.nodes_created == 19

    # Relationships:
    # 1 HAS_VERSION + 1 HAS_DESIGN + 2 HAS_ARM + 3 HAS_EPOCH + 4 CONTAINS_ENCOUNTER
    # + 2 HAS_ACTIVITY + 3 HAS_CONCEPT + 2 HAS_CRITERION
    # + 6 PERFORMS (4 vitals + 2 ECG)
    # + 3 MEASURES_CONCEPT (2 vitals + 1 ECG)
    # = 27 relationships
    assert result.relationships_created == 27

    assert result.entity_counts == {
        "study_versions": 1,
        "study_designs": 1,
        "arms": 2,
        "epochs": 3,
        "encounters": 4,
        "activities": 2,
        "biomedical_concepts": 3,
        "eligibility_criteria": 2,
    }

    # Verify MockGraphDriver recorded Cypher transactions
    assert len(mock_driver.sessions) > 0
    session = mock_driver.sessions[0]
    assert len(session.transactions) > 0
    tx = session.transactions[0]
    assert tx.committed is True
    assert tx.rolled_back is False
    assert len(tx.queries) > 0


@pytest.mark.asyncio
async def test_usdm_graph_importer_transactional_rollback(
    sample_usdm_v4_payload: dict,
) -> None:
    """Validate transactional Cypher execution rolls back atomically on database errors.

    @req:PRD-SYS-001
    """
    mock_driver = MockGraphDriver()

    # Define a failing session that raises during transaction
    class FailingSession:
        def __init__(self, s):
            self.s = s

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        def begin_transaction(self):
            from contextlib import asynccontextmanager

            @asynccontextmanager
            async def _failing_tx_cm():
                tx = self.s.transactions[0] if self.s.transactions else None
                if not tx:
                    from packages.database.mock_graph import (
                        MockGraphTransaction,
                    )

                    tx = MockGraphTransaction(self.s)
                    self.s.transactions.append(tx)

                async def fail_run(q, p=None, **kwargs):
                    raise RuntimeError("Simulated Database Connection Failure")

                tx.run = fail_run
                yield tx

            return _failing_tx_cm()

    session_inst = mock_driver.session()
    mock_driver.session = lambda **kwargs: FailingSession(session_inst)

    importer = USDMGraphImporter(mock_driver)

    with pytest.raises(RuntimeError, match="Simulated Database Connection Failure"):
        await importer.import_usdm(sample_usdm_v4_payload)

    # Confirm rollback occurred on the transaction
    assert len(session_inst.transactions) > 0
    tx = session_inst.transactions[0]
    assert tx.rolled_back is True
    assert tx.committed is False


@pytest.mark.asyncio
async def test_usdm_graph_importer_in_memory_state_sync(
    sample_usdm_v4_payload: dict,
) -> None:
    """Validate in-memory authoring state synchronization for fast mock access.

    @req:PRD-SYS-001, PRD-DDF-001
    """
    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    await importer.import_usdm(sample_usdm_v4_payload)

    study_id = "study_onc_401"
    version_id = "ver_401_1"

    # Check MOCK_STUDIES
    assert study_id in MOCK_STUDIES
    study_data = MOCK_STUDIES[study_id]
    assert study_data["protocol_title"] == "Phase III Trial in Advanced Solid Tumors"
    assert len(study_data["arms"]) == 2
    assert len(study_data["epochs"]) == 3
    assert len(study_data["encounters"]) == 4
    assert len(study_data["activities"]) == 2
    assert len(study_data["biomedical_concepts"]) == 3
    assert len(study_data["eligibility_criteria"]) == 2

    # Check MOCK_STUDY_VERSIONS
    assert study_id in MOCK_STUDY_VERSIONS
    versions = MOCK_STUDY_VERSIONS[study_id]
    assert any(v["id"] == version_id for v in versions)

    # Check MOCK_SOA_DATA
    assert version_id in MOCK_SOA_DATA
    soa = MOCK_SOA_DATA[version_id]
    assert "arm_investigational" in soa["arms"]
    assert "ep_screening" in soa["epochs"]
    assert "enc_scr_d1" in soa["visits"]
    assert "act_vital_signs" in soa["procedures"]
    assert len(soa["links"]) > 0


def test_usdm_graph_importer_sync_wrapper(
    sample_usdm_v4_payload: dict,
) -> None:
    """Validate synchronous execution wrapper for USDMGraphImporter.

    @req:PRD-SYS-001
    """
    importer = USDMGraphImporter()
    result = importer.import_usdm_sync(sample_usdm_v4_payload)

    assert isinstance(result, USDMImportResult)
    assert result.study_id == "study_onc_401"
    assert result.nodes_created == 19


@pytest.mark.asyncio
async def test_usdm_importer_alias_compatibility(
    sample_usdm_v4_payload: dict,
) -> None:
    """Validate USDMImporter class alias compatibility.

    @req:PRD-SYS-001
    """
    importer = USDMImporter()
    result = await importer.import_usdm(sample_usdm_v4_payload)

    assert isinstance(result, USDMImportResult)
    assert result.study_id == "study_onc_401"


@pytest.mark.asyncio
async def test_usdm_importer_warning_unknown_concepts() -> None:
    """Validate warning emitted when activity references unknown concept ID.

    @req:PRD-SYS-001
    """
    payload = {
        "id": "study_warn_1",
        "name": "WARN-1",
        "protocolTitle": "Warning Test Study",
        "usdmVersion": "3.0",
        "studyDesigns": [
            {
                "id": "sd_warn",
                "name": "Design Warn",
                "activities": [
                    {
                        "id": "act_unknown",
                        "name": "Activity with missing concept",
                        "biomedicalConceptIds": ["bc_nonexistent_999"],
                    }
                ],
            }
        ],
    }
    importer = USDMGraphImporter()
    result = await importer.import_usdm(payload)

    assert len(result.validation_warnings) == 1
    assert "bc_nonexistent_999" in result.validation_warnings[0]


def test_pydantic_domain_models() -> None:
    """Validate Pydantic v2 strict typing and field initialization across USDM models.

    @req:PRD-SYS-001
    """
    prop = BiomedicalConceptProperty(
        id="prop_1",
        name="SYSBP",
        cdash_variable="SYSBP",
        data_type="numeric",
        mandatory=True,
        range="60-250",
        grid_span=6,
        unit="mmHg",
    )
    assert prop.grid_span == 6
    assert prop.cdash_variable == "SYSBP"

    concept = BiomedicalConcept(
        id="bc_1",
        name="Systolic Blood Pressure",
        concept_code="C25298",
        cdash_domain="VS",
        cdash_variable="SYSBP",
        data_type="numeric",
        properties=[prop],
    )
    assert concept.concept_code == "C25298"
    assert len(concept.properties) == 1

    act = Activity(
        id="act_1",
        name="Vital Signs",
        cdash_domain="VS",
        biomedical_concept_ids=["bc_1"],
        biomedical_concepts=[concept],
    )
    assert act.cdash_domain == "VS"
    assert len(act.biomedical_concepts) == 1

    arm = StudyArm(
        id="arm_1",
        name="Arm A",
        arm_type="Treatment",
        target_sample_size=100,
    )
    assert arm.target_sample_size == 100

    epoch = StudyEpoch(
        id="ep_1",
        name="Screening",
        epoch_type="Screening",
        sequence_number=1,
    )
    assert epoch.name == "Screening"

    enc = Encounter(
        id="enc_1",
        name="Visit 1",
        encounter_type="Visit",
        epoch_id="ep_1",
        target_day=1,
    )
    assert enc.target_day == 1

    crit = EligibilityCriterion(
        id="crit_1",
        name="Age Criterion",
        identifier="INC-01",
        criterion_type="Inclusion",
        text="Age >= 18",
    )
    assert crit.criterion_type == "Inclusion"

    design = StudyDesign(
        id="sd_1",
        name="Design 1",
        arms=[arm],
        epochs=[epoch],
        encounters=[enc],
        activities=[act],
        biomedical_concepts=[concept],
        eligibility_criteria=[crit],
    )
    assert len(design.arms) == 1
    assert len(design.epochs) == 1
    assert len(design.encounters) == 1
    assert len(design.activities) == 1
    assert len(design.biomedical_concepts) == 1
    assert len(design.eligibility_criteria) == 1

    ver = StudyVersion(
        id="ver_1",
        version_tag="2.0",
        status="ACTIVE",
        version_index=2,
        study_designs=[design],
    )
    assert ver.version_tag == "2.0"
    assert len(ver.study_designs) == 1

    study = USDMStudy(
        id="study_test",
        name="TEST-01",
        protocol_title="Test Protocol",
        study_versions=[ver],
        study_designs=[design],
        biomedical_concepts=[concept],
    )
    assert study.protocol_title == "Test Protocol"
    assert len(study.study_versions) == 1
    assert len(study.study_designs) == 1
    assert len(study.biomedical_concepts) == 1
