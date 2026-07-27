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
    roles="site investigator",
    change_reason="Emergency unblinding requested",
    unblinded_access=False,
) -> dict:
    """Generate Gateway signature-compliant authentication headers."""
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
    user_id="test_inv", roles="site investigator", action="unblind"
) -> str:
    """Generate a 21 CFR Part 11 compliant re-authentication token."""
    payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 60.0,
    }
    return jwt.encode(payload, "internal-gateway-secret-12345", algorithm="HS256")


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
        headers = get_auth_headers(roles="site investigator", unblinded_access=True)
        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-001/unblind", headers=headers
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

        headers = get_auth_headers(roles="site investigator", unblinded_access=True)
        headers["X-Sig-Token"] = get_sig_token()
        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-002/unblind", headers=headers
        )
        assert res.status_code == 400
        assert (
            "Transition from SCREENING to UNBLINDED is forbidden"
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

        headers = get_auth_headers(roles="site investigator", unblinded_access=True)
        headers["X-Sig-Token"] = get_sig_token()
        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-002-W/unblind", headers=headers
        )
        assert res.status_code == 400
        assert (
            "Transition from WITHDRAWN to UNBLINDED is forbidden"
            in res.json()["detail"]
        )


@pytest.mark.asyncio
async def test_unblind_subject_not_found() -> None:
    """Attempting to unblind a non-existent subject returns a 404 error."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="site investigator", unblinded_access=True)
        headers["X-Sig-Token"] = get_sig_token()
        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-999/unblind", headers=headers
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
        headers = get_auth_headers(roles="site investigator", unblinded_access=True)
        headers["X-Sig-Token"] = get_sig_token()

        from unittest.mock import patch

        with patch(
            "apps.execution.cryptography.AllocationKeyManager.decrypt"
        ) as mock_decrypt:
            mock_decrypt.return_value = {"allocation": "Arm A Active"}
            res = await client.post(
                "/api/v1/execution/subjects/SUBJ-003/unblind", headers=headers
            )
        assert res.status_code == 200
        data = res.json()

        assert data["subject_id"] == "SUBJ-003"
        assert data["status"] == "UNBLINDED"
        assert data["is_unblinded"] is True
        assert data["treatment_arm"] == "Arm A Active"
        assert data["drug_code"] == "KIT-777"
        assert data["unblinded_by"] == "test_inv"
        assert data["unblinded_reason"] == "Emergency unblinding requested"
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
            assert subj_db.unblinded_reason == "Emergency unblinding requested"


@pytest.mark.asyncio
async def test_unblind_success_masked_access() -> None:
    """An unauthorized role or an investigator without unblinded access sees masked values (BLINDED/Obfuscated Kit)."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create an active subject
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
            await session.commit()

        # Investigator without unblinded access (unblinded_access = False)
        headers = get_auth_headers(roles="site investigator", unblinded_access=False)
        headers["X-Sig-Token"] = get_sig_token()

        res = await client.post(
            "/api/v1/execution/subjects/SUBJ-004/unblind", headers=headers
        )
        assert res.status_code == 200
        data = res.json()

        assert data["subject_id"] == "SUBJ-004"
        assert data["status"] == "UNBLINDED"
        assert data["is_unblinded"] is True
        assert data["treatment_arm"] == "BLINDED"
        assert data["drug_code"] == "Obfuscated Kit"
        assert data["unblinded_by"] == "test_inv"
        assert data["unblinded_reason"] == "Emergency unblinding requested"
