#!/usr/bin/env python3
import ast
import os
import sys
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
APPS_DIR = ROOT_DIR / "apps"


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
    """
    Parses a python file using AST and returns a list of import violations.
    """
    violations = []
    service_name = get_service_name(file_path)
    if not service_name or service_name == "__pycache__":
        return violations

    try:
        content = file_path.read_text(encoding="utf-8")
        tree = ast.parse(content, filename=str(file_path))
    except Exception as e:
        # If a file fails to parse, count it as an issue or ignore if it's not valid Python
        return [f"Failed to parse file: {e}"]

    # Determine absolute module components of the current file
    # Example: apps/etmf/sub/file.py -> ['apps', 'etmf', 'sub']
    try:
        rel_parts = list(file_path.relative_to(ROOT_DIR).parent.parts)
    except ValueError:
        rel_parts = []

    for node in ast.walk(tree):
        # Handle "import apps.execution.trial_lock" or "import apps.execution"
        if isinstance(node, ast.Import):
            for alias in node.names:
                parts = alias.name.split(".")
                if len(parts) >= 2 and parts[0] == "apps":
                    imported_service = parts[1]
                    if imported_service != service_name:
                        violations.append(
                            f"Line {node.lineno}: Direct import of service '{imported_service}' "
                            f"via '{alias.name}' is prohibited from within service '{service_name}'."
                        )

        # Handle "from apps.execution.trial_lock import TrialLockManager"
        elif isinstance(node, ast.ImportFrom):
            # Resolve relative imports if level > 0
            if node.level > 0:
                # E.g., if rel_parts is ['apps', 'etmf', 'sub'] and level is 1 (current package):
                # we drop 0 levels -> ['apps', 'etmf', 'sub']
                # If level is 2 (parent package), we drop 1 level -> ['apps', 'etmf']
                # If level is 3 (grandparent package), we drop 2 levels -> ['apps']
                drop_levels = node.level - 1
                if len(rel_parts) >= drop_levels:
                    base_parts = (
                        rel_parts[:-drop_levels] if drop_levels > 0 else rel_parts
                    )
                else:
                    base_parts = []

                if node.module:
                    resolved_parts = base_parts + node.module.split(".")
                else:
                    resolved_parts = base_parts
            else:
                if node.module:
                    resolved_parts = node.module.split(".")
                else:
                    resolved_parts = []

            if len(resolved_parts) >= 2 and resolved_parts[0] == "apps":
                imported_service = resolved_parts[1]
                if imported_service != service_name:
                    import_str = (
                        f"from {node.module or ''} import ..."
                        if node.module
                        else f"from {'.' * node.level} import ..."
                    )
                    violations.append(
                        f"Line {node.lineno}: Direct import of service '{imported_service}' "
                        f"via '{import_str}' is prohibited from within service '{service_name}'."
                    )

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

    if violations_found:
        print("\n[ERROR] Direct cross-service Python imports detected!")
        for file, errs in violations_found.items():
            print(f"\nIn file: {file}")
            for err in errs:
                print(f"  - {err}")
        print("\nBuild check failed due to cross-service import violations.")
        sys.exit(1)

    print(
        f"\n[SUCCESS] No cross-service import violations found across {total_files_checked} files."
    )
    sys.exit(0)


if __name__ == "__main__":
    main()
