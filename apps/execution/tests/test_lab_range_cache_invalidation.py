import hashlib
import hmac
import json
import os
import time

import httpx
import pytest
import pytest_asyncio

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base
from apps.execution.lab_range_cache import lab_range_cache
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
    """Generate Gateway signature-compliant authentication headers."""
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


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    # Clear the singleton cache between tests
    lab_range_cache.clear()
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    lab_range_cache.clear()


@pytest.mark.asyncio
async def test_cache_invalidation_on_create() -> None:
    """Verify that creating a lab reference range invalidates the cache for that key."""
    # Pre-populate cache for STUDY-X, WBC
    lab_range_cache.set_cached("STUDY-X", "WBC", ["cached_dummy"])
    cached, is_expired = lab_range_cache.get_cached("STUDY-X", "WBC")
    assert cached == ["cached_dummy"]

    payload = {
        "study_id": "STUDY-X",
        "test_code": "WBC",
        "test_name": "White Blood Cell Count",
        "source": "CENTRAL",
        "site_id": None,
        "unit": "10^9/L",
        "normalized_unit": "10^9/L",
        "sex_applicability": "ALL",
        "age_low": 18.0,
        "age_high": 120.0,
        "low_bound": 4.5,
        "high_bound": 11.0,
        "critical_low": 2.0,
        "critical_high": 20.0,
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/v1/execution/lab-ranges",
            json=payload,
            headers=get_auth_headers(roles="cra", change_reason="Adding WBC range"),
        )
        assert res.status_code == 201

    # Check that the cache has been invalidated
    cached, is_expired = lab_range_cache.get_cached("STUDY-X", "WBC")
    assert cached is None


@pytest.mark.asyncio
async def test_cache_invalidation_on_delete() -> None:
    """Verify that deleting a lab reference range invalidates the cache for that key."""
    headers = get_auth_headers(roles="cra", change_reason="Initial setup")

    # Create range via API
    payload = {
        "study_id": "STUDY-Y",
        "test_code": "RBC",
        "test_name": "Red Blood Cell Count",
        "source": "CENTRAL",
        "unit": "10^12/L",
        "normalized_unit": "10^12/L",
        "sex_applicability": "ALL",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/v1/execution/lab-ranges",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 201
        range_id = res.json()["id"]

        # Pre-populate cache for STUDY-Y, RBC
        lab_range_cache.set_cached("STUDY-Y", "RBC", ["cached_dummy"])
        cached, is_expired = lab_range_cache.get_cached("STUDY-Y", "RBC")
        assert cached == ["cached_dummy"]

        # Soft delete the range
        res_del = await client.delete(
            f"/api/v1/execution/lab-ranges/{range_id}",
            headers=get_auth_headers(roles="cra", change_reason="Deleting RBC range"),
        )
        assert res_del.status_code == 200

    # Check that the cache has been invalidated
    cached, is_expired = lab_range_cache.get_cached("STUDY-Y", "RBC")
    assert cached is None


@pytest.mark.asyncio
async def test_cache_invalidation_on_update_no_key_change() -> None:
    """Verify that updating a lab reference range (without changing study_id/test_code) invalidates the original key."""
    headers = get_auth_headers(roles="cra", change_reason="Initial setup")

    payload = {
        "study_id": "STUDY-Z",
        "test_code": "ALT",
        "test_name": "Alanine Aminotransferase",
        "source": "CENTRAL",
        "unit": "U/L",
        "normalized_unit": "U/L",
        "sex_applicability": "ALL",
        "low_bound": 10.0,
        "high_bound": 40.0,
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/v1/execution/lab-ranges",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 201
        range_id = res.json()["id"]

        # Pre-populate cache for STUDY-Z, ALT
        lab_range_cache.set_cached("STUDY-Z", "ALT", ["cached_dummy"])
        cached, is_expired = lab_range_cache.get_cached("STUDY-Z", "ALT")
        assert cached == ["cached_dummy"]

        # Update without key changes (just change bounds)
        res_upd = await client.put(
            f"/api/v1/execution/lab-ranges/{range_id}",
            json={"low_bound": 12.0},
            headers=get_auth_headers(roles="cra", change_reason="Updating low bound"),
        )
        assert res_upd.status_code == 200

    # Check that the cache has been invalidated
    cached, is_expired = lab_range_cache.get_cached("STUDY-Z", "ALT")
    assert cached is None


@pytest.mark.asyncio
async def test_cache_invalidation_on_update_with_key_changes() -> None:
    """Verify that updating a lab reference range's study_id or test_code invalidates both old and new keys."""
    headers = get_auth_headers(roles="cra", change_reason="Initial setup")

    payload = {
        "study_id": "STUDY-Z",
        "test_code": "ALT",
        "test_name": "Alanine Aminotransferase",
        "source": "CENTRAL",
        "unit": "U/L",
        "normalized_unit": "U/L",
        "sex_applicability": "ALL",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/v1/execution/lab-ranges",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 201
        range_id = res.json()["id"]

        # Pre-populate cache for BOTH STUDY-Z, ALT and STUDY-NEW, ALT-NEW
        lab_range_cache.set_cached("STUDY-Z", "ALT", ["cached_dummy_old"])
        lab_range_cache.set_cached("STUDY-NEW", "ALT-NEW", ["cached_dummy_new"])

        # Update both study_id and test_code
        res_upd = await client.put(
            f"/api/v1/execution/lab-ranges/{range_id}",
            json={"study_id": "STUDY-NEW", "test_code": "ALT-NEW"},
            headers=get_auth_headers(roles="cra", change_reason="Updating keys"),
        )
        assert res_upd.status_code == 200

    # Verify BOTH old and new keys have been invalidated
    cached_old, is_expired = lab_range_cache.get_cached("STUDY-Z", "ALT")
    assert cached_old is None

    cached_new, is_expired = lab_range_cache.get_cached("STUDY-NEW", "ALT-NEW")
    assert cached_new is None


@pytest.mark.asyncio
async def test_cache_invalidation_on_recalculate() -> None:
    """Verify that triggering on-demand recalculation invalidates the cache for that key."""
    # Pre-populate cache for STUDY-RECALC, AST
    lab_range_cache.set_cached("STUDY-RECALC", "AST", ["cached_dummy"])
    cached, is_expired = lab_range_cache.get_cached("STUDY-RECALC", "AST")
    assert cached == ["cached_dummy"]

    recalc_payload = {
        "study_id": "STUDY-RECALC",
        "test_code": "AST",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=get_auth_headers(roles="cra", change_reason="Recalculating AST"),
        )
        assert res.status_code == 200

    # Verify cache has been invalidated
    cached, is_expired = lab_range_cache.get_cached("STUDY-RECALC", "AST")
    assert cached is None
