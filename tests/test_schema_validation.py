"""
Tests for active CI verification via app schema introspection.

This test module verifies:
1. Safe namespacing of schema components during dynamic OpenAPI aggregation.
2. Graceful isolation of corrupt downstream schemas (avoiding gateway crashes).
3. Correct rewriting of nested references.
4. Protection against infinite recursion loops in the rewrite engine.
5. Functionality of the static validation CI script.

Compliance:
- Gate 1: Google-style docstrings and clear comments.
- Gate 3: Tested and verified under standard pytest execution.
"""

from typing import Any, Dict
from unittest.mock import patch

import httpx
import pytest
from fastapi.testclient import TestClient

from apps.gateway.main import app
from scripts.validate_schemas import validate_schemas


def test_rewrite_references_nested_references(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test that schema paths and component names are rewritten correctly
    when processing schemas with nested references.

    Ensures that recursively nested references (e.g. User -> Profile -> Image)
    have their internal $ref paths successfully prefixed and isolated.
    """
    mock_schema = {
        "openapi": "3.1.0",
        "paths": {"/item": {"get": {"responses": {"200": {"description": "OK"}}}}},
        "components": {
            "schemas": {
                "User": {
                    "type": "object",
                    "properties": {
                        "id": {"type": "integer"},
                        "profile": {"$ref": "#/components/schemas/Profile"},
                    },
                },
                "Profile": {
                    "type": "object",
                    "properties": {
                        "bio": {"type": "string"},
                        "avatar": {"$ref": "#/components/schemas/Image"},
                    },
                },
                "Image": {
                    "type": "object",
                    "properties": {
                        "url": {"type": "string"},
                    },
                },
            }
        },
    }

    class MockResponse:
        status_code = 200

        def json(self) -> Dict[str, Any]:
            return mock_schema

    async def mock_get(*args: Any, **kwargs: Any) -> MockResponse:
        return MockResponse()

    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get)

    with TestClient(app) as client:
        response = client.get("/openapi.json")
        assert response.status_code == 200
        data = response.json()

        # Check path and prefix aggregation
        assert "/designer/item" in data["paths"]
        assert "/execution/item" in data["paths"]

        # Component schemas must be properly namespaced and prefixed recursively
        designer_user = data["components"]["schemas"].get("Designer_User")
        assert designer_user is not None
        assert (
            designer_user["properties"]["profile"]["$ref"]
            == "#/components/schemas/Designer_Profile"
        )

        designer_profile = data["components"]["schemas"].get("Designer_Profile")
        assert designer_profile is not None
        assert (
            designer_profile["properties"]["avatar"]["$ref"]
            == "#/components/schemas/Designer_Image"
        )

        assert "Designer_Image" in data["components"]["schemas"]


def test_rewrite_references_recursion_protection(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Test that the rewrite_references engine identifies and protects against
    infinite recursion loops, preventing memory exhaustion or stack overflows.

    Creates a physical cyclic self-reference in the Python dictionary structure
    to verify that visited-state tracking avoids infinite loop crashes.
    """
    cyclic_schema: Dict[str, Any] = {
        "openapi": "3.1.0",
        "paths": {},
        "components": {
            "schemas": {
                "CircularModel": {
                    "type": "object",
                }
            }
        },
    }
    # Form an actual circular cycle inside the Python dictionaries
    cyclic_schema["components"]["schemas"]["CircularModel"]["loop"] = cyclic_schema[
        "components"
    ]["schemas"]["CircularModel"]

    class MockCyclicResponse:
        status_code = 200

        def json(self) -> Dict[str, Any]:
            return cyclic_schema

    async def mock_get(*args: Any, **kwargs: Any) -> MockCyclicResponse:
        return MockCyclicResponse()

    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get)

    with TestClient(app) as client:
        # If there's no cycle detection, this call will exhaust the stack/memory and crash
        response = client.get("/openapi.json")
        assert response.status_code == 200
        data = response.json()
        assert "Designer_CircularModel" in data["components"]["schemas"]


def test_gateway_graceful_handling_invalid_downstream(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Test that the gateway documentation page renders healthy API routes successfully
    even if one downstream service is returning invalid schemas or failing entirely.

    Mocks 'tickets' service returning an invalid non-dictionary payload,
    'safety' service throwing a hard network connection error,
    and all other services returning 100% correct healthy schemas.
    """
    healthy_schema = {
        "openapi": "3.1.0",
        "paths": {"/healthy": {"get": {"responses": {"200": {"description": "OK"}}}}},
        "components": {"schemas": {"HealthyModel": {"type": "string"}}},
    }

    async def mock_get_multi(self: Any, url: str, **kwargs: Any) -> Any:
        class MockCustomResponse:
            def __init__(self, status_code: int, raw_data: Any):
                self.status_code = status_code
                self._data = raw_data

            def json(self) -> Any:
                return self._data

        if "tickets" in url or "8009" in url:
            # Corrupt invalid response
            return MockCustomResponse(200, "not-a-valid-openapi-dict")
        elif "safety" in url or "8008" in url:
            # Failed connection entirely
            raise httpx.ConnectError("Mock connection refused")
        else:
            # Successful healthy response
            return MockCustomResponse(200, healthy_schema)

    # Patch the AsyncClient instance method directly
    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get_multi)

    with TestClient(app) as client:
        response = client.get("/openapi.json")
        assert response.status_code == 200
        data = response.json()

        # Corrupted and failed routes should be safely omitted/isolated
        assert "/tickets/healthy" not in data["paths"]
        assert "/safety/healthy" not in data["paths"]

        # Healthy routes must be preserved and rendered successfully
        assert "/designer/healthy" in data["paths"]
        assert "/execution/healthy" in data["paths"]
        assert "Designer_HealthyModel" in data["components"]["schemas"]
        assert "Execution_HealthyModel" in data["components"]["schemas"]


def test_static_schema_validation_script() -> None:
    """
    Test the static CI schema validation script behavior.

    Asserts that:
    1. A clean workspace run returns True.
    2. Modifying service configurations to produce duplicate prefixes/collisions
       correctly triggers validation failure and returns False.
    """
    # 1. Verify clean validation passes
    assert validate_schemas() is True

    # 2. Simulate namespace prefix collision to trigger verification failure
    bad_config = {
        "designer": {"app": app, "prefix": "Duplicate_"},
        "execution": {"app": app, "prefix": "Duplicate_"},
    }
    with patch("scripts.validate_schemas.SERVICES_CONFIG", bad_config):
        assert validate_schemas() is False
