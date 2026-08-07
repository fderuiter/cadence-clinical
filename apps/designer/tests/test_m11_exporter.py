"""Unit and integration test suite for ICH M11 Word document and USDM JSON exporter.

Requirements: PRD-SYS-001
"""

import json

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.exporters.m11_exporter import M11ProtocolExporter
from apps.designer.main import app
from apps.designer.tests.test_synopsis_router import _make_auth_headers

client = TestClient(app)


def test_export_ich_m11_docx() -> None:
    """Validate exporting USDM protocol payload to ICH M11 Word (.docx) document stream.

    Requirements: PRD-SYS-001
    """
    study_payload = {
        "id": "study_m11_101",
        "name": "M11 Study Protocol",
        "protocolTitle": "Phase III Trial of Compound X in Adult Subjects",
        "usdmVersion": "3.0",
        "studyDesigns": [
            {
                "name": "Parallel Group Design",
                "objectives": [{"name": "Demonstrate superior efficacy"}],
                "arms": [{"name": "Active 50mg", "armType": "Experimental"}],
            }
        ],
        "eligibilityCriteria": [
            {"criterionType": "Inclusion", "text": "Age 18 to 75 years"},
        ],
    }

    exporter = M11ProtocolExporter()
    docx_bytes = exporter.export_ich_m11_docx(study_payload)

    assert isinstance(docx_bytes, bytes)
    assert len(docx_bytes) > 2000
    assert docx_bytes.startswith(b"PK\x03\x04")


def test_export_usdm_json() -> None:
    """Validate exporting study payload to canonical USDM v3.0 JSON format.

    Requirements: PRD-SYS-001
    """
    study_payload = {
        "id": "study_m11_102",
        "name": "USDM Export Study",
    }

    exporter = M11ProtocolExporter()
    json_str = exporter.export_usdm_json(study_payload)

    data = json.loads(json_str)
    assert data["id"] == "study_m11_102"
    assert data["usdmVersion"] == "3.0"


def test_protocol_export_router_endpoint() -> None:
    """Validate GET /api/v1/designer/export/m11/{study_id} download endpoint.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(change_reason="Download ICH M11 export document")
    response = client.get(
        "/api/v1/designer/export/m11/study_export_103?format=docx",
        headers=headers,
    )

    assert response.status_code == 200
    assert (
        "attachment; filename=protocol_study_export_103_m11.docx"
        in response.headers["content-disposition"]
    )
    assert response.content.startswith(b"PK\x03\x04")
