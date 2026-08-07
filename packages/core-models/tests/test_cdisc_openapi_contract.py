"""Contract parity test suite for CDISC Gateway OpenAPI schemas.

Requirements: PRD-SYS-001
"""

import json
from pathlib import Path

import packages  # noqa: F401
from apps.gateway.main import app

REPO_ROOT = Path(__file__).resolve().parents[3]
OPENAPI_SCHEMA_PATH = REPO_ROOT / "docs" / "openapi" / "cdisc_openapi.json"


def test_cdisc_openapi_file_exists_and_valid() -> None:
    """Validate that exported CDISC OpenAPI JSON schema file exists and is valid JSON.

    Requirements: PRD-SYS-001
    """
    assert OPENAPI_SCHEMA_PATH.exists(), "cdisc_openapi.json artifact missing"
    content = OPENAPI_SCHEMA_PATH.read_text(encoding="utf-8")
    schema = json.loads(content)
    assert "openapi" in schema
    assert "paths" in schema
    assert "components" in schema


def test_cdisc_openapi_paths_coverage() -> None:
    """Validate OpenAPI schema contains all expected CDISC API endpoints.

    Requirements: PRD-SYS-001
    """
    schema = app.openapi()
    paths = schema.get("paths", {})

    expected_paths = [
        "/api/v1/cdisc/products",
        "/api/v1/cdisc/cdash/{domain_code}",
        "/api/v1/cdisc/sdtm/{domain_code}",
        "/api/v1/cdisc/codelists/{codelist_code}",
    ]

    for expected_path in expected_paths:
        assert expected_path in paths, f"Missing expected CDISC path: {expected_path}"
        path_item = paths[expected_path]
        assert "get" in path_item, f"GET operation missing for {expected_path}"


def test_cdisc_openapi_component_schemas() -> None:
    """Validate OpenAPI schema includes CDISC data model definitions.

    Requirements: PRD-SYS-001
    """
    schema = app.openapi()
    schemas = schema.get("components", {}).get("schemas", {})

    expected_schemas = [
        "CdiscProductSummary",
        "CdashDomainDefinition",
        "SdtmDomainDefinition",
        "CodelistDefinition",
        "CodelistTerm",
    ]

    for model_name in expected_schemas:
        assert model_name in schemas, (
            f"Missing model schema in OpenAPI specs: {model_name}"
        )


def test_cdisc_openapi_export_parity() -> None:
    """Validate live Gateway OpenAPI schema matches exported docs file.

    Requirements: PRD-SYS-001
    """
    live_schema = app.openapi()
    exported_schema = json.loads(OPENAPI_SCHEMA_PATH.read_text(encoding="utf-8"))

    assert live_schema.get("info", {}).get("title") == exported_schema.get(
        "info", {}
    ).get("title")
    assert set(live_schema.get("paths", {}).keys()) == set(
        exported_schema.get("paths", {}).keys()
    )
