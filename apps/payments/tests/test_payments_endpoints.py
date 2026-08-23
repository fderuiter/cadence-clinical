"""Endpoint integration tests for Site Payments API routes.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

import pytest
from httpx import AsyncClient

from apps.payments.tests.conftest import get_auth_headers


@pytest.mark.asyncio
async def test_health_check_endpoint(client: AsyncClient):
    """Validate payments service health check endpoint."""
    res = await client.get("/health")
    assert res.status_code == 200
    assert res.json() == {"status": "ok", "service": "payments"}


@pytest.mark.asyncio
async def test_batch_event_ingestion_and_queries(client: AsyncClient):
    """Validate batch event ingestion endpoint and subsequent queries.

    @req:ADR-2199
    @req:PRD-PAY-002
    @req:PRD-PAY-003
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-BATCH-01"
    site_id = "SITE-BATCH-01"

    # Setup contract
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Batch Ingestion Contract",
            "overhead_percentage": 10.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "V1",
            "form_id": "F1",
            "procedure_code": "P1",
            "procedure_name": "Procedure 1",
            "base_amount": 200.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    batch_payload = {
        "ecrf_submissions": [
            {
                "study_id": study_id,
                "site_id": site_id,
                "subject_id": "SUBJ-B1",
                "visit_code": "V1",
                "form_id": "F1",
                "event_id": "EVT-BATCH-ECRF-01",
            },
            {
                "study_id": study_id,
                "site_id": site_id,
                "subject_id": "SUBJ-B2",
                "visit_code": "V1",
                "form_id": "F1",
                "event_id": "EVT-BATCH-ECRF-02",
            },
        ],
        "sdv_verifications": [],
        "milestones": [
            {
                "study_id": study_id,
                "site_id": site_id,
                "milestone_code": "MILE-SITE-INIT",
                "milestone_name": "Site Initiation Visit Complete",
                "amount": 3000.0,
                "verified_by": "cra_lead",
                "event_id": "EVT-BATCH-MILE-01",
            }
        ],
    }

    res = await client.post(
        "/api/v1/payments/events/batch", json=batch_payload, headers=headers
    )
    assert res.status_code == 200
    data = res.json()
    assert data["ecrf_submissions_processed"] == 2
    assert data["milestones_processed"] == 1
    assert data["total_processed"] == 3

    # Query Payables
    payables_res = await client.get(
        f"/api/v1/payments/payables?study_id={study_id}", headers=headers
    )
    assert payables_res.status_code == 200
    payables = payables_res.json()
    assert len(payables) == 3

    # Query Single Contract
    get_c = await client.get(
        f"/api/v1/payments/contracts/{contract_id}", headers=headers
    )
    assert get_c.status_code == 200
    assert get_c.json()["id"] == contract_id

    # Query Non-Existent Contract -> 404
    bad_c = await client.get(
        "/api/v1/payments/contracts/non-existent-id", headers=headers
    )
    assert bad_c.status_code == 404
