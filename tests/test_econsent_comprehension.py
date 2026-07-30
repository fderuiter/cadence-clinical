import pytest_asyncio
from fastapi.testclient import TestClient

from apps.econsent.database import db_manager
from apps.econsent.main import app
from apps.econsent.models import Base
from tests.test_econsent import get_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_comprehension_db():
    """
    Setup in-memory eConsent database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def test_create_and_retrieve_comprehension_check():
    """
    Test creating and retrieving comprehension checks for a template version.
    """
    client = TestClient(app)

    # 1. First pre-create a template
    template_payload = {
        "template_id": "tpl-comp-1",
        "study_id": "study-comp-test",
        "template_name": "Informed Consent Form",
        "protocol_version": "v1.0",
        "requires_reconsent": True,
        "clauses": [],
        "workflow_steps": [
            {"type": "comprehension_check"},
            {"type": "signature_placeholder"},
        ],
        "reason_for_change": "Initial template creation",
        "created_by": "admin_user",
    }
    headers = get_auth_headers(user_id="designer_user", roles="Grants Manager")
    res_tpl = client.post(
        "/api/v1/econsent/templates", json=template_payload, headers=headers
    )
    assert res_tpl.status_code == 201

    # 2. Add a comprehension check definition
    check_payload = {
        "questions": [
            {"id": "q1", "text": "What is the study purpose?", "options": ["A", "B"]},
            {"id": "q2", "text": "Can you withdraw?", "options": ["Yes", "No"]},
        ],
        "expected_answers": {"q1": "A", "q2": "Yes"},
        "threshold_policy": {"min_correct": 2},
        "reason_for_change": "Defining questions for template check",
        "created_by": "designer_user",
    }

    res_check = client.post(
        "/api/v1/econsent/templates/tpl-comp-1/versions/1/comprehension-checks",
        json=check_payload,
        headers=headers,
    )
    assert res_check.status_code == 201
    data = res_check.json()
    assert data["template_id"] == "tpl-comp-1"
    assert data["version_index"] == 1
    assert len(data["questions"]) == 2
    assert data["expected_answers"]["q1"] == "A"
    assert data["threshold_policy"]["min_correct"] == 2

    # 3. Retrieve the check definition
    res_get = client.get(
        "/api/v1/econsent/templates/tpl-comp-1/versions/1/comprehension-checks",
        headers=headers,
    )
    assert res_get.status_code == 200
    data_get = res_get.json()
    assert data_get["template_id"] == "tpl-comp-1"
    assert len(data_get["questions"]) == 2


def test_submit_answers_and_evaluation_boundaries():
    """
    Test submitting answers and evaluating them based on different threshold policy boundaries.
    """
    client = TestClient(app)
    headers = get_auth_headers(user_id="designer_user", roles="Grants Manager")

    # Pre-create template and check
    template_payload = {
        "template_id": "tpl-boundary",
        "study_id": "study-comp-test",
        "template_name": "Informed Consent Form",
        "protocol_version": "v1.0",
        "clauses": [],
        "workflow_steps": [
            {"type": "comprehension_check"},
            {"type": "signature_placeholder"},
        ],
        "reason_for_change": "Initial template",
        "created_by": "designer",
    }
    client.post("/api/v1/econsent/templates", json=template_payload, headers=headers)

    # 1. Scenario A: threshold_policy = {"min_correct": 2} (out of 3)
    check_payload = {
        "questions": [
            {"id": "q1", "text": "Q1"},
            {"id": "q2", "text": "Q2"},
            {"id": "q3", "text": "Q3"},
        ],
        "expected_answers": {"q1": "A", "q2": "B", "q3": "C"},
        "threshold_policy": {"min_correct": 2},
        "reason_for_change": "Setting up check",
        "created_by": "designer",
    }
    client.post(
        "/api/v1/econsent/templates/tpl-boundary/versions/1/comprehension-checks",
        json=check_payload,
        headers=headers,
    )

    # Submit 1 correct -> should fail
    sub_payload_1 = {
        "subject_pseudonym": "SUB-001",
        "submitted_answers": {"q1": "A", "q2": "Wrong", "q3": "Wrong"},
        "reason_for_change": "Submission 1",
    }
    res_sub_1 = client.post(
        "/api/v1/econsent/templates/tpl-boundary/versions/1/submit-answers",
        json=sub_payload_1,
        headers=headers,
    )
    assert res_sub_1.status_code == 200
    res_data_1 = res_sub_1.json()
    assert res_data_1["passed"] is False
    assert res_data_1["correct_count"] == 1
    assert res_data_1["min_required"] == 2
    assert res_data_1["next_step"] == "retry_checks"

    # Submit 2 correct -> should pass boundary exactly
    sub_payload_2 = {
        "subject_pseudonym": "SUB-001",
        "submitted_answers": {"q1": "A", "q2": "B", "q3": "Wrong"},
        "reason_for_change": "Submission 2",
    }
    res_sub_2 = client.post(
        "/api/v1/econsent/templates/tpl-boundary/versions/1/submit-answers",
        json=sub_payload_2,
        headers=headers,
    )
    assert res_sub_2.status_code == 200
    res_data_2 = res_sub_2.json()
    assert res_data_2["passed"] is True
    assert res_data_2["correct_count"] == 2
    assert res_data_2["next_step"] == "sign_consent"

    # 2. Scenario B: threshold_policy = {"passing_percentage": 66.0}
    check_payload_percentage = {
        "questions": [
            {"id": "q1", "text": "Q1"},
            {"id": "q2", "text": "Q2"},
            {"id": "q3", "text": "Q3"},
        ],
        "expected_answers": {"q1": "A", "q2": "B", "q3": "C"},
        "threshold_policy": {"passing_percentage": 66.0},
        "reason_for_change": "Updating threshold policy to percentage",
        "created_by": "designer",
    }
    client.post(
        "/api/v1/econsent/templates/tpl-boundary/versions/1/comprehension-checks",
        json=check_payload_percentage,
        headers=headers,
    )

    # Submit 2 correct (66.67%) -> should pass
    res_sub_pct = client.post(
        "/api/v1/econsent/templates/tpl-boundary/versions/1/submit-answers",
        json=sub_payload_2,
        headers=headers,
    )
    assert res_sub_pct.status_code == 200
    assert res_sub_pct.json()["passed"] is True


def test_signature_blocks_if_comprehension_checks_fail_or_incomplete():
    """
    Verify that subject signing is strictly blocked unless they have successfully passed the comprehension check
    defined for the exact template version.
    """
    client = TestClient(app)
    headers = get_auth_headers(user_id="designer_user", roles="Grants Manager")

    # Create template
    template_payload = {
        "template_id": "tpl-sign-test",
        "study_id": "study-comp-test",
        "template_name": "Informed Consent Form",
        "protocol_version": "v1.0",
        "clauses": [],
        "workflow_steps": [
            {"type": "comprehension_check"},
            {"type": "signature_placeholder"},
        ],
        "reason_for_change": "Initial template",
        "created_by": "designer",
    }
    client.post("/api/v1/econsent/templates", json=template_payload, headers=headers)

    # Create check
    check_payload = {
        "questions": [{"id": "q1", "text": "Q1"}],
        "expected_answers": {"q1": "A"},
        "threshold_policy": {"min_correct": 1},
        "reason_for_change": "Setting up check",
        "created_by": "designer",
    }
    client.post(
        "/api/v1/econsent/templates/tpl-sign-test/versions/1/comprehension-checks",
        json=check_payload,
        headers=headers,
    )

    # Attempt to sign immediately without answering check -> should fail with 400
    sig_payload = {
        "subject_pseudonym": "SUB-999",
        "signature_data": "My Signed Name",
        "reason_for_change": "I consent to clinical trial terms",
    }
    res_sign = client.post(
        "/api/v1/econsent/templates/tpl-sign-test/versions/1/sign",
        json=sig_payload,
        headers=headers,
    )
    assert res_sign.status_code == 400
    assert (
        "Comprehension checks have not been completed or passed"
        in res_sign.json()["detail"]
    )

    # Submit WRONG answers -> check fails
    sub_wrong = {
        "subject_pseudonym": "SUB-999",
        "submitted_answers": {"q1": "Wrong Answer"},
        "reason_for_change": "Check submission",
    }
    client.post(
        "/api/v1/econsent/templates/tpl-sign-test/versions/1/submit-answers",
        json=sub_wrong,
        headers=headers,
    )

    # Attempt to sign after fail -> should still fail
    res_sign_after_fail = client.post(
        "/api/v1/econsent/templates/tpl-sign-test/versions/1/sign",
        json=sig_payload,
        headers=headers,
    )
    assert res_sign_after_fail.status_code == 400

    # Submit CORRECT answers -> check passes
    sub_correct = {
        "subject_pseudonym": "SUB-999",
        "submitted_answers": {"q1": "A"},
        "reason_for_change": "Check submission correct",
    }
    res_sub_correct = client.post(
        "/api/v1/econsent/templates/tpl-sign-test/versions/1/submit-answers",
        json=sub_correct,
        headers=headers,
    )
    assert res_sub_correct.status_code == 200
    assert res_sub_correct.json()["passed"] is True

    # Attempt to sign after passing -> should succeed!
    res_sign_success = client.post(
        "/api/v1/econsent/templates/tpl-sign-test/versions/1/sign",
        json=sig_payload,
        headers=headers,
    )
    assert res_sign_success.status_code == 200
    assert res_sign_success.json()["subject_pseudonym"] == "SUB-999"


def test_template_version_separation():
    """
    Verify that comprehension results are strictly bound to the exact template version.
    Passing checks on version 1 should not allow signing version 2.
    """
    client = TestClient(app)
    headers = get_auth_headers(user_id="designer_user", roles="Grants Manager")

    # 1. Create template version 1
    tpl_payload = {
        "template_id": "tpl-versioned",
        "study_id": "study-versioned-test",
        "template_name": "Informed Consent Form",
        "protocol_version": "v1.0",
        "clauses": [],
        "workflow_steps": [
            {"type": "comprehension_check"},
            {"type": "signature_placeholder"},
        ],
        "reason_for_change": "V1",
        "created_by": "designer",
    }
    client.post("/api/v1/econsent/templates", json=tpl_payload, headers=headers)

    # Configure check for version 1
    check_payload_1 = {
        "questions": [{"id": "q1", "text": "Q1"}],
        "expected_answers": {"q1": "A"},
        "threshold_policy": {"min_correct": 1},
        "reason_for_change": "Check V1",
        "created_by": "designer",
    }
    client.post(
        "/api/v1/econsent/templates/tpl-versioned/versions/1/comprehension-checks",
        json=check_payload_1,
        headers=headers,
    )

    # 2. Create template version 2
    tpl_payload_2 = {
        "study_id": "study-versioned-test",
        "template_name": "Informed Consent Form New",
        "protocol_version": "v2.0",
        "clauses": [],
        "workflow_steps": [
            {"type": "comprehension_check"},
            {"type": "signature_placeholder"},
        ],
        "reason_for_change": "V2",
        "created_by": "designer",
    }
    client.put(
        "/api/v1/econsent/templates/tpl-versioned", json=tpl_payload_2, headers=headers
    )

    # Configure check for version 2
    check_payload_2 = {
        "questions": [{"id": "q1", "text": "Q1"}],
        "expected_answers": {"q1": "B"},  # Note correct answer changed to B
        "threshold_policy": {"min_correct": 1},
        "reason_for_change": "Check V2",
        "created_by": "designer",
    }
    client.post(
        "/api/v1/econsent/templates/tpl-versioned/versions/2/comprehension-checks",
        json=check_payload_2,
        headers=headers,
    )

    # 3. Subject passes check for version 1
    sub_payload_1 = {
        "subject_pseudonym": "SUB-123",
        "submitted_answers": {"q1": "A"},
        "reason_for_change": "Submit V1 answers",
    }
    res_sub_1 = client.post(
        "/api/v1/econsent/templates/tpl-versioned/versions/1/submit-answers",
        json=sub_payload_1,
        headers=headers,
    )
    assert res_sub_1.json()["passed"] is True

    # 4. Attempt to sign version 2 -> should be blocked!
    sig_payload = {
        "subject_pseudonym": "SUB-123",
        "signature_data": "My Signed Name",
        "reason_for_change": "Signing V2",
    }
    res_sign_2 = client.post(
        "/api/v1/econsent/templates/tpl-versioned/versions/2/sign",
        json=sig_payload,
        headers=headers,
    )
    assert res_sign_2.status_code == 400
    assert (
        "Comprehension checks have not been completed or passed"
        in res_sign_2.json()["detail"]
    )


def test_auditor_restrictions_on_checks():
    """
    Ensure users with auditor/inspector/regulatory_inspector roles cannot create or configure
    comprehension check definitions, returning HTTP 403.
    """
    client = TestClient(app)

    check_payload = {
        "questions": [{"id": "q1", "text": "Q1"}],
        "expected_answers": {"q1": "A"},
        "threshold_policy": {"min_correct": 1},
        "reason_for_change": "Unauthorized define",
        "created_by": "auditor",
    }

    headers_inspector = get_auth_headers(user_id="inspector_user", roles="inspector")
    res = client.post(
        "/api/v1/econsent/templates/tpl-comp-1/versions/1/comprehension-checks",
        json=check_payload,
        headers=headers_inspector,
    )
    assert res.status_code == 403
    assert "restricted to read-only access" in res.json()["detail"]
