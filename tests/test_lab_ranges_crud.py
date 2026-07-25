import hashlib
import hmac
import os
import time
import json
import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    LabReferenceRange,
)
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


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
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_lab_reference_range_successful_crud() -> None:
    """Verify standard successful create, read, update, list, and soft-delete flows."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a LabReferenceRange
        create_payload = {
            "study_id": "STUDY-X",
            "test_code": "ALT",
            "test_name": "Alanine Aminotransferase",
            "source": "CENTRAL",
            "unit": "U/L",
            "normalized_unit": "U/L",
            "sex_applicability": "ALL",
            "age_low": 18.0,
            "age_high": 65.0,
            "low_bound": 10.0,
            "high_bound": 40.0,
            "critical_low": 5.0,
            "critical_high": 100.0,
        }
        res_create = await client.post(
            "/api/v1/execution/lab-ranges",
            json=create_payload,
            headers=get_auth_headers(),
        )
        assert res_create.status_code == 201
        data = res_create.json()
        range_id = data["id"]
        assert data["study_id"] == "STUDY-X"
        assert data["test_code"] == "ALT"
        assert data["test_name"] == "Alanine Aminotransferase"
        assert data["source"] == "CENTRAL"
        assert data["unit"] == "U/L"
        assert data["normalized_unit"] == "U/L"
        assert data["sex_applicability"] == "ALL"
        assert data["age_low"] == 18.0
        assert data["age_high"] == 65.0
        assert data["low_bound"] == 10.0
        assert data["high_bound"] == 40.0
        assert data["critical_low"] == 5.0
        assert data["critical_high"] == 100.0
        assert data["version"] == 1
        assert data["is_deleted"] is False

        # 2. Retrieve the created range
        res_get = await client.get(
            f"/api/v1/execution/lab-ranges/{range_id}",
            headers=get_auth_headers(),
        )
        assert res_get.status_code == 200
        assert res_get.json()["id"] == range_id

        # 3. Update the created range
        update_payload = {
            "study_id": "STUDY-X",
            "test_code": "ALT",
            "test_name": "Alanine Aminotransferase Updated",
            "source": "LOCAL",
            "site_id": "SITE-1",
            "unit": "U/L",
            "normalized_unit": "U/L",
            "sex_applicability": "M",
            "age_low": 20.0,
            "age_high": 60.0,
            "low_bound": 12.0,
            "high_bound": 45.0,
            "critical_low": 6.0,
            "critical_high": 110.0,
        }
        res_update = await client.put(
            f"/api/v1/execution/lab-ranges/{range_id}",
            json=update_payload,
            headers=get_auth_headers(),
        )
        assert res_update.status_code == 200
        data_up = res_update.json()
        assert data_up["test_name"] == "Alanine Aminotransferase Updated"
        assert data_up["source"] == "LOCAL"
        assert data_up["site_id"] == "SITE-1"
        assert data_up["sex_applicability"] == "M"
        assert data_up["age_low"] == 20.0
        assert data_up["age_high"] == 60.0
        assert data_up["low_bound"] == 12.0
        assert data_up["high_bound"] == 45.0
        assert data_up["critical_low"] == 6.0
        assert data_up["critical_high"] == 110.0
        # Version should have incremented
        assert data_up["version"] == 2

        # 4. List and filter ranges
        # Create a second range to verify list filtering
        create_payload_2 = {
            "study_id": "STUDY-Y",
            "test_code": "AST",
            "test_name": "Aspartate Aminotransferase",
            "source": "CENTRAL",
            "unit": "U/L",
            "normalized_unit": "U/L",
            "sex_applicability": "F",
        }
        res_create_2 = await client.post(
            "/api/v1/execution/lab-ranges",
            json=create_payload_2,
            headers=get_auth_headers(),
        )
        assert res_create_2.status_code == 201
        range_id_2 = res_create_2.json()["id"]

        # List all (un-deleted) ranges
        res_list = await client.get(
            "/api/v1/execution/lab-ranges",
            headers=get_auth_headers(),
        )
        assert res_list.status_code == 200
        items = res_list.json()
        assert len(items) == 2

        # Filter by study_id
        res_list_study = await client.get(
            "/api/v1/execution/lab-ranges?study_id=STUDY-Y",
            headers=get_auth_headers(),
        )
        assert res_list_study.status_code == 200
        items_study = res_list_study.json()
        assert len(items_study) == 1
        assert items_study[0]["id"] == range_id_2

        # Filter by test_code
        res_list_test = await client.get(
            "/api/v1/execution/lab-ranges?test_code=ALT",
            headers=get_auth_headers(),
        )
        assert res_list_test.status_code == 200
        items_test = res_list_test.json()
        assert len(items_test) == 1
        assert items_test[0]["id"] == range_id

        # Filter by source
        res_list_source = await client.get(
            "/api/v1/execution/lab-ranges?source=CENTRAL",
            headers=get_auth_headers(),
        )
        assert res_list_source.status_code == 200
        items_source = res_list_source.json()
        assert len(items_source) == 1
        assert items_source[0]["id"] == range_id_2

        # 5. Soft-delete range
        res_delete = await client.delete(
            f"/api/v1/execution/lab-ranges/{range_id}",
            headers=get_auth_headers(),
        )
        assert res_delete.status_code == 200
        assert res_delete.json()["is_deleted"] is True

        # Retrieving a soft-deleted range by ID should return 404
        res_get_deleted = await client.get(
            f"/api/v1/execution/lab-ranges/{range_id}",
            headers=get_auth_headers(),
        )
        assert res_get_deleted.status_code == 404

        # Listing by default should now exclude the soft-deleted range
        res_list_default = await client.get(
            "/api/v1/execution/lab-ranges",
            headers=get_auth_headers(),
        )
        assert res_list_default.status_code == 200
        items_default = res_list_default.json()
        assert len(items_default) == 1
        assert items_default[0]["id"] == range_id_2

        # Listing with include_deleted=true should return both
        res_list_all = await client.get(
            "/api/v1/execution/lab-ranges?include_deleted=true",
            headers=get_auth_headers(),
        )
        assert res_list_all.status_code == 200
        items_all = res_list_all.json()
        assert len(items_all) == 2


@pytest.mark.asyncio
async def test_lab_reference_range_invariants_validation() -> None:
    """Verify that contradictory or invalid range definitions are rejected with 422."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Template valid payload
        valid_template = {
            "study_id": "STUDY-V",
            "test_code": "ALT",
            "test_name": "ALT test",
            "source": "CENTRAL",
            "unit": "U/L",
            "normalized_unit": "U/L",
        }

        # Case 1: Invalid source
        p = dict(valid_template, source="INVALID_SOURCE")
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Source must be either CENTRAL or LOCAL" in res.text

        # Case 2: Invalid sex
        p = dict(valid_template, sex_applicability="X")
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Sex applicability must be one of M, F, ALL, U, or None" in res.text

        # Case 3: Blank identifier
        p = dict(valid_template, study_id="   ")
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Study ID must be a nonblank string" in res.text

        # Case 4: Blank unit
        p = dict(valid_template, unit="")
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Unit must be a nonblank string" in res.text

        # Case 5: Negative age_low
        p = dict(valid_template, age_low=-1.0)
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Age low must be non-negative" in res.text

        # Case 6: age_low > age_high
        p = dict(valid_template, age_low=50.0, age_high=10.0)
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Age low cannot be greater than Age high" in res.text

        # Case 7: low_bound > high_bound
        p = dict(valid_template, low_bound=100.0, high_bound=50.0)
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Low bound cannot be greater than High bound" in res.text

        # Case 8: critical_low > low_bound
        p = dict(valid_template, low_bound=10.0, critical_low=15.0)
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Critical low cannot be greater than Low bound" in res.text

        # Case 9: critical_high < high_bound
        p = dict(valid_template, high_bound=40.0, critical_high=35.0)
        res = await client.post("/api/v1/execution/lab-ranges", json=p, headers=get_auth_headers())
        assert res.status_code == 422
        assert "Critical high cannot be less than High bound" in res.text


@pytest.mark.asyncio
async def test_lab_reference_range_role_permissions() -> None:
    """Verify role checks: auditor roles should be blocked from mutations (403)."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Try to create as auditor
        payload = {
            "study_id": "STUDY-P",
            "test_code": "ALT",
            "test_name": "ALT test",
            "source": "CENTRAL",
            "unit": "U/L",
            "normalized_unit": "U/L",
        }
        res_create = await client.post(
            "/api/v1/execution/lab-ranges",
            json=payload,
            headers=get_auth_headers(roles="auditor"),
        )
        assert res_create.status_code == 403

        # 2. Try to update as inspector
        res_update = await client.put(
            "/api/v1/execution/lab-ranges/some-id",
            json=payload,
            headers=get_auth_headers(roles="inspector"),
        )
        assert res_update.status_code == 403

        # 3. Try to delete as regulatory_inspector
        res_delete = await client.delete(
            "/api/v1/execution/lab-ranges/some-id",
            headers=get_auth_headers(roles="regulatory_inspector"),
        )
        assert res_delete.status_code == 403
