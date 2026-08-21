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

# Add repository root to sys.path
ROOT_DIR = Path(__file__).resolve().parent.parent
if str(ROOT_DIR) not in sys.path:
    sys.path.insert(0, str(ROOT_DIR))

# Enforce Python 3.14+ runtime before loading standard modules or packages
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]} ({sys.executable}).\n"
            "Cadence Clinical requires Python 3.14+.\n"
            "Please run: uv run python scripts/validate_imports.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info

APPS_DIR = ROOT_DIR / "apps"
PACKAGES_DIR = ROOT_DIR / "packages"

_PACKAGE_DEPS_CACHE = {}

LEGACY_TESTS_EXEMPT_FROM_IMPORT_BOUNDARIES = {
    "apps/org/tests/test_org_integration_e2e.py",
    "apps/safety/tests/test_safety_gateway.py",
    "apps/safety/tests/test_emergency_unblinding.py",
    "apps/safety/tests/test_sae_reconciliation.py",
    "apps/safety/tests/test_sae_reconciliation_jobs.py",
    "apps/safety/tests/test_e2b_parser.py",
    "apps/safety/tests/test_safety_service.py",
    "apps/safety/tests/test_safety_router.py",
    "apps/safety/tests/test_sae_reconciler.py",
    "apps/ctms/tests/test_doa_router.py",
    "apps/ctms/tests/test_doa_audit_suite.py",
    "apps/ctms/tests/test_doa_service.py",
    "apps/ctms/tests/test_ctms.py",
    "apps/ctms/tests/test_delegation.py",
    "apps/ctms/tests/test_doa_models.py",
    "apps/ctms/tests/test_federated_resupply.py",
    "apps/notifications/tests/test_notification_worker.py",
    "apps/notifications/tests/test_clinical_workflow_notifications.py",
    "apps/notifications/tests/test_clinical_workflow_notifications_integration.py",
    "apps/notifications/tests/test_notifications.py",
    "apps/tickets/tests/test_tickets_integration_seam.py",
    "apps/tickets/tests/test_tickets_notifications_seam.py",
    "apps/tickets/tests/test_tickets_notifications_integration.py",
    "apps/tickets/tests/test_tickets_service.py",
    "apps/interop/tests/test_interop_prescreen.py",
    "apps/interop/tests/test_ecoa_coverage.py",
    "apps/interop/tests/test_interop_defeated.py",
    "apps/interop/tests/test_offline_sync.py",
    "apps/interop/tests/test_offline_router.py",
    "apps/interop/tests/test_interop_quarantine.py",
    "apps/interop/tests/test_interop.py",
    "apps/etmf/tests/test_etmf_redaction.py",
    "apps/etmf/tests/test_etmf_signing_lifecycle.py",
    "apps/etmf/tests/test_etmf_compliance.py",
    "apps/etmf/tests/test_etmf_taxonomy.py",
    "apps/etmf/tests/test_etmf_bulk_archival.py",
    "apps/etmf/tests/test_etmf_binder_structure_and_history.py",
    "apps/etmf/tests/test_etmf_eisf_expiration_metadata.py",
    "apps/etmf/tests/test_etmf.py",
    "apps/etmf/tests/test_etmf_qc.py",
    "apps/etmf/tests/test_etmf_site_scope.py",
    "apps/etmf/tests/test_etmf_sync_provenance.py",
    "apps/etmf/tests/conftest.py",
    "apps/econsent/tests/test_econsent_capture.py",
    "apps/econsent/tests/test_econsent_translations.py",
    "apps/econsent/tests/test_econsent_archival.py",
    "apps/econsent/tests/test_econsent_service.py",
    "apps/econsent/tests/test_econsent.py",
    "apps/econsent/tests/test_econsent_workflow.py",
    "apps/quality/tests/test_quality.py",
    "apps/quality/tests/test_quality_sentinel.py",
    "apps/quality/tests/test_quality_workflow.py",
    "apps/quality/tests/test_outbox_sync.py",
    "apps/eisf/tests/test_eisf_adapter.py",
    "apps/eisf/tests/test_eisf_isolation.py",
    "apps/eisf/tests/test_eisf_service.py",
    "apps/eisf/tests/test_eisf_router.py",
    "apps/eisf/tests/test_eisf_api.py",
    "apps/eisf/tests/test_eisf_site_scope.py",
    "apps/eisf/tests/test_eisf_models.py",
    "apps/eisf/tests/test_eisf_binder.py",
    "apps/gateway/tests/test_gateway.py",
    "apps/gateway/tests/test_auditor_router.py",
    "apps/web/tests/test_amendment_diff.py",
    "apps/web/tests/test_econsent.py",
    "packages/storage/tests/test_safe_binary_storage_watermark.py",
    "packages/compliance/tests/test_compliance_change_request.py",
    "packages/compliance/tests/test_compliance_security.py",
    "packages/database/tests/test_delta.py",
    "packages/database/tests/test_migrate.py",
    "packages/database/tests/test_ledger_and_triggers.py",
    "packages/security/tests/test_rbac_enforcement.py",
    "packages/security/tests/test_cryptography.py",
    "packages/security/tests/test_rbac.py",
    "packages/security/tests/test_rbac_e2e.py",
    "packages/security/tests/test_audit.py",
    "packages/hexagonal/tests/test_hexagonal_domain.py",
    "packages/hexagonal/tests/test_hexagonal_ports_adapters.py",
}


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

    try:
        rel_path = str(file_path.relative_to(ROOT_DIR)).replace("\\", "/")
        if rel_path in LEGACY_TESTS_EXEMPT_FROM_IMPORT_BOUNDARIES:
            return violations
    except ValueError:
        pass

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


def main() -> None:
    print_runtime_info("validate_imports.py")
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
