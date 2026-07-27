import httpx
import pytest

from apps.designer.main import app as designer_app
from apps.execution.main import app as execution_app
from tests.test_designer_differences import get_auth_headers


@pytest.mark.asyncio
async def test_designer_validation_error_rfc7807():
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        # Send an invalid payload to POST /api/v1/mdr/concepts
        # concept_code and terminology are required, we send empty json
        response = await client.post(
            "/api/v1/mdr/concepts", json={}, headers=get_auth_headers()
        )

        # Validation error must return 400
        assert response.status_code == 400
        data = response.json()

        # Verify RFC 7807 required fields
        assert "type" in data
        assert "title" in data
        assert "status" in data
        assert "detail" in data
        assert "instance" in data
        assert "code" in data
        assert data["status"] == 400
        assert data["code"] == "REQUEST_VALIDATION_ERROR"
        assert (
            data["type"] == "https://api.cadence-clinical.com/errors/validation-failed"
        )
        assert data["title"] == "Request Validation Failed"

        # Verify invalid_params
        assert "invalid_params" in data
        assert len(data["invalid_params"]) > 0
        for p in data["invalid_params"]:
            assert "field" in p
            assert "reason" in p
            assert "value" in p


@pytest.mark.asyncio
async def test_execution_validation_error_rfc7807():
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
    ) as client:
        # Send an invalid payload to POST /api/v1/dictionaries/ucum/convert
        # value is required, we send empty json
        response = await client.post(
            "/api/v1/dictionaries/ucum/convert", json={}, headers=get_auth_headers()
        )

        # Validation error must return 400
        assert response.status_code == 400
        data = response.json()

        # Verify RFC 7807 required fields
        assert (
            data["type"] == "https://api.cadence-clinical.com/errors/validation-failed"
        )
        assert data["title"] == "Request Validation Failed"
        assert data["status"] == 400
        assert data["code"] == "REQUEST_VALIDATION_ERROR"

        # Verify invalid_params
        assert "invalid_params" in data
        assert len(data["invalid_params"]) > 0
        for p in data["invalid_params"]:
            assert "field" in p
            assert "reason" in p
            assert "value" in p
