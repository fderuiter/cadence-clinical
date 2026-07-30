import os
import time

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, ClinicalSubject, SubjectRandomization
from apps.execution.main import app
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_inv",
    roles="principal_investigator",
    change_reason="Emergency unblinding requested",
    unblinded_access=False,
) -> dict:
    """Generate Gateway signature-compliant authentication headers.

    Args:
        user_id: The user identifier to embed in the gateway signature.
        roles: The role string to embed; defaults to ``principal_investigator``
            which is the minimum required role for the unblinding endpoint.
        change_reason: Audit-trail justification text.
        unblinded_access: If ``True``, the ``X-Unblinded-Access`` header is
            added so the principal can see unmasked allocation fields.

    Returns:
        dict: HTTP headers dict ready to be passed to an httpx/TestClient request.
    """
    timestamp = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
        unblinded_access=unblinded_access,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if unblinded_access:
        headers["X-Unblinded-Access"] = "true"
    return headers


def get_sig_token(
    user_id="test_inv", roles="principal_investigator", action="unblind"
) -> str:
    """Generate a 21 CFR Part 11 compliant step-up re-authentication token.

    Args:
        user_id: The subject's user identifier to embed as ``sub`` and ``username``.
        roles: The role to embed in the token ``roles`` claim.
        action: The specific action this token grants permission for.

    Returns:
        str: A HS256-signed JWT string.
    """
    payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 60.0,
    }
    return jwt.encode(payload, "internal-gateway-secret-12345", algorithm="HS256")


def get_unblind_payload(
    reason_code: str = "SAE-Life-Threatening-Event",
    justification: str = "Critical adverse event: patient non-responsive, immediate intervention required per protocol.",
) -> dict:
    """Return a valid UnblindRequest JSON payload with compliant dual-custody shares.

    The shares use the two approved CustodianEnum values and numerically valid
    Shamir-share coordinates (x > 0, y >= 0) so the payload passes Pydantic
    schema validation without reaching the cryptographic layer.

    Args:
        reason_code: One of the three approved ``UnblindingReasonCode`` values.
        justification: Clinical justification text; must be >= 50 characters.

    Returns:
        dict: A JSON-serialisable dict matching the ``UnblindRequest`` schema.
    """
    return {
        "reason_code": reason_code,
        "justification": justification,
        "shares": [
            {"custodian": "Lead Unblinded Statistician", "version": 1, "x": 1, "y": 42},
            {"custodian": "IDMC", "version": 1, "x": 2, "y": 87},
        ],
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and drop tables after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_unblind_missing_sig_token() -> None:
    """The request fails with a 401 status code if the required signature token header is missing."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a randomized subject using real state machine transitions
        async with db_manager.get_session_maker()() as session:
            subj = ClinicalSubject(
                subject_id="SUBJ-001",
                study_id="STUDY-1",
                kit_reference="KIT-1004",
            )
            session.add(subj)
            await session.flush()
            subj.status = "ENROLLED"
            await session.flush()
            subj.status = "RANDOMIZED"
            await session.commit()

        # Send request without X-Sig-Token header
        headers = get_auth_headers(
            roles="principal_investigator", unblinded_access=True
        )
        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-001/unblind",
            headers=headers,
            json=get_unblind_payload(),
        )
        assert res.status_code == 401
        assert res.json()["detail"] == "REAUTHENTICATION_REQUIRED"


@pytest.mark.asyncio
async def test_unblind_screening_status_error() -> None:
    """The system returns a 400 error if a user attempts to unblind a subject in screening status."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a subject in SCREENING status
        async with db_manager.get_session_maker()() as session:
            subj = ClinicalSubject(
                subject_id="SUBJ-002",
                study_id="STUDY-1",
            )
            session.add(subj)
            await session.commit()

        headers = get_auth_headers(
            roles="principal_investigator", unblinded_access=True
        )
        headers["X-Sig-Token"] = get_sig_token(roles="principal_investigator")
        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-002/unblind",
            headers=headers,
            json=get_unblind_payload(),
        )
        assert res.status_code == 400
        assert (
            "Subject has not been randomized; treatment allocation cannot be unblinded."
            in res.json()["detail"]
        )


@pytest.mark.asyncio
async def test_unblind_withdrawn_status_error() -> None:
    """The system returns a 400 error if a user attempts to unblind a subject in withdrawn status."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a subject in WITHDRAWN status (can transition from SCREENING to WITHDRAWN directly)
        async with db_manager.get_session_maker()() as session:
            subj = ClinicalSubject(
                subject_id="SUBJ-002-W",
                study_id="STUDY-1",
            )
            session.add(subj)
            await session.flush()
            subj.status = "WITHDRAWN"
            await session.commit()

        headers = get_auth_headers(
            roles="principal_investigator", unblinded_access=True
        )
        headers["X-Sig-Token"] = get_sig_token(roles="principal_investigator")
        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-002-W/unblind",
            headers=headers,
            json=get_unblind_payload(),
        )
        assert res.status_code == 400
        assert (
            "Subject has not been randomized; treatment allocation cannot be unblinded."
            in res.json()["detail"]
        )


@pytest.mark.asyncio
async def test_unblind_subject_not_found() -> None:
    """Attempting to unblind a non-existent subject returns a 404 error."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(
            roles="principal_investigator", unblinded_access=True
        )
        headers["X-Sig-Token"] = get_sig_token(roles="principal_investigator")
        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-999/unblind",
            headers=headers,
            json=get_unblind_payload(),
        )
        assert res.status_code == 404
        assert "Subject not found" in res.json()["detail"]


@pytest.mark.asyncio
async def test_unblind_success_authorized_access() -> None:
    """An authorized investigator with verified unblinded access can see the unmasked treatment arm and drug code."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a randomized subject
        async with db_manager.get_session_maker()() as session:
            subj = ClinicalSubject(
                subject_id="SUBJ-003",
                study_id="STUDY-1",
                kit_reference="KIT-777",
            )
            session.add(subj)
            await session.flush()
            subj.status = "ENROLLED"
            await session.flush()
            subj.status = "RANDOMIZED"

            # Add a SubjectRandomization record
            from apps.execution.cryptography import AllocationKeyManager

            key_mgr = AllocationKeyManager()
            encrypted_alloc = key_mgr.encrypt({"allocation": "Arm A Active"})

            rand = SubjectRandomization(
                study_id="STUDY-1",
                subject_id="SUBJ-003",
                encrypted_allocation=encrypted_alloc,
                kit_reference="KIT-777",
            )
            session.add(rand)
            await session.commit()

        # Investigator with unblinded access = True
        headers = get_auth_headers(
            roles="principal_investigator", unblinded_access=True
        )
        headers["X-Sig-Token"] = get_sig_token(roles="principal_investigator")

        from unittest.mock import AsyncMock, patch

        with (
            patch(
                "apps.execution.cryptography.AllocationKeyManager.load_from_db",
                new_callable=AsyncMock,
            ),
            patch(
                "apps.execution.cryptography.AllocationKeyManager.decrypt_with_shares",
                return_value={"allocation": "Arm A Active"},
            ),
        ):
            res = await client.post(
                "/api/v1/execution/subjects/SUBJ-003/unblind",
                headers=headers,
                json=get_unblind_payload(),
            )
        assert res.status_code == 200
        data = res.json()

        assert data["subject_id"] == "SUBJ-003"
        assert data["status"] == "UNBLINDED"
        assert data["is_unblinded"] is True
        assert data["treatment_arm"] == "Arm A Active"
        assert data["drug_code"] == "KIT-777"
        assert data["unblinded_by"] == "test_inv"
        expected_reason = "SAE-Life-Threatening-Event: Critical adverse event: patient non-responsive, immediate intervention required per protocol."
        assert data["unblinded_reason"] == expected_reason
        assert data["unblinded_at"] is not None

        # Verify subject is updated in DB
        async with db_manager.get_session_maker()() as session:
            stmt = select(ClinicalSubject).where(
                ClinicalSubject.subject_id == "SUBJ-003"
            )
            result = await session.execute(stmt)
            subj_db = result.scalars().first()
            assert subj_db.status == "UNBLINDED"
            assert subj_db.is_unblinded is True
            assert subj_db.unblinded_by == "test_inv"
            assert subj_db.unblinded_reason == expected_reason


@pytest.mark.asyncio
async def test_unblind_success_masked_access() -> None:
    """An unauthorized role or an investigator without unblinded access sees masked values (BLINDED/Obfuscated Kit)."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create an active subject with randomization record
        async with db_manager.get_session_maker()() as session:
            subj = ClinicalSubject(
                subject_id="SUBJ-004",
                study_id="STUDY-1",
                kit_reference="KIT-999",
            )
            session.add(subj)
            await session.flush()
            subj.status = "ENROLLED"
            await session.flush()
            subj.status = "RANDOMIZED"
            await session.flush()
            subj.status = "ACTIVE"

            from apps.execution.cryptography import AllocationKeyManager

            key_mgr = AllocationKeyManager()
            encrypted_alloc = key_mgr.encrypt({"allocation": "Arm A Active"})

            rand = SubjectRandomization(
                study_id="STUDY-1",
                subject_id="SUBJ-004",
                encrypted_allocation=encrypted_alloc,
                kit_reference="KIT-999",
            )
            session.add(rand)
            await session.commit()

        # Investigator without unblinded access (unblinded_access = False)
        # Even without unblinded_access, a PI can call the endpoint but sees masked allocation fields.
        headers = get_auth_headers(
            roles="principal_investigator", unblinded_access=False
        )
        headers["X-Sig-Token"] = get_sig_token(roles="principal_investigator")

        from unittest.mock import AsyncMock, patch

        with (
            patch(
                "apps.execution.cryptography.AllocationKeyManager.load_from_db",
                new_callable=AsyncMock,
            ),
            patch(
                "apps.execution.cryptography.AllocationKeyManager.decrypt_with_shares",
                return_value={"allocation": "Arm A Active"},
            ),
        ):
            res = await client.post(
                "/api/v1/execution/subjects/SUBJ-004/unblind",
                headers=headers,
                json=get_unblind_payload(),
            )
        assert res.status_code == 200
        data = res.json()

        assert data["subject_id"] == "SUBJ-004"
        assert data["status"] == "UNBLINDED"
        assert data["is_unblinded"] is True
        assert data["treatment_arm"] == "BLINDED"
        assert data["drug_code"] == "Obfuscated Kit"
        assert data["unblinded_by"] == "test_inv"
        assert data["unblinded_reason"] == (
            "SAE-Life-Threatening-Event: Critical adverse event: patient non-responsive, immediate intervention required per protocol."
        )
