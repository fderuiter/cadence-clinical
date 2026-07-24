# @req:PRD-ECOA-001 - Instrument and subject-assignment persistence models and tests
# This file tests the eCOA content and scheduling data models used to author questionnaires/diaries and assign them to subjects.
# Verifies 21 CFR Part 11 auditing requirements (created_at, created_by, reason_for_change, version_index),
# database schema integrity, foreign-key constraints, cascading delete logic, and authorization boundaries.

import time
from datetime import datetime, timedelta, timezone

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError

from apps.gateway.main import generate_signature
from apps.interop.database import db_manager
from apps.interop.fhir_adapter import pseudonymize_identifier, strip_pii_from_patient
from apps.interop.main import app
from apps.interop.models import (
    Base,
    EPROSubmission,
    Instrument,
    InteropAuditLog,
    SubjectAssignment,
)


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


def get_auth_headers(
    roles: str = "admin", change_reason: str = "", user_id: str = "test_user"
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
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
                    "text": {"div": "Jane Doe is a patient with history of migraine."},
                    "note": [
                        "Patient notes with direct details.",
                        {"text": "Nested patient note."},
                    ],
                }
            },
            {
                "resource": {
                    "resourceType": "Patient",
                    "id": "EHR-OTHER",
                    "gender": "other",
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
                    "resourceType": "Observation",
                    "id": "obs-pulse",
                    "status": "final",
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "8867-4",
                                "display": "Heart rate",
                            }
                        ]
                    },
                    "valueQuantity": {"value": 72, "unit": "/min"},
                }
            },
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-temp",
                    "status": "final",
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "8310-5",
                                "display": "Temperature",
                            }
                        ]
                    },
                    "valueQuantity": {"value": 36.6, "unit": "Cel"},
                }
            },
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-weight",
                    "status": "final",
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "29463-7",
                                "display": "Weight",
                            }
                        ]
                    },
                    "valueQuantity": {"value": 65, "unit": "kg"},
                }
            },
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-height",
                    "status": "final",
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "8302-2",
                                "display": "Height",
                            }
                        ]
                    },
                    "valueQuantity": {"value": 170, "unit": "cm"},
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
                    "resourceType": "Condition",
                    "id": "cond-2",
                    "code": {
                        "coding": [
                            {
                                "system": "http://snomed.info/sct",
                                "code": "123456",
                                "display": "Asthma",
                            }
                        ]
                    },
                    "recordedDate": "2026-07-21",
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
            {
                "resource": {
                    "resourceType": "MedicationStatement",
                    "id": "med-2",
                    "medicationCodeableConcept": {
                        "coding": [
                            {
                                "system": "http://hl7.org/fhir/sid/ndc",
                                "code": "54321",
                                "display": "Albuterol",
                            }
                        ]
                    },
                    "dateAsserted": "2026-07-21",
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
    assert len(clinical["vital_signs"]) == 5
    assert clinical["vital_signs"][0]["cdash_testcd"] == "SYSBP"
    assert clinical["vital_signs"][0]["value"] == 118

    assert len(clinical["labs"]) == 1
    assert clinical["labs"][0]["cdash_testcd"] == "GLUC"
    assert clinical["labs"][0]["value"] == 5.4

    assert len(clinical["conditions"]) == 2
    assert clinical["conditions"][0]["display_name"] == "Migraine headache"
    assert clinical["conditions"][0]["onset_date"] == "2026-07-20"

    assert len(clinical["medications"]) == 2
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


@pytest.mark.asyncio
async def test_subject_role_authorization_and_identity_binding():
    """
    Test Subject role authorization and identity binding rules.
    - An authenticated Subject can submit their own ePRO record.
    - An authenticated Subject cannot submit/sync records for another subject (returns 403).
    - An authenticated Subject cannot access staff-only endpoints like FHIR prefill (returns 403).
    - Staff members can submit/sync for any subject without restriction.
    """
    client = TestClient(app)

    # Case 1: Subject submitting their own record -> 201 Created
    headers_subject_self = get_auth_headers(
        roles="Subject", change_reason="Submit my diary", user_id="patient_alice"
    )
    payload_self = {
        "subject_id": "patient_alice",
        "diary_id": "daily_pain_scale",
        "device_timestamp": "2026-07-22T20:00:00Z",
        "answers": {"pain_level": 2},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "device_ios_alice",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=payload_self, headers=headers_subject_self
    )
    assert resp.status_code == 201
    assert resp.json()["status"] == "CREATED"

    # Case 2: Subject submitting another subject's record -> 403 Forbidden
    headers_subject_other = get_auth_headers(
        roles="Subject", change_reason="Submit someone's diary", user_id="patient_alice"
    )
    payload_other = {
        "subject_id": "patient_bob",
        "diary_id": "daily_pain_scale",
        "device_timestamp": "2026-07-22T20:00:00Z",
        "answers": {"pain_level": 3},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "device_ios_alice",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=payload_other, headers=headers_subject_other
    )
    assert resp.status_code == 403
    assert "Access denied" in resp.json()["detail"]

    # Case 3: Subject performing bulk sync with mixed subject IDs -> 403 Forbidden
    bulk_payload_invalid = {
        "submissions": [
            {
                "subject_id": "patient_alice",
                "diary_id": "daily_pain_scale_1",
                "device_timestamp": "2026-07-22T20:00:00Z",
                "answers": {"pain_level": 2},
                "offline_sync_markers": {
                    "sequence_number": 1,
                    "client_id": "device_ios_alice",
                    "conflict_strategy": "CLIENT_WINS",
                },
            },
            {
                "subject_id": "patient_bob",  # Mismatch!
                "diary_id": "daily_pain_scale_2",
                "device_timestamp": "2026-07-22T20:05:00Z",
                "answers": {"pain_level": 5},
                "offline_sync_markers": {
                    "sequence_number": 2,
                    "client_id": "device_ios_alice",
                    "conflict_strategy": "CLIENT_WINS",
                },
            },
        ]
    }
    resp = client.post(
        "/api/v1/interop/epro/sync",
        json=bulk_payload_invalid,
        headers=headers_subject_self,
    )
    assert resp.status_code == 403
    assert "Access denied" in resp.json()["detail"]

    # Case 4: Subject trying to access staff-only FHIR prefill -> 403 Forbidden
    fhir_payload = {"study_id": "study_xyz", "bundle": {"resourceType": "Bundle"}}
    resp = client.post(
        "/api/v1/interop/fhir/prefill", json=fhir_payload, headers=headers_subject_self
    )
    assert resp.status_code == 403
    assert "Access denied" in resp.json()["detail"]

    # Case 5: Staff member submitting for any subject -> 201 Created
    headers_staff = get_auth_headers(
        roles="admin,sponsor_dm",
        change_reason="Submit on behalf of Bob",
        user_id="staff_1",
    )
    resp = client.post(
        "/api/v1/interop/epro/submit", json=payload_other, headers=headers_staff
    )
    assert resp.status_code == 201
    assert resp.json()["status"] == "CREATED"


@pytest.mark.asyncio
async def test_instrument_and_assignment_orm_persistence():
    """
    Test direct ORM persistence of Instrument and SubjectAssignment.
    Verifies that fields, relations, and 21 CFR Part 11 audit fields work.
    """
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        # Create an Instrument
        inst = Instrument(
            name="Daily Pain Scale Questionnaire",
            description="A diary tracking daily pain levels and symptoms.",
            items={
                "q1": "Rate your overall pain today.",
                "q2": "Did you take any rescue medication?",
            },
            response_types={
                "q1": {"type": "scale", "min": 0, "max": 10},
                "q2": {"type": "boolean", "options": ["Yes", "No"]},
            },
            scoring_metadata={"sum_score_rule": "q1 + (10 if q2 == 'Yes' else 0)"},
            created_by="doctor_smith",
            reason_for_change="Author initial daily pain scale template",
            version_index=1,
        )
        session.add(inst)
        await session.flush()

        # Create an Assignment
        now_utc = datetime.now(timezone.utc)
        assign = SubjectAssignment(
            subject_id="subj_001",
            instrument_id=inst.id,
            start_date=now_utc,
            end_date=now_utc + timedelta(days=30),
            recurrence_pattern="DAILY",
            due_at=now_utc + timedelta(hours=12),
            created_by="nurse_jones",
            reason_for_change="Assign daily questionnaire for Study screening period",
            version_index=1,
        )
        session.add(assign)
        await session.commit()

    # Re-fetch and verify
    async with async_session() as session:
        from sqlalchemy.orm import selectinload

        stmt = (
            select(Instrument)
            .where(Instrument.name == "Daily Pain Scale Questionnaire")
            .options(selectinload(Instrument.assignments))
        )
        res = await session.execute(stmt)
        db_inst = res.scalars().first()
        assert db_inst is not None
        assert db_inst.created_by == "doctor_smith"
        assert db_inst.reason_for_change == "Author initial daily pain scale template"
        assert db_inst.version_index == 1
        assert db_inst.items["q1"] == "Rate your overall pain today."
        assert len(db_inst.assignments) == 1

        db_assign = db_inst.assignments[0]
        assert db_assign.subject_id == "subj_001"
        assert db_assign.recurrence_pattern == "DAILY"
        assert db_assign.created_by == "nurse_jones"


@pytest.mark.asyncio
async def test_foreign_key_and_cascade_lifecycle_integrity():
    """
    Verify database schema integrity constraints:
    - SubjectAssignment cannot refer to a non-existent instrument_id (Foreign Key violation).
    - Deleting an Instrument cascades and deletes its linked SubjectAssignments.
    """
    async_session = db_manager.get_session_maker()

    # 1. Foreign Key constraint check
    async with async_session() as session:
        now_utc = datetime.now(timezone.utc)
        invalid_assign = SubjectAssignment(
            subject_id="subj_err",
            instrument_id="non_existent_id_12345",  # Invalid ID
            start_date=now_utc,
            end_date=now_utc + timedelta(days=5),
            created_by="admin",
            reason_for_change="Invalid assignment",
            version_index=1,
        )
        session.add(invalid_assign)
        with pytest.raises(IntegrityError):
            await session.commit()
        await session.rollback()

    # 2. Cascade delete check
    async with async_session() as session:
        inst = Instrument(
            name="Temporary Survey",
            items={},
            response_types={},
            scoring_metadata={},
            created_by="admin",
            reason_for_change="Temp setup",
            version_index=1,
        )
        session.add(inst)
        await session.flush()

        assign = SubjectAssignment(
            subject_id="subj_temp",
            instrument_id=inst.id,
            start_date=datetime.now(timezone.utc),
            end_date=datetime.now(timezone.utc) + timedelta(days=1),
            created_by="admin",
            reason_for_change="Temp assign",
            version_index=1,
        )
        session.add(assign)
        await session.commit()

        # Delete instrument
        await session.delete(inst)
        await session.commit()

    # Check that assignment is deleted as well
    async with async_session() as session:
        stmt = select(SubjectAssignment).where(
            SubjectAssignment.subject_id == "subj_temp"
        )
        res = await session.execute(stmt)
        db_assign = res.scalars().first()
        assert db_assign is None


@pytest.mark.asyncio
async def test_instrument_and_assignment_endpoints_and_auditing():
    """
    Test eCOA authoring and scheduling endpoints end-to-end,
    verifying GxP role security and audit log persistence.
    """
    client = TestClient(app)

    # Headers
    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Author new Instrument definition"
    )
    subject_headers = get_auth_headers(
        roles="Subject", change_reason="View my assignments", user_id="patient_charlie"
    )

    # 1. Create Instrument (authorized)
    inst_payload = {
        "name": "EORTC QLQ-C30",
        "description": "Cancer patient quality of life questionnaire.",
        "items": {"q1": "Do you have trouble taking a long walk?"},
        "response_types": {
            "q1": {
                "type": "choice",
                "options": ["Not at all", "A little", "Quite a bit", "Very much"],
            }
        },
        "scoring_metadata": {"version": "3.0"},
        "reason_for_change": "Initial authoring of standard oncology quality of life form",
    }

    resp = client.post(
        "/api/v1/interop/instruments", json=inst_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    inst_data = resp.json()
    assert inst_data["name"] == "EORTC QLQ-C30"
    assert inst_data["created_by"] == "test_user"
    assert inst_data["reason_for_change"] == "Author new Instrument definition"
    inst_id = inst_data["id"]

    # 2. Deny non-staff Instrument creation
    resp = client.post(
        "/api/v1/interop/instruments", json=inst_payload, headers=subject_headers
    )
    assert resp.status_code == 403

    # 3. Create Subject Assignment (authorized)
    assign_payload = {
        "subject_id": "patient_charlie",
        "instrument_id": inst_id,
        "start_date": datetime.now(timezone.utc).isoformat(),
        "end_date": (datetime.now(timezone.utc) + timedelta(days=14)).isoformat(),
        "recurrence_pattern": "WEEKLY",
        "reason_for_change": "Assigning EORTC questionnaire for chemotherapy cycle 1",
    }
    resp = client.post(
        "/api/v1/interop/assignments", json=assign_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    assign_data = resp.json()
    assert assign_data["subject_id"] == "patient_charlie"
    assert assign_data["instrument_id"] == inst_id
    assert assign_data["created_by"] == "test_user"

    # 4. Assignment validation errors (invalid start/end dates)
    invalid_assign_payload = assign_payload.copy()
    invalid_assign_payload["start_date"] = (
        datetime.now(timezone.utc) + timedelta(days=5)
    ).isoformat()
    invalid_assign_payload["end_date"] = datetime.now(
        timezone.utc
    ).isoformat()  # End before start
    resp = client.post(
        "/api/v1/interop/assignments",
        json=invalid_assign_payload,
        headers=staff_headers,
    )
    assert resp.status_code == 400
    assert "Assignment start_date cannot be after end_date" in resp.json()["detail"]

    # 5. Assignment validation errors (non-existent instrument_id)
    missing_inst_payload = assign_payload.copy()
    missing_inst_payload["instrument_id"] = "non-existent-uuid"
    resp = client.post(
        "/api/v1/interop/assignments", json=missing_inst_payload, headers=staff_headers
    )
    assert resp.status_code == 404

    # 6. Retrieve Instrument definition
    resp = client.get(f"/api/v1/interop/instruments/{inst_id}", headers=staff_headers)
    assert resp.status_code == 200
    assert resp.json()["name"] == "EORTC QLQ-C30"

    # 7. Retrieve Subject Assignments (authorized self)
    resp = client.get(
        "/api/v1/interop/assignments/subject/patient_charlie", headers=subject_headers
    )
    assert resp.status_code == 200
    assert len(resp.json()) == 1
    assert resp.json()[0]["instrument_id"] == inst_id

    # 8. Retrieve Subject Assignments (denied other)
    resp = client.get(
        "/api/v1/interop/assignments/subject/patient_david", headers=subject_headers
    )
    assert resp.status_code == 403

    # 9. Verify InteropAuditLog entries for Instrument and Assignment creation
    async with db_manager.get_session_maker()() as session:
        stmt = select(InteropAuditLog).where(
            InteropAuditLog.action.in_(["CREATE_INSTRUMENT", "CREATE_ASSIGNMENT"])
        )
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) == 2
        actions = [log.action for log in logs]
        assert "CREATE_INSTRUMENT" in actions
        assert "CREATE_ASSIGNMENT" in actions
        for log in logs:
            assert log.change_reason is not None


@pytest.mark.asyncio
async def test_subject_content_submission_and_compliance_apis():
    """
    Test retrieving assigned instruments, response capture/submission, and dashboard compliance status
    for a Subject role end-to-end.
    """
    client = TestClient(app)

    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Staff setup"
    )
    subject_alice_headers = get_auth_headers(
        roles="Subject", change_reason="Alice login", user_id="alice_subject"
    )
    subject_bob_headers = get_auth_headers(
        roles="Subject", change_reason="Bob login", user_id="bob_subject"
    )

    # 1. Author an Instrument as staff
    inst_payload = {
        "name": "Sleep Quality Survey",
        "description": "Daily survey tracking patient sleep patterns.",
        "items": {"q1": "How many hours of restful sleep did you get?"},
        "response_types": {"q1": {"type": "integer"}},
        "scoring_metadata": {},
        "reason_for_change": "Sleep standard",
    }
    resp = client.post(
        "/api/v1/interop/instruments", json=inst_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    inst_id = resp.json()["id"]

    # 2. Assign to alice
    now_utc = datetime.now(timezone.utc).replace(tzinfo=None)
    assign_payload = {
        "subject_id": "alice_subject",
        "instrument_id": inst_id,
        "start_date": (now_utc - timedelta(days=2)).isoformat(),
        "end_date": (now_utc + timedelta(days=1)).isoformat(),
        "due_at": (
            now_utc - timedelta(hours=12)
        ).isoformat(),  # Overdue since past due_at
        "recurrence_pattern": "DAILY",
        "reason_for_change": "Alice assignment",
    }
    resp = client.post(
        "/api/v1/interop/assignments", json=assign_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    resp.json()["id"]

    # Assign another pending to Alice
    assign_payload_pending = {
        "subject_id": "alice_subject",
        "instrument_id": inst_id,
        "start_date": now_utc.isoformat(),
        "end_date": (now_utc + timedelta(days=5)).isoformat(),
        "due_at": (now_utc + timedelta(days=1)).isoformat(),  # Pending
        "recurrence_pattern": "DAILY",
        "reason_for_change": "Alice pending assignment",
    }
    client.post(
        "/api/v1/interop/assignments",
        json=assign_payload_pending,
        headers=staff_headers,
    )

    # 3. Retrieve Alice's assigned instruments
    resp = client.get(
        "/api/v1/interop/subjects/alice_subject/instruments",
        headers=subject_alice_headers,
    )
    assert resp.status_code == 200
    assert len(resp.json()) == 1
    assert resp.json()[0]["id"] == inst_id

    # 4. Cross-subject validation: Bob cannot fetch Alice's assignments or instruments
    resp = client.get(
        "/api/v1/interop/subjects/alice_subject/instruments",
        headers=subject_bob_headers,
    )
    assert resp.status_code == 403

    resp = client.get(
        "/api/v1/interop/assignments/subject/alice_subject", headers=subject_bob_headers
    )
    assert resp.status_code == 403

    # 5. Access control for Instrument definition retrieval:
    # Bob is not assigned the Instrument, so Bob's attempt to retrieve it should yield a 403.
    resp = client.get(
        f"/api/v1/interop/instruments/{inst_id}", headers=subject_bob_headers
    )
    assert resp.status_code == 403

    # Alice IS assigned the Instrument and should retrieve it successfully.
    resp = client.get(
        f"/api/v1/interop/instruments/{inst_id}", headers=subject_alice_headers
    )
    assert resp.status_code == 200
    assert resp.json()["name"] == "Sleep Quality Survey"

    # 6. Compliance calculations before submission
    # We should have 1 overdue and 1 pending assignment
    resp = client.get(
        "/api/v1/interop/subjects/alice_subject/compliance",
        headers=subject_alice_headers,
    )
    assert resp.status_code == 200
    comp_data = resp.json()
    assert comp_data["subject_id"] == "alice_subject"
    assert comp_data["completed_count"] == 0
    assert comp_data["overdue_count"] == 1
    assert comp_data["pending_count"] == 1
    assert comp_data["compliance_rate"] == 0.0

    # 7. Alice submits response
    sub_payload = {
        "subject_id": "alice_subject",
        "diary_id": inst_id,
        "device_timestamp": now_utc.isoformat(),
        "answers": {"q1": 8},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "device_alice",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=sub_payload, headers=subject_alice_headers
    )
    assert resp.status_code == 201
    assert resp.json()["status"] == "CREATED"

    # 8. Compliance calculations after first submission
    # The submission reconciles with the first assignment chronologically, marking it COMPLETED.
    # The remaining assignment is pending.
    resp = client.get(
        "/api/v1/interop/subjects/alice_subject/compliance",
        headers=subject_alice_headers,
    )
    assert resp.status_code == 200
    comp_data_post = resp.json()
    assert comp_data_post["completed_count"] == 1
    assert comp_data_post["overdue_count"] == 0
    assert comp_data_post["pending_count"] == 1
    assert comp_data_post["compliance_rate"] == 50.0

    # 9. Verify InteropAuditLog tracks action, role, and change_reason
    async with db_manager.get_session_maker()() as session:
        stmt = select(InteropAuditLog).where(
            InteropAuditLog.action == "EPRO_SUBMIT",
            InteropAuditLog.user_id == "alice_subject",
        )
        res = await session.execute(stmt)
        log_entry = res.scalars().first()
        assert log_entry is not None
        assert log_entry.user_role == "Subject"
        assert log_entry.change_reason == "Alice login"


@pytest.mark.asyncio
async def test_notifications_and_reminders_lifecycle():
    """
    Test the complete lifecycle of Subject Reminders and Notification Inbox.
    Verifies:
    - Deterministic due reminder computation on demand from schedules.
    - Security and identity-scoped access boundaries for fetching notifications.
    - Audit-logged acknowledgement mutations with 21 CFR Part 11 compliance fields.
    - Access restrictions on cross-subject operations (403).
    """
    client = TestClient(app)

    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Staff notification setup"
    )
    subject_david_headers = get_auth_headers(
        roles="Subject", change_reason="David login", user_id="david_subject"
    )
    subject_eva_headers = get_auth_headers(
        roles="Subject", change_reason="Eva login", user_id="eva_subject"
    )

    # 1. Author an Instrument as staff
    inst_payload = {
        "name": "Daily Diary",
        "description": "Daily symptom tracker.",
        "items": {"q1": "Any symptoms today?"},
        "response_types": {"q1": {"type": "choice", "options": ["Yes", "No"]}},
        "scoring_metadata": {},
        "reason_for_change": "Initial authoring of standard oncology daily form",
    }
    resp = client.post(
        "/api/v1/interop/instruments", json=inst_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    inst_id = resp.json()["id"]

    # 2. Assign to David - make it due (due_at in the past)
    now_utc = datetime.now(timezone.utc).replace(tzinfo=None)
    assign_payload = {
        "subject_id": "david_subject",
        "instrument_id": inst_id,
        "start_date": (now_utc - timedelta(days=2)).isoformat(),
        "end_date": (now_utc + timedelta(days=1)).isoformat(),
        "due_at": (now_utc - timedelta(hours=2)).isoformat(),  # Overdue / due
        "recurrence_pattern": "DAILY",
        "reason_for_change": "David assignment setup",
    }
    resp = client.post(
        "/api/v1/interop/assignments", json=assign_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    assign_id = resp.json()["id"]

    # 3. Compute reminders on demand
    resp = client.post(
        "/api/v1/interop/reminders/compute",
        params={"subject_id": "david_subject"},
        headers=staff_headers,
    )
    assert resp.status_code == 200
    comp_data = resp.json()
    assert comp_data["status"] == "success"
    assert comp_data["created_count"] == 4  # EMAIL, SMS, WEBHOOK, IN_APP

    # Re-running compute reminders should create 0 new notifications (idempotent/deduplicated)
    resp = client.post(
        "/api/v1/interop/reminders/compute",
        params={"subject_id": "david_subject"},
        headers=staff_headers,
    )
    assert resp.status_code == 200
    assert resp.json()["created_count"] == 0

    # 4. Fetch notifications for David (authorized)
    resp = client.get(
        "/api/v1/interop/subjects/david_subject/notifications",
        headers=subject_david_headers,
    )
    assert resp.status_code == 200
    notifications = resp.json()
    assert len(notifications) == 4

    # Assert fields on notification response
    notif = notifications[0]
    assert notif["subject_id"] == "david_subject"
    assert notif["assignment_id"] == assign_id
    assert notif["is_read"] is False
    assert notif["read_at"] is None
    assert notif["version_index"] == 1
    assert notif["created_by"] == "test_user"  # because staff computed it
    assert notif["reason_for_change"] == "Automated due reminder generation"

    # 5. Fetch notifications for David using Eva's credentials (denied - 403)
    resp = client.get(
        "/api/v1/interop/subjects/david_subject/notifications",
        headers=subject_eva_headers,
    )
    assert resp.status_code == 403

    # 6. Acknowledge/read a notification for David (authorized self)
    notif_id = notif["id"]
    ack_payload = {"reason_for_change": "Acknowledging the pain scale reminder"}
    resp = client.post(
        f"/api/v1/interop/notifications/{notif_id}/acknowledge",
        json=ack_payload,
        headers=subject_david_headers,
    )
    assert resp.status_code == 200
    ack_data = resp.json()
    assert ack_data["is_read"] is True
    assert ack_data["read_at"] is not None
    assert ack_data["version_index"] == 2
    assert ack_data["reason_for_change"] == "Acknowledging the pain scale reminder"

    # 7. Acknowledge a notification for David using Eva's credentials (denied - 403)
    other_notif_id = notifications[1]["id"]
    resp = client.post(
        f"/api/v1/interop/notifications/{other_notif_id}/acknowledge",
        json=ack_payload,
        headers=subject_eva_headers,
    )
    assert resp.status_code == 403

    # 8. Acknowledge non-existent notification (404)
    resp = client.post(
        "/api/v1/interop/notifications/non-existent-id/acknowledge",
        json=ack_payload,
        headers=subject_david_headers,
    )
    assert resp.status_code == 404

    # 9. Verify InteropAuditLog records the acknowledgement mutation
    async with db_manager.get_session_maker()() as session:
        stmt = select(InteropAuditLog).where(
            InteropAuditLog.action == "ACKNOWLEDGE_NOTIFICATION",
            InteropAuditLog.user_id == "david_subject",
        )
        res = await session.execute(stmt)
        log_entry = res.scalars().first()
        assert log_entry is not None
        assert log_entry.user_role == "Subject"
        assert log_entry.change_reason == "Acknowledging the pain scale reminder"
        assert f"acknowledged notification '{notif_id}'" in log_entry.details


@pytest.mark.asyncio
async def test_compute_reminders_by_subject_and_end_date_branch():
    """
    Test compute reminders using subject role authorization,
    and verifying fallback to assignment end_date when due_at is None.
    Also tests the delivery background task error boundaries.
    """
    client = TestClient(app)

    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Staff setup"
    )
    subject_eva_headers = get_auth_headers(
        roles="Subject", change_reason="Eva login", user_id="eva_subject"
    )
    subject_david_headers = get_auth_headers(
        roles="Subject", change_reason="David login", user_id="david_subject"
    )

    # 1. Create Instrument
    inst_payload = {
        "name": "Quality Survey",
        "description": "Eva symptom tracker.",
        "items": {"q1": "Any headache?"},
        "response_types": {"q1": {"type": "choice", "options": ["Yes", "No"]}},
        "scoring_metadata": {},
        "reason_for_change": "Initial survey",
    }
    resp = client.post(
        "/api/v1/interop/instruments", json=inst_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    inst_id = resp.json()["id"]

    # 2. Assign to Eva with no due_at, but end_date in the past (overdue/due)
    now_utc = datetime.now(timezone.utc).replace(tzinfo=None)
    assign_payload = {
        "subject_id": "eva_subject",
        "instrument_id": inst_id,
        "start_date": (now_utc - timedelta(days=2)).isoformat(),
        "end_date": (now_utc - timedelta(hours=1)).isoformat(),  # past end_date
        "due_at": None,
        "recurrence_pattern": "DAILY",
        "reason_for_change": "Eva assignment setup",
    }
    resp = client.post(
        "/api/v1/interop/assignments", json=assign_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    assign_id = resp.json()["id"]

    # 3. Compute reminders as Eva self -> 200 Success
    resp = client.post(
        "/api/v1/interop/reminders/compute",
        params={"subject_id": "eva_subject"},
        headers=subject_eva_headers,
    )
    assert resp.status_code == 200
    assert resp.json()["created_count"] == 4

    # Assert notifications are linked to assign_id
    resp_notifs = client.get(
        "/api/v1/interop/subjects/eva_subject/notifications",
        headers=subject_eva_headers,
    )
    assert resp_notifs.status_code == 200
    for n in resp_notifs.json():
        assert n["assignment_id"] == assign_id

    # 4. Compute reminders as David for Eva -> 403 Forbidden
    resp = client.post(
        "/api/v1/interop/reminders/compute",
        params={"subject_id": "eva_subject"},
        headers=subject_david_headers,
    )
    assert resp.status_code == 403

    # 5. Direct invocation of deliver_notification_task with fake ID
    from apps.interop.main import deliver_notification_task

    await deliver_notification_task("fake-id", "EMAIL", "eva_subject")

    # 6. Test subject with no assignments return empty list
    resp = client.get(
        "/api/v1/interop/subjects/eva_subject/instruments",
        headers=subject_eva_headers,
    )
    # Since eva has one assignment, she should get 1 instrument
    assert len(resp.json()) == 1

    # But subject_david has 0 assignments (we didn't assign anything to him in this test)
    resp = client.get(
        "/api/v1/interop/subjects/david_subject/instruments",
        headers=subject_david_headers,
    )
    assert resp.status_code == 200
    assert resp.json() == []


@pytest.mark.asyncio
async def test_deliver_notification_task_exception(monkeypatch):
    """
    Test the exception/error handler of the background notification delivery task.
    """
    import sqlalchemy

    from apps.interop.main import deliver_notification_task

    def mock_select(*args, **kwargs):
        raise Exception("Simulated database failure during select")

    monkeypatch.setattr(sqlalchemy, "select", mock_select)

    # Calling deliver_notification_task should trigger both outer and nested exception handlers
    await deliver_notification_task("some-id", "EMAIL", "eva_subject")


@pytest.mark.asyncio
async def test_compute_reminders_all_subjects_staff():
    """
    Test compute reminders for all subjects simultaneously (subject_id=None)
    triggered by a staff member.
    """
    client = TestClient(app)
    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Compute all reminders"
    )

    # Author an Instrument and assign to a new subject with past due_at
    # to ensure there is something to compute.
    resp = client.post(
        "/api/v1/interop/instruments",
        json={
            "name": "Global Survey",
            "description": "Global tracker.",
            "items": {"q1": "status"},
            "response_types": {"q1": {"type": "integer"}},
            "scoring_metadata": {},
            "reason_for_change": "Initial global instrument",
        },
        headers=staff_headers,
    )
    assert resp.status_code == 201
    inst_id = resp.json()["id"]

    now_utc = datetime.now(timezone.utc).replace(tzinfo=None)
    client.post(
        "/api/v1/interop/assignments",
        json={
            "subject_id": "global_subject_1",
            "instrument_id": inst_id,
            "start_date": (now_utc - timedelta(days=2)).isoformat(),
            "end_date": (now_utc + timedelta(days=1)).isoformat(),
            "due_at": (now_utc - timedelta(hours=1)).isoformat(),
            "recurrence_pattern": "DAILY",
            "reason_for_change": "Global setup",
        },
        headers=staff_headers,
    )

    # Compute reminders with subject_id=None
    resp = client.post(
        "/api/v1/interop/reminders/compute",
        headers=staff_headers,
    )
    assert resp.status_code == 200
    assert resp.json()["created_count"] >= 4
