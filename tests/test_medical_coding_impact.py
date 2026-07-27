import httpx
import pytest
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    Base,
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalSubject,
    CodingState,
    MedDRAHierarchy,
    MedDRATerm,
    RecodingState,
    WHODrugATC,
    WHODrugDrugATC,
    WHODrugDrugIngredient,
    WHODrugIngredient,
    WHODrugRecord,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from tests.test_medical_coding_lifecycle import get_auth_headers


@pytest.fixture(autouse=True)
async def setup_test_db():
    TrialLockManager.reset()
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    await db_manager.close()


async def seed_test_dictionaries():
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Old MedDRA (25.0)
            session.add(
                MedDRATerm(
                    dictionary_version="25.0",
                    code="M1",
                    term_name="Headache",
                    level="LLT",
                )
            )
            session.add(
                MedDRATerm(
                    dictionary_version="25.0",
                    code="M2",
                    term_name="Nausea",
                    level="LLT",
                )
            )
            session.add(
                MedDRATerm(
                    dictionary_version="25.0",
                    code="M3",
                    term_name="Fatigue",
                    level="LLT",
                )
            )

            session.add(
                MedDRAHierarchy(
                    dictionary_version="25.0",
                    llt_code="M1",
                    pt_code="M1",
                    hlt_code="H1",
                    hlgt_code="HG1",
                    soc_code="S1",
                    primary_soc_flag="Y",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="25.0",
                    llt_code="M2",
                    pt_code="M2",
                    hlt_code="H2",
                    hlgt_code="HG2",
                    soc_code="S2",
                    primary_soc_flag="Y",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="25.0",
                    llt_code="M3",
                    pt_code="M3",
                    hlt_code="H3",
                    hlgt_code="HG3",
                    soc_code="S3",
                    primary_soc_flag="Y",
                )
            )

            # New MedDRA (26.0)
            # M1 is unchanged (same code and same hierarchy)
            session.add(
                MedDRATerm(
                    dictionary_version="26.0",
                    code="M1",
                    term_name="Headache",
                    level="LLT",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="26.0",
                    llt_code="M1",
                    pt_code="M1",
                    hlt_code="H1",
                    hlgt_code="HG1",
                    soc_code="S1",
                    primary_soc_flag="Y",
                )
            )

            # M2 is reclassified (different hlt_code)
            session.add(
                MedDRATerm(
                    dictionary_version="26.0",
                    code="M2",
                    term_name="Nausea",
                    level="LLT",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="26.0",
                    llt_code="M2",
                    pt_code="M2",
                    hlt_code="H2_NEW",
                    hlgt_code="HG2",
                    soc_code="S2",
                    primary_soc_flag="Y",
                )
            )

            # M3 is deprecated (does not exist in 26.0)

            # Old WHODrug (2023-03)
            session.add(
                WHODrugRecord(
                    dictionary_version="2023-03",
                    drug_code="W1",
                    preferred_name="ASPIRIN",
                )
            )
            session.add(
                WHODrugATC(
                    dictionary_version="2023-03", atc_code="A1", description="Analgesic"
                )
            )
            session.add(
                WHODrugDrugATC(
                    dictionary_version="2023-03", drug_code="W1", atc_code="A1"
                )
            )
            session.add(
                WHODrugIngredient(
                    dictionary_version="2023-03",
                    ingredient_code="I1",
                    ingredient_name="Aspirin Active",
                )
            )
            session.add(
                WHODrugDrugIngredient(
                    dictionary_version="2023-03", drug_code="W1", ingredient_code="I1"
                )
            )

            session.add(
                WHODrugRecord(
                    dictionary_version="2023-03",
                    drug_code="W2",
                    preferred_name="IBUPROFEN",
                )
            )
            session.add(
                WHODrugATC(
                    dictionary_version="2023-03", atc_code="A2", description="NSAID"
                )
            )
            session.add(
                WHODrugDrugATC(
                    dictionary_version="2023-03", drug_code="W2", atc_code="A2"
                )
            )

            session.add(
                WHODrugRecord(
                    dictionary_version="2023-03",
                    drug_code="W3",
                    preferred_name="PARACETAMOL",
                )
            )

            # New WHODrug (2024-03)
            # W1 is unchanged
            session.add(
                WHODrugRecord(
                    dictionary_version="2024-03",
                    drug_code="W1",
                    preferred_name="ASPIRIN",
                )
            )
            session.add(
                WHODrugATC(
                    dictionary_version="2024-03", atc_code="A1", description="Analgesic"
                )
            )
            session.add(
                WHODrugDrugATC(
                    dictionary_version="2024-03", drug_code="W1", atc_code="A1"
                )
            )
            session.add(
                WHODrugIngredient(
                    dictionary_version="2024-03",
                    ingredient_code="I1",
                    ingredient_name="Aspirin Active",
                )
            )
            session.add(
                WHODrugDrugIngredient(
                    dictionary_version="2024-03", drug_code="W1", ingredient_code="I1"
                )
            )

            # W2 is reclassified (different ATC code)
            session.add(
                WHODrugRecord(
                    dictionary_version="2024-03",
                    drug_code="W2",
                    preferred_name="IBUPROFEN",
                )
            )
            session.add(
                WHODrugATC(
                    dictionary_version="2024-03",
                    atc_code="A2_NEW",
                    description="NSAID New",
                )
            )
            session.add(
                WHODrugDrugATC(
                    dictionary_version="2024-03", drug_code="W2", atc_code="A2_NEW"
                )
            )

            # W3 is deprecated (does not exist in 2024-03)

            # Seed clinical subject & existing assignments
            session.add(
                ClinicalSubject(
                    id="SUBJ-UUID-1", subject_id="SUBJ-001", study_id="STUDY-001"
                )
            )

            # MedDRA Assignments (25.0)
            session.add(
                ClinicalCodingAssignment(
                    id="A-M1",
                    verbatim_text="Headache",
                    source_field="AE.AETERM",
                    dictionary_type="MEDDRA",
                    dictionary_version="25.0",
                    coded_code="M1",
                    coded_term="Headache",
                    status=CodingState.CODED,
                    hierarchy={
                        "hierarchies": [
                            {
                                "llt_code": "M1",
                                "pt_code": "M1",
                                "hlt_code": "H1",
                                "hlgt_code": "HG1",
                                "soc_code": "S1",
                                "primary_soc_flag": "Y",
                            }
                        ]
                    },
                )
            )
            session.add(
                ClinicalCodingAssignment(
                    id="A-M2",
                    verbatim_text="Nausea",
                    source_field="AE.AETERM",
                    dictionary_type="MEDDRA",
                    dictionary_version="25.0",
                    coded_code="M2",
                    coded_term="Nausea",
                    status=CodingState.CODED,
                    hierarchy={
                        "hierarchies": [
                            {
                                "llt_code": "M2",
                                "pt_code": "M2",
                                "hlt_code": "H2",
                                "hlgt_code": "HG2",
                                "soc_code": "S2",
                                "primary_soc_flag": "Y",
                            }
                        ]
                    },
                )
            )
            session.add(
                ClinicalCodingAssignment(
                    id="A-M3",
                    verbatim_text="Fatigue",
                    source_field="AE.AETERM",
                    dictionary_type="MEDDRA",
                    dictionary_version="25.0",
                    coded_code="M3",
                    coded_term="Fatigue",
                    status=CodingState.CODED,
                    hierarchy={
                        "hierarchies": [
                            {
                                "llt_code": "M3",
                                "pt_code": "M3",
                                "hlt_code": "H3",
                                "hlgt_code": "HG3",
                                "soc_code": "S3",
                                "primary_soc_flag": "Y",
                            }
                        ]
                    },
                )
            )

            # WHODrug Assignments (2023-03)
            session.add(
                ClinicalCodingAssignment(
                    id="A-W1",
                    verbatim_text="Aspirin",
                    source_field="CM.CMTRT",
                    dictionary_type="WHODRUG",
                    dictionary_version="2023-03",
                    coded_code="W1",
                    coded_term="ASPIRIN",
                    status=CodingState.CODED,
                    hierarchy={
                        "atc_context": [{"atc_code": "A1", "description": "Analgesic"}],
                        "ingredients": [
                            {
                                "ingredient_code": "I1",
                                "ingredient_name": "Aspirin Active",
                            }
                        ],
                    },
                )
            )
            session.add(
                ClinicalCodingAssignment(
                    id="A-W2",
                    verbatim_text="Ibuprofen",
                    source_field="CM.CMTRT",
                    dictionary_type="WHODRUG",
                    dictionary_version="2023-03",
                    coded_code="W2",
                    coded_term="IBUPROFEN",
                    status=CodingState.CODED,
                    hierarchy={
                        "atc_context": [{"atc_code": "A2", "description": "NSAID"}],
                        "ingredients": [],
                    },
                )
            )
            session.add(
                ClinicalCodingAssignment(
                    id="A-W3",
                    verbatim_text="Paracetamol",
                    source_field="CM.CMTRT",
                    dictionary_type="WHODRUG",
                    dictionary_version="2023-03",
                    coded_code="W3",
                    coded_term="PARACETAMOL",
                    status=CodingState.CODED,
                    hierarchy={"atc_context": [], "ingredients": []},
                )
            )


@pytest.mark.asyncio
async def test_impact_analysis_meddra_and_whodrug_lifecycle():
    await seed_test_dictionaries()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Run MedDRA Impact Analysis (Manual execution via REST API)
        resp_meddra = await client.post(
            "/api/v1/execution/coding/impact-analysis",
            json={"dictionary_type": "MEDDRA", "new_version": "26.0"},
            headers=get_auth_headers(),
        )
        assert resp_meddra.status_code == 200
        data_m = resp_meddra.json()
        assert data_m["status"] == "success"
        metrics_m = data_m["metrics"]
        assert metrics_m["unchanged"] == 1  # Headache (M1)
        assert metrics_m["reclassified"] == 1  # Nausea (M2)
        assert metrics_m["deprecated"] == 1  # Fatigue (M3)
        assert metrics_m["skipped"] == 0

        # Verify MedDRA assignment updates
        async with db_manager.get_session_maker()() as session:
            # M1 unchanged: promoted automatically
            stmt = select(ClinicalCodingAssignment).where(
                ClinicalCodingAssignment.id == "A-M1"
            )
            res = await session.execute(stmt)
            a_m1 = res.scalar_one()
            assert a_m1.status == CodingState.CODED
            assert a_m1.dictionary_version == "26.0"

            # M2 reclassified: changed to RECODING_REQUIRED, recoding_status=PENDING
            # Also preserve enrollment-time coding fields (historical values remain queryable)
            stmt = select(ClinicalCodingAssignment).where(
                ClinicalCodingAssignment.id == "A-M2"
            )
            res = await session.execute(stmt)
            a_m2 = res.scalar_one()
            assert a_m2.status == CodingState.RECODING_REQUIRED
            assert a_m2.recoding_status == RecodingState.PENDING
            assert a_m2.coded_code == "M2"
            assert (
                a_m2.dictionary_version == "25.0"
            )  # Enrollment-time version preserved

            # M3 deprecated: changed to RECODING_REQUIRED, recoding_status=PENDING
            # Preserve enrollment-time coding fields intact
            stmt = select(ClinicalCodingAssignment).where(
                ClinicalCodingAssignment.id == "A-M3"
            )
            res = await session.execute(stmt)
            a_m3 = res.scalar_one()
            assert a_m3.status == CodingState.RECODING_REQUIRED
            assert a_m3.recoding_status == RecodingState.PENDING
            assert a_m3.coded_code == "M3"
            assert a_m3.dictionary_version == "25.0"

            # Check ledger entries exist correctly
            stmt_ledger = select(ClinicalCodingLedger).where(
                ClinicalCodingLedger.new_dictionary_version == "26.0"
            )
            res_ledger = await session.execute(stmt_ledger)
            ledgers = list(res_ledger.scalars().all())
            assert len(ledgers) == 3

            # Verify ledger records historical and current coding meanings
            m1_ledger = next(x for x in ledgers if x.assignment_id == "A-M1")
            assert m1_ledger.old_dictionary_version == "25.0"
            assert m1_ledger.new_dictionary_version == "26.0"
            assert m1_ledger.old_coded_code == "M1"
            assert m1_ledger.new_coded_code == "M1"
            assert m1_ledger.recoding_status == RecodingState.NONE

            m2_ledger = next(x for x in ledgers if x.assignment_id == "A-M2")
            assert m2_ledger.old_dictionary_version == "25.0"
            assert m2_ledger.new_dictionary_version == "26.0"
            assert m2_ledger.old_coded_code == "M2"
            assert m2_ledger.new_coded_code == "M2"
            assert m2_ledger.recoding_status == RecodingState.PENDING
            assert (
                m2_ledger.old_hierarchy["hierarchies"][0]["hlt_code"] == "H1"
                or m2_ledger.old_hierarchy["hierarchies"][0]["hlt_code"] == "H2"
            )
            assert m2_ledger.new_hierarchy["hierarchies"][0]["hlt_code"] == "H2_NEW"

            m3_ledger = next(x for x in ledgers if x.assignment_id == "A-M3")
            assert m3_ledger.old_dictionary_version == "25.0"
            assert m3_ledger.new_dictionary_version == "26.0"
            assert m3_ledger.old_coded_code == "M3"
            assert m3_ledger.new_coded_code == "M3"
            assert m3_ledger.recoding_status == RecodingState.PENDING
            assert m3_ledger.new_hierarchy == {}

        # 2. Run WHODrug Impact Analysis
        resp_whodrug = await client.post(
            "/api/v1/execution/coding/impact-analysis",
            json={"dictionary_type": "WHODRUG", "new_version": "2024-03"},
            headers=get_auth_headers(),
        )
        assert resp_whodrug.status_code == 200
        data_w = resp_whodrug.json()
        assert data_w["status"] == "success"
        metrics_w = data_w["metrics"]
        assert metrics_w["unchanged"] == 1  # Aspirin (W1)
        assert metrics_w["reclassified"] == 1  # Ibuprofen (W2)
        assert metrics_w["deprecated"] == 1  # Paracetamol (W3)
        assert metrics_w["skipped"] == 0

        # Verify WHODrug assignment updates
        async with db_manager.get_session_maker()() as session:
            # W1 unchanged: promoted automatically
            stmt = select(ClinicalCodingAssignment).where(
                ClinicalCodingAssignment.id == "A-W1"
            )
            res = await session.execute(stmt)
            a_w1 = res.scalar_one()
            assert a_w1.status == CodingState.CODED
            assert a_w1.dictionary_version == "2024-03"

            # W2 reclassified
            stmt = select(ClinicalCodingAssignment).where(
                ClinicalCodingAssignment.id == "A-W2"
            )
            res = await session.execute(stmt)
            a_w2 = res.scalar_one()
            assert a_w2.status == CodingState.RECODING_REQUIRED
            assert a_w2.recoding_status == RecodingState.PENDING

            # W3 deprecated
            stmt = select(ClinicalCodingAssignment).where(
                ClinicalCodingAssignment.id == "A-W3"
            )
            res = await session.execute(stmt)
            a_w3 = res.scalar_one()
            assert a_w3.status == CodingState.RECODING_REQUIRED
            assert a_w3.recoding_status == RecodingState.PENDING

        # 3. Verify Idempotency: Re-running impact analysis does not duplicate ledger events.
        # Let's revert the status and version of assignments to CODED / 25.0
        # but keep the ledger entries to simulate an interrupted or re-run process
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                stmt = select(ClinicalCodingAssignment).where(
                    ClinicalCodingAssignment.id.in_(["A-M1", "A-M2", "A-M3"])
                )
                res = await session.execute(stmt)
                for a in res.scalars().all():
                    a.status = CodingState.CODED
                    a.dictionary_version = "25.0"
                    session.add(a)

        resp_meddra_rerun = await client.post(
            "/api/v1/execution/coding/impact-analysis",
            json={"dictionary_type": "MEDDRA", "new_version": "26.0"},
            headers=get_auth_headers(),
        )
        assert resp_meddra_rerun.status_code == 200
        data_m_rerun = resp_meddra_rerun.json()
        metrics_m_rerun = data_m_rerun["metrics"]
        assert metrics_m_rerun["unchanged"] == 0
        assert metrics_m_rerun["reclassified"] == 0
        assert metrics_m_rerun["deprecated"] == 0
        assert metrics_m_rerun["skipped"] == 3

        async with db_manager.get_session_maker()() as session:
            # Verify no new/duplicate ledger entries exist in the database
            stmt_ledger = select(ClinicalCodingLedger).where(
                ClinicalCodingLedger.new_dictionary_version == "26.0"
            )
            res_ledger = await session.execute(stmt_ledger)
            ledgers = list(res_ledger.scalars().all())
            assert len(ledgers) == 3
