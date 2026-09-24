import os

import pytest
import pytest_asyncio
from sqlalchemy import select

import apps.execution.database.audit  # noqa: F401
from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base
from apps.execution.services.change_request_service import (
    CURRENT_SETTINGS,
    ComplianceChangeRequestService,
)
from packages.security.context import audit_context


@pytest_asyncio.fixture
async def db_session():
    """Setup PostgreSQL/SQLite database schema and triggers for change request testing."""
    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        ),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        from apps.execution.database.migrate import deploy_database_triggers

        await deploy_database_triggers(conn, db_manager.engine.dialect.name)

    async with db_manager.get_session_maker()() as session:
        yield session

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_compliance_change_request_audit_trail(db_session):
    """Validate compliance setting change request logs non-repudiable audit events.

    Requirements: PRD-SYS-001
    """
    service = ComplianceChangeRequestService(session=db_session)
    cr = await service.create_change_request(
        setting_key="session_timeout_minutes",
        old_value="30",
        new_value="15",
        requested_by="admin_user",
        reason="Security hardening compliance update",
    )

    assert cr.status == "PENDING_APPROVAL"

    await service.approve_change_request(
        cr.id, approver_id="qa_lead", signature_token="sig_tok_102"
    )

    updated_cr = await service.get_change_request(cr.id)
    assert updated_cr.status == "APPROVED"


@pytest.mark.asyncio
async def test_change_request_requires_dual_approval(db_session):
    """Validate that change request remains pending until the dual-approval threshold is reached.

    Requirements: PRD-SYS-001
    """
    CURRENT_SETTINGS["esignature_timeout_thresholds"] = "120"
    service = ComplianceChangeRequestService(session=db_session)

    cr = await service.create_change_request(
        setting_key="esignature_timeout_thresholds",
        old_value="120",
        new_value="60",
        requested_by="system_admin",
        reason="Hardening GxP electronic signature timeout",
    )

    assert cr.status == "PENDING_APPROVAL"
    assert CURRENT_SETTINGS["esignature_timeout_thresholds"] == "120"

    # Attempting duplicate signature from same approver must fail
    with pytest.raises(ValueError, match="This approver has already signed"):
        await service.approve_change_request(
            cr.id, approver_id="system_admin", signature_token="sig_tok_dup"
        )

    # Attempting empty signature token must fail
    with pytest.raises(ValueError, match="Signature token is invalid or missing"):
        await service.approve_change_request(
            cr.id, approver_id="qa_manager", signature_token=""
        )

    # 2nd approver signs (QA Lead role) -> Dual approval threshold met
    await service.approve_change_request(
        cr_id=cr.id,
        approver_id="qa_manager",
        signature_token="sig_tok_qa_lead_unique",
    )

    updated_cr = await service.get_change_request(cr.id)
    assert updated_cr.status == "APPROVED"
    assert CURRENT_SETTINGS["esignature_timeout_thresholds"] == "60"

    # Non-repudiation: signature token reuse must be strictly rejected
    with pytest.raises(ValueError, match="Signature token has already been used"):
        await service.approve_change_request(
            cr_id=cr.id,
            approver_id="another_qa_user",
            signature_token="sig_tok_qa_lead_unique",
        )


@pytest.mark.asyncio
async def test_change_request_audit_trail_recorded(db_session):
    """Verify that every change request modification writes immutable AuditLog records.

    Requirements: PRD-SYS-001
    """
    service = ComplianceChangeRequestService(session=db_session)

    with audit_context(
        user_id="system_admin",
        change_reason="Update password policies",
        ip_address="192.168.1.10",
    ):
        await service.create_change_request(
            setting_key="password_expiration_days",
            old_value="90",
            new_value="60",
            requested_by="system_admin",
            reason="Update password policies",
        )

    stmt = select(AuditLog).where(AuditLog.table_name == "compliance_change_requests")
    result = await db_session.execute(stmt)
    logs = result.scalars().all()

    assert len(logs) >= 1
    insert_log = [log for log in logs if log.action == "INSERT"][0]
    assert insert_log.user_id == "system_admin"
    assert insert_log.change_reason == "Update password policies"
    assert insert_log.new_values["setting_key"] == "password_expiration_days"
    assert insert_log.new_values["old_value"] == "90"
    assert insert_log.new_values["new_value"] == "60"
