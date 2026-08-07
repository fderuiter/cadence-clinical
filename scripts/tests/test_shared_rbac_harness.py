from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest

from tests.rbac_helpers import (
    auditor,
    cra,
    data_manager,
    external_monitor,
    investigator,
    sponsor_admin,
    sponsor_designer,
)


@pytest.mark.asyncio
async def test_persona_builders_contain_correct_claims():
    """Verify that named persona builders generate correct headers with valid signatures."""
    # Test sponsor_admin persona
    admin_headers = sponsor_admin(site_id="site_999", sponsor_id="sponsor_abc")
    assert admin_headers["X-User-Id"] == "test_sponsor_admin"
    assert admin_headers["X-User-Roles"] == "sponsor_admin"
    assert admin_headers["X-Site-Id"] == "site_999"
    assert admin_headers["X-Assigned-Sites"] == "site_999"
    assert admin_headers["X-Sponsor-Id"] == "sponsor_abc"
    assert admin_headers["X-Assigned-Studies"] == "sponsor_abc"
    assert admin_headers["X-Signature-Version"] == "2"
    assert "X-Gateway-Signature" in admin_headers

    # Test investigator persona
    inv_headers = investigator(site_id="site_123")
    assert inv_headers["X-User-Id"] == "test_investigator"
    assert inv_headers["X-User-Roles"] == "investigator"
    assert inv_headers["X-Site-Id"] == "site_123"

    # Test cra persona
    cra_headers = cra(change_reason="CRA review")
    assert cra_headers["X-User-Roles"] == "cra"
    assert cra_headers["X-Change-Reason"] == "CRA review"

    # Test auditor persona
    aud_headers = auditor()
    assert aud_headers["X-User-Roles"] == "auditor"

    # Test external_monitor persona
    mon_headers = external_monitor()
    assert mon_headers["X-User-Roles"] == "external_monitor"


@pytest.mark.asyncio
async def test_shared_sqlite_dbs_and_clients(
    shared_sqlite_dbs, execution_client, etmf_client
):
    """
    Verify that the SQLite fixtures successfully setup in-memory databases and
    allow the execution and etmf clients to handle authenticated calls.
    """
    # Use auditor headers to access etmf audit-logs (which should be 200 for auditors in test_rbac)
    headers = auditor()
    resp = await etmf_client.get("/api/v1/etmf/audit-logs", headers=headers)
    assert resp.status_code == 200
    assert "items" in resp.json()

    # Access execution queries (which should be 200 for a data_manager, returning an empty list)
    dm_headers = data_manager()
    resp_exec = await execution_client.get(
        "/api/v1/execution/queries", headers=dm_headers
    )
    assert resp_exec.status_code == 200
    assert isinstance(resp_exec.json(), list)


@pytest.mark.asyncio
async def test_mock_designer_driver(designer_client, mock_designer_driver):
    """Verify that mock_designer_driver fixture properly injects Neo4j mock and intercepts driver calls."""
    from apps.designer.main import app as designer_app

    assert designer_app.state.driver is not None

    # Setup database query mock results for create_study_arm to succeed (returning 201)
    lock_res = AsyncMock()
    duplicate_res = AsyncMock()
    duplicate_res.single.return_value = None

    create_record_mock = MagicMock()
    create_record_mock.__getitem__.return_value = "arm_harness"
    create_res = AsyncMock()
    create_res.single.return_value = create_record_mock

    # Configure our tx_mock run to return success for create study arm queries
    mock_designer_driver._tx_mock.run.side_effect = [
        lock_res,
        duplicate_res,
        create_res,
    ]

    headers = sponsor_designer()
    res = await designer_client.post(
        "/api/v1/studies/study_harness/versions/v_draft/arms",
        json={
            "id": "arm_harness",
            "properties": {"name": "Arm Harness", "type": "Active"},
        },
        headers=headers,
    )
    assert res.status_code == 201
    assert res.json() == {"status": "success", "id": "arm_harness"}


@pytest.mark.asyncio
async def test_cross_service_interception(
    shared_sqlite_dbs, intercept_cross_service_calls
):
    """
    Verify that global httpx.AsyncClient.send interception correctly captures outbound URLs,
    routes them in-process to the respective apps, and maintains headers for authentication.
    """
    headers = auditor()
    async with httpx.AsyncClient() as client:
        # Outbound call targeting the local etmf service port/URL
        resp = await client.get(
            "http://localhost:8003/api/v1/etmf/audit-logs", headers=headers
        )
        assert resp.status_code == 200
        assert "items" in resp.json()

        # Outbound call targeting the execution service
        dm_headers = data_manager()
        resp_exec = await client.get(
            "http://localhost:8002/api/v1/execution/queries", headers=dm_headers
        )
        assert resp_exec.status_code == 200
        assert isinstance(resp_exec.json(), list)
