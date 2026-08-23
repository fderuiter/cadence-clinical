"""Test suite for Study Designer Startup Prerequisite Rules Matrix router.

Requirements:
- PRD-SSU-001: Country-Aware Startup Prerequisite Rules Matrix
- PRD-SYS-121: Protocol Design Startup Matrix Artifact Generation
- ADR-2199: Architecture Decision Record for Startup Prerequisite Matrix
"""

import time

from fastapi.testclient import TestClient

from apps.designer.main import app
from packages.testing.security import generate_signature


def get_auth_headers(
    roles: str = "Sponsor Designer",
    change_reason: str = "Designer Startup Matrix Test",
    user_id: str = "protocol_author_01",
) -> dict:
    timestamp = str(time.time())
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


def test_designer_startup_matrix_presets_and_jurisdictions():
    """Validate Study Designer startup prerequisite matrix presets and jurisdictions metadata.

    @req:PRD-SSU-001, PRD-SYS-121, ADR-2199
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="Sponsor Designer")

    # 1. Presets endpoint
    res_presets = client.get("/api/v1/designer/startup-matrix/presets", headers=headers)
    assert res_presets.status_code == 200
    presets = res_presets.json()
    assert "US" in presets
    assert "EU" in presets
    assert "JP" in presets
    assert "GB" in presets
    assert "GLOBAL" in presets

    # 2. Jurisdictions metadata endpoint
    res_juris = client.get(
        "/api/v1/designer/startup-matrix/jurisdictions", headers=headers
    )
    assert res_juris.status_code == 200
    jurisdictions = res_juris.json()
    assert len(jurisdictions) >= 5
    codes = {j["code"] for j in jurisdictions}
    assert {"US", "EU", "JP", "GB", "GLOBAL"}.issubset(codes)


def test_designer_startup_matrix_template_generation():
    """Validate generating country-specific startup prerequisite rules template for multi-regional study design.

    @req:PRD-SSU-001, PRD-SYS-121, ADR-2199
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="Sponsor Designer")

    gen_payload = {
        "study_id": "STUDY-DESIGNER-MULTI-01",
        "country_codes": ["US", "EU", "JP", "GB"],
    }
    res_gen = client.post(
        "/api/v1/designer/startup-matrix/generate-template",
        json=gen_payload,
        headers=headers,
    )
    assert res_gen.status_code == 200
    gen_data = res_gen.json()
    assert gen_data["study_id"] == "STUDY-DESIGNER-MULTI-01"
    assert len(gen_data["generated_rules"]) >= 25
    assert "US" in gen_data["summary_by_country"]
    assert "EU" in gen_data["summary_by_country"]
    assert "JP" in gen_data["summary_by_country"]
    assert "GB" in gen_data["summary_by_country"]
