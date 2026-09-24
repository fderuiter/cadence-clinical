"""Unit test suite for live subject data migration engine.

Requirements: PRD-SYS-001
"""

import hashlib
import hmac
import json
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

import packages  # noqa: F401
from apps.execution.database.core import db_manager
from apps.execution.database.models import Base
from apps.execution.database.models.form import FormSubmission
from apps.execution.database.sealer import validate_ledger_integrity
from apps.execution.main import app
from apps.execution.services.subject_migration import LiveSubjectMigrationEngine


def test_migrate_subject_submissions_field_remapping() -> None:
    """Validate migrating subject eCRF submissions re-maps renamed fields and updates version.

    Requirements: PRD-SYS-001
    """
    engine = LiveSubjectMigrationEngine()

    submissions = [
        {
            "form_id": "form_vs_01",
            "protocol_version": "1.0",
            "data": {"SYSBP": 120, "DIABP": 80, "OLD_WEIGHT": 70.5},
        },
        {
            "form_id": "form_lb_01",
            "protocol_version": "1.0",
            "data": {"GLUCOSE": 95},
        },
    ]

    field_mapping = {"OLD_WEIGHT": "WEIGHT_KG"}

    res = engine.migrate_subject_submissions(
        subject_id="sub_mig_01",
        old_version="1.0",
        new_version="2.0",
        form_submissions=submissions,
        field_mapping=field_mapping,
    )

    assert res["status"] == "COMPLETED"
    assert res["migrated_submissions_count"] == 2
    assert res["updated_fields_count"] == 1

    # Assert field was re-mapped correctly
    assert submissions[0]["protocol_version"] == "2.0"
    assert "OLD_WEIGHT" not in submissions[0]["data"]
    assert submissions[0]["data"]["WEIGHT_KG"] == 70.5


GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation", action=None
):
    """Generate Gateway signature-compliant authentication headers."""
    from jose import jwt

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
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if action:
        sig_payload = {
            "sub": user_id,
            "username": "test_user",
            "action": action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
        headers["X-Sig-Token"] = sig_token
    return headers


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_db_migration_cloning_and_sealing() -> None:
    """Validate database-backed soft-versioning, active/inactive cloning, and cryptographic sealing.

    @req:PRD-SYS-001
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Create a FormSubmission record directly in the DB
        original = FormSubmission(
            study_id="STUDY-XYZ",
            site_id="SITE-XYZ",
            subject_id="SUBJ-XYZ",
            visit_id="VISIT-XYZ",
            form_id="FORM-XYZ",
            status="COMPLETED",
            protocol_version="1.0",
            payload={"SYSBP": 120, "DIABP": 80, "OLD_WEIGHT": 70.5},
            is_active=True,
            is_readonly=False,
            cloned_from_id=None,
        )
        session.add(original)
        await session.commit()
        original_id = original.id

    # 2. Run the database-backed migration engine
    engine = LiveSubjectMigrationEngine()
    async with db_manager.get_session_maker()() as session:
        res = await engine.migrate_subject_submissions_db(
            session=session,
            subject_id="SUBJ-XYZ",
            old_version="1.0",
            new_version="2.0",
            field_mapping={"OLD_WEIGHT": "WEIGHT_KG"},
        )
        assert res["status"] == "COMPLETED"
        assert res["migrated_submissions_count"] == 1
        assert res["updated_fields_count"] == 1

    # 3. Retrieve and inspect the records in the database
    async with db_manager.get_session_maker()() as session:
        stmt_all = select(FormSubmission).where(FormSubmission.subject_id == "SUBJ-XYZ")
        rows = (await session.execute(stmt_all)).scalars().all()
        assert (
            len(rows) == 3
        )  # Original (now read-only & inactive), Inactive Clone, and Mutated Active Row

        # Find the specific records
        orig_row = next(r for r in rows if r.id == original_id)
        inactive_clone = next(
            r for r in rows if r.is_active is False and r.id != original_id
        )
        active_mutated = next(r for r in rows if r.is_active is True)

        # Requirement 1 & 2: Clone must be inactive and original read-only & inactive
        assert inactive_clone.is_active is False
        assert inactive_clone.is_readonly is True
        assert inactive_clone.protocol_version == "1.0"
        assert inactive_clone.payload == {"SYSBP": 120, "DIABP": 80, "OLD_WEIGHT": 70.5}

        assert orig_row.is_readonly is True
        assert orig_row.is_active is False

        # Requirement 3: Active mutated row must store a direct link to parent inactive cloned record
        assert active_mutated.is_active is True
        assert active_mutated.is_readonly is False
        assert active_mutated.protocol_version == "2.0"
        assert active_mutated.payload == {"SYSBP": 120, "DIABP": 80, "WEIGHT_KG": 70.5}
        assert active_mutated.cloned_from_id == inactive_clone.id

        active_mutated_id = active_mutated.id
        inactive_clone_id = inactive_clone.id

        # Requirement 4: Every cloning and mutation event must integrate with cryptographic ledger sealing
        ledger_valid = await validate_ledger_integrity(session)
        assert ledger_valid is True

        # Test blockages of modifications on read-only and inactive records
        orig_row.status = "APPROVED"
        with pytest.raises(Exception) as exc_info:
            await session.commit()
        assert "Cannot modify a read-only or inactive form submission" in str(
            exc_info.value
        )
        await session.rollback()

        inactive_clone.status = "APPROVED"
        with pytest.raises(Exception) as exc_info:
            await session.commit()
        assert "Cannot modify a read-only or inactive form submission" in str(
            exc_info.value
        )
        await session.rollback()

        # Test blockage of deletion
        await session.delete(inactive_clone)
        with pytest.raises(Exception) as exc_info:
            await session.commit()
        assert "Cannot delete a form submission record" in str(exc_info.value)
        await session.rollback()

    # 4. Requirement 5: Test API query default filters (exclude inactive records by default)
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Get list without include_inactive
        res_list = await client.get(
            "/api/v1/execution/form-submissions?subject_id=SUBJ-XYZ",
            headers=get_auth_headers(),
        )
        assert res_list.status_code == 200
        items = res_list.json()
        assert len(items) == 1  # Only the active mutated one!
        assert items[0]["id"] == active_mutated_id
        assert items[0]["is_active"] is True
        assert items[0]["protocol_version"] == "2.0"

        # Get list with include_inactive=True
        res_list_all = await client.get(
            "/api/v1/execution/form-submissions?subject_id=SUBJ-XYZ&include_inactive=true",
            headers=get_auth_headers(),
        )
        assert res_list_all.status_code == 200
        items_all = res_list_all.json()
        assert len(items_all) == 3  # All three records!

        # Test API completion/approval blockage on read-only/inactive
        res_complete = await client.post(
            f"/api/v1/execution/form-submissions/{inactive_clone_id}/complete",
            headers=get_auth_headers(),
        )
        assert res_complete.status_code == 400
        assert (
            "Cannot modify a read-only or inactive form submission"
            in res_complete.json()["detail"]
        )

        res_approve = await client.post(
            f"/api/v1/execution/form-submissions/{inactive_clone_id}/approve",
            json={
                "signature_manifest": {"signer_id": "pi_1"},
                "signing_reason": "I attest that this data is accurate and complete.",
            },
            headers=get_auth_headers(
                roles="investigator",
                action=f"/api/v1/execution/form-submissions/{inactive_clone_id}/approve",
            ),
        )
        assert res_approve.status_code == 400
        assert (
            "Cannot modify a read-only or inactive form submission"
            in res_approve.json()["detail"]
        )
