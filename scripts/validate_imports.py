#!/usr/bin/env python3
"""Static & dynamic cross-service import validator.

Enforces package boundary rules and checks both standard and dynamic import pathways
(such as importlib.import_module, __import__, sys.modules) across apps and packages.

Requirements: PRD-SYS-001
"""

import ast
import os
import re
import sys
import tomllib
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
APPS_DIR = ROOT_DIR / "apps"
PACKAGES_DIR = ROOT_DIR / "packages"

_PACKAGE_DEPS_CACHE = {}


def get_package_dependencies(package_name: str) -> set[str]:
    """Reads and parses dependencies declared in a package's pyproject.toml."""
    if package_name in _PACKAGE_DEPS_CACHE:
        return _PACKAGE_DEPS_CACHE[package_name]

    pyproject_path = PACKAGES_DIR / package_name / "pyproject.toml"
    if not pyproject_path.exists():
        _PACKAGE_DEPS_CACHE[package_name] = set()
        return set()

    try:
        with open(pyproject_path, "rb") as f:
            data = tomllib.load(f)
            deps = data.get("project", {}).get("dependencies", [])
            clean_deps = set()
            for dep in deps:
                # Extract clean package name (alphanumeric/hyphen/underscore before operators/version)
                parts = re.split(r"[>=<!\[\s@]", dep)
                if parts:
                    clean_deps.add(parts[0].strip())
            _PACKAGE_DEPS_CACHE[package_name] = clean_deps
            return clean_deps
    except Exception:
        _PACKAGE_DEPS_CACHE[package_name] = set()
        return set()


def get_service_name(file_path: Path) -> str:
    """
    Extracts the service name (first directory name under apps/) from the file path.
    Example: apps/etmf/main.py -> etmf
    """
    try:
        relative = file_path.relative_to(APPS_DIR)
        return relative.parts[0]
    except ValueError:
        return ""


def check_file_imports(file_path: Path) -> list[str]:
    """Parses a Python file using AST and returns a list of import violations."""
    violations = []

    # 1. Determine if file is under apps/ or packages/
    is_app = False
    is_package = False
    entity_name = ""

    try:
        relative_to_apps = file_path.relative_to(APPS_DIR)
        is_app = True
        entity_name = relative_to_apps.parts[0]
    except ValueError:
        try:
            relative_to_packages = file_path.relative_to(PACKAGES_DIR)
            is_package = True
            entity_name = relative_to_packages.parts[0]
        except ValueError:
            return violations

    if "tests" in file_path.parts:
        return violations

    if not entity_name or entity_name == "__pycache__":
        return violations

    try:
        content = file_path.read_text(encoding="utf-8")
        tree = ast.parse(content, filename=str(file_path))
    except Exception as e:
        return [f"Failed to parse file: {e}"]

    # Helper to check if a module name is prohibited
    def check_module_prohibited(mod_name: str, lineno: int):
        parts = mod_name.split(".")
        if len(parts) >= 2 and parts[0] == "apps":
            imported_service = parts[1]
            if is_app:
                if imported_service != entity_name:
                    violations.append(
                        f"Line {lineno}: Direct import of service '{imported_service}' via '{mod_name}' "
                        f"is prohibited from within service '{entity_name}'."
                    )
            elif is_package:
                violations.append(
                    f"Line {lineno}: Package boundary violation! Package '{entity_name}' is importing "
                    f"from app service '{imported_service}' via '{mod_name}'."
                )
        elif len(parts) >= 2 and parts[0] == "packages":
            imported_package = parts[1]
            if is_package:
                if imported_package != entity_name:
                    declared_deps = get_package_dependencies(entity_name)
                    required_dep = f"packages-{imported_package.replace('_', '-')}"
                    if required_dep not in declared_deps:
                        violations.append(
                            f"Line {lineno}: Package dependency boundary violation! "
                            f"Package '{entity_name}' imports from package '{imported_package}' via '{mod_name}', "
                            f"but '{required_dep}' is not declared in the dependencies of "
                            f"'{entity_name}''s package configuration (pyproject.toml)."
                        )

    for node in ast.walk(tree):
        # 1. Standard "import apps.foo"
        if isinstance(node, ast.Import):
            for alias in node.names:
                check_module_prohibited(alias.name, node.lineno)

        # 2. Standard "from apps.foo import bar"
        elif isinstance(node, ast.ImportFrom):
            if node.level > 0:
                try:
                    rel_parts = list(file_path.relative_to(ROOT_DIR).parent.parts)
                except ValueError:
                    rel_parts = []
                drop_levels = node.level - 1
                base_parts = (
                    rel_parts[:-drop_levels]
                    if (drop_levels > 0 and len(rel_parts) >= drop_levels)
                    else rel_parts
                )
                if node.module:
                    resolved_parts = base_parts + node.module.split(".")
                else:
                    resolved_parts = base_parts
                resolved_module = ".".join(resolved_parts)
            else:
                resolved_module = node.module if node.module else ""

            if resolved_module:
                check_module_prohibited(resolved_module, node.lineno)

        # 3. Dynamic import via calls (importlib.import_module, __import__, etc.)
        elif isinstance(node, ast.Call):
            is_import_call = False
            if isinstance(node.func, ast.Attribute):
                if node.func.attr == "import_module":
                    is_import_call = True
            elif isinstance(node.func, ast.Name):
                if node.func.id in ("import_module", "__import__"):
                    is_import_call = True

            if is_import_call and node.args:
                first_arg = node.args[0]
                if isinstance(first_arg, ast.Constant) and isinstance(
                    first_arg.value, str
                ):
                    check_module_prohibited(first_arg.value, node.lineno)

        # 4. Dynamic import via sys.modules["apps.foo"]
        elif isinstance(node, ast.Subscript):
            is_sys_modules = False
            if isinstance(node.value, ast.Attribute):
                if (
                    node.value.attr == "modules"
                    and isinstance(node.value.value, ast.Name)
                    and node.value.value.id == "sys"
                ):
                    is_sys_modules = True
            elif isinstance(node.value, ast.Name):
                if node.value.id == "modules":
                    is_sys_modules = True

            if is_sys_modules:
                slice_node = node.slice
                # Support older python versions (ast.Index wrapper)
                if isinstance(slice_node, ast.Index):
                    slice_node = slice_node.value

                if isinstance(slice_node, ast.Constant) and isinstance(
                    slice_node.value, str
                ):
                    check_module_prohibited(slice_node.value, node.lineno)

        # 5. Dynamic import via sys.modules.get("apps.foo")
        elif (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Attribute)
            and node.func.attr == "get"
        ):
            is_sys_modules_get = False
            val = node.func.value
            if isinstance(val, ast.Attribute):
                if (
                    val.attr == "modules"
                    and isinstance(val.value, ast.Name)
                    and val.value.id == "sys"
                ):
                    is_sys_modules_get = True
            elif isinstance(val, ast.Name):
                if val.id == "modules":
                    is_sys_modules_get = True

            if is_sys_modules_get and node.args:
                first_arg = node.args[0]
                if isinstance(first_arg, ast.Constant) and isinstance(
                    first_arg.value, str
                ):
                    check_module_prohibited(first_arg.value, node.lineno)

    return violations


def main():
    print("--- Starting AST Cross-Service Import Validator ---")
    violations_found = {}
    total_files_checked = 0

    # Walk all .py files in apps directory
    for root, _, files in os.walk(APPS_DIR):
        for file in files:
            if file.endswith(".py"):
                file_path = Path(root) / file
                total_files_checked += 1
                violations = check_file_imports(file_path)
                if violations:
                    violations_found[str(file_path.relative_to(ROOT_DIR))] = violations

    # Walk all .py files in packages directory
    for root, _, files in os.walk(PACKAGES_DIR):
        for file in files:
            if file.endswith(".py"):
                file_path = Path(root) / file
                total_files_checked += 1
                violations = check_file_imports(file_path)
                if violations:
                    violations_found[str(file_path.relative_to(ROOT_DIR))] = violations

    if violations_found:
        print(
            "\n[ERROR] Direct/Dynamic cross-service or package boundary violations detected!"
        )
        for file, errs in violations_found.items():
            print(f"\nIn file: {file}")
            for err in errs:
                print(f"  - {err}")
        print("\nBuild check failed due to cross-service import violations.")
        sys.exit(1)

    print(
        f"\n[SUCCESS] No cross-service import or package boundary violations found across {total_files_checked} files."
    )
    sys.exit(0)


if __name__ == "__main__":
    main()
