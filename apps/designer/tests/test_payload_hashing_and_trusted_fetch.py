import hashlib
import hmac
import json
import time
from unittest.mock import MagicMock, patch

import pytest
from fastapi.testclient import TestClient

from apps.designer.main import app as designer_app
from apps.designer.usdm_ingestion import (
    clear_validation_cache,
    validate_usdm_payload,
)

client = TestClient(designer_app)


@pytest.fixture(autouse=True)
def run_around_tests():
    # Clear the validation cache before and after each test
    clear_validation_cache()
    yield
    clear_validation_cache()


def get_auth_headers(roles="sponsor_designer"):
    user_id = "test-user"
    change_reason = "system_operation"
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        b"internal-gateway-secret-12345", serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


def get_valid_payload():
    return """
id: 00000000-0000-0000-0000-000000000001
name: Cached Validation Study
versions: []
"""


def test_payload_hashing_and_cache_bypass():
    payload = get_valid_payload()

    # 1. First validation (cache miss)
    start_miss = time.perf_counter()
    report_miss = validate_usdm_payload(payload)
    _ = (time.perf_counter() - start_miss) * 1000

    assert report_miss.validity is True
    # Ensure there is no bypass warning on a cache miss
    assert not any("bypassed" in warn.reason for warn in report_miss.warnings)

    # 2. Second validation (cache hit)
    start_hit = time.perf_counter()
    report_hit = validate_usdm_payload(payload)
    duration_hit = (time.perf_counter() - start_hit) * 1000

    assert report_hit.validity is True
    # Ensure a bypass warning is added for GxP compliance tracking
    bypass_warnings = [
        warn for warn in report_hit.warnings if "bypassed" in warn.reason
    ]
    assert len(bypass_warnings) == 1
    assert bypass_warnings[0].field == "payload"

    # Verify that the cache hit is extremely fast (well under 200ms)
    assert duration_hit < 200.0


def test_modified_payload_triggers_full_validation():
    payload_1 = get_valid_payload()
    payload_2 = """
id: 00000000-0000-0000-0000-000000000001
name: Cached Validation Study - Modified
versions: []
"""

    # Validate first payload (success, cached)
    report_1 = validate_usdm_payload(payload_1)
    assert report_1.validity is True

    # Validate modified payload (must be cache miss)
    report_2 = validate_usdm_payload(payload_2)
    assert report_2.validity is True
    # Modified payload should NOT contain bypass warning
    assert not any("bypassed" in warn.reason for warn in report_2.warnings)


def test_target_version_update_invalidates_cache():
    payload = get_valid_payload()

    # Validate first payload (no override) -> cached
    report_1 = validate_usdm_payload(payload)
    assert report_1.validity is True

    # Validate same payload with explicit override (version updated / target version changes) -> cache miss
    report_2 = validate_usdm_payload(payload, override="v3")
    assert report_2.validity is True
    assert not any("bypassed" in warn.reason for warn in report_2.warnings)

    # Subsequent same call with override "v3" -> cache hit
    report_3 = validate_usdm_payload(payload, override="v3")
    assert report_3.validity is True
    assert any("bypassed" in warn.reason for warn in report_3.warnings)


def test_get_usdm_study_skips_recursive_validation():
    headers = get_auth_headers()
    # Retrieve the study from database and ensure validate_usdm_payload is NOT called.
    with patch(
        "apps.designer.presentation.routers.designer_routes.validate_usdm_payload"
    ) as mock_validate:
        # Request via HTTP GET
        response = client.get(
            "/api/v2/studies/study_1/usdm?format=json", headers=headers
        )
        assert response.status_code == 200

        # Verify that validate_usdm_payload was NEVER called during the load/retrieve operation
        mock_validate.assert_not_called()


def test_export_workflows_continue_preflight_validation():
    # Make sure we still run usdm_model Study validation on export
    from usdm_model import Study

    headers = get_auth_headers(roles="sponsor_designer,protocol_export:generate")

    with patch.object(
        Study, "model_validate", return_value=MagicMock()
    ) as mock_validate:
        # Simulate export request with proper headers
        client.get(
            "/api/v1/studies/study_1/export?format=pdf&output=narrative",
            headers=headers,
        )
        assert mock_validate.called
