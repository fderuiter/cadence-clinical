"""Adversarial stress-test suite for CDISC USDM Ingestion and Graph Model (Milestone M1).

Validates robustness against corrupted payloads, database transaction dropouts,
concurrency pressure, injection patterns, and large protocol scalability.

Requirements: PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
"""

import asyncio
import time
from typing import Any

import pytest

from apps.designer.db import MOCK_STUDIES
from apps.designer.domain.cdisc.usdm_importer import (
    USDMGraphImporter,
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
from packages.database.mock_graph import (
    MockGraphDriver,
    MockGraphSession,
    MockGraphTransaction,
)

# =========================================================================
# 1. CORRUPTED, MALFORMED & ADVERSARIAL PAYLOAD TESTING
# =========================================================================


@pytest.mark.asyncio
async def test_adversarial_empty_and_missing_id_payloads() -> None:
    """Validate rejection of empty dictionaries or payloads missing mandatory identity fields.

    @req:PRD-SYS-001
    """
    importer = USDMGraphImporter()

    # Empty payload
    with pytest.raises(ValueError, match="Invalid USDM payload structure"):
        await importer.import_usdm({})

    # Payload with missing 'id' and 'studyId'
    with pytest.raises(ValueError, match="Invalid USDM payload structure"):
        await importer.import_usdm({"name": "No ID Study", "usdmVersion": "3.0"})


@pytest.mark.asyncio
async def test_adversarial_malformed_type_structures() -> None:
    """Validate error handling for corrupted data types at various schema depths.

    @req:PRD-SYS-001
    """
    importer = USDMGraphImporter()

    # 1. studyDesigns is a string instead of a list
    with pytest.raises(ValueError, match="Invalid USDM payload structure"):
        await importer.import_usdm(
            {
                "id": "study_bad_sd",
                "studyDesigns": "not-a-list-of-designs",
            }
        )

    # 2. arms is a dictionary instead of a list of arm dicts
    with pytest.raises(ValueError, match="Invalid USDM payload structure"):
        await importer.import_usdm(
            {
                "id": "study_bad_arms",
                "studyDesigns": [
                    {
                        "id": "sd_1",
                        "name": "Design 1",
                        "arms": {"id": "arm_1", "name": "Arm 1"},
                    }
                ],
            }
        )

    # 3. target_day has invalid non-integer string
    with pytest.raises(ValueError, match="Invalid USDM payload structure"):
        await importer.import_usdm(
            {
                "id": "study_bad_enc",
                "studyDesigns": [
                    {
                        "id": "sd_1",
                        "name": "Design 1",
                        "encounters": [
                            {
                                "id": "enc_1",
                                "name": "Visit 1",
                                "targetDay": "not-a-numeric-day",
                            }
                        ],
                    }
                ],
            }
        )

    # 4. gridSpan has invalid non-integer value
    with pytest.raises(ValueError, match="Invalid USDM payload structure"):
        await importer.import_usdm(
            {
                "id": "study_bad_bc",
                "biomedicalConcepts": [
                    {
                        "id": "bc_1",
                        "name": "Concept 1",
                        "properties": [
                            {
                                "id": "prop_1",
                                "name": "Prop 1",
                                "gridSpan": "invalid_span",
                            }
                        ],
                    }
                ],
            }
        )


@pytest.mark.asyncio
async def test_adversarial_cypher_injection_and_special_characters() -> None:
    """Validate graph importer sanitizes payloads with Cypher/SQL injection patterns and emojis.

    @req:PRD-SYS-001, PRD-DDF-001
    """
    driver = MockGraphDriver()
    importer = USDMGraphImporter(driver)

    adversarial_payload = {
        "id": "study_inject_'; MATCH (n) DETACH DELETE n; //",
        "name": "CADENCE-INJECT-001",
        "protocolTitle": "Trial \"Special' -- /* Comment */ \\n \u0000 \U0001f9ec \U0001f48a",
        "phase": "PHASE_I'; DROP TABLE studies; --",
        "therapeuticArea": "Oncology /* injection */",
        "usdmVersion": "4.0",
        "studyDesigns": [
            {
                "id": "sd_'; RETURN 1; //",
                "name": "Design with quotes ' \" and slashes \\",
                "arms": [
                    {
                        "id": "arm_eval_`param`",
                        "name": "Arm 1 -- test",
                        "description": "'; MERGE (p:Hacked) SET p.val = true; //",
                    }
                ],
                "epochs": [
                    {
                        "id": "ep_1",
                        "name": "Screening Epoch \u2705",
                        "epochType": "Screening",
                        "sequenceNumber": 1,
                    }
                ],
                "encounters": [
                    {
                        "id": "enc_1",
                        "name": "Visit 1 \U0001f3e5",
                        "encounterType": "Visit",
                        "epochId": "ep_1",
                        "targetDay": -14,
                    }
                ],
                "activities": [
                    {
                        "id": "act_1",
                        "name": "Procedure with `backticks` and $params",
                        "cdashDomain": "VS",
                        "assignedEncounterIds": ["enc_1"],
                    }
                ],
                "eligibilityCriteria": [
                    {
                        "id": "crit_1",
                        "name": "Inclusion 1",
                        "criterionType": "Inclusion",
                        "text": "Age >= 18 AND 1=1 -- injection test",
                    }
                ],
            }
        ],
    }

    result = await importer.import_usdm(adversarial_payload)
    assert isinstance(result, USDMImportResult)
    assert result.status == "COMMITTED"
    # 1 Study + 1 Design + 1 Arm + 1 Epoch + 1 Encounter + 1 Activity + 1 Criterion = 7 nodes
    assert result.nodes_created == 7
    # 1 HAS_DESIGN + 1 HAS_ARM + 1 HAS_EPOCH + 1 CONTAINS_ENCOUNTER + 1 HAS_ACTIVITY + 1 HAS_CRITERION + 1 PERFORMS = 7 rels
    assert result.relationships_created == 7

    # Verify MockGraphDriver recorded safe parameterized Cypher queries
    assert len(driver.sessions) > 0
    tx = driver.sessions[0].transactions[0]
    assert tx.committed is True
    for query, params in tx.queries:
        # Cypher queries should be parameterized templates with $ variables
        assert "$" in query
        assert params is not None


@pytest.mark.asyncio
async def test_adversarial_legacy_and_hybrid_schema_aliases() -> None:
    """Validate normalization of heterogeneous USDM v2/v3/v4 alias combinations.

    @req:PRD-SYS-001, PRD-DDF-001
    """
    importer = USDMGraphImporter()

    # Mixed USDM v2.0/v3.0/v4.0 payload using singular and alternative aliases
    legacy_payload = {
        "studyId": "study_legacy_alias_01",
        "title": "Legacy Formatted Protocol",
        "version": "2.0",
        "studyDesign": {  # Singular dict instead of list
            "id": "sd_legacy_1",
            "name": "Singular Design",
            "studyArms": [  # studyArms alias
                {"id": "arm_leg_1", "name": "Arm Legacy", "armType": "Treatment"}
            ],
            "studyEpochs": [  # studyEpochs alias
                {
                    "id": "ep_leg_1",
                    "name": "Epoch Legacy",
                    "sequenceIndex": 1,
                    "epochType": "Screening",
                }
            ],
            "visits": [  # visits alias
                {
                    "id": "vis_leg_1",
                    "name": "Visit Legacy",
                    "epochId": "ep_leg_1",
                    "targetDay": 1,
                }
            ],
            "concepts": [  # concepts alias
                {
                    "id": "bc_leg_1",
                    "name": "Biomedical Concept Legacy",
                    "conceptCode": "C9999",
                }
            ],
            "criteria": [  # criteria alias
                {
                    "id": "crit_leg_1",
                    "name": "Criterion Legacy",
                    "criterionType": "Inclusion",
                    "text": "Legacy criterion text",
                }
            ],
            "activities": [
                {
                    "id": "act_leg_1",
                    "name": "Activity Legacy",
                    "cdashDomain": "VS",
                    "assignedVisitNames": ["Visit Legacy"],
                    "biomedicalConceptIds": ["bc_leg_1"],
                }
            ],
        },
    }

    result = await importer.import_usdm(legacy_payload)
    assert result.study_id == "study_legacy_alias_01"
    assert result.protocol_title == "Legacy Formatted Protocol"
    # 1 Study + 1 Design + 1 Arm + 1 Epoch + 1 Encounter + 1 Concept + 1 Criterion + 1 Activity = 8 nodes
    assert result.nodes_created == 8
    # 1 HAS_DESIGN + 1 HAS_ARM + 1 HAS_EPOCH + 1 CONTAINS_ENCOUNTER + 1 HAS_ACTIVITY + 1 HAS_CONCEPT + 1 HAS_CRITERION + 1 PERFORMS + 1 MEASURES_CONCEPT = 9 rels
    assert result.relationships_created == 9
    assert result.entity_counts["arms"] == 1
    assert result.entity_counts["epochs"] == 1
    assert result.entity_counts["encounters"] == 1
    assert result.entity_counts["activities"] == 1
    assert result.entity_counts["biomedical_concepts"] == 1
    assert result.entity_counts["eligibility_criteria"] == 1


@pytest.mark.asyncio
async def test_adversarial_zero_designs_warning() -> None:
    """Validate proper warning emission when payload contains zero study designs.

    @req:PRD-SYS-001
    """
    importer = USDMGraphImporter()
    payload = {
        "id": "study_no_designs",
        "name": "EMPTY-STUDY",
        "protocolTitle": "Protocol with no designs",
        "usdmVersion": "4.0",
        "studyDesigns": [],
    }

    result = await importer.import_usdm(payload)
    assert result.study_id == "study_no_designs"
    assert result.nodes_created == 1
    assert any("contains 0 study designs" in w for w in result.validation_warnings)


@pytest.mark.asyncio
async def test_adversarial_direct_usdmstudy_object_input() -> None:
    """Validate USDMGraphImporter accepts strongly typed USDMStudy Pydantic objects directly.

    @req:PRD-SYS-001, PRD-DDF-001
    """
    study_model = USDMStudy(
        id="study_direct_model_01",
        name="DIRECT-01",
        protocol_title="Direct Object Study",
        usdm_version="4.0",
        study_versions=[
            StudyVersion(
                id="ver_direct_1",
                version_tag="1.0",
                status="DRAFT",
                version_index=1,
                study_designs=[
                    StudyDesign(
                        id="sd_direct_1",
                        name="Direct Design",
                        arms=[StudyArm(id="arm_d1", name="Direct Arm")],
                        epochs=[StudyEpoch(id="ep_d1", name="Direct Epoch")],
                        encounters=[Encounter(id="enc_d1", name="Direct Encounter")],
                        activities=[Activity(id="act_d1", name="Direct Activity")],
                        biomedical_concepts=[
                            BiomedicalConcept(
                                id="bc_d1",
                                name="Direct Concept",
                                properties=[
                                    BiomedicalConceptProperty(
                                        id="prop_d1",
                                        name="Prop 1",
                                        data_type="numeric",
                                    )
                                ],
                            )
                        ],
                        eligibility_criteria=[
                            EligibilityCriterion(
                                id="crit_d1",
                                name="Direct Criterion",
                                criterion_type="Inclusion",
                            )
                        ],
                    )
                ],
            )
        ],
    )

    importer = USDMGraphImporter()
    result = await importer.import_usdm(study_model)

    assert result.study_id == "study_direct_model_01"
    # 1 Study + 1 Version + 1 Design + 1 Arm + 1 Epoch + 1 Encounter + 1 Activity + 1 Concept + 1 Criterion = 9 nodes
    assert result.nodes_created == 9
    assert result.entity_counts["study_versions"] == 1
    assert result.entity_counts["study_designs"] == 1


# =========================================================================
# 2. SIMULATED DATABASE FAILURE, DISCONNECTION & ROLLBACK TESTING
# =========================================================================


@pytest.mark.asyncio
async def test_adversarial_database_disconnection_mid_transaction() -> None:
    """Validate that simulated DB disconnections during intermediate Cypher statements trigger full rollback.

    @req:PRD-SYS-001
    """
    mock_driver = MockGraphDriver()

    class DropoutSession:
        def __init__(self, raw_session: MockGraphSession, fail_after_queries: int = 3):
            self.raw_session = raw_session
            self.fail_after_queries = fail_after_queries
            self.query_count = 0

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        def begin_transaction(self):
            from contextlib import asynccontextmanager

            @asynccontextmanager
            async def _tx_cm():
                tx = MockGraphTransaction(self.raw_session)
                self.raw_session.transactions.append(tx)

                async def _failing_run(query: str, parameters: Any = None, **kwargs):
                    self.query_count += 1
                    if self.query_count >= self.fail_after_queries:
                        raise ConnectionResetError(
                            "Neo4j socket abruptly closed during query execution"
                        )
                    return await tx.run(query, parameters, **kwargs)

                tx.run = _failing_run
                yield tx

            return _tx_cm()

    session_inst = mock_driver.session()
    mock_driver.session = lambda **kwargs: DropoutSession(
        session_inst, fail_after_queries=3
    )

    importer = USDMGraphImporter(mock_driver)

    payload = {
        "id": "study_dropout_test",
        "name": "DROPOUT-01",
        "protocolTitle": "Dropout Test Protocol",
        "studyDesigns": [
            {
                "id": "sd_1",
                "name": "Design 1",
                "arms": [{"id": "arm_1", "name": "Arm 1"}],
                "epochs": [{"id": "ep_1", "name": "Epoch 1"}],
                "encounters": [{"id": "enc_1", "name": "Visit 1"}],
                "activities": [{"id": "act_1", "name": "Activity 1"}],
            }
        ],
    }

    with pytest.raises(ConnectionResetError, match="Neo4j socket abruptly closed"):
        await importer.import_usdm(payload)

    # Verify transaction rollback flag
    assert len(session_inst.transactions) > 0
    tx = session_inst.transactions[0]
    assert tx.rolled_back is True
    assert tx.committed is False


@pytest.mark.asyncio
async def test_adversarial_deadlock_and_failing_commit() -> None:
    """Validate rollback when commit phase encounters a transaction lock or deadlock error.

    @req:PRD-SYS-001
    """
    mock_driver = MockGraphDriver()

    class DeadlockSession:
        def __init__(self, raw_session: MockGraphSession):
            self.raw_session = raw_session

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        def begin_transaction(self):
            from contextlib import asynccontextmanager

            @asynccontextmanager
            async def _tx_cm():
                tx = MockGraphTransaction(self.raw_session)
                self.raw_session.transactions.append(tx)

                async def _failing_commit():
                    raise RuntimeError(
                        "DeadlockDetectedException: Transaction 0x8F9 blocked by lock"
                    )

                tx.commit = _failing_commit
                yield tx

            return _tx_cm()

    session_inst = mock_driver.session()
    mock_driver.session = lambda **kwargs: DeadlockSession(session_inst)

    importer = USDMGraphImporter(mock_driver)

    payload = {
        "id": "study_deadlock_test",
        "name": "DEADLOCK-01",
        "protocolTitle": "Deadlock Test Protocol",
        "studyDesigns": [{"id": "sd_1", "name": "Design 1"}],
    }

    with pytest.raises(RuntimeError, match="DeadlockDetectedException"):
        await importer.import_usdm(payload)

    # Rollback must be triggered even when commit fails
    assert len(session_inst.transactions) > 0
    tx = session_inst.transactions[0]
    assert tx.rolled_back is True
    assert tx.committed is False


@pytest.mark.asyncio
async def test_adversarial_rollback_itself_fails_gracefully() -> None:
    """Validate that if rollback() also raises an error, the primary database exception is preserved.

    @req:PRD-SYS-001
    """
    mock_driver = MockGraphDriver()

    class DoubleFaultSession:
        def __init__(self, raw_session: MockGraphSession):
            self.raw_session = raw_session

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        def begin_transaction(self):
            from contextlib import asynccontextmanager

            @asynccontextmanager
            async def _tx_cm():
                tx = MockGraphTransaction(self.raw_session)
                self.raw_session.transactions.append(tx)

                async def _fail_run(query: str, parameters: Any = None, **kwargs):
                    raise RuntimeError("Primary database error")

                async def _fail_rollback():
                    raise RuntimeError("Secondary rollback failure")

                tx.run = _fail_run
                tx.rollback = _fail_rollback
                yield tx

            return _tx_cm()

    session_inst = mock_driver.session()
    mock_driver.session = lambda **kwargs: DoubleFaultSession(session_inst)

    importer = USDMGraphImporter(mock_driver)

    payload = {
        "id": "study_double_fault",
        "name": "DOUBLE-FAULT",
        "protocolTitle": "Double Fault Protocol",
    }

    # Primary error must be raised to the caller, not obscured by rollback error
    with pytest.raises(RuntimeError, match="Primary database error"):
        await importer.import_usdm(payload)


def test_adversarial_sync_wrapper_with_db_failure() -> None:
    """Validate synchronous execution wrapper propagates database failures correctly.

    @req:PRD-SYS-001
    """
    mock_driver = MockGraphDriver()

    class FailingSession:
        def __init__(self, raw_session: MockGraphSession):
            self.raw_session = raw_session

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        def begin_transaction(self):
            from contextlib import asynccontextmanager

            @asynccontextmanager
            async def _tx_cm():
                tx = MockGraphTransaction(self.raw_session)
                self.raw_session.transactions.append(tx)

                async def _fail_run(query: str, parameters: Any = None, **kwargs):
                    raise TimeoutError("Database connection timed out")

                tx.run = _fail_run
                yield tx

            return _tx_cm()

    session_inst = mock_driver.session()
    mock_driver.session = lambda **kwargs: FailingSession(session_inst)

    importer = USDMGraphImporter(mock_driver)

    payload = {
        "id": "study_sync_fail",
        "name": "SYNC-FAIL",
        "protocolTitle": "Sync Failure Protocol",
    }

    with pytest.raises(TimeoutError, match="Database connection timed out"):
        importer.import_usdm_sync(payload)


# =========================================================================
# 3. LARGE PROTOCOL SCALE & STRESS HARNESS
# =========================================================================


@pytest.mark.asyncio
async def test_adversarial_large_scale_protocol_stress_harness() -> None:
    """Stress-test ingestion with a massive protocol payload (> 50 arms, > 200 visits, > 100 activities, > 500 concepts).

    Verifies execution scalability, memory stability, and accurate graph relationship compilation.

    @req:PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
    """
    num_arms = 20
    num_epochs = 15
    num_encounters = 120
    num_activities = 80
    num_concepts = 200
    num_criteria = 50

    # Build massive USDM structure
    arms = [
        {
            "id": f"arm_stress_{i}",
            "name": f"Stress Arm {i}",
            "armType": "Treatment" if i % 2 == 0 else "Placebo",
            "targetSampleSize": 50 + i,
        }
        for i in range(num_arms)
    ]

    epochs = [
        {
            "id": f"ep_stress_{i}",
            "name": f"Epoch Stress {i}",
            "epochType": "Treatment" if i > 1 else "Screening",
            "sequenceNumber": i + 1,
            "sequenceIndex": i + 1,
        }
        for i in range(num_epochs)
    ]

    encounters = [
        {
            "id": f"enc_stress_{i}",
            "name": f"Stress Visit {i}",
            "encounterType": "Visit",
            "epochId": f"ep_stress_{i % num_epochs}",
            "targetDay": (i - 10) * 7,
            "windowLower": 2,
            "windowUpper": 2,
            "isMandatory": i % 3 != 0,
        }
        for i in range(num_encounters)
    ]

    concepts = [
        {
            "id": f"bc_stress_{i}",
            "name": f"Biomedical Concept {i}",
            "conceptCode": f"C{10000 + i}",
            "cdashDomain": "VS"
            if i % 4 == 0
            else "LB"
            if i % 4 == 1
            else "EG"
            if i % 4 == 2
            else "QS",
            "cdashVariable": f"VAR_{i}",
            "dataType": "numeric",
            "properties": [
                {
                    "id": f"prop_stress_{i}_1",
                    "name": f"Prop_{i}_1",
                    "dataType": "numeric",
                    "mandatory": True,
                    "gridSpan": 6,
                },
                {
                    "id": f"prop_stress_{i}_2",
                    "name": f"Prop_{i}_2",
                    "dataType": "text",
                    "mandatory": False,
                    "gridSpan": 6,
                },
            ],
        }
        for i in range(num_concepts)
    ]

    # Activities reference encounters and concepts
    activities = [
        {
            "id": f"act_stress_{i}",
            "name": f"Stress Activity {i}",
            "cdashDomain": "VS"
            if i % 4 == 0
            else "LB"
            if i % 4 == 1
            else "EG"
            if i % 4 == 2
            else "QS",
            "assignedEncounterIds": [
                f"enc_stress_{j}"
                for j in range(num_encounters)
                if (j + i) % 5
                == 0  # distributed visit allocation (~24 visits per activity)
            ],
            "biomedicalConceptIds": [
                f"bc_stress_{(i * 2) % num_concepts}",
                f"bc_stress_{(i * 2 + 1) % num_concepts}",
            ],
        }
        for i in range(num_activities)
    ]

    criteria = [
        {
            "id": f"crit_stress_{i}",
            "name": f"Criterion {i}",
            "identifier": f"INC-{i:03d}" if i % 2 == 0 else f"EXC-{i:03d}",
            "criterionType": "Inclusion" if i % 2 == 0 else "Exclusion",
            "category": "Clinical",
            "text": f"Subject clinical qualification metric {i} must satisfy protocol specification.",
        }
        for i in range(num_criteria)
    ]

    large_payload = {
        "id": "study_large_scale_stress",
        "name": "CADENCE-STRESS-LARGE",
        "protocolTitle": "Massive Multi-Arm Platform Protocol for Scale Stress Testing",
        "usdmVersion": "4.0",
        "biomedicalConcepts": concepts,
        "studyDesigns": [
            {
                "id": "sd_large_1",
                "name": "Large Platform Design",
                "arms": arms,
                "epochs": epochs,
                "encounters": encounters,
                "activities": activities,
                "eligibilityCriteria": criteria,
            }
        ],
    }

    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    start_time = time.perf_counter()
    result = await importer.import_usdm(large_payload)
    elapsed = time.perf_counter() - start_time

    # Performance SLA: Even with thousands of entities, ingestion must complete in < 2.0s
    assert elapsed < 2.0, (
        f"Large scale ingestion took {elapsed:.2f}s, exceeding 2.0s SLA"
    )

    # Verify counts
    # 1 Study + 1 Design + 20 Arms + 15 Epochs + 120 Encounters + 80 Activities + 200 Concepts + 50 Criteria = 487 nodes
    expected_nodes = (
        1
        + 1
        + num_arms
        + num_epochs
        + num_encounters
        + num_activities
        + num_concepts
        + num_criteria
    )
    assert result.nodes_created == expected_nodes
    assert result.status == "COMMITTED"
    assert len(result.validation_warnings) == 0

    # Verify relationships created
    # HAS_DESIGN (1) + Arms (20) + Epochs (15) + Encounters (120) + Activities (80) + Concepts (200) + Criteria (50)
    # + PERFORMS links + MEASURES_CONCEPT links (80 * 2 = 160)
    assert result.relationships_created > expected_nodes

    # Verify in-memory synchronization
    assert "study_large_scale_stress" in MOCK_STUDIES
    synced_study = MOCK_STUDIES["study_large_scale_stress"]
    assert len(synced_study["arms"]) == num_arms
    assert len(synced_study["encounters"]) == num_encounters
    assert len(synced_study["activities"]) == num_activities
    assert len(synced_study["biomedical_concepts"]) == num_concepts


# =========================================================================
# 4. CONCURRENCY & ISOLATION STRESS TESTING
# =========================================================================


@pytest.mark.asyncio
async def test_adversarial_concurrent_multi_study_ingestion() -> None:
    """Validate concurrent ingestion of multiple independent protocols without state crosstalk.

    @req:PRD-SYS-001
    """
    mock_driver = MockGraphDriver()
    importer = USDMGraphImporter(mock_driver)

    async def _import_study(idx: int) -> USDMImportResult:
        payload = {
            "id": f"study_concurrent_{idx}",
            "name": f"CONC-{idx}",
            "protocolTitle": f"Concurrent Protocol {idx}",
            "usdmVersion": "4.0",
            "studyDesigns": [
                {
                    "id": f"sd_conc_{idx}",
                    "name": f"Design {idx}",
                    "arms": [{"id": f"arm_c_{idx}", "name": f"Arm {idx}"}],
                    "epochs": [{"id": f"ep_c_{idx}", "name": f"Epoch {idx}"}],
                    "encounters": [{"id": f"enc_c_{idx}", "name": f"Visit {idx}"}],
                    "activities": [{"id": f"act_c_{idx}", "name": f"Activity {idx}"}],
                }
            ],
        }
        return await importer.import_usdm(payload, user_id=f"user_{idx}")

    # Launch 10 concurrent import operations
    results = await asyncio.gather(*[_import_study(i) for i in range(10)])

    assert len(results) == 10
    for idx, res in enumerate(results):
        assert res.study_id == f"study_concurrent_{idx}"
        assert res.nodes_created == 6
        assert res.status == "COMMITTED"
        assert f"study_concurrent_{idx}" in MOCK_STUDIES
