"""Unit and integration tests for DIA TMF Exchange Mechanism Standard (EMS) export packages.

Validates compliant generation of tmf-ems.xml, tmf-ems.json, and SHA-256 checksums in ZIP archives.
"""

import io
import json
import time
import zipfile

import defusedxml.ElementTree as ET
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.models import Base
from apps.etmf.main import app
from packages.testing.security import generate_signature


@pytest.fixture(autouse=True)
def allow_legacy_signatures_for_this_suite(monkeypatch):
    monkeypatch.setenv("ALLOW_LEGACY_MOCK_SIGNATURES", "true")


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    timestamp = str(time.time())
    user_id = "test_user"
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


@pytest.mark.asyncio
async def test_etmf_ems_export_package_structure():
    """Test generating a standardized DIA TMF EMS export ZIP archive."""
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="sysadmin,sponsor_designer",
        change_reason="Ingest documents for EMS",
    )

    study_id = "STUDY-EMS-101"

    # Ingest document 1: Protocol (Zone 1)
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": study_id,
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol.pdf",
            "content": "Clinical Trial Protocol content version 1.",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )

    # Ingest document 2: IB (Zone 2)
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": study_id,
            "artifact_type": "Investigator's Brochure",
            "filename": "ib.pdf",
            "content": "Investigator Brochure content version 1.",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )

    # Export EMS package
    inspector_headers = get_auth_headers(roles="regulatory_inspector,auditor")
    resp = client.get(
        f"/api/v1/etmf/studies/{study_id}/ems-export?study_title=Phase+III+Cardio+Trial",
        headers=inspector_headers,
    )
    assert resp.status_code == 200
    assert resp.headers["content-type"] == "application/zip"
    assert f"study_{study_id}_tmf_ems.zip" in resp.headers["content-disposition"]

    # Inspect ZIP contents
    zip_bytes = resp.content
    with zipfile.ZipFile(io.BytesIO(zip_bytes), "r") as z:
        file_list = z.namelist()
        assert "tmf-ems.json" in file_list
        assert "tmf-ems.xml" in file_list
        assert "checksums.sha256" in file_list

        # Validate JSON manifest
        json_data = json.loads(z.read("tmf-ems.json").decode("utf-8"))
        assert json_data["ems_version"] == "1.0"
        assert json_data["study_id"] == study_id
        assert json_data["study_title"] == "Phase III Cardio Trial"
        assert json_data["document_count"] == 2
        assert len(json_data["documents"]) == 2

        doc_codes = {d["artifact_code"] for d in json_data["documents"]}
        assert "01.01.01" in doc_codes
        assert "02.01.01" in doc_codes

        # Validate XML manifest
        xml_str = z.read("tmf-ems.xml").decode("utf-8")
        root = ET.fromstring(xml_str)
        assert root.tag == "TmfExchangePackage"
        assert root.attrib["studyId"] == study_id
        header = root.find("Header")
        assert header is not None
        assert header.find("StudyTitle").text == "Phase III Cardio Trial"
        docs_node = root.find("Documents")
        assert docs_node is not None
        assert len(docs_node.findall("Document")) == 2

        # Validate SHA-256 Checksums
        checksums_txt = z.read("checksums.sha256").decode("utf-8")
        lines = [
            line.strip() for line in checksums_txt.strip().split("\n") if line.strip()
        ]
        assert len(lines) == 2
        for line in lines:
            sha, path = line.split("  ", 1)
            assert len(sha) == 64
            assert path in file_list
            actual_content = z.read(path)
            import hashlib

            assert hashlib.sha256(actual_content).hexdigest() == sha


@pytest.mark.asyncio
async def test_etmf_ems_export_permissions():
    """Verify RBAC permissions for EMS package export."""
    client = TestClient(app)
    unauthorized_headers = get_auth_headers(
        roles="anonymous_guest", change_reason="Unauthorized export"
    )

    resp = client.get(
        "/api/v1/etmf/studies/study_001/ems-export",
        headers=unauthorized_headers,
    )
    assert resp.status_code == 403
