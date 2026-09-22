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

os.environ.setdefault("AUDIT_LOG_SECRET_KEY", "dev-secret-key-12345")
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)
os.environ.setdefault("GATEWAY_SECRET", "internal-gateway-secret-12345")
os.environ.setdefault("JWT_SECRET_KEY", "dev-jwt-secret-key-12345")
os.environ.setdefault("BRAND_DOMAIN", "ccrsoft.com")

import glob
import json
import os
import sys
from typing import Any

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


def rewrite_refs(obj: Any, prefix: str) -> Any:
    """Recursively rewrite schema $ref pointers to include the service prefix."""
    if isinstance(obj, dict):
        new_dict = {}
        for k, v in obj.items():
            if (
                k == "$ref"
                and isinstance(v, str)
                and v.startswith("#/components/schemas/")
            ):
                ref_name = v.split("/")[-1]
                target_name = (
                    ref_name if ref_name.startswith(prefix) else f"{prefix}{ref_name}"
                )
                new_dict[k] = f"#/components/schemas/{target_name}"
            else:
                new_dict[k] = rewrite_refs(v, prefix)
        return new_dict
    if isinstance(obj, list):
        return [rewrite_refs(item, prefix) for item in obj]
    return obj


def aggregate_from_openapi_files() -> dict[str, Any]:
    """Aggregate individual OpenAPI JSON files from docs/openapi/ into a unified specification."""
    openapi_dir = os.path.join(app_root, "docs", "openapi")
    merged_paths: dict[str, Any] = {}
    merged_schemas: dict[str, Any] = {}
    merged_security_schemes: dict[str, Any] = {}

    for spec_path in sorted(glob.glob(os.path.join(openapi_dir, "*_openapi.json"))):
        base_name = os.path.basename(spec_path).replace("_openapi.json", "")
        if base_name == "cdisc":
            continue

        prefix = "ETMF_" if base_name == "etmf" else f"{base_name.capitalize()}_"

        try:
            with open(spec_path, encoding="utf-8") as f:
                spec = json.load(f)

            schemas = spec.get("components", {}).get("schemas", {})
            for s_name, s_def in schemas.items():
                namespaced = (
                    s_name if s_name.startswith(prefix) else f"{prefix}{s_name}"
                )
                merged_schemas[namespaced] = rewrite_refs(s_def, prefix)

            paths = spec.get("paths", {})
            rewritten_paths = rewrite_refs(paths, prefix)
            for p_key, p_val in rewritten_paths.items():
                if p_key in merged_paths:
                    merged_paths[p_key].update(p_val)
                else:
                    merged_paths[p_key] = p_val

            sec_schemes = spec.get("components", {}).get("securitySchemes", {})
            merged_security_schemes.update(sec_schemes)
        except Exception as e:
            print(
                f"Warning: Failed to load OpenAPI schema file '{spec_path}': {e}",
                file=sys.stderr,
            )

    return {
        "paths": merged_paths,
        "components": {
            "schemas": merged_schemas,
            "securitySchemes": merged_security_schemes,
        },
    }


def main():
    print("Generating and merging OpenAPI specifications statically...")
    try:
        from apps.gateway.main import app as gateway_app

        gateway_app.openapi_schema = None
        aggregated_spec = gateway_app.openapi()
    except (ImportError, Exception) as e:
        print(
            f"Gateway entrypoint unavailable ({e}). Aggregating OpenAPI schemas directly from docs/openapi/*.json..."
        )
        aggregated_spec = aggregate_from_openapi_files()

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
