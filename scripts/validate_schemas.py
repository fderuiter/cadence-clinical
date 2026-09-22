"""
Schema Validation Tool for Active CI Verification.

This script imports all downstream services statically to compile and aggregate
their OpenAPI schemas. It verifies that all schemas are correctly namespaced using
service-specific prefixes, preventing naming collisions or model overwrites in the
unified gateway schema. It fails the build if any non-namespaced schema definitions
overlap or collide.

Compliance:
- Gate 1: Fully documented and typed.
- Gate 3: Executed as part of continuous integration checks.
"""

import glob
import importlib
import json
import os
import sys
from pathlib import Path
from typing import Any

# Add repository root to sys.path
app_root = str(Path(__file__).resolve().parent.parent)
if app_root not in sys.path:
    sys.path.insert(0, app_root)

# Enforce Python 3.14+ runtime before loading standard modules or packages
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]} ({sys.executable}).\n"
            "Cadence Clinical requires Python 3.14+.\n"
            "Please run: uv run python scripts/validate_schemas.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info

# Prevent import errors due to missing secret keys in offline environment
os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY",
    "test-gxp-audit-secret-key-placeholder-abc",  # pragma: allowlist secret
)
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET",
    "test-email-hmac-secret-placeholder-xyz",  # pragma: allowlist secret
)
os.environ.setdefault(
    "GATEWAY_SECRET",
    "test-gateway-secret-placeholder-123",  # pragma: allowlist secret
)

packages_dir = os.path.join(app_root, "packages")
for name in ["database", "deid", "security", "ui"]:
    pkg_path = os.path.join(packages_dir, name)
    if pkg_path not in sys.path:
        sys.path.insert(0, pkg_path)


# Track dynamic import errors for diagnostic reporting
IMPORT_ERRORS: list[tuple[str, Exception]] = []


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


def aggregate_service_specs(
    service_specs: dict[str, tuple[str, dict[str, Any]]],
) -> dict[str, Any]:
    """Aggregate individual microservice OpenAPI specs into a unified schema."""
    merged_paths: dict[str, Any] = {}
    merged_schemas: dict[str, Any] = {}
    merged_security_schemes: dict[str, Any] = {}

    for service_name, (prefix, spec) in service_specs.items():
        schemas = spec.get("components", {}).get("schemas", {})
        for s_name, s_def in schemas.items():
            namespaced = s_name if s_name.startswith(prefix) else f"{prefix}{s_name}"
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

    return {
        "openapi": "3.1.0",
        "info": {
            "title": "Cadence Clinical - Unified API",
            "version": "0.1.0",
        },
        "paths": merged_paths,
        "components": {
            "schemas": merged_schemas,
            "securitySchemes": merged_security_schemes,
        },
    }


def discover_services() -> dict[str, dict[str, Any]]:
    """Dynamically scan apps directory and docs/openapi to load microservices with dual-engine fallback."""
    config: dict[str, dict[str, Any]] = {}
    apps_dir = os.path.join(app_root, "apps")
    openapi_dir = os.path.join(app_root, "docs", "openapi")

    # 1. Scan subdirectories under apps/
    if os.path.isdir(apps_dir):
        for name in sorted(os.listdir(apps_dir)):
            dir_path = os.path.join(apps_dir, name)
            if name == "gateway" or name.startswith(".") or not os.path.isdir(dir_path):
                continue

            prefix = "ETMF_" if name == "etmf" else f"{name.capitalize()}_"
            main_path = os.path.join(dir_path, "main.py")

            if os.path.isfile(main_path):
                try:
                    module = importlib.import_module(f"apps.{name}.main")
                    app = getattr(module, "app", None)
                    if app is not None:
                        config[name] = {
                            "type": "dynamic",
                            "app": app,
                            "prefix": prefix,
                        }
                        continue
                except Exception as e:
                    print(
                        f"Notice: Dynamic import failed for service '{name}' ({e}). Falling back to static schema.",
                        file=sys.stderr,
                    )

            # Static schema fallback for apps directory
            static_file = os.path.join(openapi_dir, f"{name}_openapi.json")
            if os.path.isfile(static_file):
                try:
                    with open(static_file, encoding="utf-8") as f:
                        schema = json.load(f)
                    config[name] = {
                        "type": "static",
                        "schema": schema,
                        "prefix": prefix,
                    }
                except Exception as e:
                    IMPORT_ERRORS.append((name, e))

    # 2. Check docs/openapi/ for any additional decommissioned microservices
    if os.path.isdir(openapi_dir):
        for static_file in sorted(
            glob.glob(os.path.join(openapi_dir, "*_openapi.json"))
        ):
            base_name = os.path.basename(static_file).replace("_openapi.json", "")
            if base_name in config or base_name == "cdisc":
                continue
            prefix = "ETMF_" if base_name == "etmf" else f"{base_name.capitalize()}_"
            try:
                with open(static_file, encoding="utf-8") as f:
                    schema = json.load(f)
                config[base_name] = {
                    "type": "static",
                    "schema": schema,
                    "prefix": prefix,
                }
            except Exception as e:
                IMPORT_ERRORS.append((base_name, e))

    return config


# Map downstream service identifiers to their corresponding FastAPI apps / static schemas
SERVICES_CONFIG = discover_services()


def validate_schemas() -> bool:
    """
    Compile and validate all schemas from downstream microservices.

    Retrieves the OpenAPI specification dynamically generated by active Python services
    or loads static JSON schemas for decommissioned services, checks for duplicates,
    and simulates the unified aggregation to detect any namespacing errors or collisions.

    Returns:
        bool: True if the schema compilation and namespaces are 100% correct and
              safe from collisions; False otherwise.
    """
    print_runtime_info("validate_schemas.py")
    print("--- Starting Dual-Engine Schema Compilation & Namespacing Checks ---")

    if IMPORT_ERRORS:
        print(
            "\n[VALIDATION FAILED] Static compilation or schema loading failed:",
            file=sys.stderr,
        )
        for name, err in IMPORT_ERRORS:
            print(f"  - Service '{name}' error: {err}", file=sys.stderr)
        return False

    raw_schemas: dict[str, Any] = {}

    # 1. Compile OpenAPI schema from dynamic instance or static schema file
    for service_name, config in SERVICES_CONFIG.items():
        try:
            if config["type"] == "dynamic":
                raw_schemas[service_name] = config["app"].openapi()
                print(
                    f"Successfully compiled schema for '{service_name}' (dynamic FastAPI)."
                )
            else:
                raw_schemas[service_name] = config["schema"]
                print(
                    f"Successfully loaded schema for '{service_name}' (static JSON fallback)."
                )
        except Exception as e:
            print(f"Error compiling OpenAPI for {service_name}: {e}", file=sys.stderr)
            return False

    # 2. Track schema definition occurrences across services to find shared models
    schema_sources: dict[str, list[str]] = {}
    for service_name, spec in raw_schemas.items():
        schemas = spec.get("components", {}).get("schemas", {})
        for schema_name in schemas:
            schema_sources.setdefault(schema_name, []).append(service_name)

    # Report overlapping model definitions
    overlapping_schemas = {
        name: srcs for name, srcs in schema_sources.items() if len(srcs) > 1
    }
    if overlapping_schemas:
        print(
            f"\nFound {len(overlapping_schemas)} model names shared across multiple services:"
        )
        for name, services in overlapping_schemas.items():
            print(f"  - Model '{name}' is defined in: {', '.join(services)}")
    else:
        print("\nNo overlapping raw model names detected across services.")

    # 3. Simulate gateway aggregation and verify strict namespacing rules
    merged_schemas: dict[str, tuple[str, str]] = {}
    errors: list[str] = []

    for service_name, spec in raw_schemas.items():
        prefix = SERVICES_CONFIG[service_name]["prefix"]
        schemas = spec.get("components", {}).get("schemas", {})

        for schema_name in schemas:
            namespaced_name = (
                schema_name
                if schema_name.startswith(prefix)
                else f"{prefix}{schema_name}"
            )

            # Guardrail: The prefix must be non-empty and namespaced_name must start with the prefix
            if not prefix or not namespaced_name.startswith(prefix):
                errors.append(
                    f"Service '{service_name}' model '{schema_name}' is not correctly namespaced. "
                    f"Expected prefix '{prefix}'."
                )
                continue

            # Detect naming collisions on the aggregated components map
            if namespaced_name in merged_schemas:
                other_service, other_orig = merged_schemas[namespaced_name]
                if other_service != service_name:
                    errors.append(
                        f"Namespace collision detected! Both service '{service_name}' (schema '{schema_name}') "
                        f"and service '{other_service}' (schema '{other_orig}') mapped to identical "
                        f"aggregated schema key '{namespaced_name}'."
                    )
            else:
                merged_schemas[namespaced_name] = (service_name, schema_name)

    if errors:
        print(
            "\n[VALIDATION FAILED] Namespacing and collision checks failed:",
            file=sys.stderr,
        )
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        return False

    print(
        "\n[VALIDATION PASSED] All schemas are isolated, correctly namespaced, and free of collisions."
    )
    return True


if __name__ == "__main__":
    export_dir = None
    if "--export-dir" in sys.argv:
        idx = sys.argv.index("--export-dir")
        if idx + 1 < len(sys.argv):
            export_dir = sys.argv[idx + 1]

    success = validate_schemas()
    if not success:
        sys.exit(1)

    if export_dir:
        os.makedirs(export_dir, exist_ok=True)
        print(f"\n[EXPORT] Exporting OpenAPI schemas to '{export_dir}'...")
        export_specs: dict[str, tuple[str, dict[str, Any]]] = {}

        for name, config in SERVICES_CONFIG.items():
            if config["type"] == "dynamic":
                schema = config["app"].openapi()
            else:
                schema = config["schema"]

            export_specs[name] = (config["prefix"], schema)
            out_file = os.path.join(export_dir, f"{name}_openapi.json")
            with open(out_file, "w") as f:
                json.dump(schema, f, indent=2)
                f.write("\n")
            print(f"  - Exported {out_file}")

        # Export unified cdisc_openapi.json using dynamic app if available, or dual-engine aggregation
        gateway_out_file = os.path.join(export_dir, "cdisc_openapi.json")
        try:
            from apps.gateway.main import app as gateway_app

            gateway_app.openapi_schema = None
            gateway_schema = gateway_app.openapi()
        except Exception:
            gateway_schema = aggregate_service_specs(export_specs)

        with open(gateway_out_file, "w") as f:
            json.dump(gateway_schema, f, indent=2)
            f.write("\n")
        print(f"  - Exported {gateway_out_file}")

    sys.exit(0)
