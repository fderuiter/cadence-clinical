"""Tests for Automated Minor Age-of-Majority Milestone Detection and Adult Re-Consent Engine.

@req:PRD-SYS-178
Complies with 21 CFR Part 11, EU CTR 536/2014, and ICH GCP E6(R2)/(R3).
"""

import time
from datetime import date, timedelta
from unittest.mock import AsyncMock

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import Base
from apps.econsent.application.use_cases import ReconsentService
from apps.econsent.domain.majority_engine import (
    calculate_exact_age,
    calculate_majority_date,
    evaluate_subject_majority_milestone,
    get_legal_age_threshold,
)
from apps.econsent.infrastructure.repositories import (
    SQLConsentAuditRepository,
    SQLReconsentRepository,
    SQLSubjectConsentRepository,
)
from apps.econsent.main import app
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "site_coordinator",
    roles: str = "investigator",
    change_reason: str = "Age of majority test",
) -> dict:
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


@pytest.mark.asyncio
async def test_domain_jurisdiction_legal_age_thresholds() -> None:
    """Test resolution of jurisdiction-specific legal age thresholds.

    @req:PRD-SYS-178
    """
    assert get_legal_age_threshold("DEFAULT") == 18
    assert get_legal_age_threshold("US") == 18
    assert get_legal_age_threshold("US-AL") == 19
    assert get_legal_age_threshold("US-NE") == 19
    assert get_legal_age_threshold("US-MS") == 21
    assert get_legal_age_threshold("US-PR") == 21
    assert get_legal_age_threshold("CA-BC") == 19
    assert get_legal_age_threshold("CA-ON") == 18
    assert get_legal_age_threshold("GB") == 18
    assert get_legal_age_threshold("KR") == 19
    assert get_legal_age_threshold("UNKNOWN_REGION") == 18


@pytest.mark.asyncio
async def test_domain_age_calculation_and_leap_year() -> None:
    """Test exact age calculation and leap year majority date computation.

    @req:PRD-SYS-178
    """
    # Standard birthday
    dob = "2008-05-15"
    ref_17 = date(2025, 5, 14)
    ref_18 = date(2026, 5, 15)

    years_17, exact_17 = calculate_exact_age(dob, ref_17)
    assert years_17 == 16

    years_18, exact_18 = calculate_exact_age(dob, ref_18)
    assert years_18 == 18
    assert exact_18 >= 18.0

    # Leap year born participant (Feb 29, 2008)
    dob_leap = "2008-02-29"
    maj_date = calculate_majority_date(dob_leap, 18)
    # 2026 is not a leap year, resolves to March 1
    assert maj_date == date(2026, 3, 1)


@pytest.mark.asyncio
async def test_domain_evaluate_subject_majority_milestones() -> None:
    """Test evaluation of individual subject milestone classifications.

    @req:PRD-SYS-178
    """
    ref_date = date(2026, 8, 24)

    # 1. Active minor far from majority (14 years old in US)
    m1 = evaluate_subject_majority_milestone(
        subject_pseudonym="SUBJ-MINOR-01",
        date_of_birth="2012-01-10",
        jurisdiction="US",
        as_of_date=ref_date,
        lead_time_days=30,
        current_consent_role="MINOR_ASSENT",
    )
    assert m1.has_reached_majority is False
    assert m1.is_transition_due is False
    assert m1.requires_adult_reconsent is False
    assert m1.milestone_status == "ACTIVE_MINOR"

    # 2. Approaching majority (turns 18 in 15 days on Sep 8, 2026)
    dob_approaching = (ref_date + timedelta(days=15)).replace(year=ref_date.year - 18)
    m2 = evaluate_subject_majority_milestone(
        subject_pseudonym="SUBJ-APPROACHING-02",
        date_of_birth=dob_approaching.isoformat(),
        jurisdiction="US",
        as_of_date=ref_date,
        lead_time_days=30,
        current_consent_role="MINOR_ASSENT",
    )
    assert m2.has_reached_majority is False
    assert m2.is_transition_due is True
    assert m2.days_until_majority == 15
    assert m2.milestone_status == "APPROACHING_MAJORITY"

    # 3. Reached majority (turned 18 last week in US) -> requires adult reconsent
    dob_adult = (ref_date - timedelta(days=7)).replace(year=ref_date.year - 18)
    m3 = evaluate_subject_majority_milestone(
        subject_pseudonym="SUBJ-ADULT-03",
        date_of_birth=dob_adult.isoformat(),
        jurisdiction="US",
        as_of_date=ref_date,
        current_consent_role="MINOR_ASSENT",
    )
    assert m3.has_reached_majority is True
    assert m3.is_transition_due is True
    assert m3.requires_adult_reconsent is True
    assert m3.milestone_status == "MAJORITY_REACHED_RECONSENT_REQUIRED"

    # 4. 18 years old in Alabama (legal age is 19) -> still minor
    m4 = evaluate_subject_majority_milestone(
        subject_pseudonym="SUBJ-ALABAMA-04",
        date_of_birth=dob_adult.isoformat(),
        jurisdiction="US-AL",
        as_of_date=ref_date,
        current_consent_role="MINOR_ASSENT",
    )
    assert m4.legal_age_threshold == 19
    assert m4.has_reached_majority is False
    assert m4.requires_adult_reconsent is False

    # 5. Already re-consented directly as adult (role == SUBJECT)
    m5 = evaluate_subject_majority_milestone(
        subject_pseudonym="SUBJ-ADULT-DONE",
        date_of_birth=dob_adult.isoformat(),
        jurisdiction="US",
        as_of_date=ref_date,
        current_consent_role="SUBJECT",
    )
    assert m5.has_reached_majority is True
    assert m5.requires_adult_reconsent is False
    assert m5.milestone_status == "ADULT_RECONSENT_COMPLETED"


@pytest.mark.asyncio
async def test_reconsent_service_trigger_majority_reconsent() -> None:
    """Test ReconsentService triggering majority re-consent requirements and audit logging.

    @req:PRD-SYS-178
    """
    async with db_manager.get_session_maker()() as session:
        reconsent_repo = SQLReconsentRepository(session)
        consent_repo = SQLSubjectConsentRepository(session)
        audit_repo = SQLConsentAuditRepository(session)
        mock_dispatcher = AsyncMock()

        svc = ReconsentService(
            reconsent_repo,
            consent_repo,
            audit_repo,
            notification_dispatcher=mock_dispatcher,
        )

        cohort = [
            {
                "subject_pseudonym": "SUBJ-M-01",
                "date_of_birth": "2007-01-15",  # 19 years old (reached 18)
                "jurisdiction": "US",
                "current_consent_role": "MINOR_ASSENT",
            },
            {
                "subject_pseudonym": "SUBJ-M-02",
                "date_of_birth": "2012-06-01",  # 14 years old (minor)
                "jurisdiction": "US",
                "current_consent_role": "MINOR_ASSENT",
            },
        ]

        reqs = await svc.trigger_majority_reconsent_for_subjects(
            study_id="STUDY-PEDIATRIC-101",
            site_id="SITE-001",
            template_id="tpl-pediatric-v1",
            adult_version_index=2,
            subjects=cohort,
            lead_time_days=0,
            created_by="dr_investigator",
            reason_for_change="Minor reached age of majority milestone",
        )

        assert len(reqs) == 1
        assert reqs[0].subject_pseudonym == "SUBJ-M-01"
        assert reqs[0].status == "PENDING"
        assert "reached legal age of majority" in reqs[0].change_summary
        assert mock_dispatcher.call_count == 1

        # Complete the requirement
        completed = await svc.complete_reconsent_requirement(
            requirement_id=reqs[0].id,
            completed_consent_id="consent-adult-signed-001",
            created_by="SUBJ-M-01",
            reason_for_change="Adult participant signed direct self-consent",
        )
        assert completed.status == "COMPLETED"
        assert completed.completed_consent_id == "consent-adult-signed-001"


@pytest.mark.asyncio
async def test_api_minor_milestone_evaluate_and_trigger_endpoints() -> None:
    """Test FastAPI endpoints for minor milestone evaluation, triggering, and pending queries.

    @req:PRD-SYS-178
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Minor Milestone API Verification")

    eval_payload = {
        "study_id": "STUDY-PEDS-202",
        "as_of_date": "2026-08-24",
        "lead_time_days": 30,
        "subjects": [
            {
                "subject_pseudonym": "PEDS-001",
                "date_of_birth": "2007-05-10",
                "jurisdiction": "US",
                "current_consent_role": "MINOR_ASSENT",
            },
            {
                "subject_pseudonym": "PEDS-002",
                "date_of_birth": "2008-09-01",  # turns 18 in 8 days
                "jurisdiction": "US",
                "current_consent_role": "MINOR_ASSENT",
            },
            {
                "subject_pseudonym": "PEDS-003",
                "date_of_birth": "2013-11-20",
                "jurisdiction": "US",
                "current_consent_role": "MINOR_ASSENT",
            },
        ],
    }

    # 1. Evaluate endpoint
    res_eval = client.post(
        "/api/v1/econsent/reconsent/minor-milestones/evaluate",
        json=eval_payload,
        headers=headers,
    )
    assert res_eval.status_code == 200
    data_eval = res_eval.json()
    assert data_eval["total_evaluated"] == 3
    assert data_eval["majority_reached_count"] == 1
    assert data_eval["approaching_count"] == 1

    # 2. Trigger majority re-consent endpoint
    trigger_payload = {
        "study_id": "STUDY-PEDS-202",
        "site_id": "SITE-01",
        "template_id": "tpl-peds-01",
        "adult_version_index": 2,
        "lead_time_days": 0,
        "as_of_date": eval_payload["as_of_date"],
        "subjects": eval_payload["subjects"],
        "reason_for_change": "Age of majority reached milestone trigger",
    }
    res_trigger = client.post(
        "/api/v1/econsent/reconsent/minor-milestones/trigger",
        json=trigger_payload,
        headers=headers,
    )
    assert res_trigger.status_code == 201
    data_trigger = res_trigger.json()
    assert len(data_trigger) == 1
    assert data_trigger[0]["subject_pseudonym"] == "PEDS-001"

    # 3. Query pending majority re-consent endpoint
    res_pending = client.get(
        "/api/v1/econsent/reconsent/minor-milestones/pending/STUDY-PEDS-202",
        headers=headers,
    )
    assert res_pending.status_code == 200
    data_pending = res_pending.json()
    assert len(data_pending) == 1
    assert data_pending[0]["subject_pseudonym"] == "PEDS-001"
