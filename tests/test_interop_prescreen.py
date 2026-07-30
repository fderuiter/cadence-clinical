import time
from datetime import datetime
from unittest.mock import AsyncMock, patch

import pytest
import pytest_asyncio
from eligibility import EligibilityCriterion, parse_dsl
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.interop.database import db_manager
from apps.interop.fhir_adapter import FHIRAdapter, pseudonymize_identifier
from apps.interop.main import app
from apps.interop.models import Base, InteropAuditLog


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


def test_build_ecrf_context_mapping():
    """
    Assert the projection produces expected eCRF.<DOMAIN>.<VARIABLE> keys/values
    for a representative Bundle, including AGE derivation and vitals/labs/conditions/meds mapping.
    """
    adapter = FHIRAdapter("study_123")

    parsed = {
        "study_id": "study_123",
        "subject_pseudonym": "pseudonym_123",
        "mapped_fields": {
            "DM.USUBJID": "study_123-pseudonym_123",
            "DM.SUBJID": "pseudonym_123",
            "DM.BRTHDTC": "1995-10-24",
            "DM.SEX": "F",
        },
        "clinical_records": {
            "vital_signs": [
                {"cdash_testcd": "SYSBP", "value": 118},
                {"cdash_testcd": "DIABP", "value": 76},
            ],
            "labs": [{"cdash_testcd": "GLUC", "value": 5.4}],
            "conditions": [{"display_name": "Asthma"}],
            "medications": [{"display_name": "Albuterol"}],
        },
    }

    context = adapter.build_ecrf_context(parsed)

    # 1. Sex
    assert context["eCRF.DM.SEX"] == "F"

    # 2. Derived Age
    current_year = datetime.now().year
    expected_age = current_year - 1995
    assert context["eCRF.DM.AGE"] == expected_age

    # 3. Vital signs
    assert context["eCRF.VS.SYSBP"] == 118
    assert context["eCRF.VS.DIABP"] == 76

    # 4. Labs
    assert context["eCRF.LB.GLUC"] == 5.4

    # 5. Conditions and medications (single values)
    assert context["eCRF.MH.MHTERM"] == "Asthma"
    assert context["eCRF.CM.CMTRT"] == "Albuterol"


def test_build_ecrf_context_multiple_and_missing():
    """
    Verify multiple conditions/medications are parsed into lists and missing resources
    are gracefully omitted from the context.
    """
    adapter = FHIRAdapter("study_123")

    parsed = {
        "study_id": "study_123",
        "subject_pseudonym": "pseudonym_123",
        "mapped_fields": {"DM.SEX": "M"},
        "clinical_records": {
            "vital_signs": [],
            "labs": [],
            "conditions": [
                {"display_name": "Hypertension"},
                {"display_name": "Type 2 Diabetes"},
            ],
            "medications": [
                {"display_name": "Metformin"},
                {"display_name": "Lisinopril"},
            ],
        },
    }

    context = adapter.build_ecrf_context(parsed)

    # Missing fields must be omitted
    assert "eCRF.DM.AGE" not in context
    assert "eCRF.VS.SYSBP" not in context
    assert "eCRF.LB.GLUC" not in context

    # Sex is present
    assert context["eCRF.DM.SEX"] == "M"

    # Multiple elements should form lists
    assert context["eCRF.MH.MHTERM"] == ["Hypertension", "Type 2 Diabetes"]
    assert context["eCRF.CM.CMTRT"] == ["Metformin", "Lisinopril"]


@pytest.mark.asyncio
@patch("apps.interop.main.fetch_eligibility_criteria", new_callable=AsyncMock)
async def test_pre_screen_eligible(mock_fetch):
    """
    Test pre-screen endpoint for an eligible scenario.
    """
    # Setup mock criteria
    audit_args = {
        "created_by": "designer-system",
        "reason_for_change": "Initial eligibility rules.",
    }
    mock_fetch.return_value = [
        EligibilityCriterion(
            criterion_id="INC01",
            criterion_type="inclusion",
            description="Subject must be 18 years of age or older.",
            dsl_source="eCRF.DM.AGE >= 18",
            condition=parse_dsl("eCRF.DM.AGE >= 18"),
            expected_outcome=True,
            **audit_args,
        ),
        EligibilityCriterion(
            criterion_id="INC02",
            criterion_type="inclusion",
            description="Subject must be female.",
            dsl_source="eCRF.DM.SEX == 'F'",
            condition=parse_dsl("eCRF.DM.SEX == 'F'"),
            expected_outcome=True,
            **audit_args,
        ),
    ]

    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Pre-screening test"
    )

    # Patient born 2000 (age is >= 24) and Female
    mock_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Patient",
                    "id": "EHR-PATIENT-1",
                    "gender": "female",
                    "birthDate": "2000-01-01",
                }
            }
        ],
    }

    req_payload = {"study_id": "study_abc", "bundle": mock_bundle}

    resp = client.post(
        "/api/v1/interop/fhir/pre-screen", json=req_payload, headers=headers
    )
    assert resp.status_code == 200
    data = resp.json()

    assert data["eligible"] is True
    assert len(data["failed_criteria"]) == 0
    assert len(data["indeterminate_criteria"]) == 0

    explanations = data["criteria_explanations"]
    assert len(explanations) == 2
    assert explanations[0]["criterion_id"] == "INC01"
    assert explanations[0]["is_met"] is True
    assert explanations[0]["is_indeterminate"] is False


@pytest.mark.asyncio
@patch("apps.interop.main.fetch_eligibility_criteria", new_callable=AsyncMock)
async def test_pre_screen_ineligible(mock_fetch):
    """
    Test pre-screen endpoint for an ineligible scenario (one criterion fails).
    """
    audit_args = {
        "created_by": "designer-system",
        "reason_for_change": "Initial eligibility rules.",
    }
    mock_fetch.return_value = [
        EligibilityCriterion(
            criterion_id="INC01",
            criterion_type="inclusion",
            description="Subject must be 18 years of age or older.",
            dsl_source="eCRF.DM.AGE >= 18",
            condition=parse_dsl("eCRF.DM.AGE >= 18"),
            expected_outcome=True,
            **audit_args,
        )
    ]

    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Pre-screening test"
    )

    # Patient born 2015 (underage)
    mock_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Patient",
                    "id": "EHR-PATIENT-2",
                    "gender": "male",
                    "birthDate": "2015-01-01",
                }
            }
        ],
    }

    req_payload = {"study_id": "study_abc", "bundle": mock_bundle}

    resp = client.post(
        "/api/v1/interop/fhir/pre-screen", json=req_payload, headers=headers
    )
    assert resp.status_code == 200
    data = resp.json()

    assert data["eligible"] is False
    assert "INC01" in data["failed_criteria"]
    assert len(data["indeterminate_criteria"]) == 0


@pytest.mark.asyncio
@patch("apps.interop.main.fetch_eligibility_criteria", new_callable=AsyncMock)
async def test_pre_screen_indeterminate(mock_fetch):
    """
    Test pre-screen endpoint for an indeterminate scenario (missing birthDate).
    """
    audit_args = {
        "created_by": "designer-system",
        "reason_for_change": "Initial eligibility rules.",
    }
    mock_fetch.return_value = [
        EligibilityCriterion(
            criterion_id="INC01",
            criterion_type="inclusion",
            description="Subject must be 18 years of age or older.",
            dsl_source="eCRF.DM.AGE >= 18",
            condition=parse_dsl("eCRF.DM.AGE >= 18"),
            expected_outcome=True,
            **audit_args,
        )
    ]

    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Pre-screening test"
    )

    # Patient bundle with no birthDate
    mock_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Patient",
                    "id": "EHR-PATIENT-3",
                    "gender": "male",
                }
            }
        ],
    }

    req_payload = {"study_id": "study_abc", "bundle": mock_bundle}

    resp = client.post(
        "/api/v1/interop/fhir/pre-screen", json=req_payload, headers=headers
    )
    assert resp.status_code == 200
    data = resp.json()

    assert data["eligible"] is None
    assert len(data["failed_criteria"]) == 0
    assert "INC01" in data["indeterminate_criteria"]

    explanations = data["criteria_explanations"]
    assert explanations[0]["is_indeterminate"] is True


@pytest.mark.asyncio
@patch("apps.interop.main.fetch_eligibility_criteria", new_callable=AsyncMock)
async def test_pre_screen_audit_evidence_non_phi(mock_fetch):
    """
    Assert an InteropAuditLog row with the new FHIR_PRESCREEN action is
    written and its details contain no PHI (no names, raw demographics, or clinical values).
    """
    audit_args = {
        "created_by": "designer-system",
        "reason_for_change": "Initial eligibility rules.",
    }
    mock_fetch.return_value = [
        EligibilityCriterion(
            criterion_id="INC01",
            criterion_type="inclusion",
            description="Subject must be 18.",
            dsl_source="eCRF.DM.AGE >= 18",
            condition=parse_dsl("eCRF.DM.AGE >= 18"),
            expected_outcome=True,
            **audit_args,
        )
    ]

    client = TestClient(app)
    headers = get_auth_headers(roles="admin,sponsor_dm", change_reason="Audit check")

    mock_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Patient",
                    "id": "EHR-PATIENT-4",
                    "gender": "male",
                    "birthDate": "2000-01-01",
                }
            }
        ],
    }

    req_payload = {"study_id": "study_abc", "bundle": mock_bundle}

    resp = client.post(
        "/api/v1/interop/fhir/pre-screen", json=req_payload, headers=headers
    )
    assert resp.status_code == 200

    # Retrieve audit log entry
    async with db_manager.get_session_maker()() as session:
        stmt = select(InteropAuditLog).where(InteropAuditLog.action == "FHIR_PRESCREEN")
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) == 1
        log = logs[0]

        # Verify it has absolutely no PHI
        assert "EHR-PATIENT-4" not in log.details  # de-identified pseudonym only
        assert "male" not in log.details
        assert "2000-01-01" not in log.details

        pseudonym = pseudonymize_identifier("EHR-PATIENT-4")
        assert pseudonym in log.details
        assert "Criteria evaluated: 1 total" in log.details
        assert "Aggregate Outcome: True" in log.details


def test_no_edc_mutation_boundary():
    """
    Assert the endpoint neither imports nor invokes Execution subject lifecycle
    code and creates no ClinicalSubject-equivalent records.
    """
    # Read the imports of apps/interop/main.py and apps/interop/fhir_adapter.py
    # to ensure no apps.execution database models are imported.
    with open("apps/interop/main.py", "r") as f:
        content = f.read()
        assert "apps.execution" not in content
        assert "ClinicalSubject" not in content

    with open("apps/interop/fhir_adapter.py", "r") as f:
        content = f.read()
        assert "apps.execution" not in content
        assert "ClinicalSubject" not in content
