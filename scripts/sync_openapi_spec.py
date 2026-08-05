#!/usr/bin/env python3
"""Sync OpenAPI Specification inside docs/SDLC/03_API_Integration_Specification.md.

This script aggregates the OpenAPI specifications of the active services
(designer, execution, ctms, etmf, and quality) completely in-memory (offline)
using a declarative registry. It gracefully handles any service load failures,
appends service tags to all aggregated routes, and replaces the yaml block
under Section 7 of docs/SDLC/03_API_Integration_Specification.md.
"""

import importlib
import os
import sys
from typing import Any

import yaml

# Prevent import errors due to missing secret keys in offline environment
os.environ.setdefault("TERMINOLOGY_OFFLINE", "true")
os.environ.setdefault("ALLOW_MOCK_SIGNATURES", "1")
os.environ.setdefault("GATEWAY_SECRET", "internal-gateway-secret-12345")
os.environ.setdefault("SIGNING_SECRET", "designer-amendment-secure-key-12345")
os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "test-gxp-audit-secret-key-placeholder-abc"
)
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)
os.environ.setdefault("QUALITY_DATABASE_URL", "sqlite+aiosqlite:///:memory:")
os.environ.setdefault("CTMS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")
os.environ.setdefault("ETMF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

# Set up python path for local imports and package paths
app_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if app_root not in sys.path:
    sys.path.insert(0, app_root)

packages_dir = os.path.join(app_root, "packages")
for name in [
    "core-models",
    "database",
    "deid",
    "security",
    "ui",
    "hexagonal",
    "storage",
]:
    pkg_path = os.path.join(packages_dir, name)
    if pkg_path not in sys.path:
        sys.path.insert(0, pkg_path)


# Declarative Configuration Registry for the 5 services
SERVICES_REGISTRY = [
    {
        "name": "designer",
        "module": "apps.designer.main",
        "app_var": "app",
        "tag": "designer",
        "prefix": "Designer_",
    },
    {
        "name": "execution",
        "module": "apps.execution.main",
        "app_var": "app",
        "tag": "execution",
        "prefix": "Execution_",
    },
    {
        "name": "ctms",
        "module": "apps.ctms.main",
        "app_var": "app",
        "tag": "ctms",
        "prefix": "Ctms_",
    },
    {
        "name": "etmf",
        "module": "apps.etmf.main",
        "app_var": "app",
        "tag": "etmf",
        "prefix": "ETMF_",
    },
    {
        "name": "quality",
        "module": "apps.quality.main",
        "app_var": "app",
        "tag": "quality",
        "prefix": "Quality_",
    },
]


def deep_merge(dict1, dict2):
    """Deep merge dict2 into dict1 in place."""
    for key, val in dict2.items():
        if isinstance(val, dict) and key in dict1 and isinstance(dict1[key], dict):
            deep_merge(dict1[key], val)
        elif isinstance(val, list) and key in dict1 and isinstance(dict1[key], list):
            for item in val:
                if item not in dict1[key]:
                    dict1[key].append(item)
        else:
            dict1[key] = val
    return dict1


def rewrite_references(data: Any, prefix: str, visited: set | None = None) -> Any:
    """Recursively rewrite component references in an OpenAPI schema payload."""
    if visited is None:
        visited = set()

    if id(data) in visited:
        return {
            "type": "object",
            "description": "Circular reference detected and isolated",
        }

    if isinstance(data, dict):
        visited.add(id(data))
        new_data = {}
        for k, v in data.items():
            if (
                k == "$ref"
                and isinstance(v, str)
                and v.startswith("#/components/schemas/")
            ):
                ref_name = v[len("#/components/schemas/") :]
                new_data[k] = f"#/components/schemas/{prefix}{ref_name}"
            else:
                new_data[k] = rewrite_references(v, prefix, visited)
        visited.remove(id(data))
        return new_data
    if isinstance(data, list):
        visited.add(id(data))
        new_list = [rewrite_references(item, prefix, visited) for item in data]
        visited.remove(id(data))
        return new_list
    return data


def apply_service_tags(spec, service_tag):
    """Automatically append standardized service-level tag to all routes in the spec."""
    paths = spec.get("paths", {})
    for path, path_item in paths.items():
        if not isinstance(path_item, dict):
            continue
        for method, operation in path_item.items():
            if method.lower() in [
                "get",
                "post",
                "put",
                "delete",
                "options",
                "head",
                "patch",
                "trace",
            ]:
                if isinstance(operation, dict):
                    tags = operation.get("tags", [])
                    tags = [tags] if not isinstance(tags, list) else list(tags)
                    if service_tag not in tags:
                        tags.append(service_tag)
                    operation["tags"] = tags


def main():
    print("Generating and merging OpenAPI specifications statically...")

    # Create the aggregated structure
    merged_spec = {
        "openapi": "3.1.0",
        "info": {
            "title": "Cadence Clinical Unified Gateway API",
            "description": "Unified microservices API contract for Cadence Clinical Platform.\nEnforces OIDC/Keycloak authentication, RFC 7807 problem details, and ISO 14155:2020 regulatory compliance.",
            "version": "1.0.0-PROD",
        },
        "servers": [
            {
                "url": "https://api.cadence-clinical.com/api/v1",
                "description": "Production API Gateway",
            },
            {
                "url": "http://localhost:8000/api/v1",
                "description": "Local Dev Gateway Proxy",
            },
        ],
        "paths": {},
        "components": {},
    }

    loaded_any = False

    # Programmatically load and process all registered services
    for service in SERVICES_REGISTRY:
        name = service["name"]
        module_path = service["module"]
        app_var = service["app_var"]
        tag = service["tag"]
        prefix = service["prefix"]

        try:
            # Dynamically import the module
            mod = importlib.import_module(module_path)
            app = getattr(mod, app_var, None)
            if app is None:
                raise AttributeError(
                    f"Module '{module_path}' has no attribute '{app_var}'"
                )

            # Statically fetch the OpenAPI specification
            spec = app.openapi()
            if not isinstance(spec, dict):
                raise TypeError(f"OpenAPI spec is not a dictionary: {type(spec)}")

            # Apply the standardized service-level tag to all routes
            apply_service_tags(spec, tag)

            # Rewrite references and components schema names to ensure Namespace Cleanliness
            spec = rewrite_references(spec, prefix)
            if "components" in spec and "schemas" in spec["components"]:
                prefixed_schemas = {}
                for s_name, s_val in spec["components"]["schemas"].items():
                    prefixed_schemas[f"{prefix}{s_name}"] = s_val
                spec["components"]["schemas"] = prefixed_schemas

            # Deep merge paths
            deep_merge(merged_spec["paths"], spec.get("paths", {}))

            # Deep merge components
            deep_merge(merged_spec["components"], spec.get("components", {}))

            print(
                f"Successfully aggregated spec for service '{name}' with tag '{tag}' and prefix '{prefix}'"
            )
            loaded_any = True

        except Exception as e:
            # Gracefully log warnings for any failed loads instead of raising raw tracebacks
            print(
                f"Warning: Service module '{name}' failed to load and was skipped.",
                file=sys.stderr,
            )
            print(f"Reason: {type(e).__name__}: {e}", file=sys.stderr)

    if not loaded_any:
        print(
            "Warning: No registered service modules could be successfully imported.",
            file=sys.stderr,
        )

    # Convert merged spec to YAML string
    yaml_content = yaml.dump(merged_spec, sort_keys=False, width=1000)

    # Find and update Section 7 in docs/SDLC/03_API_Integration_Specification.md
    markdown_path = os.path.join(
        app_root, "docs", "SDLC", "03_API_Integration_Specification.md"
    )
    print(f"Updating specification file at: {markdown_path}")

    if not os.path.exists(markdown_path):
        print(f"Error: Spec file not found at {markdown_path}", file=sys.stderr)
        sys.exit(1)

    with open(markdown_path, encoding="utf-8") as f:
        content = f.read()

    sec_title = "## 7. Complete OpenAPI 3.0 Contract Specification"
    idx = content.find(sec_title)
    if idx == -1:
        print(f"Error: Could not find section title '{sec_title}'", file=sys.stderr)
        sys.exit(1)

    before_sec = content[:idx]
    sec_content_and_after = content[idx:]

    start_fence = "```yaml"
    start_idx = sec_content_and_after.find(start_fence)
    if start_idx == -1:
        print(
            "Error: Could not find start of ```yaml block in Section 7", file=sys.stderr
        )
        sys.exit(1)

    end_idx = sec_content_and_after.find("```", start_idx + len(start_fence))
    if end_idx == -1:
        print(
            "Error: Could not find end of ```yaml block in Section 7", file=sys.stderr
        )
        sys.exit(1)

    new_sec_content_and_after = (
        sec_content_and_after[: start_idx + len(start_fence)]
        + "\n"
        + yaml_content.strip()
        + "\n"
        + sec_content_and_after[end_idx:]
    )

    final_content = before_sec + new_sec_content_and_after

    with open(markdown_path, "w", encoding="utf-8") as f:
        f.write(final_content)

    print(
        "Successfully updated OpenAPI Specification in docs/SDLC/03_API_Integration_Specification.md!"
    )


if __name__ == "__main__":
    main()
