import os
import time
import uuid
from unittest.mock import patch

import pytest
from eligibility.models import EligibilityCriterion
from httpx import ASGITransport, AsyncClient
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog
from apps.execution.main import app
from packages.security.signing import generate_gateway_signature
from tests.test_execution_eligibility import make_mock_criterion

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


def get_auth_headers(
    user_id: str = "test_compliance_officer",
    roles: str = "investigator,datamanager,Monitor",
    change_reason: str = "compliance verification test",
) -> dict[str, str]:
    """Helper to generate signed gateway auth headers with custom role and reason."""
    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET,
        change_reason=change_reason,
        tenant_id="tenant_default",
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }


@pytest.mark.asyncio
async def test_site_compliance_cache_webhook_and_retrieval(shared_sqlite_dbs):
    """Verify that inbound webhooks update the cache relational state.

    @req:PRD-COMP-001
    @req:PRD-COMP-002
    """
    study_id = f"study_{uuid.uuid4()}"
    site_id = f"site_{uuid.uuid4()}"

    headers = get_auth_headers()

    async with AsyncClient(
        transport=ASGITransport(app=app), base_url="http://test"
    ) as ac:
        # 1. Check initial state is compliant (default fallback since no entry exists)
        resp_badge = await ac.get(
            f"/api/v1/execution/compliance/badge?study_id={study_id}&site_id={site_id}",
            headers=headers,
        )
        assert resp_badge.status_code == 200
        badge_data = resp_badge.json()
        assert badge_data["is_compliant"] is True
        assert badge_data["status_label"] == "READY"

        # 2. Ingest compliance update via webhook - setting is_compliant = False
        webhook_payload = {
            "study_id": study_id,
            "site_id": site_id,
            "milestone_type": "SITE_ACTIVATION",
            "is_compliant": False,
            "missing_documents": ["Investigator CV", "FDA Form 1572"],
        }
        resp_webhook = await ac.post(
            "/api/v1/execution/compliance/webhook",
            json=webhook_payload,
            headers=headers,
        )
        assert resp_webhook.status_code == 200
        assert resp_webhook.json()["status"] == "SUCCESS"

        # 3. Retrieve badge again and assert it is now non-compliant
        resp_badge = await ac.get(
            f"/api/v1/execution/compliance/badge?study_id={study_id}&site_id={site_id}",
            headers=headers,
        )
        assert resp_badge.status_code == 200
        badge_data = resp_badge.json()
        assert badge_data["is_compliant"] is False
        assert badge_data["status_label"] == "NON_COMPLIANT"
        assert "Investigator CV" in badge_data["missing_documents"]
        assert "FDA Form 1572" in badge_data["missing_documents"]

        # 4. Assert listing endpoint works
        resp_list = await ac.get(
            f"/api/v1/execution/compliance/cache?study_id={study_id}", headers=headers
        )
        assert resp_list.status_code == 200
        cache_items = resp_list.json()
        assert len(cache_items) == 1
        assert cache_items[0]["is_compliant"] is False
        assert cache_items[0]["milestone_type"] == "SITE_ACTIVATION"


@pytest.mark.asyncio
async def test_site_compliance_cache_gating_and_audit_trail(shared_sqlite_dbs):
    """Verify that site activation and subject enrollment are gated and logged upon compliance violation.

    @req:PRD-COMP-003
    @req:PRD-COMP-004
    """
    study_id = f"study_{uuid.uuid4()}"
    site_id = f"site_{uuid.uuid4()}"
    subject_id = f"SUBJ-{uuid.uuid4().hex[:6]}"

    headers = get_auth_headers()

    mock_criteria = [
        EligibilityCriterion(
            **make_mock_criterion("INC_01", "inclusion", "eCRF.DM.AGE", ">=", 18)
        )
    ]

    with patch(
        "apps.execution.eligibility_service.fetch_study_criteria",
        return_value=mock_criteria,
    ):
        async with AsyncClient(
            transport=ASGITransport(app=app), base_url="http://test"
        ) as ac:
            # 1. Update cache to be NON-COMPLIANT
            webhook_payload = {
                "study_id": study_id,
                "site_id": site_id,
                "milestone_type": "SITE_ACTIVATION",
                "is_compliant": False,
                "missing_documents": ["FDA Form 1572"],
            }
            resp_webhook = await ac.post(
                "/api/v1/execution/compliance/webhook",
                json=webhook_payload,
                headers=headers,
            )
            assert resp_webhook.status_code == 200

            # 2. Attempt to activate site - should be BLOCKED
            resp_activate = await ac.post(
                f"/api/v1/execution/sites/{site_id}/activate?study_id={study_id}",
                headers=headers,
            )
            assert resp_activate.status_code == 400
            assert "Site Activation blocked" in resp_activate.json()["detail"]

            # 3. Verify audit log entry exists for the blocked activation
            async with db_manager.get_session_maker()() as session:
                stmt = select(AuditLog).where(
                    AuditLog.action == "BLOCKED_SITE_ACTIVATION",
                    AuditLog.record_id == site_id,
                )
                res = await session.execute(stmt)
                audit_records = res.scalars().all()
                assert len(audit_records) >= 1
                assert "FDA Form 1572" in audit_records[0].change_reason
                assert (
                    "FDA Form 1572" in audit_records[0].new_values["missing_documents"]
                )

            # 4. Attempt to transition subject screening to ENROLLED - should be BLOCKED
            # Create screening subject
            create_payload = {
                "subject_id": subject_id,
                "study_id": study_id,
                "site_id": site_id,
                "demographics": {"birthdate": "2000-01-01", "gender": "Female"},
            }
            resp_create = await ac.post(
                "/api/v1/execution/subjects", json=create_payload, headers=headers
            )
            assert resp_create.status_code == 200
            subj_data = resp_create.json()
            assert subj_data["site_id"] == site_id

            # Try screening transition
            resp_screening = await ac.post(
                f"/api/v1/execution/subjects/{subject_id}/screening",
                json={"study_id": study_id},
                headers=headers,
            )
            assert resp_screening.status_code == 400
            assert "Subject enrollment blocked" in resp_screening.json()["detail"]

            # 5. Verify audit log entry exists for the blocked enrollment
            async with db_manager.get_session_maker()() as session:
                stmt = select(AuditLog).where(
                    AuditLog.action == "BLOCKED_SUBJECT_ENROLLMENT",
                )
                res = await session.execute(stmt)
                audit_records = res.scalars().all()
                assert len(audit_records) >= 1
                assert "FDA Form 1572" in audit_records[0].change_reason
                assert (
                    "FDA Form 1572" in audit_records[0].new_values["missing_documents"]
                )

            # 6. Update cache to be COMPLIANT
            webhook_payload_success = {
                "study_id": study_id,
                "site_id": site_id,
                "milestone_type": "SITE_ACTIVATION",
                "is_compliant": True,
                "missing_documents": [],
            }
            resp_webhook_success = await ac.post(
                "/api/v1/execution/compliance/webhook",
                json=webhook_payload_success,
                headers=headers,
            )
            assert resp_webhook_success.status_code == 200

            # 7. Attempt to activate site again - should now succeed
            resp_activate_success = await ac.post(
                f"/api/v1/execution/sites/{site_id}/activate?study_id={study_id}",
                headers=headers,
            )
            assert resp_activate_success.status_code == 200
            assert resp_activate_success.json()["status"] == "ACTIVE"

            # 8. Attempt screening transition again - should now succeed
            resp_screening_success = await ac.post(
                f"/api/v1/execution/subjects/{subject_id}/screening",
                json={"study_id": study_id},
                headers=headers,
            )
            assert resp_screening_success.status_code == 200
