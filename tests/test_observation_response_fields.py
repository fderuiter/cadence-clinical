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
async def test_observation_response_extended_fields() -> None:
    """Verify that creating an observation populates the extended fields in the response."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a subject
        subject_payload = {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-001",
            "demographics": {
                "name": "Jane Doe",
                "birthdate": "1995-06-15",
                "gender": "Female",
            },
        }
        headers = get_auth_headers(roles="cra", change_reason="Creating Jane Doe")
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=headers,
        )
        assert res_subj.status_code == 200

        # 2. Create a reference range
        range_payload = {
            "study_id": "STUDY-001",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "source": "CENTRAL",
            "site_id": None,
            "unit": "g/dL",
            "normalized_unit": "g/dL",
            "sex_applicability": "ALL",
            "age_low": None,
            "age_high": None,
            "low_bound": 12.0,
            "high_bound": 16.0,
            "critical_low": 8.0,
            "critical_high": 20.0,
        }
        res_range = await client.post(
            "/api/v1/execution/lab-ranges",
            json=range_payload,
            headers=headers,
        )
        assert res_range.status_code == 201

        # 3. Create an observation
        obs_payload = {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-001",
            "domain": "LB",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "value": 11.0,
            "unit": "g/dL",
            "lab_source": "CENTRAL",
        }
        res_obs = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=headers,
        )
        assert res_obs.status_code == 200
        obs_data = res_obs.json()

        # 4. Verify extended fields
        assert "range_indicator" in obs_data
        assert "is_out_of_range" in obs_data
        assert "reference_range_low" in obs_data
        assert "reference_range_high" in obs_data
        assert "lab_source" in obs_data

        # 11.0 is below low_bound (12.0), so indicator is LOW, out of range is True
        assert obs_data["range_indicator"] == "LOW"
        assert obs_data["is_out_of_range"] is True
        assert obs_data["reference_range_low"] == 12.0
        assert obs_data["reference_range_high"] == 16.0
        assert obs_data["lab_source"] == "CENTRAL"
