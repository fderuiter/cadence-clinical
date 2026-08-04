"""Isolated router tests using FastAPI Depends dependency overrides to mock services and database sessions.

Requirements: Acceptance Criteria
- All endpoints retrieve their business/security instances through framework dependencies.
- Routing tests can run, mock errors, and pass without initializing a live database or integration.
- Mock definitions and security dependencies can be overridden dynamically and target specific routes individually.
- Overridden dependencies automatically clear and reset to production defaults after each isolated test execution completes.
"""

import hashlib
import os
import time
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest
from fastapi import status
from jose import jwt

from apps.execution.dependencies import (
    get_doa_service,
    get_offline_sync_engine,
    get_signature_builder,
)
from apps.execution.main import app
from apps.execution.services.doa_service import DOAService
from apps.execution.services.offline_sync import OfflineSyncEngine
from packages.security.signature_builder import CryptographicSignatureBuilder
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


def _make_auth_headers(
    user_id: str = "pi_user_101",
    roles: str = "principal_investigator",
    change_reason: str = "PI Casebook Approval",
    action: str = "/api/v1/execution/signatures/batch-sign-off",
    payload: dict | None = None,
) -> dict:
    """Generate signed Gateway authentication headers with X-Sig-Token and batch binding for testing."""
    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET,
        change_reason=change_reason,
        tenant_id="tenant_default",
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }

    sig_payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "semantic_action": "execution.form.signoff",
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 300.0,
        "jti": f"jti_{time.time()}_{user_id}",
    }

    if payload and payload.get("target_ids") is not None:
        norm_study = str(payload.get("study_id", "")).strip()
        norm_type = str(payload.get("target_type", "FORM")).strip().upper()
        target_ids = payload.get("target_ids", [])
        sorted_ids = sorted([str(tid).strip() for tid in target_ids])
        norm_ids = ",".join(sorted_ids)
        norm_reason = str(payload.get("signing_reason", "")).strip()
        binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
        batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()
        sig_payload["batch_id"] = batch_id

    sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
    headers["X-Sig-Token"] = sig_token
    return headers


@pytest.fixture(autouse=True)
def run_around_tests():
    """Clear dependency overrides before and after each test to ensure isolation."""
    app.dependency_overrides.clear()
    yield
    app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_offline_sync_endpoint_mocked_success() -> None:
    """Demonstrate mocking the business service (OfflineSyncEngine) to bypass DB entirely."""
    mock_engine = MagicMock(spec=OfflineSyncEngine)
    mock_engine.process_delta_batch = AsyncMock(
        return_value={
            "status": "MOCKED_SUCCESS",
            "processed_count": 99,
            "conflicts": [],
        }
    )

    app.dependency_overrides[get_offline_sync_engine] = lambda: mock_engine

    headers = _make_auth_headers(
        user_id="datamanager_test_user",
        roles="datamanager",
        change_reason="Execute Offline Synchronization",
        action="/api/v1/execution/offline/sync",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/execution/offline/sync",
            json={"dummy_payload": "data"},
            headers=headers,
        )

        assert response.status_code == status.HTTP_200_OK
        data = response.json()
        assert data["status"] == "MOCKED_SUCCESS"
        assert data["processed_count"] == 99
        mock_engine.process_delta_batch.assert_called_once_with(
            {"dummy_payload": "data"}
        )


@pytest.mark.asyncio
async def test_offline_sync_endpoint_mocked_failure() -> None:
    """Demonstrate mocking a service error (e.g. ValueError) to verify router error handling."""
    mock_engine = MagicMock(spec=OfflineSyncEngine)
    mock_engine.process_delta_batch = AsyncMock(
        side_effect=ValueError("Simulated synchronization mismatch")
    )

    app.dependency_overrides[get_offline_sync_engine] = lambda: mock_engine

    headers = _make_auth_headers(
        user_id="datamanager_test_user",
        roles="datamanager",
        change_reason="Execute Offline Synchronization",
        action="/api/v1/execution/offline/sync",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/execution/offline/sync",
            json={"bad_payload": "value"},
            headers=headers,
        )

        assert response.status_code == status.HTTP_400_BAD_REQUEST
        assert "Simulated synchronization mismatch" in response.json()["detail"]


@pytest.mark.asyncio
async def test_doa_assignment_mocked_success() -> None:
    """Demonstrate mocking the business service (DOAService) on delegation assignment endpoints."""
    mock_doa_service = MagicMock(spec=DOAService)

    from execution.doa_models import (
        DOAAssignmentRecord,
        DOATaskDelegationEnum,
        DOATaskRoleEnum,
    )

    mock_record = DOAAssignmentRecord(
        record_id="mock_doa_record_123",
        study_id="study_mock_01",
        site_id="site_mock_01",
        personnel_name="John Doe",
        personnel_email="john@doe.com",
        role=DOATaskRoleEnum.SUB_INVESTIGATOR,
        delegated_tasks=[DOATaskDelegationEnum.SUBJECT_INFORMED_CONSENT],
        start_date="2026-08-04",
        status="ACTIVE",
        is_active=True,
        signed_off=True,
    )
    mock_doa_service.add_assignment = MagicMock(return_value=mock_record)

    app.dependency_overrides[get_doa_service] = lambda: mock_doa_service

    payload = {
        "study_id": "study_mock_01",
        "site_id": "site_mock_01",
        "personnel_name": "John Doe",
        "personnel_email": "john@doe.com",
        "role": "SUB_INVESTIGATOR",
        "delegated_tasks": ["SUBJECT_INFORMED_CONSENT"],
        "start_date": "2026-08-04",
    }

    headers = _make_auth_headers(
        user_id="pi_user",
        roles="investigator",
        change_reason="PI Delegation Approval",
        action="/api/v1/execution/doa/assignment",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/execution/doa/assignment", json=payload, headers=headers
        )

        assert response.status_code == status.HTTP_201_CREATED
        data = response.json()
        assert data["record_id"] == "mock_doa_record_123"
        assert data["personnel_name"] == "John Doe"
        mock_doa_service.add_assignment.assert_called_once()


@pytest.mark.asyncio
async def test_signature_sign_off_mocked_builder() -> None:
    """Demonstrate mocking signature digest calculation without real CryptographicSignatureBuilder."""
    mock_builder = MagicMock(spec=CryptographicSignatureBuilder)
    mock_builder.compute_content_digest = MagicMock(
        return_value="mocked_content_sha256_digest"
    )

    app.dependency_overrides[get_signature_builder] = lambda: mock_builder

    req_body = {
        "study_id": "study_sig_001",
        "subject_id": "sub_sig_101",
        "target_type": "FORM",
        "target_ids": ["form_vs_01", "form_lb_01", "form_ae_01"],
        "target_form_ids": ["form_vs_01", "form_lb_01", "form_ae_01"],
        "signing_reason": "I approve the accuracy and completeness of this casebook",
        "password": "test-placeholder-pw",  # nosec B106
        "printed_name": "Dr. Alice Smith, MD",
    }

    headers = _make_auth_headers(
        user_id="pi_user_101",
        roles="principal_investigator",
        change_reason="PI Casebook Approval",
        payload=req_body,
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/execution/signatures/batch-sign-off",
            json=req_body,
            headers=headers,
        )

        assert response.status_code == 201
        data = response.json()
        assert data["content_digest"] == "mocked_content_sha256_digest"
        mock_builder.compute_content_digest.assert_called_once_with(
            ["form_vs_01", "form_lb_01", "form_ae_01"]
        )
