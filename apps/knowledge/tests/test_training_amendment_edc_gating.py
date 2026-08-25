"""Comprehensive test suite for Protocol Amendment Training Curricula, Interactive Quizzes, Part 11 Electronic Signatures, and Gating.

Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
"""

from __future__ import annotations

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.domain.training_models import (
    AssignmentStatus,
)
from apps.knowledge.infrastructure.models import Base
from apps.knowledge.infrastructure.training_models import (
    TrainingAssignment,
    TrainingAuditLog,
)
from apps.knowledge.main import app
from apps.knowledge.services.training_service import TrainingService


@pytest_asyncio.fixture
async def training_db_session():
    """Create isolated in-memory SQLite database for training tests."""
    engine = create_async_engine("sqlite+aiosqlite:///:memory:", echo=False)
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_factory = async_sessionmaker(
        engine, class_=AsyncSession, expire_on_commit=False
    )
    async with session_factory() as session:
        yield session
        await session.rollback()

    await engine.dispose()


@pytest_asyncio.fixture
async def client(training_db_session: AsyncSession):
    """Async HTTP test client with database override."""

    async def override_get_db():
        yield training_db_session

    app.dependency_overrides[get_db_session] = override_get_db

    from packages.testing.security import create_test_auth_headers

    headers = create_test_auth_headers(
        user_id="test_investigator_01",
        roles=["site_investigator", "crc", "sponsor_designer", "sponsor_admin"],
    )
    transport = ASGITransport(app=app)
    async with AsyncClient(
        transport=transport, base_url="http://test", headers=headers
    ) as ac:
        yield ac

    app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_protocol_amendment_delta_curriculum_generation(
    training_db_session: AsyncSession,
):
    """Test generating delta-training curriculum and procedural modules from amendment diffs.

    Requirements: PRD-TRN-001
    """
    service = TrainingService()
    baseline = {
        "study_id": "STUDY-ONC-2026",
        "version": "1.0.0",
        "activities": [
            {"id": "ACT-01", "name": "Informed Consent", "description": "Baseline ICF"},
            {"id": "ACT-02", "name": "Vital Signs", "description": "BP and Heart Rate"},
        ],
    }
    amended = {
        "study_id": "STUDY-ONC-2026",
        "version": "2.0.0",
        "activities": [
            {"id": "ACT-01", "name": "Informed Consent", "description": "Baseline ICF"},
            {
                "id": "ACT-02",
                "name": "Vital Signs & ECG",
                "description": "BP, HR, and 12-lead ECG calibration",
            },
            {
                "id": "ACT-03",
                "name": "Pharmacokinetic Blood Draw",
                "description": "Serial PK sampling at 0h, 2h, 4h",
            },
        ],
    }

    curriculum = await service.generate_amendment_curriculum(
        session=training_db_session,
        study_id="STUDY-ONC-2026",
        baseline_snapshot=baseline,
        amended_snapshot=amended,
        protocol_version="2.0.0",
        created_by="lead_designer_01",
        reason_for_change="Protocol Amendment v2.0 delta curriculum creation",
        site_assignments=[
            {
                "site_id": "SITE-101",
                "user_id": "inv_dr_smith",
                "user_name": "Dr. Smith",
                "user_role": "Principal Investigator",
            }
        ],
    )

    assert curriculum.id.startswith("trn_curr_")
    assert curriculum.study_id == "STUDY-ONC-2026"
    assert curriculum.protocol_version == "2.0.0"
    assert curriculum.is_mandatory is True
    assert curriculum.passing_score == 0.80

    # Verify generated delta modules
    assert (
        len(curriculum.modules) >= 2
    )  # Added PK blood draw + Modified Vital Signs & ECG
    mod_titles = [m.title for m in curriculum.modules]
    assert any("Pharmacokinetic Blood Draw" in t for t in mod_titles)
    assert any("Vital Signs & ECG" in t or "Modified" in t for t in mod_titles)

    # Verify comprehension quiz questions were automatically constructed
    total_questions = sum(len(m.quiz_questions) for m in curriculum.modules)
    assert total_questions >= 2

    # Verify auto-assignment
    stmt = select(TrainingAssignment).where(
        TrainingAssignment.curriculum_id == curriculum.id
    )
    res = await training_db_session.execute(stmt)
    assignments = res.scalars().all()
    assert len(assignments) == 1
    assert assignments[0].user_id == "inv_dr_smith"
    assert assignments[0].status == AssignmentStatus.PENDING.value


@pytest.mark.asyncio
async def test_time_tracking_and_quiz_scoring_threshold(
    training_db_session: AsyncSession,
):
    """Test interactive time-on-page tracking and strict >= 80% comprehension quiz evaluation.

    Requirements: PRD-TRN-003
    """
    service = TrainingService()
    baseline = {"study_id": "STUDY-101", "version": "1.0", "activities": []}
    amended = {
        "study_id": "STUDY-101",
        "version": "2.0",
        "activities": [
            {
                "id": "ACT-NEW",
                "name": "Genomic Biopsy",
                "description": "Tumor tissue biopsy",
            }
        ],
    }

    curriculum = await service.generate_amendment_curriculum(
        session=training_db_session,
        study_id="STUDY-101",
        baseline_snapshot=baseline,
        amended_snapshot=amended,
        protocol_version="2.0",
        created_by="admin",
    )

    assignment = await service.assign_curriculum(
        session=training_db_session,
        curriculum_id=curriculum.id,
        study_id="STUDY-101",
        site_id="SITE-A",
        user_id="user_crc_01",
        user_name="Sarah Connor",
        user_role="Study Coordinator",
        created_by="admin",
    )

    # 1. Record time-on-page progress
    await service.record_progress(
        session=training_db_session,
        assignment_id=assignment.id,
        user_id="user_crc_01",
        time_spent_delta=45,
    )
    assert assignment.time_spent_seconds == 45
    assert assignment.status == AssignmentStatus.IN_PROGRESS.value

    # 2. Submit failing quiz attempt (all wrong answers)
    questions = []
    for m in curriculum.modules:
        questions.extend(m.quiz_questions)

    wrong_answers = {q["id"]: 99 for q in questions}
    failing_result = await service.evaluate_and_submit_quiz(
        session=training_db_session,
        assignment_id=assignment.id,
        user_id="user_crc_01",
        answers=wrong_answers,
    )
    assert failing_result.passed is False
    assert failing_result.score_percentage == 0.0
    assert assignment.status == AssignmentStatus.FAILED.value
    assert assignment.attempts_count == 1

    # 3. Submit passing quiz attempt (correct answers >= 80%)
    correct_answers = {q["id"]: q["correct_option_index"] for q in questions}
    passing_result = await service.evaluate_and_submit_quiz(
        session=training_db_session,
        assignment_id=assignment.id,
        user_id="user_crc_01",
        answers=correct_answers,
    )
    assert passing_result.passed is True
    assert passing_result.score_percentage == 1.0
    assert assignment.quiz_score == 1.0
    assert assignment.attempts_count == 2


@pytest.mark.asyncio
async def test_part11_signature_certificate_and_vault_routing(
    training_db_session: AsyncSession,
):
    """Test 21 CFR Part 11 electronic signature sign-off, SHA-256 seal, PDF certificate, and eTMF/eISF routing.

    Requirements: PRD-TRN-003, PRD-TRN-004
    """
    service = TrainingService()
    baseline = {"study_id": "STUDY-CARDIO-01", "version": "1.0", "activities": []}
    amended = {
        "study_id": "STUDY-CARDIO-01",
        "version": "2.0",
        "activities": [
            {
                "id": "ACT-DOSE",
                "name": "Titration Dosing",
                "description": "Modified step-up dosing",
            }
        ],
    }

    curriculum = await service.generate_amendment_curriculum(
        session=training_db_session,
        study_id="STUDY-CARDIO-01",
        baseline_snapshot=baseline,
        amended_snapshot=amended,
        protocol_version="2.0",
        created_by="admin",
    )

    assignment = await service.assign_curriculum(
        session=training_db_session,
        curriculum_id=curriculum.id,
        study_id="STUDY-CARDIO-01",
        site_id="SITE-55",
        user_id="pi_cardio_dr_lee",
        user_name="Dr. Kevin Lee, MD",
        user_role="Principal Investigator",
        created_by="admin",
    )

    # Pass the quiz
    questions = []
    for m in curriculum.modules:
        questions.extend(m.quiz_questions)
    answers = {q["id"]: q["correct_option_index"] for q in questions}
    await service.evaluate_and_submit_quiz(
        session=training_db_session,
        assignment_id=assignment.id,
        user_id="pi_cardio_dr_lee",
        answers=answers,
    )

    # Execute 21 CFR Part 11 Digital Sign-off
    cert = await service.sign_and_complete_training(
        session=training_db_session,
        assignment_id=assignment.id,
        user_id="pi_cardio_dr_lee",
        user_name="Dr. Kevin Lee, MD",
        user_role="Principal Investigator",
        meaning="I certify that I have reviewed, understood, and agree to execute clinical procedures in accordance with approved Protocol Version 2.0.",
    )

    assert cert.id.startswith("cert_")
    assert cert.user_id == "pi_cardio_dr_lee"
    assert cert.protocol_version == "2.0"
    assert cert.score_achieved == 1.0
    assert cert.sha256_seal is not None and len(cert.sha256_seal) == 64
    assert cert.pdf_base64 is not None
    assert cert.signature_manifestation["signer_name"] == "Dr. Kevin Lee, MD"
    assert cert.signature_manifestation["signer_role"] == "Principal Investigator"

    # Verify assignment status updated to COMPLETED
    assert assignment.status == AssignmentStatus.COMPLETED.value
    assert assignment.completed_at is not None
    assert assignment.certificate_id == cert.id

    # Verify immutable audit log records
    stmt = select(TrainingAuditLog).where(
        TrainingAuditLog.study_id == "STUDY-CARDIO-01"
    )
    res = await training_db_session.execute(stmt)
    logs = res.scalars().all()
    actions = [log.action for log in logs]
    assert "SIGNATURE_CAPTURED" in actions
    assert "TRAINING_COMPLETED" in actions


@pytest.mark.asyncio
async def test_edc_write_qualification_gating_lifecycle(
    training_db_session: AsyncSession,
):
    """Test automated EDC qualification gating: blocked while training is pending, instantly cleared upon completion.

    Requirements: PRD-TRN-002
    """
    service = TrainingService()
    baseline = {"study_id": "STUDY-IMMUNO-99", "version": "1.0", "activities": []}
    amended = {
        "study_id": "STUDY-IMMUNO-99",
        "version": "3.0",
        "activities": [
            {
                "id": "ACT-CAR-T",
                "name": "Cell Infusion",
                "description": "Autologous CAR-T cell infusion",
            }
        ],
    }

    curriculum = await service.generate_amendment_curriculum(
        session=training_db_session,
        study_id="STUDY-IMMUNO-99",
        baseline_snapshot=baseline,
        amended_snapshot=amended,
        protocol_version="3.0",
        created_by="admin",
    )

    # 1. Check qualification before assignment -> BLOCKED (mandatory curriculum exists with no completed assignment)
    gate_1 = await service.verify_qualification_gating(
        session=training_db_session,
        user_id="nurse_john",
        study_id="STUDY-IMMUNO-99",
        protocol_version="3.0",
        site_id="SITE-01",
    )
    assert gate_1.is_qualified is False
    assert "TrainingRequiredError" in (gate_1.blocked_reason or "")
    assert len(gate_1.pending_curricula) == 1

    # 2. Assign curriculum -> Still BLOCKED (status PENDING)
    assignment = await service.assign_curriculum(
        session=training_db_session,
        curriculum_id=curriculum.id,
        study_id="STUDY-IMMUNO-99",
        site_id="SITE-01",
        user_id="nurse_john",
        user_name="John Doe, RN",
        user_role="Study Coordinator",
        created_by="admin",
    )
    gate_2 = await service.verify_qualification_gating(
        session=training_db_session,
        user_id="nurse_john",
        study_id="STUDY-IMMUNO-99",
        protocol_version="3.0",
    )
    assert gate_2.is_qualified is False

    # 3. Complete Quiz and Sign Off -> INSTANTLY QUALIFIED (UNLOCKED)
    questions = []
    for m in curriculum.modules:
        questions.extend(m.quiz_questions)
    answers = {q["id"]: q["correct_option_index"] for q in questions}
    await service.evaluate_and_submit_quiz(
        session=training_db_session,
        assignment_id=assignment.id,
        user_id="nurse_john",
        answers=answers,
    )
    await service.sign_and_complete_training(
        session=training_db_session,
        assignment_id=assignment.id,
        user_id="nurse_john",
        user_name="John Doe, RN",
        user_role="Study Coordinator",
    )

    gate_3 = await service.verify_qualification_gating(
        session=training_db_session,
        user_id="nurse_john",
        study_id="STUDY-IMMUNO-99",
        protocol_version="3.0",
    )
    assert gate_3.is_qualified is True
    assert gate_3.blocked_reason is None
    assert len(gate_3.pending_curricula) == 0


@pytest.mark.asyncio
async def test_rest_api_training_endpoints(client: AsyncClient):
    """Test full REST API lifecycle for training curricula, quizzes, and certificates.

    Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
    """
    # 1. Generate delta curriculum via API
    gen_res = await client.post(
        "/api/v1/training/curricula/generate-amendment",
        json={
            "study_id": "STUDY-REST-01",
            "protocol_version": "2.0",
            "baseline_snapshot": {
                "study_id": "STUDY-REST-01",
                "version": "1.0",
                "activities": [],
            },
            "amended_snapshot": {
                "study_id": "STUDY-REST-01",
                "version": "2.0",
                "activities": [
                    {
                        "id": "ACT-A",
                        "name": "Echo Imaging",
                        "description": "Echocardiogram assessment",
                    }
                ],
            },
            "reason_for_change": "REST API amendment curriculum test",
        },
    )
    assert gen_res.status_code == 201
    curr_data = gen_res.json()
    curr_id = curr_data["id"]
    assert curr_data["study_id"] == "STUDY-REST-01"
    assert len(curr_data["modules"]) >= 1

    # 2. List curricula
    list_res = await client.get(
        "/api/v1/training/curricula", params={"study_id": "STUDY-REST-01"}
    )
    assert list_res.status_code == 200
    assert len(list_res.json()) >= 1

    # 3. Assign curriculum
    assign_res = await client.post(
        f"/api/v1/training/curricula/{curr_id}/assign",
        json={
            "study_id": "STUDY-REST-01",
            "site_id": "SITE-01",
            "user_id": "test_investigator_01",
            "user_name": "Test Investigator",
            "user_role": "Principal Investigator",
            "reason_for_change": "API test assignment",
        },
    )
    assert assign_res.status_code == 201
    asn_data = assign_res.json()
    asn_id = asn_data["id"]

    # 4. Check initial gating status via API -> BLOCKED
    gate_check_1 = await client.get(
        "/api/v1/training/gating-status",
        params={
            "user_id": "test_investigator_01",
            "study_id": "STUDY-REST-01",
            "protocol_version": "2.0",
        },
    )
    assert gate_check_1.status_code == 200
    assert gate_check_1.json()["is_qualified"] is False

    # 5. Record reading progress
    prog_res = await client.post(
        f"/api/v1/training/assignments/{asn_id}/progress",
        json={"time_spent_delta_seconds": 30, "reason_for_change": "Reading progress"},
    )
    assert prog_res.status_code == 200
    assert prog_res.json()["time_spent_seconds"] >= 30

    # 6. Submit comprehension quiz
    all_answers = {}
    for m in curr_data["modules"]:
        for q in m.get("quiz_questions", []):
            all_answers[q["id"]] = q["correct_option_index"]

    quiz_res = await client.post(
        f"/api/v1/training/assignments/{asn_id}/submit-quiz",
        json={"answers": all_answers, "reason_for_change": "Quiz submission"},
    )
    assert quiz_res.status_code == 200
    assert quiz_res.json()["passed"] is True

    # 7. Part 11 Sign-off
    import time
    import uuid

    from jose import jwt

    from packages.testing.security import create_test_auth_headers

    sig_payload = {
        "sub": "test_investigator_01",
        "username": "test_investigator_01",
        "action": f"/api/v1/training/assignments/{asn_id}/sign-off",
        "roles": ["site_investigator", "crc", "sponsor_designer", "sponsor_admin"],
        "iat": time.time(),
        "exp": time.time() + 300.0,
        "jti": str(uuid.uuid4()),
    }
    sig_token = jwt.encode(
        sig_payload, "valid-test-gateway-secret-key-2026", algorithm="HS256"
    )
    sign_headers = create_test_auth_headers(
        user_id="test_investigator_01",
        roles=["site_investigator", "crc", "sponsor_designer", "sponsor_admin"],
        sig_token=sig_token,
    )

    sign_res = await client.post(
        f"/api/v1/training/assignments/{asn_id}/sign-off",
        json={
            "signer_name": "Dr. Test Investigator, MD",
            "signer_role": "Principal Investigator",
            "meaning": "I certify that I have reviewed, understood, and agree to execute clinical procedures in accordance with this approved protocol curriculum.",
            "reason_for_change": "Part 11 sign-off API test",
        },
        headers=sign_headers,
    )
    assert sign_res.status_code == 201
    cert_data = sign_res.json()
    assert cert_data["sha256_seal"] is not None
    assert cert_data["score_achieved"] >= 0.80

    # 8. Check gating status via API -> UNLOCKED
    gate_check_2 = await client.get(
        "/api/v1/training/gating-status",
        params={
            "user_id": "test_investigator_01",
            "study_id": "STUDY-REST-01",
            "protocol_version": "2.0",
        },
    )
    assert gate_check_2.status_code == 200
    assert gate_check_2.json()["is_qualified"] is True
