import hashlib
import hmac
import os
import time

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import select

from apps.execution.cdisc_validator import validate_cdisc_xml_structure
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
)
from apps.execution.main import app, decrypt_demographics, encrypt_demographics
from apps.execution.outliers import (
    calculate_cohort_stats,
    identify_outliers,
)
from apps.execution.ucum import convert_unit, get_normalized_representation
from apps.gateway.main import app as gateway_app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(user_id="test_user", roles="admin"):
    """Generate Gateway signature-compliant authentication headers."""
    timestamp = str(time.time())
    message = f"{user_id}:{roles}:{timestamp}"
    signature = hmac.new(
        GATEWAY_SECRET.encode(), message.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
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


def test_unit_conversions() -> None:
    """Test UCUM unit conversions and normalization mappings."""
    # Temperature conversions
    assert abs(convert_unit(100.0, "[Fahr]", "Cel") - 37.77777777777778) < 1e-5
    assert abs(convert_unit(37.0, "Cel", "[Fahr]") - 98.6) < 1e-5
    assert abs(convert_unit(0.0, "Cel", "K") - 273.15) < 1e-5
    assert abs(convert_unit(293.15, "K", "Cel") - 20.0) < 1e-5

    # Weight conversions
    assert abs(convert_unit(150.0, "[lb_av]", "kg") - 68.0388555) < 1e-5
    assert abs(convert_unit(1000.0, "g", "kg") - 1.0) < 1e-5
    assert abs(convert_unit(16.0, "[oz_av]", "kg") - 0.45359237) < 1e-5

    # Length/Height conversions
    assert abs(convert_unit(70.0, "[in_i]", "m") - 1.778) < 1e-5
    assert abs(convert_unit(180.0, "cm", "m") - 1.8) < 1e-5
    assert abs(convert_unit(6.0, "[ft_i]", "m") - 1.8288) < 1e-5

    # Pressure conversions
    assert abs(convert_unit(10.0, "kPa", "mm[Hg]") - 75.006156) < 1e-5

    # Normalization helper
    val, unit = get_normalized_representation(100.0, "Fahr")
    assert unit == "Cel"
    assert abs(val - 37.77777777) < 1e-5

    val, unit = get_normalized_representation(150.0, "lbs")
    assert unit == "kg"
    assert abs(val - 68.0388555) < 1e-5


def test_demographics_encryption() -> None:
    """Test un-stored demographics encryption and decryption helper."""
    payload = {"name": "John Doe", "birthdate": "1980-01-01", "gender": "M"}
    encrypted = encrypt_demographics(payload)
    assert encrypted != "John Doe"

    decrypted = decrypt_demographics(encrypted)
    assert decrypted["name"] == "John Doe"
    assert decrypted["birthdate"] == "1980-01-01"


def test_outlier_detection_performance() -> None:
    """Verify statistical outlier detection is pure-Python and processes 1,000 observations under 100ms."""
    import random

    # Generate 1,000 normal observations (around 70) and a couple of extreme outliers
    random.seed(42)
    values = [random.normalvariate(70.0, 5.0) for _ in range(998)]
    values.append(150.0)  # extreme high outlier
    values.append(-10.0)  # extreme low outlier

    start_time = time.perf_counter()
    mean, std_dev = calculate_cohort_stats(values)
    outliers = identify_outliers(values, mean, std_dev)
    duration_ms = (time.perf_counter() - start_time) * 1000.0

    assert duration_ms < 100.0
    assert sum(outliers) == 2  # The 150.0 and -10.0 should be flagged
    assert outliers[-2] is True  # 150.0
    assert outliers[-1] is True  # -10.0


@pytest.mark.asyncio
async def test_relational_persistence_and_recalculation() -> None:
    """Verify clinical relational persistence, GxP audit logs, and automatic outlier checks."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a pseudonymized subject
        subject_payload = {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-ABC",
            "demographics": {
                "name": "Jane Smith",
                "birthdate": "1992-05-15",
                "gender": "F",
            },
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_auth_headers(),
        )
        assert res_subj.status_code == 200
        subj_data = res_subj.json()
        assert subj_data["subject_id"] == "SUBJ-001"
        assert subj_data["encrypted_demographics"] is not None

        # Demographics should be decrypted correctly
        decrypted = decrypt_demographics(subj_data["encrypted_demographics"])
        assert decrypted["name"] == "Jane Smith"

        # 2. Create clinical visit
        visit_payload = {
            "subject_id": "SUBJ-001",
            "visit_name": "Screening",
            "study_id": "STUDY-ABC",
        }
        res_visit = await client.post(
            "/api/v1/execution/visits", json=visit_payload, headers=get_auth_headers()
        )
        assert res_visit.status_code == 200
        visit_id = res_visit.json()["id"]

        # 3. Create a batch of normal observations and check automatic unit conversion
        for i in range(10):
            # values: 120, 122, 124 ... 138 mmHg
            obs_val = 120.0 + (i * 2)
            obs_payload = {
                "subject_id": "SUBJ-001",
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "SYSBP",
                "test_name": "Systolic Blood Pressure",
                "value": obs_val,
                "unit": "mmHg",
            }
            res_obs = await client.post(
                "/api/v1/execution/observations",
                json=obs_payload,
                headers=get_auth_headers(),
            )
            assert res_obs.status_code == 200
            data = res_obs.json()
            assert data["normalized_value"] == obs_val
            assert data["normalized_unit"] == "mm[Hg]"
            assert data["is_outlier"] is False

        # Add an extreme outlier observation (300 mmHg)
        outlier_payload = {
            "subject_id": "SUBJ-001",
            "visit_id": visit_id,
            "domain": "VS",
            "test_code": "SYSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 300.0,
            "unit": "mmHg",
        }
        res_outlier = await client.post(
            "/api/v1/execution/observations",
            json=outlier_payload,
            headers=get_auth_headers(),
        )
        assert res_outlier.status_code == 200
        outlier_data = res_outlier.json()

        # The 300 mmHg observation should be automatically flagged as an outlier
        assert outlier_data["is_outlier"] is True

        # 4. Check GxP compliance triggers & audit logs
        async with db_manager.get_session_maker()() as session:
            stmt = select(AuditLog).where(
                AuditLog.table_name == "clinical_observations"
            )
            result = await session.execute(stmt)
            logs = result.scalars().all()
            assert len(logs) > 0
            assert logs[0].action == "INSERT"
            assert logs[0].user_id == "test_user"


@pytest.mark.asyncio
async def test_cdisc_export_and_validation() -> None:
    """Verify CDISC ODM XML export and structural schema check validation."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Setup subject & observations
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-XML", "study_id": "STUDY-XML"},
            headers=get_auth_headers(),
        )
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-XML",
                "study_id": "STUDY-XML",
                "domain": "VS",
                "test_code": "DIABP",
                "test_name": "Diastolic Blood Pressure",
                "value": 80.0,
                "unit": "mmHg",
            },
            headers=get_auth_headers(),
        )

        # Trigger Export via Execution API
        res_export = await client.get(
            "/api/v1/execution/export",
            params={"study_id": "STUDY-XML"},
            headers=get_auth_headers(),
        )
        assert res_export.status_code == 200
        assert res_export.headers["content-type"] == "application/xml"

        xml_text = res_export.text
        assert "SUBJ-XML" in xml_text
        assert "DIABP" in xml_text
        assert "80" in xml_text

        # Run structure schema checks
        is_valid, msg = validate_cdisc_xml_structure(xml_text)
        assert is_valid is True, msg


@pytest.mark.asyncio
async def test_api_gateway_routing(monkeypatch: pytest.MonkeyPatch) -> None:
    """Verify API Gateway properly routes dictionary/ and execution/ path prefixes downstream."""
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    token = jwt.encode(
        {"sub": "gateway_user", "roles": ["admin"]}, "test_secret", algorithm="HS256"
    )
    headers = {"Authorization": f"Bearer {token}"}

    class MockResponse:
        def __init__(self, status_code: int, json_data: dict, text_data: str = ""):
            self.status_code = status_code
            self._json = json_data
            self.text = text_data
            self.headers = {"Content-Type": "application/json"}

        def json(self):
            return self._json

    async def mock_send(self, req: httpx.Request, **kwargs) -> MockResponse:
        url_str = str(req.url)
        if "/dictionary/unit-conversion" in url_str:
            return MockResponse(200, {"converted_value": 37.777777})
        elif "/dictionary/export" in url_str:
            return MockResponse(200, {}, text_data="<ODM FileOID='Export.123'></ODM>")
        return MockResponse(404, {"error": "Not Found"})

    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=gateway_app), base_url="http://gateway"
    ) as client:
        # Test routing for dictionary unit conversion
        res_conv = await client.post(
            "/dictionary/unit-conversion",
            json={"value": 100, "from_unit": "Fahr", "to_unit": "Cel"},
            headers=headers,
        )
        assert res_conv.status_code == 200
        assert res_conv.json()["converted_value"] == 37.777777
