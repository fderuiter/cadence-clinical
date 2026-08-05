"""
Unit tests for the Config-Driven Tagged Aggregator (scripts/sync_openapi_spec.py).

This test suite verifies:
1. Declarative configuration registry properties.
2. Deep merge behavior for schemas and lists.
3. Automated appending of service tags to routes.
4. Correct reference prefix rewriting.
"""

from scripts.sync_openapi_spec import (
    SERVICES_REGISTRY,
    apply_service_tags,
    deep_merge,
    rewrite_references,
)


def test_services_registry_completeness() -> None:
    """Verify that all 5 active services are registered declaratively."""
    active_services = {s["name"] for s in SERVICES_REGISTRY}
    expected_services = {"designer", "execution", "ctms", "etmf", "quality"}
    assert expected_services.issubset(active_services), (
        f"Missing services in registry: {expected_services - active_services}"
    )

    for service in SERVICES_REGISTRY:
        assert "module" in service
        assert "app_var" in service
        assert "tag" in service
        assert "prefix" in service


def test_deep_merge_behavior() -> None:
    """Verify that deep_merge correctly merges dictionaries and lists without duplicating elements."""
    dict1 = {
        "paths": {
            "/item": {
                "get": {
                    "tags": ["original"],
                    "responses": {"200": {"description": "OK"}},
                }
            }
        },
        "components": {
            "schemas": {
                "Item": {
                    "type": "object",
                    "properties": {"id": {"type": "integer"}},
                }
            }
        },
    }

    dict2 = {
        "paths": {
            "/item": {
                "get": {
                    "tags": ["original", "new"],
                }
            }
        },
        "components": {
            "schemas": {
                "Item": {
                    "properties": {"name": {"type": "string"}},
                }
            }
        },
    }

    merged = deep_merge(dict1, dict2)

    # Check that tags are merged and deduplicated
    tags = merged["paths"]["/item"]["get"]["tags"]
    assert tags == ["original", "new"]

    # Check that schemas are merged recursively
    properties = merged["components"]["schemas"]["Item"]["properties"]
    assert "id" in properties
    assert "name" in properties


def test_apply_service_tags() -> None:
    """Verify that apply_service_tags appends the service-level tag to all operations."""
    dummy_spec = {
        "paths": {
            "/user/profile": {
                "get": {
                    "tags": ["Profile"],
                    "summary": "Get Profile",
                },
                "post": {
                    "summary": "Update Profile",
                },
            }
        }
    }

    apply_service_tags(dummy_spec, "ctms")

    # Both get and post operations must have 'ctms' tag
    get_tags = dummy_spec["paths"]["/user/profile"]["get"]["tags"]
    assert "Profile" in get_tags
    assert "ctms" in get_tags

    post_tags = dummy_spec["paths"]["/user/profile"]["post"]["tags"]
    assert post_tags == ["ctms"]


def test_rewrite_references() -> None:
    """Verify that rewrite_references correctly prefixes schema reference pointers recursively."""
    schema = {
        "type": "object",
        "properties": {
            "user": {"$ref": "#/components/schemas/User"},
            "metadata": {
                "anyOf": [
                    {"$ref": "#/components/schemas/Meta1"},
                    {"type": "null"},
                ]
            },
        },
    }

    rewritten = rewrite_references(schema, "Ctms_")

    user_ref = rewritten["properties"]["user"]["$ref"]
    assert user_ref == "#/components/schemas/Ctms_User"

    meta_ref = rewritten["properties"]["metadata"]["anyOf"][0]["$ref"]
    assert meta_ref == "#/components/schemas/Ctms_Meta1"
