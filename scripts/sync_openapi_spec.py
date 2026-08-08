#!/usr/bin/env python3
"""Sync OpenAPI Specification inside docs/SDLC/03_API_Integration_Specification.md.

This script aggregates the OpenAPI specifications of the active services
(designer and execution) completely in-memory (offline) and replaces the
yaml block under Section 7 of docs/SDLC/03_API_Integration_Specification.md.
"""

import os
import sys

import yaml

# Set up python path for local imports and package paths
app_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if app_root not in sys.path:
    sys.path.insert(0, app_root)

packages_dir = os.path.join(app_root, "packages")
for name in ["database", "deid", "security", "ui"]:
    pkg_path = os.path.join(packages_dir, name)
    if pkg_path not in sys.path:
        sys.path.insert(0, pkg_path)

try:
    from apps.designer.main import app as designer_app
    from apps.execution.main import app as execution_app
except ImportError as e:
    print(f"Error importing service entrypoints: {e}", file=sys.stderr)
    sys.exit(1)


def deep_merge(dict1, dict2):
    """Deep merge dict2 into dict1 in place."""
    for key, val in dict2.items():
        if isinstance(val, dict) and key in dict1 and isinstance(dict1[key], dict):
            deep_merge(dict1[key], val)
        else:
            dict1[key] = val
    return dict1


def main():
    print("Generating and merging OpenAPI specifications statically...")
    designer_spec = designer_app.openapi()
    execution_spec = execution_app.openapi()

    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
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
                "url": f"https://api.{brand_domain}/api/v1",
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

    # Deep merge paths
    deep_merge(merged_spec["paths"], designer_spec.get("paths", {}))
    deep_merge(merged_spec["paths"], execution_spec.get("paths", {}))

    # Deep merge components
    deep_merge(merged_spec["components"], designer_spec.get("components", {}))
    deep_merge(merged_spec["components"], execution_spec.get("components", {}))

    # Convert merged spec to YAML string
    # We want a clean string representation
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
