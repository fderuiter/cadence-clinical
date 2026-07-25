import hashlib
import hmac
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
    """Generate Gateway signature-compliant authentication headers."""
    import json

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
async def test_create_lab_reference_range_success() -> None:
    """Verify successful reference range creation with valid payload and roles."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        payload = {
            "study_id": "STUDY-001",
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

        # Roles: CRA (authorized)
        headers = get_auth_headers(
            roles="cra", change_reason="Adding WBC normal bounds"
        )
        res = await client.post(
            "/api/v1/execution/lab-ranges",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 201
        data = res.json()
        assert data["id"] is not None
        assert data["study_id"] == "STUDY-001"
        assert data["test_code"] == "WBC"
        assert data["source"] == "CENTRAL"
        assert data["version"] == 1
        assert data["is_deleted"] is False

        # Verify audit log recorded INSERT
        async with db_manager.get_session_maker()() as session:
            stmt = select(AuditLog).where(
                AuditLog.table_name == "lab_reference_ranges",
                AuditLog.action == "INSERT",
            )
            res_audit = await session.execute(stmt)
            logs = res_audit.scalars().all()
            assert len(logs) == 1
            assert logs[0].change_reason == "Adding WBC normal bounds"


@pytest.mark.asyncio
async def test_create_lab_reference_range_unauthorized() -> None:
    """Verify that unauthorized roles are blocked from creating ranges."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
        }

        # Role: subject (unauthorized)
        headers = get_auth_headers(roles="subject")
        res = await client.post(
            "/api/v1/execution/lab-ranges",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 403


@pytest.mark.asyncio
async def test_create_lab_reference_range_validation_errors() -> None:
    """Verify that logical and data validation rules are strictly enforced upon creation."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="cra")

        # 1. Non-blank fields
        bad_payload = {
            "study_id": " ",  # blank
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "cannot be blank" in res.json()["detail"]

        # 2. Invalid source
        bad_payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "INVALID-SOURCE",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "source" in res.json()["detail"]

        # 3. Invalid sex applicability
        bad_payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "X",  # invalid
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "sex_applicability" in res.json()["detail"]

        # 4. Negative age_low
        bad_payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
            "age_low": -1.0,
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "age_low" in res.json()["detail"]

        # 5. age_low > age_high
        bad_payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
            "age_low": 50.0,
            "age_high": 40.0,
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "age_low" in res.json()["detail"]

        # 6. low_bound > high_bound
        bad_payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
            "low_bound": 15.0,
            "high_bound": 10.0,
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "low_bound" in res.json()["detail"]

        # 7. critical_low > critical_high
        bad_payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
            "critical_low": 25.0,
            "critical_high": 20.0,
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "critical_low" in res.json()["detail"]

        # 8. critical_low > low_bound
        bad_payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
            "low_bound": 5.0,
            "critical_low": 6.0,
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "critical_low" in res.json()["detail"]

        # 9. critical_high < high_bound
        bad_payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
            "high_bound": 15.0,
            "critical_high": 12.0,
        }
        res = await client.post(
            "/api/v1/execution/lab-ranges", json=bad_payload, headers=headers
        )
        assert res.status_code == 400
        assert "critical_high" in res.json()["detail"]


@pytest.mark.asyncio
async def test_list_and_filter_lab_reference_ranges() -> None:
    """Verify that reference ranges can be listed and filtered correctly."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="cra")

        # Create range 1 (WBC - STUDY-A)
        range1 = {
            "study_id": "STUDY-A",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
        }
        res1 = await client.post(
            "/api/v1/execution/lab-ranges", json=range1, headers=headers
        )
        assert res1.status_code == 201

        # Create range 2 (RBC - STUDY-A)
        range2 = {
            "study_id": "STUDY-A",
            "test_code": "RBC",
            "test_name": "Red Blood Cell Count",
            "source": "LOCAL",
            "unit": "10^12/L",
            "normalized_unit": "10^12/L",
            "sex_applicability": "M",
        }
        res2 = await client.post(
            "/api/v1/execution/lab-ranges", json=range2, headers=headers
        )
        assert res2.status_code == 201

        # Create range 3 (WBC - STUDY-B)
        range3 = {
            "study_id": "STUDY-B",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
        }
        res3 = await client.post(
            "/api/v1/execution/lab-ranges", json=range3, headers=headers
        )
        assert res3.status_code == 201

        # 1. Default list (all active)
        res = await client.get("/api/v1/execution/lab-ranges", headers=headers)
        assert res.status_code == 200
        assert len(res.json()) == 3

        # 2. Filter by study_id
        res = await client.get(
            "/api/v1/execution/lab-ranges",
            params={"study_id": "STUDY-A"},
            headers=headers,
        )
        assert res.status_code == 200
        assert len(res.json()) == 2
        test_codes = {r["test_code"] for r in res.json()}
        assert test_codes == {"WBC", "RBC"}

        # 3. Filter by test_code
        res = await client.get(
            "/api/v1/execution/lab-ranges",
            params={"test_code": "WBC"},
            headers=headers,
        )
        assert res.status_code == 200
        assert len(res.json()) == 2
        studies = {r["study_id"] for r in res.json()}
        assert studies == {"STUDY-A", "STUDY-B"}

        # 4. Filter by source
        res = await client.get(
            "/api/v1/execution/lab-ranges",
            params={"source": "LOCAL"},
            headers=headers,
        )
        assert res.status_code == 200
        assert len(res.json()) == 1
        assert res.json()[0]["test_code"] == "RBC"


@pytest.mark.asyncio
async def test_get_and_update_lab_reference_range() -> None:
    """Verify single range retrieval and logical update of bounds."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="cra", change_reason="Initial load")

        # Create range
        payload = {
            "study_id": "STUDY-001",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
            "low_bound": 4.5,
            "high_bound": 11.0,
        }
        create_res = await client.post(
            "/api/v1/execution/lab-ranges", json=payload, headers=headers
        )
        assert create_res.status_code == 201
        range_id = create_res.json()["id"]

        # 1. Retrieve range
        get_res = await client.get(
            f"/api/v1/execution/lab-ranges/{range_id}", headers=headers
        )
        assert get_res.status_code == 200
        assert get_res.json()["low_bound"] == 4.5

        # 2. Retrieve with invalid ID (404)
        get_res_fake = await client.get(
            "/api/v1/execution/lab-ranges/fake-id", headers=headers
        )
        assert get_res_fake.status_code == 404

        # 3. Successful update
        update_payload = {
            "low_bound": 4.0,
            "high_bound": 12.0,
        }
        update_res = await client.put(
            f"/api/v1/execution/lab-ranges/{range_id}",
            json=update_payload,
            headers=get_auth_headers(roles="cra", change_reason="Updating bounds"),
        )
        assert update_res.status_code == 200
        assert update_res.json()["low_bound"] == 4.0
        assert update_res.json()["high_bound"] == 12.0
        assert update_res.json()["version"] == 2

        # 4. Contradictory update based on merged state
        # Setting critical_low > existing low_bound (which is now 4.0)
        bad_update = {
            "critical_low": 5.0,  # invalid since low_bound is 4.0
        }
        bad_res = await client.put(
            f"/api/v1/execution/lab-ranges/{range_id}",
            json=bad_update,
            headers=get_auth_headers(roles="cra"),
        )
        assert bad_res.status_code == 400


@pytest.mark.asyncio
async def test_soft_delete_lab_reference_range() -> None:
    """Verify soft-delete moves state to is_deleted and excludes it from default listings and matching."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="cra")

        # Create
        payload = {
            "study_id": "STUDY-DEL",
            "test_code": "PLATELETS",
            "test_name": "Platelets",
            "source": "CENTRAL",
            "unit": "10^9/L",
            "normalized_unit": "10^9/L",
            "sex_applicability": "ALL",
            "low_bound": 150.0,
            "high_bound": 450.0,
        }
        res_create = await client.post(
            "/api/v1/execution/lab-ranges", json=payload, headers=headers
        )
        assert res_create.status_code == 201
        range_id = res_create.json()["id"]

        # Delete
        res_delete = await client.delete(
            f"/api/v1/execution/lab-ranges/{range_id}",
            headers=get_auth_headers(roles="cra", change_reason="Deleting range"),
        )
        assert res_delete.status_code == 200
        assert res_delete.json()["is_deleted"] is True
        assert res_delete.json()["version"] == 2

        # Excluded from default list
        res_list = await client.get("/api/v1/execution/lab-ranges", headers=headers)
        assert len(res_list.json()) == 0

        # Included in list when include_deleted=True
        res_list_all = await client.get(
            "/api/v1/execution/lab-ranges",
            params={"include_deleted": True},
            headers=headers,
        )
        assert len(res_list_all.json()) == 1
        assert res_list_all.json()[0]["id"] == range_id
