"""Integration test suite for Safety Gateway dispatch and SAE reconciliation REST API.

Requirements: PRD-SYS-001
"""

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.tests.test_lock_router import _make_auth_headers
from apps.execution.main import app

client = TestClient(app)


def test_dispatch_safety_report_post_endpoint() -> None:
    """Validate POST /api/v1/execution/safety/dispatch dispatches ICSR report to gateway.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(
        user_id="pv_officer_01",
        roles="safety_officer",
        change_reason="Expedited Safety Dispatch",
    )

    response = client.post(
        "/api/v1/execution/safety/dispatch",
        json={
            "study_id": "study_safety_api_01",
            "subject_id": "sub_safety_101",
            "safety_report_id": "US-SPONSOR-2026-9999",
            "destination_gateway": "ARGUS",
            "expedited": True,
            "reason_for_change": "Expedited 15-day reporting requirement",
        },
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "DISPATCHED"
    assert data["safety_report_id"] == "US-SPONSOR-2026-9999"
    assert "dispatch_id" in data


def test_reconcile_sae_cases_post_endpoint() -> None:
    """Validate POST /api/v1/execution/safety/reconcile executes AE/SAE reconciliation.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()

    xml_sample = """<?xml version="1.0" encoding="UTF-8"?>
    <icsr>
        <safety_report_id>US-SPONSOR-2026-0001</safety_report_id>
        <study_id>study_safety_api_01</study_id>
        <subject_id>sub_safety_202</subject_id>
        <reaction_pt>Hypotension</reaction_pt>
        <meddra_code>10021097</meddra_code>
        <onset_date>2026-07-28</onset_date>
        <seriousness_criteria>HOSPITALIZATION</seriousness_criteria>
        <causality>POSSIBLE</causality>
    </icsr>
    """

    response = client.post(
        "/api/v1/execution/safety/reconcile",
        json={
            "study_id": "study_safety_api_01",
            "edc_ae_events": [
                {
                    "subject_id": "sub_safety_202",
                    "onset_date": "2026-07-28",
                    "meddra_code": "10021097",
                }
            ],
            "safety_cases_xml": [xml_sample],
        },
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["matched_cases_count"] == 1
    assert data["reconciliation_status"] == "CONCORDANT"
