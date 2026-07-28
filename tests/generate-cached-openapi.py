import json
import os
import sys

app_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if app_root not in sys.path:
    sys.path.insert(0, app_root)

packages_dir = os.path.join(app_root, "packages")
for name in ["core-models", "database", "deid", "security", "ui"]:
    pkg_path = os.path.join(packages_dir, name)
    if pkg_path not in sys.path:
        sys.path.insert(0, pkg_path)

from apps.ctms.main import app as ctms_app  # noqa: E402
from apps.designer.main import app as designer_app  # noqa: E402
from apps.etmf.main import app as etmf_app  # noqa: E402
from apps.execution.main import app as execution_app  # noqa: E402
from apps.interop.main import app as interop_app  # noqa: E402
from apps.notifications.main import app as notifications_app  # noqa: E402
from apps.quality.main import app as quality_app  # noqa: E402
from apps.safety.main import app as safety_app  # noqa: E402
from apps.tickets.main import app as tickets_app  # noqa: E402

SERVICES_CONFIG = {
    "designer": {"app": designer_app, "prefix": "Designer_"},
    "execution": {"app": execution_app, "prefix": "Execution_"},
    "etmf": {"app": etmf_app, "prefix": "ETMF_"},
    "interop": {"app": interop_app, "prefix": "Interop_"},
    "ctms": {"app": ctms_app, "prefix": "Ctms_"},
    "notifications": {"app": notifications_app, "prefix": "Notifications_"},
    "quality": {"app": quality_app, "prefix": "Quality_"},
    "safety": {"app": safety_app, "prefix": "Safety_"},
    "tickets": {"app": tickets_app, "prefix": "Tickets_"},
}


def rewrite_references(data, prefix):
    if isinstance(data, dict):
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
                new_data[k] = rewrite_references(v, prefix)
        return new_data
    elif isinstance(data, list):
        return [rewrite_references(item, prefix) for item in data]
    return data


merged = {
    "openapi": "3.1.0",
    "info": {"title": "Cadence Clinical - Unified API", "version": "0.1.0"},
    "paths": {},
    "components": {"schemas": {}},
}

for service_name, config in SERVICES_CONFIG.items():
    try:
        spec = config["app"].openapi()
        prefix = config["prefix"]
        spec = rewrite_references(spec, prefix)

        # Merge paths
        for path_str, path_item in spec.get("paths", {}).items():
            # Standard prefix
            merged["paths"][f"/{service_name}{path_str}"] = path_item
            # Direct path
            merged["paths"][path_str] = path_item

        # Merge schemas
        for schema_name, schema_val in (
            spec.get("components", {}).get("schemas", {}).items()
        ):
            merged["components"]["schemas"][f"{prefix}{schema_name}"] = schema_val
    except Exception as e:
        print(f"Skipping {service_name}: {e}")

# Write to file
target_path = os.path.join(app_root, "tests", "cached-openapi.json")
with open(target_path, "w", encoding="utf-8") as f:
    json.dump(merged, f, indent=2)

print(f"Generated combined OpenAPI schema and wrote to {target_path}")
