import time
import uuid

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sae_icsr import MedDRACoding, SeriousAdverseEvent
from sqlalchemy import select, text

from apps.gateway.main import generate_signature
from apps.safety.adapter import SafetyDatabaseAdapter
from apps.safety.database import db_manager
from apps.safety.execution_client import ExecutionClient
from apps.safety.main import app
from apps.safety.models import (
    Base,
    SAEDiscrepancy,
    SAEReconciliationRun,
    SafetyAuditLog,
)
from apps.safety.reconciliation import (
    compare_sae_records,
    generate_stable_event_key,
    normalize_edc_ae_to_sae,
    normalize_external_icsr_to_saes,
)

# Ensure all tests in this module run on the same xdist worker.
# The db_manager singleton is reinitialised per-fixture; distributing tests
# across workers risks a concurrent module reinitialising the engine mid-test.
pytestmark = pytest.mark.xdist_group("sae_reconciliation")


@pytest_asyncio.fixture(autouse=True)
async def setup_reconciliation_db():
    """
    Setup in-memory Safety database for reconciliation tests.
    """
    db_uri = f"sqlite+aiosqlite:///file:memdb_sae_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
    db_manager.init_db(db_uri, echo=False)

    from sqlalchemy import event as sa_event

    @sa_event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_conn, record):
        cursor = dbapi_conn.cursor()
        try:
            cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        except Exception:
            pass
        finally:
            cursor.close()

    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def get_signed_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """Helper to generate gateway headers."""
    timestamp = str(time.time())
    user_id = "safety_recon_user"
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


# ==========================================
# 1. Pure Comparison Tests
# ==========================================


def test_pure_comparison_missing_on_either_side():
    # EDC has 1 event, Safety has 0
    edc_sae = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="HEADACHE",
        AESTDTC="2026-07-25",
        AESEV="MILD",
        AESER="N",
        AESEQ=1,
    )
    discrepancies = compare_sae_records([edc_sae], [], meddra_version="26.0")

    assert len(discrepancies) == 1
    d = discrepancies[0]
    assert d["source"] == "SAFETY"
    assert d["field_name"] == "event_presence"
    assert d["expected_value"] == "PRESENT"
    assert d["actual_value"] == "MISSING"
    assert d["case_event_key"] == "SUBJ-001:SEQ-1"
    assert d["meddra_version"] == "26.0"

    # EDC has 0 events, Safety has 1
    safety_sae = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="HEADACHE",
        AESTDTC="2026-07-25",
        AESEV="MILD",
        AESER="N",
        AESEQ=1,
    )
    discrepancies2 = compare_sae_records([], [safety_sae], meddra_version="26.0")

    assert len(discrepancies2) == 1
    d2 = discrepancies2[0]
    assert d2["source"] == "EDC"
    assert d2["field_name"] == "event_presence"
    assert d2["expected_value"] == "MISSING"
    assert d2["actual_value"] == "PRESENT"
    assert d2["case_event_key"] == "SUBJ-001:SEQ-1"


def test_pure_comparison_same_code_different_terms():
    coding_edc = MedDRACoding(
        llt_code="10019211",
        llt_name="Headache verbatim",
        pt_code="10019211",
        pt_name="Headache Preferred",
        hlt_code="10019231",
        hlt_name="Headaches NEC",
        hlgt_code="10029214",
        hlgt_name="Headache and facial pain",
        soc_code="10029205",
        soc_name="Nervous system disorders",
        primary_soc_flag="yes",
        score=1.0,
    )
    # Different free text name, same codes
    coding_safety = MedDRACoding(
        llt_code="10019211",
        llt_name="Cephalea",
        pt_code="10019211",
        pt_name="Headache Preferred",
        hlt_code="10019231",
        hlt_name="Headaches NEC",
        hlgt_code="10029214",
        hlgt_name="Headache and facial pain",
        soc_code="10029205",
        soc_name="Nervous system disorders",
        primary_soc_flag="yes",
        score=1.0,
    )

    sae_edc = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="Severe Headache",
        AESTDTC="2026-07-25",
        AESEV="SEVERE",
        AESER="Y",
        AESEQ=1,
        meddra_coding=coding_edc,
    )
    sae_safety = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="Cephalea",
        AESTDTC="2026-07-25",
        AESEV="SEVERE",
        AESER="Y",
        AESEQ=1,
        meddra_coding=coding_safety,
    )

    discrepancies = compare_sae_records([sae_edc], [sae_safety], meddra_version="26.0")
    # There should be NO discrepancy because codes/hierarchy are identical
    assert len(discrepancies) == 0


def test_pure_comparison_differing_fields():
    sae_edc = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="HEADACHE",
        AESTDTC="2026-07-25",
        AEENDTC="2026-07-26",
        AESEV="MODERATE",
        AESER="Y",
        AEREL="RELATED",
        AEOUT="RECOVERED",
        AESEQ=1,
    )
    sae_safety = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="HEADACHE",
        AESTDTC="2026-07-26",  # Mismatch stdtc
        AEENDTC="2026-07-27",  # Mismatch enddtc
        AESEV="SEVERE",  # Mismatch severity
        AESER="N",  # Mismatch seriousness
        AEREL="NOT RELATED",  # Mismatch relatedness
        AEOUT="NOT RECOVERED",  # Mismatch outcome
        AESEQ=1,
    )

    discrepancies = compare_sae_records([sae_edc], [sae_safety], meddra_version="26.0")

    # Check that we detect mismatches across fields
    fields = [d["field_name"] for d in discrepancies]
    assert "AESTDTC" in fields
    assert "AEENDTC" in fields
    assert "AESEV" in fields
    assert "AESER" in fields
    assert "AEREL" in fields
    assert "AEOUT" in fields


def test_deterministic_output_sorting():
    # Unordered list of SeriousAdverseEvent objects
    sae1 = SeriousAdverseEvent(
        subject_key="SUBJ-002",
        AETERM="HEADACHE",
        AESTDTC="2026-07-25",
        AESEV="MILD",
        AESER="N",
        AESEQ=1,
    )
    sae2 = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="FEVER",
        AESTDTC="2026-07-25",
        AESEV="MILD",
        AESER="N",
        AESEQ=1,
    )

    discrepancies = compare_sae_records([sae1, sae2], [], meddra_version="26.0")
    # Sorting should place SUBJ-001 before SUBJ-002
    keys = [d["case_event_key"] for d in discrepancies]
    assert keys == ["SUBJ-001:SEQ-1", "SUBJ-002:SEQ-1"]


# ==========================================
# 2. Client & Adapter Integration Tests
# ==========================================


@pytest.mark.asyncio
async def test_execution_client_and_adapter_methods():
    class MockAsyncClient:
        async def get(self, url, headers=None, params=None, timeout=10.0):
            # 1. Dataset-JSON AE Contract mock
            if "sdtm/AE" in url:
                mock_json = {
                    "clinicalData": {
                        "studyOID": "STUDY-001",
                        "metaDataVersionOID": "MDV.001",
                        "itemGroupData": {
                            "IG.AE": {
                                "records": 1,
                                "name": "AE",
                                "label": "Adverse Events",
                                "items": [
                                    {
                                        "name": "STUDYID",
                                        "label": "Study ID",
                                        "type": "string",
                                    },
                                    {
                                        "name": "USUBJID",
                                        "label": "Subject ID",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AETERM",
                                        "label": "AE Term",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AESTDTC",
                                        "label": "Start Date",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AESEV",
                                        "label": "Severity",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AESER",
                                        "label": "Serious",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AESEQ",
                                        "label": "Sequence",
                                        "type": "integer",
                                    },
                                ],
                                "itemData": [
                                    [
                                        "STUDY-001",
                                        "SUBJ-001",
                                        "SEVERE HEADACHE",
                                        "2026-07-25",
                                        "SEVERE",
                                        "Y",
                                        1,
                                    ]
                                ],
                            }
                        },
                    }
                }
                return httpx.Response(status_code=200, json=mock_json)

            # 2. MedDRA code resolution mock
            if "meddra/code" in url:
                mock_res = {
                    "status": "AUTO-CODED",
                    "matches": [
                        {
                            "llt_code": "10019211",
                            "llt_name": "Severe Headache",
                            "pt_code": "10019211",
                            "pt_name": "Headache",
                            "hlt_code": "10019231",
                            "hlt_name": "Headaches NEC",
                            "hlgt_code": "10029214",
                            "hlgt_name": "Headache and facial pain",
                            "soc_code": "10029205",
                            "soc_name": "Nervous system disorders",
                            "primary_soc_flag": "Y",
                            "score": 1.0,
                        }
                    ],
                }
                return httpx.Response(status_code=200, json=mock_res)

            # 3. External safety-system cases mock
            if "cases-mock" in url:
                mock_cases = [
                    {
                        "header": {
                            "sender_organization": "SPONSOR_A",
                            "receiver_organization": "FDA",
                            "transmission_date": "2026-07-25T15:00:00Z",
                            "message_id": "MSG-001",
                        },
                        "report_identifiers": {
                            "worldwide_unique_case_id": "WW-CASE-001"
                        },
                        "patient": {"patient_id": "SUBJ-001", "sex": "F"},
                        "reactions": [
                            {
                                "reaction_term": "SEVERE HEADACHE",
                                "start_date": "2026-07-25",
                                "seriousness_hospitalization": "Y",  # Serious
                                "meddra_coding": {
                                    "llt_code": "10019211",
                                    "llt_name": "Severe Headache",
                                    "pt_code": "10019211",
                                    "pt_name": "Headache",
                                    "hlt_code": "10019231",
                                    "hlt_name": "Headaches NEC",
                                    "hlgt_code": "10029214",
                                    "hlgt_name": "Headache and facial pain",
                                    "soc_code": "10029205",
                                    "soc_name": "Nervous system disorders",
                                    "primary_soc_flag": "Y",
                                    "score": 1.0,
                                },
                            }
                        ],
                    }
                ]
                return httpx.Response(status_code=200, json=mock_cases)

            return httpx.Response(status_code=404)

    mock_client = MockAsyncClient()

    # 1. Test ExecutionClient
    exec_cli = ExecutionClient()
    res_ae = await exec_cli.fetch_ae_data("STUDY-001", client=mock_client)
    assert len(res_ae["AE"]) == 1
    assert res_ae["AE"][0]["AETERM"] == "SEVERE HEADACHE"

    res_meddra = await exec_cli.resolve_meddra_code(
        "SEVERE HEADACHE", client=mock_client
    )
    assert res_meddra["status"] == "AUTO-CODED"
    assert res_meddra["matches"][0]["llt_code"] == "10019211"

    # 2. Test SafetyDatabaseAdapter
    adapter = SafetyDatabaseAdapter(client=mock_client)
    cases = await adapter.fetch_cases()
    assert len(cases) == 1
    assert cases[0]["patient"]["patient_id"] == "SUBJ-001"


# ==========================================
# 3. Persistence + Audit E2E Integration
# ==========================================


@pytest.mark.asyncio
async def test_reconciliation_persistence_and_audit():
    class MockAsyncClient:
        async def get(self, url, headers=None, params=None, timeout=10.0):
            if "sdtm/AE" in url:
                mock_json = {
                    "clinicalData": {
                        "studyOID": "STUDY-001",
                        "metaDataVersionOID": "MDV.001",
                        "itemGroupData": {
                            "IG.AE": {
                                "records": 1,
                                "name": "AE",
                                "label": "Adverse Events",
                                "items": [
                                    {
                                        "name": "STUDYID",
                                        "label": "Study ID",
                                        "type": "string",
                                    },
                                    {
                                        "name": "USUBJID",
                                        "label": "Subject ID",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AETERM",
                                        "label": "AE Term",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AESTDTC",
                                        "label": "Start Date",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AESEV",
                                        "label": "Severity",
                                        "type": "string",
                                    },
                                    {
                                        "name": "AESER",
                                        "label": "Serious",
                                        "type": "string",
                                    },
                                ],
                                "itemData": [
                                    [
                                        "STUDY-001",
                                        "SUBJ-001",
                                        "SEVERE HEADACHE",
                                        "2026-07-25",
                                        "SEVERE",
                                        "Y",
                                    ]
                                ],
                            }
                        },
                    }
                }
                return httpx.Response(status_code=200, json=mock_json)

            if "meddra/code" in url:
                mock_res = {
                    "status": "AUTO-CODED",
                    "matches": [
                        {
                            "llt_code": "10019211",
                            "llt_name": "Severe Headache",
                            "pt_code": "10019211",
                            "pt_name": "Headache",
                            "hlt_code": "10019231",
                            "hlt_name": "Headaches NEC",
                            "hlgt_code": "10029214",
                            "hlgt_name": "Headache and facial pain",
                            "soc_code": "10029205",
                            "soc_name": "Nervous system disorders",
                            "primary_soc_flag": "Y",
                            "score": 1.0,
                        }
                    ],
                }
                return httpx.Response(status_code=200, json=mock_res)

            if "cases-mock" in url:
                mock_cases = [
                    {
                        "header": {
                            "sender_organization": "SPONSOR_A",
                            "receiver_organization": "FDA",
                            "transmission_date": "2026-07-25T15:00:00Z",
                            "message_id": "MSG-001",
                        },
                        "report_identifiers": {
                            "worldwide_unique_case_id": "WW-CASE-001"
                        },
                        "patient": {"patient_id": "SUBJ-001", "sex": "F"},
                        "reactions": [
                            {
                                "reaction_term": "SEVERE HEADACHE",
                                "start_date": "2026-07-25",
                                "seriousness_hospitalization": "Y",
                                "meddra_coding": {
                                    # Differing code to trigger discrepancy
                                    "llt_code": "99999999",
                                    "llt_name": "Severe Headache",
                                    "pt_code": "99999999",
                                    "pt_name": "Headache",
                                    "hlt_code": "99999999",
                                    "hlt_name": "Headaches NEC",
                                    "hlgt_code": "99999999",
                                    "hlgt_name": "Headache and facial pain",
                                    "soc_code": "99999999",
                                    "soc_name": "Nervous system disorders",
                                    "primary_soc_flag": "Y",
                                    "score": 1.0,
                                },
                            }
                        ],
                    }
                ]
                return httpx.Response(status_code=200, json=mock_cases)

            return httpx.Response(status_code=404)

    mock_client = MockAsyncClient()
    app.state.test_httpx_client = mock_client

    client = TestClient(app)
    headers = get_signed_headers(
        roles="sponsor_statistician",
        change_reason="Perform SAE Reconciliation audit trial run",
    )

    payload = {"study_id": "STUDY-001"}

    res = client.post(
        "/api/v1/safety/reconciliation/runs", json=payload, headers=headers
    )
    assert res.status_code == 201

    data = res.json()
    assert data["id"] is not None
    assert data["study_id"] == "STUDY-001"
    assert len(data["discrepancies"]) == 1

    disc = data["discrepancies"][0]
    assert disc["field_name"] == "meddra_coding"
    assert "LLT:10019211" in disc["expected_value"]
    assert "LLT:99999999" in disc["actual_value"]

    # Verify database persistence
    async with db_manager.get_session_maker()() as session:
        stmt_run = select(SAEReconciliationRun).where(
            SAEReconciliationRun.study_id == "STUDY-001"
        )
        res_run = await session.execute(stmt_run)
        runs_db = res_run.scalars().all()
        assert len(runs_db) == 1

        assert runs_db[0].created_by == "safety_recon_user"
        assert (
            runs_db[0].reason_for_change == "Perform SAE Reconciliation audit trial run"
        )
        assert runs_db[0].version_index == 1

        stmt_disc = select(SAEDiscrepancy).where(SAEDiscrepancy.run_id == runs_db[0].id)
        res_disc = await session.execute(stmt_disc)
        discs_db = res_disc.scalars().all()
        assert len(discs_db) == 1
        assert discs_db[0].created_by == "safety_recon_user"
        assert discs_db[0].version_index == 1

        # Check safety audit log
        stmt_audit = select(SafetyAuditLog).where(
            SafetyAuditLog.action == "SAE_RECONCILIATION_RUN"
        )
        res_audit = await session.execute(stmt_audit)
        audits_db = res_audit.scalars().all()
        assert len(audits_db) == 1
        assert "safety_recon_user" in audits_db[0].created_by
        assert "Identified 1 discrepancies" in audits_db[0].details


# ==========================================
# 4. Phase 2 Gaps & Pure Function Tests
# ==========================================


def test_pure_function_generate_stable_event_key():
    # @req:Trace-14
    # USUBJID/AESEQ path
    sae_seq = SeriousAdverseEvent(
        subject_key="SUBJ-123",
        AETERM="HEADACHE",
        AESTDTC="2026-07-25",
        AESEV="SEVERE",
        AESER="Y",
        AESEQ=5,
    )
    key_seq = generate_stable_event_key("SUBJ-123", sae_seq)
    assert key_seq == "SUBJ-123:SEQ-5"

    # Verbatim term + start-date path
    sae_no_seq = SeriousAdverseEvent(
        subject_key="SUBJ-456",
        AETERM="  FEVER OF UNKNOWN ORIGIN ",
        AESTDTC="2026-08-26",
        AESEV="SEVERE",
        AESER="Y",
    )
    key_no_seq = generate_stable_event_key("SUBJ-456", sae_no_seq)
    assert key_no_seq == "SUBJ-456:TERM-FEVER OF UNKNOWN ORIGIN:2026-08-26"


def test_pure_function_normalize_edc_ae_to_sae():
    # @req:Trace-14
    ae_dict = {
        "USUBJID": "SUBJ-789",
        "AETERM": "VOMITING",
        "AESTDTC": "2026-08-25",
        "AESEV": "MODERATE",
        "AESER": "Y",
    }
    sae = normalize_edc_ae_to_sae(ae_dict)
    assert sae.subject_key == "SUBJ-789"
    assert sae.AETERM == "VOMITING"
    assert sae.AESTDTC == "2026-08-25"
    assert sae.AESEV == "MODERATE"
    assert sae.AESER == "Y"


def test_pure_function_normalize_external_icsr_to_saes():
    # @req:Trace-14
    icsr_dict = {
        "header": {
            "sender_organization": "SPONSOR_XYZ",
            "receiver_organization": "FDA",
            "transmission_date": "2026-08-26T10:00:00Z",
            "message_id": "MSG-999",
        },
        "report_identifiers": {"worldwide_unique_case_id": "WW-CASE-999"},
        "patient": {"patient_id": "SUBJ-888", "sex": "M"},
        "reactions": [
            {
                "reaction_term": "ANAPHYLAXIS",
                "start_date": "2026-08-26",
                "seriousness_hospitalization": "Y",
            }
        ],
    }
    saes = normalize_external_icsr_to_saes(icsr_dict)
    assert len(saes) == 1
    assert saes[0].subject_key == "SUBJ-888"
    assert saes[0].AETERM == "ANAPHYLAXIS"
    assert saes[0].AESER == "Y"

    bad_icsr_dict = {
        "patient": {"patient_id": "SUBJ-FALLBACK"},
        "reactions": [
            {
                "reaction_term": "NAUSEA",
                "start_date": "2026-08-26",
                "seriousness_death": "Y",
            }
        ],
    }
    fallback_saes = normalize_external_icsr_to_saes(bad_icsr_dict)
    assert len(fallback_saes) == 1
    assert fallback_saes[0].subject_key == "SUBJ-FALLBACK"
    assert fallback_saes[0].AETERM == "NAUSEA"
    assert fallback_saes[0].AESER == "Y"


@pytest.mark.asyncio
async def test_reconciliation_runs_read_endpoints():
    # @req:Trace-14
    class MockAsyncClient:
        async def get(self, url, headers=None, params=None, timeout=10.0):
            if "sdtm/AE" in url:
                return httpx.Response(
                    status_code=200, json={"clinicalData": {"itemGroupData": {}}}
                )
            if "cases-mock" in url:
                return httpx.Response(status_code=200, json=[])
            return httpx.Response(status_code=404)

    mock_client = MockAsyncClient()
    app.state.test_httpx_client = mock_client

    client = TestClient(app)
    headers = get_signed_headers(
        roles="sponsor_medical_monitor", change_reason="Retrieve reconciliation runs"
    )

    res_list = client.get("/api/v1/safety/reconciliation/runs", headers=headers)
    assert res_list.status_code == 200
    assert isinstance(res_list.json(), list)

    res_detail_404 = client.get(
        f"/api/v1/safety/reconciliation/runs/{uuid.uuid4()}", headers=headers
    )
    assert res_detail_404.status_code == 404
    assert "not found" in res_detail_404.json()["detail"].lower()


@pytest.mark.asyncio
async def test_reconciliation_jobs_read_endpoints_and_gating():
    # @req:Trace-14
    client = TestClient(app)
    headers = get_signed_headers(roles="safety_reviewer", change_reason="Retrieve jobs")

    res_list = client.get("/api/v1/safety/reconciliation/jobs", headers=headers)
    assert res_list.status_code == 200
    assert isinstance(res_list.json(), list)

    res_detail_404 = client.get(
        f"/api/v1/safety/reconciliation/jobs/{uuid.uuid4()}", headers=headers
    )
    assert res_detail_404.status_code == 404

    gated_headers = get_signed_headers(
        roles="sponsor_statistician", change_reason="Trigger gated job"
    )
    res_post_gated = client.post(
        "/api/v1/safety/reconciliation/jobs",
        json={"study_id": "STUDY-GATED"},
        headers=gated_headers,
    )
    assert res_post_gated.status_code == 403
    assert "insufficient role" in res_post_gated.json()["detail"].lower()


@pytest.mark.asyncio
async def test_reconciliation_version_index_increment():
    # @req:Trace-14
    class MockAsyncClient:
        async def get(self, url, headers=None, params=None, timeout=10.0):
            if "sdtm/AE" in url:
                mock_json = {
                    "clinicalData": {
                        "studyOID": "STUDY-DOUBLE",
                        "metaDataVersionOID": "MDV.001",
                        "itemGroupData": {
                            "IG.AE": {
                                "records": 1,
                                "name": "AE",
                                "label": "Adverse Events",
                                "items": [
                                    {"name": "STUDYID", "type": "string"},
                                    {"name": "USUBJID", "type": "string"},
                                    {"name": "AETERM", "type": "string"},
                                    {"name": "AESTDTC", "type": "string"},
                                    {"name": "AESEV", "type": "string"},
                                    {"name": "AESER", "type": "string"},
                                ],
                                "itemData": [
                                    [
                                        "STUDY-DOUBLE",
                                        "SUBJ-DOUBLE",
                                        "SEVERE HEADACHE",
                                        "2026-07-25",
                                        "SEVERE",
                                        "Y",
                                    ]
                                ],
                            }
                        },
                    }
                }
                return httpx.Response(status_code=200, json=mock_json)

            if "meddra/code" in url:
                return httpx.Response(
                    status_code=200, json={"status": "AUTO-CODED", "matches": []}
                )

            if "cases-mock" in url:
                return httpx.Response(status_code=200, json=[])

            return httpx.Response(status_code=404)

    mock_client = MockAsyncClient()
    app.state.test_httpx_client = mock_client

    client = TestClient(app)
    headers = get_signed_headers(
        roles="safety_reviewer", change_reason="Run double reconciliation"
    )

    payload = {"study_id": "STUDY-DOUBLE"}

    res1 = client.post(
        "/api/v1/safety/reconciliation/runs", json=payload, headers=headers
    )
    assert res1.status_code == 201
    run1_data = res1.json()
    assert run1_data["version_index"] == 1
    for d in run1_data["discrepancies"]:
        assert d["version_index"] == 1

    res2 = client.post(
        "/api/v1/safety/reconciliation/runs", json=payload, headers=headers
    )
    assert res2.status_code == 201
    run2_data = res2.json()
    assert run2_data["version_index"] == 2
    for d in run2_data["discrepancies"]:
        assert d["version_index"] == 2


# ==========================================
# 5. Gateway Signature Negative Tests
# ==========================================


def test_safety_mutations_negative_signatures():
    # @req:Trace-14
    client = TestClient(app)
    endpoints = [
        ("/api/v1/safety/export", {"job_name": "Test Export", "icsr": {}}),
        ("/api/v1/safety/reconciliation/runs", {"study_id": "STUDY-NEG"}),
        ("/api/v1/safety/reconciliation/jobs", {"study_id": "STUDY-NEG"}),
    ]

    for url, payload in endpoints:
        # Case 1: Invalid signature (garbage)
        headers_bad_sig = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": "sponsor_medical_monitor",
            "X-Gateway-Timestamp": str(time.time()),
            "X-Gateway-Signature": "garbage_signature_value",
            "X-Signature-Version": "2",
            "X-Change-Reason": "Legit mutation",
        }
        res_bad_sig = client.post(url, json=payload, headers=headers_bad_sig)
        assert res_bad_sig.status_code == 403
        assert "invalid gateway signature" in res_bad_sig.json()["detail"].lower()

        # Case 2: Expired timestamp (backdated by 301 seconds)
        expired_ts = str(time.time() - 301)
        sig_expired = generate_signature(
            user_id="safety_neg_user",
            roles="sponsor_medical_monitor",
            timestamp=expired_ts,
            version="2",
            change_reason="Legit mutation",
        )
        headers_expired = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": "sponsor_medical_monitor",
            "X-Gateway-Timestamp": expired_ts,
            "X-Gateway-Signature": sig_expired,
            "X-Signature-Version": "2",
            "X-Change-Reason": "Legit mutation",
        }
        res_expired = client.post(url, json=payload, headers=headers_expired)
        assert res_expired.status_code == 403
        assert "gateway signature expired" in res_expired.json()["detail"].lower()

        # Case 3: Tampered field (change_reason tampered)
        signed_reason = "Original change reason"
        tampered_reason = "Modified change reason value"
        ts = str(time.time())
        sig_tampered = generate_signature(
            user_id="safety_neg_user",
            roles="sponsor_medical_monitor",
            timestamp=ts,
            version="2",
            change_reason=signed_reason,
        )
        headers_tampered = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": "sponsor_medical_monitor",
            "X-Gateway-Timestamp": ts,
            "X-Gateway-Signature": sig_tampered,
            "X-Signature-Version": "2",
            "X-Change-Reason": tampered_reason,
        }
        res_tampered = client.post(url, json=payload, headers=headers_tampered)
        assert res_tampered.status_code == 403
        assert "invalid gateway signature" in res_tampered.json()["detail"].lower()


def test_safety_reads_negative_signatures():
    # @req:Trace-14
    client = TestClient(app)
    some_id = str(uuid.uuid4())
    endpoints = [
        "/api/v1/safety/reconciliation/runs",
        f"/api/v1/safety/reconciliation/runs/{some_id}",
        "/api/v1/safety/reconciliation/jobs",
        f"/api/v1/safety/reconciliation/jobs/{some_id}",
    ]

    for url in endpoints:
        # Case 1: Invalid signature (garbage) -> GET endpoint
        headers_bad_sig = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": "sponsor_medical_monitor",
            "X-Gateway-Timestamp": str(time.time()),
            "X-Gateway-Signature": "garbage_signature_value",
            "X-Signature-Version": "2",
        }
        res_bad_sig = client.get(url, headers=headers_bad_sig)
        assert res_bad_sig.status_code == 401
        assert "invalid gateway signature" in res_bad_sig.json()["detail"].lower()

        # Case 2: Expired timestamp -> GET endpoint
        expired_ts = str(time.time() - 301)
        sig_expired = generate_signature(
            user_id="safety_neg_user",
            roles="sponsor_medical_monitor",
            timestamp=expired_ts,
            version="2",
            change_reason="",
        )
        headers_expired = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": "sponsor_medical_monitor",
            "X-Gateway-Timestamp": expired_ts,
            "X-Gateway-Signature": sig_expired,
            "X-Signature-Version": "2",
        }
        res_expired = client.get(url, headers=headers_expired)
        assert res_expired.status_code == 401
        assert "gateway signature expired" in res_expired.json()["detail"].lower()

        # Case 3: Tampered field (roles) -> GET endpoint
        signed_roles = "sponsor_medical_monitor"
        tampered_roles = "safety_reviewer"
        ts = str(time.time())
        sig_tampered = generate_signature(
            user_id="safety_neg_user",
            roles=signed_roles,
            timestamp=ts,
            version="2",
            change_reason="",
        )
        headers_tampered = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": tampered_roles,
            "X-Gateway-Timestamp": ts,
            "X-Gateway-Signature": sig_tampered,
            "X-Signature-Version": "2",
        }
        res_tampered = client.get(url, headers=headers_tampered)
        assert res_tampered.status_code == 401
        assert "invalid gateway signature" in res_tampered.json()["detail"].lower()
