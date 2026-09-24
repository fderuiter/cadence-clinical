#!/usr/bin/env python3
"""Sync OpenAPI Specification inside docs/SDLC/03_API_Integration_Specification.md.

This script aggregates the OpenAPI specifications of all microservices
completely in-memory (offline) and replaces the yaml block under Section 7 of
docs/SDLC/03_API_Integration_Specification.md.
"""

import glob
import json
import os
import sys
from typing import Any

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

os.environ.setdefault("AUDIT_LOG_SECRET_KEY", "dev-secret-key-12345")
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)
os.environ.setdefault("GATEWAY_SECRET", "internal-gateway-secret-12345")
os.environ.setdefault("JWT_SECRET_KEY", "dev-jwt-secret-key-12345")
os.environ.setdefault("BRAND_DOMAIN", "ccrsoft.com")


def load_aggregated_spec() -> dict[str, Any]:
    """Aggregate OpenAPI schemas directly from docs/openapi/*.json."""
    cdisc_file = os.path.join(app_root, "docs", "openapi", "cdisc_openapi.json")
    if os.path.exists(cdisc_file):
        try:
            with open(cdisc_file, encoding="utf-8") as f:
                return json.load(f)
        except Exception as e:
            print(f"Warning: Failed to load cdisc_openapi.json: {e}", file=sys.stderr)

    aggregated_paths = {}
    aggregated_components = {}
    pattern = os.path.join(app_root, "docs", "openapi", "*_openapi.json")
    for openapi_path in sorted(glob.glob(pattern)):
        if "cdisc_openapi.json" in openapi_path:
            continue
        try:
            with open(openapi_path, encoding="utf-8") as f:
                spec = json.load(f)
                paths = spec.get("paths", {})
                for p, p_val in paths.items():
                    aggregated_paths[p] = p_val
                components = spec.get("components", {})
                for comp_type, comp_vals in components.items():
                    if comp_type not in aggregated_components:
                        aggregated_components[comp_type] = {}
                    if isinstance(comp_vals, dict):
                        aggregated_components[comp_type].update(comp_vals)
        except Exception as e:
            print(f"Warning: Failed to parse {openapi_path}: {e}", file=sys.stderr)

    return {
        "paths": aggregated_paths,
        "components": aggregated_components,
    }


def main():
    print("Generating and merging OpenAPI specifications statically...")
    aggregated_spec = load_aggregated_spec()

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
        "paths": aggregated_spec.get("paths", {}),
        "components": aggregated_spec.get("components", {}),
    }

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
