"""Unit test suite for Delegation of Authority (DOA) log sign-off and task delegation.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import os
import time
from datetime import UTC, datetime
import pytest
import pytest_asyncio
import httpx
from httpx import ASGITransport
from packages.security.signing import generate_gateway_signature

from apps.ctms.services.doa_service import (
    DOAManagerService,
    approve_delegation_with_esignature,
    delegate_task,
    revoke_delegation,
)

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_dm",
    roles="Data Manager",
    change_reason="system_operation",
):
    timestamp = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id or "",
        roles=roles or "",
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_doa_db(monkeypatch):
    """Setup in-memory SQLite database containing Execution base tables for DOA testing."""
    from apps.execution.database.core import db_manager as exec_db_manager
    from apps.execution.database.models import Base as ExecBase
    from apps.execution.main import app as execution_app

    exec_db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
        execution_options={"schema_translate_map": {"audit_schema": None}},
    )
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.create_all)

    # Monkeypatch AsyncClient to route requests to execution_app in-memory
    original_client_init = httpx.AsyncClient.__init__

    def mocked_client_init(self, *args, **kwargs):
        kwargs["transport"] = ASGITransport(app=execution_app)
        kwargs["base_url"] = "http://test"
        original_client_init(self, *args, **kwargs)

    monkeypatch.setattr(httpx.AsyncClient, "__init__", mocked_client_init)

    yield

    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()


@pytest.mark.asyncio
async def test_doa_task_delegation_and_esignature_lifecycle():
    """Validate full GxP task delegation, credential re-authentication, eSignature approval, and revocation.

    Requirements: PRD-SYS-001
    """
    # 1. Pre-populate trained site staff member via API
    async with httpx.AsyncClient() as client:
        staff_res = await client.post(
            "/api/v1/execution/doa/staff",
            headers=get_auth_headers(),
            json={
                "site_id": "site-101",
                "staff_user_id": "staff-01",
                "name": "Alice Smith",
                "email": "alice@site.org",
                "has_gcp_training": True,
            }
        )
        assert staff_res.status_code == 201

    # 2. Test delegate_task creates a pending record for trained staff
    record = await delegate_task(
        session=None,
        site_id="site-101",
        staff_user_id="staff-01",
        task_code="ICF_CONSENT",
        pi_user_id="pi-99",
        reason_for_change="Initial ICF delegation",
    )

    assert record.id is not None
    assert record.status == "PENDING_PI_APPROVAL"
    assert record.is_active is True
    assert record.reason_for_change == "Initial ICF delegation"

    # Check audit log was written via API
    async with httpx.AsyncClient() as client:
        audit_res = await client.get("/api/v1/execution/doa/audit-logs", headers=get_auth_headers())
        assert audit_res.status_code == 200
        audit_logs = audit_res.json()
        delegate_logs = [log for log in audit_logs if log["action"] == "DELEGATE_TASK"]
        assert len(delegate_logs) > 0
        assert "Delegated task ICF_CONSENT" in delegate_logs[0]["details"]

    # 3. Test delegate_task blocks untrained staff member
    async with httpx.AsyncClient() as client:
        untrained_res = await client.post(
            "/api/v1/execution/doa/staff",
            headers=get_auth_headers(),
            json={
                "site_id": "site-101",
                "staff_user_id": "staff-02",
                "name": "Bob Jones",
                "email": "bob@site.org",
                "has_gcp_training": False,
            }
        )
        assert untrained_res.status_code == 201

    with pytest.raises(ValueError) as exc:
        await delegate_task(
            session=None,
            site_id="site-101",
            staff_user_id="staff-02",
            task_code="ECRF_ENTRY",
            pi_user_id="pi-99",
            reason_for_change="Untrained attempt",
        )
    assert "has not completed required GCP training" in str(exc.value)

    # Retrieve the delegation record ID via API
    async with httpx.AsyncClient() as client:
        delegations_res = await client.get("/api/v1/execution/doa/delegations", headers=get_auth_headers())
        assert delegations_res.status_code == 200
        delegations = delegations_res.json()
        target_delegation = [d for d in delegations if d["staff_user_id"] == "staff-01"][0]
        delegation_id = target_delegation["id"]

    # 4. Test credential re-authentication rejection
    with pytest.raises(ValueError) as exc:
        await approve_delegation_with_esignature(
            session=None,
            delegation_id=delegation_id,
            pi_user_id="pi-99",
            password="wrong_password",  # pragma: allowlist secret
        )
    assert "Invalid credentials" in str(exc.value)

    # 5. Test successful PI eSignature sign-off transitions status to ACTIVE
    approved = await approve_delegation_with_esignature(
        session=None,
        delegation_id=delegation_id,
        pi_user_id="pi-99",
        password="valid_password",  # pragma: allowlist secret
        totp_code="123456",
    )

    assert approved.status == "ACTIVE"
    assert approved.pi_approved_at is not None
    assert approved.pi_signature_hash is not None
    assert approved.reason_for_change == "PI Delegation Approval"

    # Check approval audit log was written via API
    async with httpx.AsyncClient() as client:
        audit_res = await client.get("/api/v1/execution/doa/audit-logs", headers=get_auth_headers())
        assert audit_res.status_code == 200
        audit_logs = audit_res.json()
        approve_logs = [log for log in audit_logs if log["action"] == "APPROVE_DELEGATION"]
        assert len(approve_logs) > 0
        assert "Approved delegation" in approve_logs[0]["details"]

    # 6. Test successful revocation and end dating
    end_date = datetime.now(UTC)
    revoked = await revoke_delegation(
        session=None,
        delegation_id=delegation_id,
        end_date=end_date,
        reason_for_change="Staff left site",
    )

    assert revoked.status == "REVOKED"
    assert revoked.end_date.year == end_date.year
    assert revoked.is_active is False
    assert revoked.reason_for_change == "Staff left site"

    # Check revocation audit log was written via API
    async with httpx.AsyncClient() as client:
        audit_res = await client.get("/api/v1/execution/doa/audit-logs", headers=get_auth_headers())
        assert audit_res.status_code == 200
        audit_logs = audit_res.json()
        revoke_logs = [log for log in audit_logs if log["action"] == "REVOKE_DELEGATION"]
        assert len(revoke_logs) > 0
        assert "Revoked delegation" in revoke_logs[0]["details"]


@pytest.mark.asyncio
async def test_doa_manager_service_class_interface():
    """Validate class-based DOAManagerService interface operations.

    Requirements: PRD-SYS-001
    """
    # Pre-populate trained site staff member via API
    async with httpx.AsyncClient() as client:
        staff_res = await client.post(
            "/api/v1/execution/doa/staff",
            headers=get_auth_headers(),
            json={
                "site_id": "site-202",
                "staff_user_id": "staff-99",
                "name": "Charlie Brown",
                "email": "charlie@site.org",
                "has_gcp_training": True,
            }
        )
        assert staff_res.status_code == 201

    service = DOAManagerService(session=None)

    # 1. Test delegate_task method
    record = await service.delegate_task(
        site_id="site-202",
        staff_user_id="staff-99",
        task_code="DRUG_DISPENSE",
        pi_user_id="pi-01",
        reason_for_change="Pharmacy delegation",
    )
    assert record.status == "PENDING_PI_APPROVAL"

    # 2. Test approve_delegation_with_esignature method
    approved = await service.approve_delegation_with_esignature(
        delegation_id=record.id,
        pi_user_id="pi-01",
        password="secure_password",  # pragma: allowlist secret
        totp_code="654321",
    )
    assert approved.status == "ACTIVE"

    # 3. Test revoke_delegation method
    end_date = datetime.now(UTC)
    revoked = await service.revoke_delegation(
        delegation_id=record.id,
        end_date=end_date,
        reason_for_change="Completed study duty",
    )
    assert revoked.status == "REVOKED"
    assert revoked.is_active is False
