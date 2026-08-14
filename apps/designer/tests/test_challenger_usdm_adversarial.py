"""Adversarial stress-test suite for CDISC USDM Ingestion and Neo4j Graph Model (Milestone M1).

Validates:
1. Cypher query syntax, parameter bindings, and strict conformance to validate_cypher_query.
2. Relational semantics: PERFORMS (Encounter -> Activity), MEASURES_CONCEPT (Activity -> BiomedicalConcept),
   HAS_CRITERION (StudyDesign/Study -> EligibilityCriterion), CONTAINS_ENCOUNTER, HAS_ARM, HAS_EPOCH.
3. Complex multi-arm, multi-epoch, and multi-design protocol parsing and exact entity counting.
4. Resistance to SQL/Cypher injection payloads and special character handling.
5. Transactional rollback integrity on mid-stream database execution errors.

Requirements: PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
"""

import pytest

from apps.designer.domain.cdisc.usdm_importer import (
    USDMGraphImporter,
)
from apps.designer.domain.cdisc.usdm_models import (
    BiomedicalConcept,
)
from packages.database.graph import validate_cypher_query
from packages.database.mock_graph import MockGraphDriver


@pytest.fixture
def complex_multi_arm_multi_epoch_payload() -> dict:
    """Provides a complex multi-arm, multi-epoch, multi-design clinical protocol."""
    return {
        "id": "study_complex_800",
        "name": "CADENCE-ADVERSARIAL-800",
        "protocolTitle": "A Phase III 4-Arm Multi-Center Oncology Protocol with Nested Epochs",
        "protocolId": "PROT-ADV-800",
        "phase": "PHASE_III",
        "therapeuticArea": "Oncology / Immunotherapy",
        "usdmVersion": "4.0",
        "studyVersions": [
            {
                "id": "ver_800_1",
                "versionTag": "1.0",
                "status": "APPROVED",
                "versionIndex": 1,
            },
            {
                "id": "ver_800_2",
                "versionTag": "2.0",
                "status": "DRAFT",
                "versionIndex": 2,
            },
        ],
        "biomedicalConcepts": [
            {
                "id": "bc_root_sbp",
                "name": "Systolic Blood Pressure",
                "conceptCode": "C25298",
                "displayName": "Systolic BP",
                "cdashDomain": "VS",
                "cdashVariable": "SYSBP",
                "dataType": "numeric",
                "allowableUnits": ["mmHg"],
                "properties": [
                    {
                        "id": "vlm_sbp",
                        "name": "SYSBP",
                        "cdashVariable": "SYSBP",
                        "dataType": "numeric",
                        "mandatory": True,
                        "range": "60-250",
                    }
                ],
            },
            {
                "id": "bc_root_dbp",
                "name": "Diastolic Blood Pressure",
                "conceptCode": "C25299",
                "displayName": "Diastolic BP",
                "cdashDomain": "VS",
                "cdashVariable": "DIABP",
                "dataType": "numeric",
                "allowableUnits": ["mmHg"],
                "properties": [
                    {
                        "id": "vlm_dbp",
                        "name": "DIABP",
                        "cdashVariable": "DIABP",
                        "dataType": "numeric",
                        "mandatory": True,
                        "range": "40-150",
                    }
                ],
            },
        ],
        "studyDesigns": [
            {
                "id": "sd_main_parallel",
                "name": "Main Parallel Arms Design",
                "designType": "Parallel",
                "arms": [
                    {
                        "id": "arm_mono_low",
                        "name": "Arm 1: Monotherapy Low Dose",
                        "armType": "Experimental",
                        "description": "Drug X 50mg QD",
                        "targetSampleSize": 100,
                    },
                    {
                        "id": "arm_mono_high",
                        "name": "Arm 2: Monotherapy High Dose",
                        "armType": "Experimental",
                        "description": "Drug X 150mg QD",
                        "targetSampleSize": 100,
                    },
                    {
                        "id": "arm_combo",
                        "name": "Arm 3: Combination Therapy",
                        "armType": "Experimental",
                        "description": "Drug X 100mg + Drug Y 20mg",
                        "targetSampleSize": 100,
                    },
                    {
                        "id": "arm_soc_control",
                        "name": "Arm 4: Standard of Care Control",
                        "armType": "Active Comparator",
                        "description": "SOC chemotherapy",
                        "targetSampleSize": 100,
                    },
                ],
                "epochs": [
                    {
                        "id": "ep_pre_scr",
                        "name": "Pre-Screening",
                        "epochType": "Screening",
                        "sequenceNumber": 1,
                        "sequenceIndex": 1,
                    },
                    {
                        "id": "ep_scr",
                        "name": "Screening",
                        "epochType": "Screening",
                        "sequenceNumber": 2,
                        "sequenceIndex": 2,
                    },
                    {
                        "id": "ep_runin",
                        "name": "Lead-In Washout",
                        "epochType": "Washout",
                        "sequenceNumber": 3,
                        "sequenceIndex": 3,
                    },
                    {
                        "id": "ep_c1_c6",
                        "name": "Cycle 1 to 6 Induction",
                        "epochType": "Treatment",
                        "sequenceNumber": 4,
                        "sequenceIndex": 4,
                    },
                    {
                        "id": "ep_maint",
                        "name": "Maintenance Phase",
                        "epochType": "Treatment",
                        "sequenceNumber": 5,
                        "sequenceIndex": 5,
                    },
                    {
                        "id": "ep_fu_survival",
                        "name": "Long-Term Survival Follow-Up",
                        "epochType": "Follow-up",
                        "sequenceNumber": 6,
                        "sequenceIndex": 6,
                    },
                ],
                "encounters": [
                    {
                        "id": "enc_pscr",
                        "name": "Pre-Screening Consent",
                        "encounterType": "Visit",
                        "epochId": "ep_pre_scr",
                        "targetDay": -28,
                        "windowLower": 7,
                        "windowUpper": 0,
                    },
                    {
                        "id": "enc_scr",
                        "name": "Screening Baseline",
                        "encounterType": "Visit",
                        "epochId": "ep_scr",
                        "targetDay": -14,
                        "windowLower": 3,
                        "windowUpper": 0,
                    },
                    {
                        "id": "enc_washout",
                        "name": "Washout Check",
                        "encounterType": "Visit",
                        "epochId": "ep_runin",
                        "targetDay": -7,
                        "windowLower": 1,
                        "windowUpper": 1,
                    },
                    {
                        "id": "enc_c1d1",
                        "name": "Cycle 1 Day 1",
                        "encounterType": "Visit",
                        "epochId": "ep_c1_c6",
                        "targetDay": 1,
                        "windowLower": 0,
                        "windowUpper": 2,
                    },
                    {
                        "id": "enc_c1d15",
                        "name": "Cycle 1 Day 15",
                        "encounterType": "Visit",
                        "epochId": "ep_c1_c6",
                        "targetDay": 15,
                        "windowLower": 1,
                        "windowUpper": 1,
                    },
                    {
                        "id": "enc_c2d1",
                        "name": "Cycle 2 Day 1",
                        "encounterType": "Visit",
                        "epochId": "ep_c1_c6",
                        "targetDay": 29,
                        "windowLower": 2,
                        "windowUpper": 2,
                    },
                    {
                        "id": "enc_maint_m1",
                        "name": "Maintenance Month 1",
                        "encounterType": "Visit",
                        "epochId": "ep_maint",
                        "targetDay": 180,
                        "windowLower": 7,
                        "windowUpper": 7,
                    },
                    {
                        "id": "enc_eot",
                        "name": "End of Treatment",
                        "encounterType": "Visit",
                        "epochId": "ep_maint",
                        "targetDay": 360,
                        "windowLower": 7,
                        "windowUpper": 7,
                    },
                    {
                        "id": "enc_fu_m6",
                        "name": "Survival Follow-Up Month 6",
                        "encounterType": "Visit",
                        "epochId": "ep_fu_survival",
                        "targetDay": 540,
                        "windowLower": 14,
                        "windowUpper": 14,
                    },
                ],
                "activities": [
                    {
                        "id": "act_ic",
                        "name": "Informed Consent",
                        "cdashDomain": "DM",
                        "assignedVisitNames": ["Pre-Screening Consent"],
                    },
                    {
                        "id": "act_vs",
                        "name": "Vital Signs",
                        "cdashDomain": "VS",
                        "biomedicalConceptIds": ["bc_root_sbp", "bc_root_dbp"],
                        "assignedVisitNames": [
                            "Pre-Screening Consent",
                            "Screening Baseline",
                            "Washout Check",
                            "Cycle 1 Day 1",
                            "Cycle 1 Day 15",
                            "Cycle 2 Day 1",
                            "Maintenance Month 1",
                            "End of Treatment",
                        ],
                    },
                    {
                        "id": "act_ecg",
                        "name": "12-Lead ECG",
                        "cdashDomain": "EG",
                        "biomedicalConceptCode": "C117761",
                        "assignedVisitNames": [
                            "Screening Baseline",
                            "Cycle 1 Day 1",
                            "Cycle 2 Day 1",
                            "End of Treatment",
                        ],
                    },
                    {
                        "id": "act_labs",
                        "name": "Hematology & Chemistry Panel",
                        "cdashDomain": "LB",
                        "biomedicalConceptIds": ["bc_design_neut", "bc_design_alt"],
                        "assignedEncounterIds": [
                            "enc_scr",
                            "enc_c1d1",
                            "enc_c1d15",
                            "enc_c2d1",
                            "enc_eot",
                        ],
                    },
                    {
                        "id": "act_survival",
                        "name": "Survival Status Check",
                        "cdashDomain": "DS",
                        "assignedEncounterIds": ["enc_fu_m6"],
                    },
                ],
                "biomedicalConcepts": [
                    {
                        "id": "bc_design_neut",
                        "name": "Absolute Neutrophil Count",
                        "conceptCode": "C64848",
                        "displayName": "ANC",
                        "cdashDomain": "LB",
                        "cdashVariable": "NEUT",
                        "dataType": "numeric",
                    },
                    {
                        "id": "bc_design_alt",
                        "name": "Alanine Aminotransferase",
                        "conceptCode": "C64433",
                        "displayName": "ALT",
                        "cdashDomain": "LB",
                        "cdashVariable": "ALT",
                        "dataType": "numeric",
                    },
                    {
                        "id": "bc_design_ecg_qtc",
                        "name": "Fridericia QTc Interval",
                        "conceptCode": "C117761",
                        "displayName": "QTcF",
                        "cdashDomain": "EG",
                        "cdashVariable": "QTCFR",
                        "dataType": "numeric",
                    },
                ],
                "eligibilityCriteria": [
                    {
                        "id": "crit_inc_age",
                        "name": "Age Inclusion",
                        "identifier": "INC-01",
                        "criterionType": "Inclusion",
                        "category": "Demographics",
                        "text": "Subject must be >= 18 years.",
                    },
                    {
                        "id": "crit_inc_ecog",
                        "name": "ECOG Performance Status",
                        "identifier": "INC-02",
                        "criterionType": "Inclusion",
                        "category": "Clinical Status",
                        "text": "ECOG PS 0 or 1.",
                    },
                    {
                        "id": "crit_exc_organ",
                        "name": "Inadequate Organ Function",
                        "identifier": "EXC-01",
                        "criterionType": "Exclusion",
                        "category": "Laboratory",
                        "text": "ANC < 1.5 x 10^9/L or Platelets < 100 x 10^9/L.",
                    },
                ],
            },
            {
                "id": "sd_biomarker_substudy",
                "name": "Exploratory Biomarker Sub-Study",
                "designType": "Observational",
                "arms": [
                    {
                        "id": "arm_sub_cohort",
                        "name": "Biomarker Cohort",
                        "armType": "Sub-Study",
                        "targetSampleSize": 50,
                    }
                ],
                "epochs": [
                    {
                        "id": "ep_sub_baseline",
                        "name": "Biomarker Baseline",
                        "epochType": "Screening",
                        "sequenceNumber": 1,
                    }
                ],
                "encounters": [
                    {
                        "id": "enc_sub_b1",
                        "name": "Biomarker Sample 1",
                        "encounterType": "Sampling Visit",
                        "epochId": "ep_sub_baseline",
                        "targetDay": 1,
                    }
                ],
                "activities": [
                    {
                        "id": "act_sub_cfdna",
                        "name": "cfDNA Blood Collection",
                        "cdashDomain": "BS",
                        "assignedVisitNames": ["Biomarker Sample 1"],
                    }
                ],
                "biomedicalConcepts": [
                    {
                        "id": "bc_sub_cfdna",
                        "name": "Circulating Tumor DNA",
                        "conceptCode": "C123456",
                        "cdashDomain": "BS",
                    }
                ],
                "eligibilityCriteria": [
                    {
                        "id": "crit_sub_consent",
                        "name": "Optional Biobank Consent",
                        "identifier": "SUB-INC-01",
                        "criterionType": "Inclusion",
                        "text": "Optional genomics consent signed.",
                    }
                ],
            },
        ],
    }


@pytest.mark.asyncio
async def test_adversarial_cypher_query_validation_and_safety(
    complex_multi_arm_multi_epoch_payload: dict,
) -> None:
    """Stress-test: Verify all generated Cypher statements pass strict validate_cypher_query bounds.

    @req:PRD-SYS-001, PRD-DDF-001
    """
    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    result = await importer.import_usdm(complex_multi_arm_multi_epoch_payload)
    assert result.status == "COMMITTED"

    session = mock_driver.sessions[0]
    assert len(session.transactions) > 0
    tx = session.transactions[0]

    # Check every Cypher query recorded by the mock driver
    assert len(tx.queries) >= 11, f"Expected at least 11 queries, got {len(tx.queries)}"

    for query_text, params in tx.queries:
        assert isinstance(params, dict)

        # 1. Must pass validate_cypher_query without throwing ValueError
        validate_cypher_query(query_text)

        # 2. Assert no unbounded relationship traversals exist
        assert "-[*]->" not in query_text
        assert "-[:" not in query_text or "*]" not in query_text

        # 3. Assert all variables are bound via parameters, not string concatenation
        assert (
            "$study_id" in query_text
            or "$versions" in query_text
            or "$performs" in query_text
            or "$measures" in query_text
            or "$criteria" in query_text
            or "$designs" in query_text
            or "$epochs" in query_text
            or "$arms" in query_text
            or "$encounters" in query_text
            or "$concepts" in query_text
            or "$activities" in query_text
        )


@pytest.mark.asyncio
async def test_adversarial_relational_edge_semantics_and_directions(
    complex_multi_arm_multi_epoch_payload: dict,
) -> None:
    """Stress-test: Verify exact edge directions for PERFORMS, MEASURES_CONCEPT, and HAS_CRITERION.

    @req:PRD-SYS-001, PRD-MDR-007
    """
    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    result = await importer.import_usdm(complex_multi_arm_multi_epoch_payload)
    assert result.status == "COMMITTED"

    session = mock_driver.sessions[0]
    tx = session.transactions[0]

    # Find the PERFORMS query
    performs_records = [(q, p) for q, p in tx.queries if "PERFORMS" in q]
    assert len(performs_records) == 1
    p_query, p_params = performs_records[0]

    # Verify edge direction is Encounter -> Activity
    assert (
        "MATCH (en:Encounter {id: p.encounter_id}), (ac:Activity {id: p.activity_id})"
        in p_query
    )
    assert "MERGE (en)-[:PERFORMS]->(ac)" in p_query
    assert p_params is not None and "performs" in p_params
    performs_list = p_params["performs"]
    assert len(performs_list) > 0
    # Check that vital signs has 8 visits assigned
    vs_links = [p for p in performs_list if p["activity_id"] == "act_vs"]
    assert len(vs_links) == 8
    # Check that labs (assigned via assignedEncounterIds) has 5 visits assigned
    lab_links = [p for p in performs_list if p["activity_id"] == "act_labs"]
    assert len(lab_links) == 5

    # Find the MEASURES_CONCEPT query
    measures_records = [(q, p) for q, p in tx.queries if "MEASURES_CONCEPT" in q]
    assert len(measures_records) == 1
    m_query, m_params = measures_records[0]

    # Verify edge direction is Activity -> BiomedicalConcept
    assert (
        "MATCH (ac:Activity {id: m.activity_id}), (bc:BiomedicalConcept {id: m.concept_id})"
        in m_query
    )
    assert "MERGE (ac)-[:MEASURES_CONCEPT]->(bc)" in m_query
    assert m_params is not None and "measures" in m_params
    measures_list = m_params["measures"]
    assert len(measures_list) > 0
    # Vital signs measures 2 concepts (sbp, dbp)
    vs_concepts = [m for m in measures_list if m["activity_id"] == "act_vs"]
    assert len(vs_concepts) == 2
    # ECG measures concept matched by concept_code C117761 -> bc_design_ecg_qtc
    ecg_concepts = [m for m in measures_list if m["activity_id"] == "act_ecg"]
    assert len(ecg_concepts) == 1
    assert ecg_concepts[0]["concept_id"] == "bc_design_ecg_qtc"

    # Find the HAS_CRITERION query
    criteria_records = [(q, p) for q, p in tx.queries if "HAS_CRITERION" in q]
    assert len(criteria_records) == 1
    c_query, _ = criteria_records[0]
    assert "MERGE (sd)-[:HAS_CRITERION]->(crit)" in c_query
    assert "MERGE (s)-[:HAS_CRITERION]->(crit)" in c_query


@pytest.mark.asyncio
async def test_adversarial_entity_counts_complex_multi_arm_protocol(
    complex_multi_arm_multi_epoch_payload: dict,
) -> None:
    """Stress-test: Verify exact node and relationship counts on multi-arm, multi-design protocol.

    @req:PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
    """
    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    result = await importer.import_usdm(complex_multi_arm_multi_epoch_payload)

    # Node count breakdown:
    # 1 Study
    # + 2 StudyVersions
    # + 2 StudyDesigns (main + biomarker)
    # + 5 Arms (4 in main + 1 in biomarker)
    # + 7 Epochs (6 in main + 1 in biomarker)
    # + 10 Encounters (9 in main + 1 in biomarker)
    # + 6 Activities (5 in main + 1 in biomarker)
    # + 6 BiomedicalConcepts (2 root + 3 in main + 1 in biomarker)
    # + 4 EligibilityCriteria (3 in main + 1 in biomarker)
    # Total Nodes = 1 + 2 + 2 + 5 + 7 + 10 + 6 + 6 + 4 = 43
    assert result.nodes_created == 43

    assert result.entity_counts == {
        "study_versions": 2,
        "study_designs": 2,
        "arms": 5,
        "epochs": 7,
        "encounters": 10,
        "activities": 6,
        "biomedical_concepts": 6,
        "eligibility_criteria": 4,
    }

    # Relationships breakdown:
    # 2 HAS_VERSION (Study -> StudyVersion)
    # + 2 HAS_DESIGN (Study -> StudyDesign)
    # + 5 HAS_ARM (StudyDesign -> StudyArm)
    # + 7 HAS_EPOCH (StudyDesign -> StudyEpoch)
    # + 10 CONTAINS_ENCOUNTER (StudyEpoch -> Encounter)
    # + 6 HAS_ACTIVITY (StudyDesign -> Activity)
    # + 6 HAS_CONCEPT (StudyDesign -> BiomedicalConcept)
    # + 4 HAS_CRITERION (StudyDesign/Study -> EligibilityCriterion)
    # + 20 PERFORMS (1 IC + 8 VS + 4 ECG + 5 Labs + 1 Survival + 1 Sub-study)
    # + 5 MEASURES_CONCEPT (2 VS + 1 ECG + 2 Labs)
    # Total Relationships = 2 + 2 + 5 + 7 + 10 + 6 + 6 + 4 + 20 + 5 = 67
    assert result.relationships_created == 67


@pytest.mark.asyncio
async def test_adversarial_injection_resistance_and_special_characters() -> None:
    """Stress-test: Verify injection strings, apostrophes, Unicode, and Cypher syntax tokens are safely parameterized.

    @req:PRD-SYS-001
    """
    adversarial_payload = {
        "id": "study_inject_999",
        "name": "CADENCE-INJECT-999'; DROP (n); MATCH (m) DETACH DELETE m; //",
        "protocolTitle": 'Trial of "Special" & <Escaped> Characters: 🧪 \u00e9\u00e8\u00e0 \u4e2d\u6587 -- // /* comment */',
        "phase": "PHASE_II/III",
        "therapeuticArea": "O'Brien & D'Souza Oncology Clinic",
        "usdmVersion": "4.0",
        "studyDesigns": [
            {
                "id": "sd_inject_1",
                "name": "Design with 'Quotes' and /* nested comments */",
                "arms": [
                    {
                        "id": "arm_sql_inject",
                        "name": "Arm 1: $user_id OR 1=1 --",
                        "description": "WHERE n.id = $id RETURN n UNION MATCH (x) RETURN x",
                    }
                ],
                "epochs": [
                    {
                        "id": "ep_unicode",
                        "name": "Époque de Dépistage (Screening) 🧬",
                        "epochType": "Screening",
                        "sequenceNumber": 1,
                    }
                ],
                "encounters": [
                    {
                        "id": "enc_spec_char",
                        "name": "Visit #1: Baseline & Pre-Dose [T=0h]",
                        "encounterType": "Visit",
                        "epochId": "ep_unicode",
                        "targetDay": 1,
                    }
                ],
                "activities": [
                    {
                        "id": "act_cypher_kw",
                        "name": "MERGE (n:Hacked) SET n.pwned = true",
                        "cdashDomain": "VS",
                        "biomedicalConceptIds": ["bc_inject_1"],
                        "assignedVisitNames": ["Visit #1: Baseline & Pre-Dose [T=0h]"],
                    }
                ],
                "biomedicalConcepts": [
                    {
                        "id": "bc_inject_1",
                        "name": "SYSBP'; MATCH (a) DELETE a;",
                        "conceptCode": "C25298",
                        "cdashDomain": "VS",
                        "cdashVariable": "SYSBP",
                    }
                ],
                "eligibilityCriteria": [
                    {
                        "id": "crit_inject_1",
                        "name": "Age >= 18 AND 1=1; //",
                        "criterionType": "Inclusion",
                        "text": "Subject age >= 18; DROP ALL;",
                    }
                ],
            }
        ],
    }

    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    result = await importer.import_usdm(adversarial_payload)
    assert result.status == "COMMITTED"
    assert result.study_id == "study_inject_999"

    # Verify queries were safe
    session = mock_driver.sessions[0]
    tx = session.transactions[0]
    for q_text, _ in tx.queries:
        validate_cypher_query(q_text)


@pytest.mark.asyncio
async def test_adversarial_empty_protocol_and_missing_optional_fields() -> None:
    """Stress-test: Verify importer handles minimal and empty protocol specifications cleanly.

    @req:PRD-SYS-001, PRD-DDF-001
    """
    minimal_payload = {
        "id": "study_minimal_001",
        "name": "MINIMAL-001",
    }

    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    result = await importer.import_usdm(minimal_payload)
    assert result.status == "COMMITTED"
    assert result.study_id == "study_minimal_001"
    assert result.nodes_created == 1  # Just the Study node
    assert result.relationships_created == 0
    assert len(result.validation_warnings) == 1
    assert "0 study designs" in result.validation_warnings[0]


@pytest.mark.asyncio
async def test_adversarial_concept_deduplication_and_multiple_references() -> None:
    """Stress-test: Verify duplicate concept definitions across root, design, and activities are deduplicated.

    @req:PRD-SYS-001, PRD-MDR-007
    """
    shared_concept = {
        "id": "bc_shared_vitals",
        "name": "Shared Vitals Concept",
        "conceptCode": "C25298",
        "cdashDomain": "VS",
        "cdashVariable": "SYSBP",
    }

    payload = {
        "id": "study_dedup_001",
        "name": "DEDUP-001",
        "biomedicalConcepts": [shared_concept],  # Defined at root
        "studyDesigns": [
            {
                "id": "sd_dedup",
                "name": "Design 1",
                "biomedicalConcepts": [shared_concept],  # Defined also in design
                "activities": [
                    {
                        "id": "act_v1",
                        "name": "Vital Signs 1",
                        "biomedicalConceptIds": ["bc_shared_vitals"],
                        "biomedicalConcepts": [
                            BiomedicalConcept.model_validate(shared_concept)
                        ],  # Defined inside activity
                    },
                    {
                        "id": "act_v2",
                        "name": "Vital Signs 2",
                        "biomedicalConceptIds": ["bc_shared_vitals"],
                    },
                ],
            }
        ],
    }

    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    result = await importer.import_usdm(payload)
    # The concept should only be counted once
    assert result.entity_counts["biomedical_concepts"] == 1
    # Nodes: 1 Study + 1 Design + 2 Activities + 1 Concept = 5
    assert result.nodes_created == 5
    # Relationships: 1 HAS_DESIGN + 2 HAS_ACTIVITY + 1 HAS_CONCEPT + 3 MEASURES_CONCEPT = 7
    assert result.relationships_created == 7


@pytest.mark.asyncio
async def test_adversarial_transaction_rollback_on_query_failure() -> None:
    """Stress-test: Verify transaction rollback executes and leaves mock driver in clean rolled_back state when any query fails.

    @req:PRD-SYS-001
    """
    mock_driver = MockGraphDriver()

    class RollbackFailingSession:
        def __init__(self, s):
            self.s = s

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        def begin_transaction(self):
            from contextlib import asynccontextmanager

            @asynccontextmanager
            async def _failing_cm():
                from packages.database.mock_graph import MockGraphTransaction

                tx = MockGraphTransaction(self.s)
                self.s.transactions.append(tx)

                call_count = 0

                async def step_fail_run(query, params=None, **kwargs):
                    nonlocal call_count
                    call_count += 1
                    if call_count > 2:  # Fail on 3rd query
                        raise ConnectionResetError(
                            "Simulated Neo4j socket drop during batch MERGE"
                        )
                    return []

                tx.run = step_fail_run
                yield tx

            return _failing_cm()

    session_inst = mock_driver.session()
    mock_driver.session = lambda **kwargs: RollbackFailingSession(session_inst)

    importer = USDMGraphImporter(mock_driver)

    with pytest.raises(ConnectionResetError, match="Simulated Neo4j socket drop"):
        await importer.import_usdm(
            {
                "id": "study_fail_rb",
                "name": "FAIL-RB",
                "studyDesigns": [
                    {
                        "id": "sd_1",
                        "name": "Design 1",
                        "arms": [{"id": "a1", "name": "Arm 1"}],
                        "epochs": [{"id": "e1", "name": "Epoch 1"}],
                    }
                ],
            }
        )

    assert len(session_inst.transactions) > 0
    tx = session_inst.transactions[0]
    assert tx.rolled_back is True
    assert tx.committed is False
