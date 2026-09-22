#!/usr/bin/env python3
"""Automated scaffolding utility to generate localized README.md files across packages/ and apps/.

Extracts metadata from package configuration files (pyproject.toml, package.json, csproj, main.py, etc.)
and generates compliant README.md files adhering to the 5 standard section headers:
1. Overview
2. Architectural Boundaries
3. Public APIs / Exports
4. Dependencies
5. Local Development & Testing
"""

import argparse
import contextlib
import json
import re
import sys
from pathlib import Path
from typing import Any

with contextlib.suppress(ImportError):
    import tomllib


REQUIRED_HEADERS = [
    "Overview",
    "Architectural Boundaries",
    "Public APIs / Exports",
    "Dependencies",
    "Local Development & Testing",
]


def clean_title(name: str) -> str:
    parts = name.replace("-", " ").replace("_", " ").split()
    return " ".join(p.capitalize() for p in parts)


def extract_pyproject_metadata(pyproject_path: Path) -> dict[str, Any]:
    metadata: dict[str, Any] = {}
    if not pyproject_path.exists():
        return metadata
    try:
        content = pyproject_path.read_text(encoding="utf-8")
        if "tomllib" in sys.modules:
            data = tomllib.loads(content)
            proj = data.get("project", {})
            metadata["name"] = proj.get("name")
            metadata["description"] = proj.get("description")
            metadata["dependencies"] = proj.get("dependencies", [])
            metadata["scripts"] = proj.get("scripts", {})
        else:
            name_m = re.search(r'name\s*=\s*["\']([^"\']+)["\']', content)
            if name_m:
                metadata["name"] = name_m.group(1)
            desc_m = re.search(r'description\s*=\s*["\']([^"\']+)["\']', content)
            if desc_m:
                metadata["description"] = desc_m.group(1)
    except Exception:
        pass
    return metadata


def extract_package_json_metadata(package_json_path: Path) -> dict[str, Any]:
    metadata: dict[str, Any] = {}
    if not package_json_path.exists():
        return metadata
    try:
        data = json.loads(package_json_path.read_text(encoding="utf-8"))
        metadata["name"] = data.get("name")
        metadata["description"] = data.get("description")
        deps = list(data.get("dependencies", {}).keys())
        dev_deps = list(data.get("devDependencies", {}).keys())
        metadata["dependencies"] = deps + dev_deps
        metadata["scripts"] = data.get("scripts", {})
    except Exception:
        pass
    return metadata


def extract_fastapi_metadata(module_dir: Path) -> dict[str, Any]:
    metadata: dict[str, Any] = {}
    main_files = [module_dir / "main.py"] + list(module_dir.rglob("main.py"))
    for main_py in main_files:
        if main_py.exists():
            try:
                content = main_py.read_text(encoding="utf-8")
                title_m = re.search(r'title\s*=\s*["\']([^"\']+)["\']', content)
                if title_m:
                    metadata["app_title"] = title_m.group(1)
                desc_m = re.search(r'description\s*=\s*["\']([^"\']+)["\']', content)
                if desc_m:
                    metadata["app_description"] = desc_m.group(1)
                routers = re.findall(r'prefix\s*=\s*["\']([^"\']+)["\']', content)
                if routers:
                    metadata["route_prefixes"] = sorted(set(routers))
                break
            except Exception:
                pass
    return metadata


def extract_python_exports(module_dir: Path) -> list[str]:
    exports = []
    init_files = [module_dir / "__init__.py"] + list(module_dir.rglob("__init__.py"))
    for init_py in init_files:
        if init_py.exists():
            try:
                content = init_py.read_text(encoding="utf-8")
                all_m = re.search(r"__all__\s*=\s*\[([^\]]+)\]", content)
                if all_m:
                    found = [
                        x.strip(" \"'\n\r")
                        for x in all_m.group(1).split(",")
                        if x.strip(" \"'\n\r")
                    ]
                    exports.extend(found)
            except Exception:
                pass
    if not exports:
        for py_file in module_dir.glob("*.py"):
            if py_file.name.startswith("_"):
                continue
            try:
                txt = py_file.read_text(encoding="utf-8")
                classes = re.findall(r"^class\s+([A-Za-z0-9_]+)", txt, re.MULTILINE)
                funcs = re.findall(r"^def\s+([A-Za-z0-9_]+)", txt, re.MULTILINE)
                for c in classes:
                    if not c.startswith("_"):
                        exports.append(f"class `{c}`")
                for f in funcs:
                    if not f.startswith("_"):
                        exports.append(f"function `{f}()`")
            except Exception:
                pass
    return exports[:8]


def generate_package_readme(dir_path: Path) -> str:
    pkg_name = dir_path.name
    title = clean_title(pkg_name)

    pyproject_meta = extract_pyproject_metadata(dir_path / "pyproject.toml")
    pkg_json_meta = extract_package_json_metadata(dir_path / "package.json")
    exports = extract_python_exports(dir_path)

    description = (
        pyproject_meta.get("description")
        or pkg_json_meta.get("description")
        or f"Shared domain and infrastructure component `{pkg_name}` within the Cadence Clinical research software platform."
    )

    dependencies = (
        pyproject_meta.get("dependencies") or pkg_json_meta.get("dependencies") or []
    )

    scripts = pyproject_meta.get("scripts") or pkg_json_meta.get("scripts") or {}

    deps_section = ""
    if dependencies:
        deps_list = "\n".join(f"- `{d}`" for d in sorted(dependencies))
        deps_section = (
            f"This package relies on the following standard dependencies:\n{deps_list}"
        )
    else:
        deps_section = f"This package is maintained as a standard shared library within `packages/{pkg_name}`."

    exports_section = ""
    if exports:
        exp_list = "\n".join(f"- {e}" for e in exports)
        exports_section = f"Key exported interfaces and components:\n{exp_list}"
    else:
        exports_section = (
            f"Exports shared utilities, domain interfaces, and models for `{pkg_name}`."
        )

    if scripts:
        cli_list = "\n".join(f"- `{k}`: `{v}`" for k, v in scripts.items())
        exports_section += f"\n\n#### Exposed CLI Commands\n{cli_list}"

    has_ts = (dir_path / "package.json").exists() or (
        dir_path / "tsconfig.json"
    ).exists()
    has_dotnet = len(list(dir_path.glob("*.csproj"))) > 0

    if has_ts:
        test_cmd = f"pnpm --filter {pkg_name} test"
    elif has_dotnet:
        test_cmd = f"dotnet test packages/{pkg_name}"
    else:
        test_cmd = f"uv run pytest packages/{pkg_name}"

    return f"""# {title} Package

## Overview

{description}

---

## Architectural Boundaries

The `{pkg_name}` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/{pkg_name}/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

{exports_section}

---

## Dependencies

{deps_section}

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
{test_cmd}
```
"""


def generate_app_readme(dir_path: Path) -> str:
    app_name = dir_path.name
    title = clean_title(app_name)

    pyproject_meta = extract_pyproject_metadata(dir_path / "pyproject.toml")
    pkg_json_meta = extract_package_json_metadata(dir_path / "package.json")
    fastapi_meta = extract_fastapi_metadata(dir_path)

    description = (
        fastapi_meta.get("app_description")
        or pyproject_meta.get("description")
        or pkg_json_meta.get("description")
        or f"Clinical application microservice for `{app_name}` within the Cadence Clinical platform."
    )

    dependencies = (
        pyproject_meta.get("dependencies") or pkg_json_meta.get("dependencies") or []
    )

    route_prefixes = fastapi_meta.get("route_prefixes", [])
    if not route_prefixes:
        route_prefixes = [f"/api/v1/{app_name.replace('_', '-')}"]

    routes_formatted = "\n".join(
        f"- `HTTP` endpoints under `{prefix}`" for prefix in route_prefixes
    )

    subdirs = [
        d.name for d in dir_path.iterdir() if d.is_dir() and not d.name.startswith(".")
    ]
    has_hexagonal = any(
        sd in subdirs
        for sd in ["ports", "adapters", "domain", "application", "presentation"]
    )

    layout_details = ""
    if has_hexagonal:
        layout_details = """
Conforms strictly to Enterprise Hexagonal standards with clean decoupling:
- `ports/`: Port interfaces defining driving and driven contracts.
- `adapters/`: Database, S3/blob, and HTTP adapter implementations.
- `domain/`: Pure business entity domain models and core domain logic.
- `application/`: Business use case drivers coordinating ports and entities.
- `presentation/`: FastAPI / Minimal API router wrappers.
"""
    else:
        layout_details = f"Implements clinical domain services and application workflows within `apps/{app_name}/`."

    deps_section = ""
    if dependencies:
        deps_list = "\n".join(f"- `{d}`" for d in sorted(dependencies))
        deps_section = f"This microservice includes the following module dependencies:\n{deps_list}"
    else:
        deps_section = "Interacts with core platform capabilities and shared libraries in `packages/`."

    has_ts = (dir_path / "package.json").exists()
    has_dotnet = (
        len(list(dir_path.glob("*.csproj"))) > 0 or (dir_path / "Program.cs").exists()
    )

    if has_ts:
        test_cmd = f"pnpm --filter {app_name} test"
    elif has_dotnet:
        test_cmd = f"dotnet test apps/{app_name}"
    else:
        test_cmd = f"uv run pytest apps/{app_name}/tests"

    return f"""# {title} Microservice

## Overview

{description}

---

## Architectural Boundaries

{layout_details}
- **GxP Data Isolation:** Operates within isolated domain boundaries without direct cross-service database access.
- **Gateway Interception:** All client traffic is routed via the API Gateway for authentication, rate-limiting, and audit logging.

---

## Public APIs / Exports

Service routes and contract interfaces:
{routes_formatted}

---

## Dependencies

{deps_section}

---

## Local Development & Testing

Run microservice unit and integration tests:

```bash
{test_cmd}
```
"""


def scaffold_directory(dir_path: Path, is_app: bool, overwrite: bool = False) -> bool:
    readme_path = dir_path / "README.md"
    if readme_path.exists() and not overwrite:
        content = readme_path.read_text(encoding="utf-8")
        if content.strip():
            print(
                f"Skipping existing README.md in {dir_path.relative_to(dir_path.parent.parent)}"
            )
            return False

    content = (
        generate_app_readme(dir_path) if is_app else generate_package_readme(dir_path)
    )
    readme_path.write_text(content, encoding="utf-8")
    print(f"✔ Scaffolded README.md in {dir_path.relative_to(dir_path.parent.parent)}")
    return True


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Bulk scaffold localized README.md files for packages/ and apps/."
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Path to repository root",
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Overwrite existing README.md files (use with caution)",
    )
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    packages_dir = repo_root / "packages"
    apps_dir = repo_root / "apps"

    count = 0
    if packages_dir.exists():
        for p in sorted(packages_dir.iterdir()):
            if p.is_dir() and not p.name.startswith(".") and p.name != "__pycache__":
                if scaffold_directory(p, is_app=False, overwrite=args.overwrite):
                    count += 1

    if apps_dir.exists():
        for a in sorted(apps_dir.iterdir()):
            if a.is_dir() and not a.name.startswith(".") and a.name != "__pycache__":
                if scaffold_directory(a, is_app=True, overwrite=args.overwrite):
                    count += 1

    print(f"\nScaffolding complete. Generated/updated {count} README.md files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
