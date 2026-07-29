import os
import pytest
import pytest_asyncio
import hashlib
import hmac
import time
import json
from datetime import datetime, date
from typing import Any, Dict, List
from unittest.mock import AsyncMock, patch

from sqlalchemy import select
from fastapi import HTTPException
from httpx import AsyncClient, ASGITransport

from apps.execution.database.context import (
    current_change_reason,
    current_session,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import (
    Base,
    ClinicalSubject,
    ClinicalObservation,
    AuditLog,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.designer_client import DesignerCriteriaClient, fetch_study_criteria
from apps.execution.eligibility_context import build_eligibility_context
from apps.execution.eligibility_service import (
    evaluate_subject_eligibility,
    verify_subject_eligible_for_randomization,
)
from apps.execution.main import app
from eligibility.models import EligibilityCriterion, ExpressionNode, FieldReference
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    from apps.execution.database.migrate import deploy_database_triggers

    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id="test_site_investigator",
    roles="site_investigator",
    change_reason="Performing screening evaluation",
):
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


def make_mock_criterion(
    criterion_id: str,
    criterion_type: str,
    raw_ref: str,
    operator: str,
    const_val: Any,
    expected_outcome: bool = True,
) -> Dict[str, Any]:
    # Construct a valid eligibility criterion dict representing condition: e.g. eCRF.DM.AGE >= 18
    # Match the pydantic schema of EligibilityCriterion
    domain, var = raw_ref.replace("eCRF.", "").split(".")
    return {
        "criterion_id": criterion_id,
        "criterion_type": criterion_type,
        "description": f"Mock check {criterion_id}",
        "dsl_source": f"{raw_ref} {operator} {const_val}",
        "condition": {
            "type": "comparison",
            "operator": operator,
            "operands": [
                {
                    "type": "field_ref",
                    "field_ref": {
                        "raw_reference": raw_ref,
                        "domain": domain,
                        "variable": var,
                    },
                },
                {"type": "constant", "value": const_val},
            ],
        },
        "expected_outcome": expected_outcome,
        "created_by": "designer",
        "reason_for_change": "Initial mock definition",
    }


# @req:PRD-ELIGIBILITY-008
@pytest.mark.asyncio
async def test_designer_criteria_client_retrieval_and_parsing():
    """Verify that the DesignerCriteriaClient retrieves and correctly parses EligibilityCriterion models."""
    mock_response_data = [
        make_mock_criterion("INC_01", "inclusion", "eCRF.DM.AGE", ">=", 18, True),
        make_mock_criterion("EXC_01", "exclusion", "eCRF.LB.ALT", ">", 150, False),
    ]

    # Mock the http call
    with patch("httpx.AsyncClient.get") as mock_get:
        from unittest.mock import MagicMock
        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.json.return_value = mock_response_data
        mock_get.return_value = mock_resp

        client = DesignerCriteriaClient()
        criteria = await client.get_eligibility_criteria("study_1")

        assert len(criteria) == 2
        assert isinstance(criteria[0], EligibilityCriterion)
        assert criteria[0].criterion_id == "INC_01"
        assert criteria[0].criterion_type == "inclusion"
        assert criteria[0].condition.operator == ">="
        assert criteria[0].condition.operands[0].field_ref.raw_reference == "eCRF.DM.AGE"

        assert isinstance(criteria[1], EligibilityCriterion)
        assert criteria[1].criterion_id == "EXC_01"
        assert criteria[1].criterion_type == "exclusion"


# @req:PRD-ELIGIBILITY-009
@pytest.mark.asyncio
async def test_ecrf_context_builder_demographics_and_precedence():
    """Verify demographics mapping, standard observation formatting, and repeated-observation precedence rules."""
    current_user_id.set("test_user")
    current_change_reason.set("Setup test context")

    async with db_manager.get_session_maker()() as session:
        # 1. Create a subject with encrypted demographics
        encrypted_demo = encrypt_demographics(
            {"birthdate": "2000-01-01", "gender": "Male"}
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-010",
            study_id="STUDY_XYZ",
            encrypted_demographics=encrypted_demo,
        )
        session.add(subj)
        await session.commit()

        # 2. Add repeated observations with different dates/versions to test precedence
        obs1 = ClinicalObservation(
            subject_id="SUBJ-010",
            study_id="STUDY_XYZ",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic BP",
            value=120.0,
            observation_date=datetime(2026, 1, 1, 10, 0),
            version=1,
        )
        # Newer date should win
        obs2 = ClinicalObservation(
            subject_id="SUBJ-010",
            study_id="STUDY_XYZ",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic BP",
            value=130.0,
            observation_date=datetime(2026, 1, 2, 10, 0),
            version=1,
        )
        # Same date as obs2, but higher version desc should win
        obs3 = ClinicalObservation(
            subject_id="SUBJ-010",
            study_id="STUDY_XYZ",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic BP",
            value=135.0,
            observation_date=datetime(2026, 1, 2, 10, 0),
            version=2,
        )

        session.add_all([obs1, obs2, obs3])
        await session.commit()

        # Build context
        context = await build_eligibility_context(subj, session)

        # Assert AGE computed correctly relative to observation date (approx 26 years)
        assert "eCRF.DM.AGE" in context
        assert context["eCRF.DM.AGE"] == 26
        assert context["eCRF.DM.SEX"] == "M"

        # Assert correct highest precedence observation wins
        assert "eCRF.VS.SYSBP" in context
        assert context["eCRF.VS.SYSBP"] == 135.0


# @req:PRD-ELIGIBILITY-010
@pytest.mark.asyncio
async def test_ecrf_context_builder_kleene_absent_semantics():
    """Verify that absent/null observations remain absent in context dictionary to support Kleene indeterminate semantics."""
    current_user_id.set("test_user")
    current_change_reason.set("Setup test context")

    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-011",
            study_id="STUDY_XYZ",
        )
        # Observation with null value
        obs_null = ClinicalObservation(
            subject_id="SUBJ-011",
            study_id="STUDY_XYZ",
            domain="LB",
            test_code="ALT",
            test_name="ALT",
            value=None,
            value_string=None,
            observation_date=datetime(2026, 1, 1),
        )
        session.add_all([subj, obs_null])
        await session.commit()

        context = await build_eligibility_context(subj, session)

        # eCRF.LB.ALT must not exist in context dict since it is missing/null
        assert "eCRF.LB.ALT" not in context
        assert "eCRF.DM.AGE" not in context


# @req:PRD-ELIGIBILITY-011
@pytest.mark.asyncio
async def test_screening_endpoint_eligible_and_transition():
    """Verify POST screening endpoint transitions eligible subjects to ENROLLED state and returns non-PHI payload."""
    current_user_id.set("investigator_1")
    current_change_reason.set("Test screening workflow")

    async with db_manager.get_session_maker()() as session:
        encrypted_demo = encrypt_demographics(
            {"birthdate": "2000-01-01", "gender": "Female"}
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-100",
            study_id="STUDY_XYZ",
            encrypted_demographics=encrypted_demo,
            status="SCREENING",
        )
        session.add(subj)
        await session.commit()

    # Mock criteria from designer service
    mock_criteria = [
        EligibilityCriterion(**make_mock_criterion("INC_01", "inclusion", "eCRF.DM.AGE", ">=", 18, True))
    ]

    with patch("apps.execution.eligibility_service.fetch_study_criteria", return_value=mock_criteria):
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            res = await client.post(
                "/api/v1/execution/subjects/SUBJ-100/screening",
                json={"study_id": "STUDY_XYZ"},
                headers=get_auth_headers(),
            )
            assert res.status_code == 200
            data = res.json()
            assert data["eligible"] is True
            assert len(data["failed_criteria"]) == 0
            assert len(data["indeterminate_criteria"]) == 0
            assert data["criterion_evaluations"][0]["criterion_id"] == "INC_01"
            assert data["criterion_evaluations"][0]["is_met"] is True

            # Verify no PHI is returned
            body_str = json.dumps(data)
            assert "birthdate" not in body_str
            assert "2000" not in body_str

            # Verify database state has transitioned to ENROLLED
            async with db_manager.get_session_maker()() as session:
                res_sub = await session.execute(
                    select(ClinicalSubject).where(ClinicalSubject.subject_id == "SUBJ-100")
                )
                db_subj = res_sub.scalars().one()
                assert db_subj.status == "ENROLLED"


# @req:PRD-ELIGIBILITY-012
@pytest.mark.asyncio
async def test_screening_endpoint_ineligible_transition_and_audit():
    """Verify POST screening transitions ineligible to SCREEN_FAILED and produces immutable, attributable audit evidence."""
    current_user_id.set("investigator_1")
    current_change_reason.set("Test screening failure")

    async with db_manager.get_session_maker()() as session:
        encrypted_demo = encrypt_demographics(
            {"birthdate": "2015-01-01", "gender": "Female"}  # 11 years old -> failed
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-200",
            study_id="STUDY_XYZ",
            encrypted_demographics=encrypted_demo,
            status="SCREENING",
        )
        session.add(subj)
        await session.commit()

    mock_criteria = [
        EligibilityCriterion(**make_mock_criterion("INC_01", "inclusion", "eCRF.DM.AGE", ">=", 18, True))
    ]

    with patch("apps.execution.eligibility_service.fetch_study_criteria", return_value=mock_criteria):
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            res = await client.post(
                "/api/v1/execution/subjects/SUBJ-200/screening",
                json={"study_id": "STUDY_XYZ"},
                headers=get_auth_headers(user_id="dr_smith", change_reason="Primary screening"),
            )
            assert res.status_code == 200
            data = res.json()
            assert data["eligible"] is False
            assert "INC_01" in data["failed_criteria"]

            # Verify database state transitioned to SCREEN_FAILED
            async with db_manager.get_session_maker()() as session:
                res_sub = await session.execute(
                    select(ClinicalSubject).where(ClinicalSubject.subject_id == "SUBJ-200")
                )
                db_subj = res_sub.scalars().one()
                assert db_subj.status == "SCREEN_FAILED"

                # Verify immutable audit ledger trail attributes failure details to actor
                stmt_audit = select(AuditLog).where(
                    AuditLog.table_name == "clinical_subjects",
                    AuditLog.record_id == db_subj.id,
                ).order_by(AuditLog.timestamp.desc())
                res_audit = await session.execute(stmt_audit)
                audit_logs = res_audit.scalars().all()

                assert len(audit_logs) >= 1
                # Find the log mapping the SCREEN_FAILED transition
                screen_fail_log = None
                for log in audit_logs:
                    new_vals = log.new_values or {}
                    if new_vals.get("status") == "SCREEN_FAILED":
                        screen_fail_log = log
                        break

                assert screen_fail_log is not None
                assert screen_fail_log.user_id == "dr_smith"
                assert "Screen failure due to failed criteria: INC_01" in screen_fail_log.change_reason


# @req:PRD-ELIGIBILITY-013
@pytest.mark.asyncio
async def test_screening_endpoint_indeterminate_behavior():
    """Verify indeterminate results leave the subject status as SCREENING with no terminal transition."""
    current_user_id.set("investigator_1")
    current_change_reason.set("Test screening indeterminate")

    async with db_manager.get_session_maker()() as session:
        # No demographics (missing) -> Indeterminate outcome
        subj = ClinicalSubject(
            subject_id="SUBJ-300",
            study_id="STUDY_XYZ",
            status="SCREENING",
        )
        session.add(subj)
        await session.commit()

    mock_criteria = [
        EligibilityCriterion(**make_mock_criterion("INC_01", "inclusion", "eCRF.DM.AGE", ">=", 18, True))
    ]

    with patch("apps.execution.eligibility_service.fetch_study_criteria", return_value=mock_criteria):
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            res = await client.post(
                "/api/v1/execution/subjects/SUBJ-300/screening",
                json={"study_id": "STUDY_XYZ"},
                headers=get_auth_headers(),
            )
            assert res.status_code == 200
            data = res.json()
            assert data["eligible"] is None  # Indeterminate
            assert "INC_01" in data["indeterminate_criteria"]

            # Verify subject status remains as SCREENING (no terminal transition)
            async with db_manager.get_session_maker()() as session:
                res_sub = await session.execute(
                    select(ClinicalSubject).where(ClinicalSubject.subject_id == "SUBJ-300")
                )
                db_subj = res_sub.scalars().one()
                assert db_subj.status == "SCREENING"


# @req:PRD-ELIGIBILITY-014
@pytest.mark.asyncio
async def test_randomization_allocation_rejection_gate():
    """Verify that allocation/randomization is strictly rejected for SCREENING and SCREEN_FAILED subjects."""
    # Test gate on different subject state states
    # Note: Subject initialization must start with "SCREENING" to obey transitions guard, then transition to SCREEN_FAILED or ENROLLED
    subj_screening = ClinicalSubject(subject_id="S_SCR", study_id="STUDY_XYZ", status="SCREENING")

    subj_screen_failed = ClinicalSubject(subject_id="S_FAIL", study_id="STUDY_XYZ", status="SCREENING")
    subj_screen_failed.status = "SCREEN_FAILED"

    subj_enrolled = ClinicalSubject(subject_id="S_ENR", study_id="STUDY_XYZ", status="SCREENING")
    subj_enrolled.status = "ENROLLED"

    # 1. SCREENING subject randomization must be blocked
    with pytest.raises(HTTPException) as exc:
        subj_screening.randomize("RAND-101", "KIT-01", {})
    assert exc.value.status_code == 400
    assert "Only ENROLLED subjects can proceed" in exc.value.detail

    # 2. SCREEN_FAILED subject randomization must be blocked
    with pytest.raises(HTTPException) as exc:
        subj_screen_failed.randomize("RAND-102", "KIT-02", {})
    assert exc.value.status_code == 400
    assert "Only ENROLLED subjects can proceed" in exc.value.detail

    # 3. ENROLLED subject randomization proceeds successfully
    # Since randomization transitions state, let's verify it transitions correctly to RANDOMIZED
    subj_enrolled.randomize("RAND-103", "KIT-03", {"gender": "F"})
    assert subj_enrolled.status == "RANDOMIZED"
    assert subj_enrolled.randomization_id == "RAND-103"
    assert subj_enrolled.kit_reference == "KIT-03"
