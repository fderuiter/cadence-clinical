import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.interop.database import db_manager
from apps.interop.fhir_adapter import pseudonymize_identifier, strip_pii_from_patient
from apps.interop.main import app
from apps.interop.models import Base, EPROSubmission, InteropAuditLog


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory Interop database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    user_id = "test_user"
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


def test_pseudonymization_and_pii_stripping():
    """
    Test that pseudonymization utility creates deterministic irreversible hashes and
    correctly strips PII (direct identifiers) from FHIR patient resources.
    """
    raw_id = "PAT-12345"
    p_id = pseudonymize_identifier(raw_id)
    assert p_id != raw_id
    assert len(p_id) == 64
    # Deterministic check
    assert pseudonymize_identifier(raw_id) == p_id

    mock_patient = {
        "resourceType": "Patient",
        "id": "PAT-12345",
        "identifier": [{"system": "http://hospital.org/mrn", "value": "MRN-555"}],
        "name": [{"use": "official", "family": "Smith", "given": ["John"]}],
        "telecom": [{"system": "phone", "value": "555-0199"}],
        "gender": "male",
        "birthDate": "1980-01-01",
        "address": [{"line": ["123 Main St"]}],
    }

    stripped = strip_pii_from_patient(mock_patient)
    # Check that direct PII is stripped
    assert "name" not in stripped
    assert "telecom" not in stripped
    assert "address" not in stripped
    # ID is pseudonymized
    assert stripped["id"] == p_id
    assert stripped["identifier"][0]["value"] == pseudonymize_identifier("MRN-555")
    # Non-PII is kept
    assert stripped["gender"] == "male"
    assert stripped["birthDate"] == "1980-01-01"


@pytest.mark.asyncio
async def test_fhir_prefill_bundle_pipeline():
    """
    Verify that FHIR Bundles are parsed, Patient ID is pseudonymized, PII is stripped,
    and clinical observation, condition, and medicationstatement records are pre-filled
    to designated CDASH fields correctly.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Ingest FHIR records"
    )

    mock_bundle = {
        "resourceType": "Bundle",
        "type": "transaction",
        "entry": [
            {
                "resource": {
                    "resourceType": "Patient",
                    "id": "EHR-9988",
                    "identifier": [
                        {"system": "http://hospital.org/mrn", "value": "MRN-9988"}
                    ],
                    "name": [{"family": "Doe", "given": ["Jane"]}],
                    "telecom": [{"system": "email", "value": "jane.doe@example.com"}],
                    "gender": "female",
                    "birthDate": "1992-08-15",
                }
            },
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-1",
                    "status": "final",
                    "category": [
                        {
                            "coding": [
                                {
                                    "system": "http://terminology.hl7.org/CodeSystem/observation-category",
                                    "code": "vital-signs",
                                }
                            ]
                        }
                    ],
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "8480-6",
                                "display": "Systolic blood pressure",
                            }
                        ]
                    },
                    "subject": {"reference": "Patient/EHR-9988"},
                    "effectiveDateTime": "2026-07-22T10:00:00Z",
                    "valueQuantity": {"value": 118, "unit": "mmHg"},
                }
            },
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-2",
                    "status": "final",
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "2339-0",
                                "display": "Glucose [Moles/volume] in Blood",
                            }
                        ]
                    },
                    "subject": {"reference": "Patient/EHR-9988"},
                    "effectiveDateTime": "2026-07-22T10:15:00Z",
                    "valueQuantity": {"value": 5.4, "unit": "mmol/L"},
                }
            },
            {
                "resource": {
                    "resourceType": "Condition",
                    "id": "cond-1",
                    "clinicalStatus": {
                        "coding": [
                            {
                                "system": "http://terminology.hl7.org/CodeSystem/condition-clinical",
                                "code": "active",
                            }
                        ]
                    },
                    "code": {
                        "coding": [
                            {
                                "system": "http://snomed.info/sct",
                                "code": "25064002",
                                "display": "Headache",
                            }
                        ],
                        "text": "Migraine headache",
                    },
                    "subject": {"reference": "Patient/EHR-9988"},
                    "onsetDateTime": "2026-07-20",
                }
            },
            {
                "resource": {
                    "resourceType": "MedicationStatement",
                    "id": "med-1",
                    "status": "active",
                    "medicationCodeableConcept": {
                        "coding": [
                            {
                                "system": "http://hl7.org/fhir/sid/ndc",
                                "code": "00904-2012-60",
                                "display": "Aspirin 325mg",
                            }
                        ],
                        "text": "Aspirin",
                    },
                    "subject": {"reference": "Patient/EHR-9988"},
                    "effectiveDateTime": "2026-07-21",
                }
            },
        ],
    }

    payload = {"study_id": "study_test_99", "bundle": mock_bundle}

    response = client.post(
        "/api/v1/interop/fhir/prefill", json=payload, headers=headers
    )
    assert response.status_code == 200
    data = response.json()

    assert data["study_id"] == "study_test_99"
    pseudonym = pseudonymize_identifier("EHR-9988")
    assert data["subject_pseudonym"] == pseudonym

    # De-identified patient check
    stripped_pat = data["de_identified_patient"]
    assert stripped_pat is not None
    assert stripped_pat["id"] == pseudonym
    assert "name" not in stripped_pat
    assert "telecom" not in stripped_pat

    # CDASH mapped fields assertions
    mapped = data["mapped_fields"]
    assert mapped["DM.USUBJID"] == f"study_test_99-{pseudonym}"
    assert mapped["DM.SUBJID"] == pseudonym[:12]
    assert mapped["DM.BRTHDTC"] == "1992-08-15"
    assert mapped["DM.SEX"] == "F"

    # Clinical records check
    clinical = data["clinical_records"]
    assert len(clinical["vital_signs"]) == 1
    assert clinical["vital_signs"][0]["cdash_testcd"] == "SYSBP"
    assert clinical["vital_signs"][0]["value"] == 118

    assert len(clinical["labs"]) == 1
    assert clinical["labs"][0]["cdash_testcd"] == "GLUC"
    assert clinical["labs"][0]["value"] == 5.4

    assert len(clinical["conditions"]) == 1
    assert clinical["conditions"][0]["display_name"] == "Migraine headache"
    assert clinical["conditions"][0]["onset_date"] == "2026-07-20"

    assert len(clinical["medications"]) == 1
    assert clinical["medications"][0]["display_name"] == "Aspirin"
    assert clinical["medications"][0]["start_date"] == "2026-07-21"

    # Verify audit logs in database
    async with db_manager.get_session_maker()() as session:
        stmt = select(InteropAuditLog).where(InteropAuditLog.action == "FHIR_PREFILL")
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) == 1
        assert "EHR-9988" not in logs[0].details  # Strips raw PII ID from audit details
        assert pseudonym[:12] in logs[0].details


@pytest.mark.asyncio
async def test_epro_submission_and_conflict_resolution():
    """
    Verify ePRO submissions, device timestamp preservation, and offline sync conflict
    strategies (CLIENT_WINS, SERVER_WINS, MERGE).
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="patient_user", change_reason="Submit ePRO diary")

    # 1. Initial creation
    sub_payload = {
        "subject_id": "subj_abc",
        "diary_id": "daily_pain_scale",
        "device_timestamp": "2026-07-22T20:00:00Z",
        "answers": {"pain_level": 4, "nausea": "none"},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "device_ios_1",
            "conflict_strategy": "CLIENT_WINS",
        },
    }

    resp = client.post("/api/v1/interop/epro/submit", json=sub_payload, headers=headers)
    assert resp.status_code == 201
    data = resp.json()
    assert data["status"] == "CREATED"
    assert data["answers"]["pain_level"] == 4
    assert data["version_index"] == 1

    # 2. Conflicting submission - CLIENT_WINS
    sub_payload_client_wins = {
        "subject_id": "subj_abc",
        "diary_id": "daily_pain_scale",
        "device_timestamp": "2026-07-22T20:05:00Z",
        "answers": {"pain_level": 5, "nausea": "none"},
        "offline_sync_markers": {
            "sequence_number": 2,
            "client_id": "device_ios_1",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=sub_payload_client_wins, headers=headers
    )
    assert resp.status_code == 201
    data = resp.json()
    assert data["status"] == "UPDATED_CLIENT_WINS"
    assert data["answers"]["pain_level"] == 5
    assert data["version_index"] == 2

    # 3. Conflicting submission - SERVER_WINS
    sub_payload_server_wins = {
        "subject_id": "subj_abc",
        "diary_id": "daily_pain_scale",
        "device_timestamp": "2026-07-22T20:10:00Z",
        "answers": {"pain_level": 1, "nausea": "severe"},
        "offline_sync_markers": {
            "sequence_number": 3,
            "client_id": "device_ios_1",
            "conflict_strategy": "SERVER_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=sub_payload_server_wins, headers=headers
    )
    assert resp.status_code == 201
    data = resp.json()
    assert data["status"] == "IGNORED_SERVER_WINS"
    assert data["answers"]["pain_level"] == 5  # Retains server's value (which is 5)
    assert data["version_index"] == 2

    # 4. Conflicting submission - MERGE
    sub_payload_merge = {
        "subject_id": "subj_abc",
        "diary_id": "daily_pain_scale",
        "device_timestamp": "2026-07-22T20:15:00Z",
        "answers": {"nausea": "mild", "headache": "yes"},
        "offline_sync_markers": {
            "sequence_number": 4,
            "client_id": "device_ios_1",
            "conflict_strategy": "MERGE",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=sub_payload_merge, headers=headers
    )
    assert resp.status_code == 201
    data = resp.json()
    assert data["status"] == "MERGED"
    assert data["answers"]["pain_level"] == 5  # Retained
    assert data["answers"]["nausea"] == "mild"  # Overwritten
    assert data["answers"]["headache"] == "yes"  # Merged
    assert data["version_index"] == 3


@pytest.mark.asyncio
async def test_bulk_offline_sync():
    """
    Test bulk mobile sync endpoint with offline queue reconciliation.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="patient_user", change_reason="Bulk offline sync")

    bulk_payload = {
        "submissions": [
            {
                "subject_id": "subj_1",
                "diary_id": "q_pain",
                "device_timestamp": "2026-07-22T19:00:00Z",
                "answers": {"pain": 2},
                "offline_sync_markers": {
                    "sequence_number": 1,
                    "client_id": "dev_1",
                    "conflict_strategy": "CLIENT_WINS",
                },
            },
            {
                "subject_id": "subj_2",
                "diary_id": "q_pain",
                "device_timestamp": "2026-07-22T19:01:00Z",
                "answers": {"pain": 3},
                "offline_sync_markers": {
                    "sequence_number": 1,
                    "client_id": "dev_2",
                    "conflict_strategy": "CLIENT_WINS",
                },
            },
            # Conflict on subj_1 with MERGE
            {
                "subject_id": "subj_1",
                "diary_id": "q_pain",
                "device_timestamp": "2026-07-22T19:05:00Z",
                "answers": {"nausea": "mild"},
                "offline_sync_markers": {
                    "sequence_number": 2,
                    "client_id": "dev_1",
                    "conflict_strategy": "MERGE",
                },
            },
        ]
    }

    resp = client.post("/api/v1/interop/epro/sync", json=bulk_payload, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["status"] == "success"
    assert data["processed_count"] == 3
    assert data["created_count"] == 2
    assert data["updated_count"] == 1
    assert data["ignored_count"] == 0

    # Retrieve and check results
    async with db_manager.get_session_maker()() as session:
        # subj_1 should have merged answers
        stmt = select(EPROSubmission).where(EPROSubmission.subject_id == "subj_1")
        res = await session.execute(stmt)
        sub1 = res.scalars().first()
        assert sub1.answers["pain"] == 2
        assert sub1.answers["nausea"] == "mild"
        assert sub1.version_index == 2
