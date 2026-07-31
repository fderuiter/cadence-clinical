"""Unit test suite for Delegation of Authority (DOA) log sign-off and task delegation.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

from datetime import datetime, timezone

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.ctms.database import db_manager
from apps.ctms.services.doa_service import (
    DOAManagerService,
    approve_delegation_with_esignature,
    delegate_task,
    revoke_delegation,
)
from apps.execution.database.models import (
    Base as ExecBase,
)
from apps.execution.database.models import (
    DOAAuditLog,
    DOADelegationRecord,
    SiteStaffMember,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_doa_db():
    """Setup in-memory SQLite database containing Execution base tables for DOA testing."""
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
        execution_options={"schema_translate_map": {"audit_schema": None}},
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_doa_task_delegation_and_esignature_lifecycle():
    """Validate full GxP task delegation, credential re-authentication, eSignature approval, and revocation.

    Requirements: PRD-SYS-001
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Pre-populate trained site staff member
        staff = SiteStaffMember(
            site_id="site-101",
            staff_user_id="staff-01",
            name="Alice Smith",
            email="alice@site.org",
            has_gcp_training=True,
        )
        session.add(staff)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        # 2. Test delegate_task creates a pending record for trained staff
        record = await delegate_task(
            session=session,
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

        # Check audit log was written
        stmt_audit = select(DOAAuditLog).where(DOAAuditLog.action == "DELEGATE_TASK")
        res_audit = await session.execute(stmt_audit)
        audit_log = res_audit.scalars().first()
        assert audit_log is not None
        assert "Delegated task ICF_CONSENT" in audit_log.details

    async with db_manager.get_session_maker()() as session:
        # 3. Test delegate_task blocks untrained staff member
        untrained_staff = SiteStaffMember(
            site_id="site-101",
            staff_user_id="staff-02",
            name="Bob Jones",
            email="bob@site.org",
            has_gcp_training=False,
        )
        session.add(untrained_staff)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        with pytest.raises(ValueError) as exc:
            await delegate_task(
                session=session,
                site_id="site-101",
                staff_user_id="staff-02",
                task_code="ECRF_ENTRY",
                pi_user_id="pi-99",
                reason_for_change="Untrained attempt",
            )
        assert "has not completed required GCP training" in str(exc.value)

    async with db_manager.get_session_maker()() as session:
        # Retrieve the delegation record ID
        stmt = select(DOADelegationRecord).where(
            DOADelegationRecord.staff_user_id == "staff-01"
        )
        res = await session.execute(stmt)
        record = res.scalars().first()
        delegation_id = record.id

        # 4. Test credential re-authentication rejection
        with pytest.raises(ValueError) as exc:
            await approve_delegation_with_esignature(
                session=session,
                delegation_id=delegation_id,
                pi_user_id="pi-99",
                password="wrong_password",  # pragma: allowlist secret
            )
        assert "Invalid credentials" in str(exc.value)

        # 5. Test successful PI eSignature sign-off transitions status to ACTIVE
        approved = await approve_delegation_with_esignature(
            session=session,
            delegation_id=delegation_id,
            pi_user_id="pi-99",
            password="valid_password",  # pragma: allowlist secret
            totp_code="123456",
        )

        assert approved.status == "ACTIVE"
        assert approved.pi_approved_at is not None
        assert approved.pi_signature_hash is not None
        assert approved.reason_for_change == "PI Delegation Approval"

        # Check approval audit log was written
        stmt_audit = select(DOAAuditLog).where(
            DOAAuditLog.action == "APPROVE_DELEGATION"
        )
        res_audit = await session.execute(stmt_audit)
        audit_log = res_audit.scalars().first()
        assert audit_log is not None
        assert "Approved delegation" in audit_log.details

    async with db_manager.get_session_maker()() as session:
        # 6. Test successful revocation and end dating
        end_date = datetime.now(timezone.utc)
        revoked = await revoke_delegation(
            session=session,
            delegation_id=delegation_id,
            end_date=end_date,
            reason_for_change="Staff left site",
        )

        assert revoked.status == "REVOKED"
        assert revoked.end_date == end_date
        assert revoked.is_active is False
        assert revoked.reason_for_change == "Staff left site"

        # Check revocation audit log was written
        stmt_audit = select(DOAAuditLog).where(
            DOAAuditLog.action == "REVOKE_DELEGATION"
        )
        res_audit = await session.execute(stmt_audit)
        audit_log = res_audit.scalars().first()
        assert audit_log is not None
        assert "Revoked delegation" in audit_log.details


@pytest.mark.asyncio
async def test_doa_manager_service_class_interface():
    """Validate class-based DOAManagerService interface operations.

    Requirements: PRD-SYS-001
    """
    async with db_manager.get_session_maker()() as session:
        staff = SiteStaffMember(
            site_id="site-202",
            staff_user_id="staff-99",
            name="Charlie Brown",
            email="charlie@site.org",
            has_gcp_training=True,
        )
        session.add(staff)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        service = DOAManagerService(session=session)

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
        end_date = datetime.now(timezone.utc)
        revoked = await service.revoke_delegation(
            delegation_id=record.id,
            end_date=end_date,
            reason_for_change="Completed study duty",
        )
        assert revoked.status == "REVOKED"
        assert revoked.is_active is False
