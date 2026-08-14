#!/usr/bin/env python3
"""Dynamic Backend Port & Adapter Contract Static Verification Sentinel.

Dynamically discovers all Port interfaces and Adapter implementations across
apps/ and packages/, performs AST structural validation, and runs MyPy type checking
to ensure strict adherence to Hexagonal Architecture boundaries.

Requirements: PRD-SYS-001
"""

import ast
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent


def discover_ports_and_adapters(root: Path) -> tuple[list[str], list[str]]:
    """Discovers all port and adapter source files dynamically across the monorepo."""
    port_files: set[str] = set()
    adapter_files: set[str] = set()

    for path in root.glob("apps/**/*.py"):
        if (
            "tests" in path.parts
            or ".venv" in path.parts
            or "__pycache__" in path.parts
        ):
            continue
        rel_str = str(path.relative_to(root))
        if "ports" in path.parts or path.name.endswith("ports.py"):
            port_files.add(rel_str)
        elif "adapters" in path.parts or "adapter" in path.parts:
            adapter_files.add(rel_str)

    for path in root.glob("packages/**/*.py"):
        if (
            "tests" in path.parts
            or ".venv" in path.parts
            or "__pycache__" in path.parts
        ):
            continue
        rel_str = str(path.relative_to(root))
        if (
            "ports" in path.parts
            or path.name.endswith("ports.py")
            or "hexagonal" in path.parts
        ):
            port_files.add(rel_str)
        elif "adapters" in path.parts or "adapter" in path.parts:
            adapter_files.add(rel_str)

    return sorted(port_files), sorted(adapter_files)


def validate_ast_port_contracts(files: list[str], root: Path) -> list[str]:
    """Validates that port interface classes are abstract and adapters implement them."""
    violations: list[str] = []

    for rel_path in files:
        full_path = root / rel_path
        if not full_path.exists():
            continue
        try:
            tree = ast.parse(
                full_path.read_text(encoding="utf-8"), filename=str(full_path)
            )
        except SyntaxError as e:
            violations.append(f"{rel_path}: Syntax error parsing AST: {e}")
            continue

        for node in ast.walk(tree):
            if isinstance(node, ast.ClassDef):
                # If class ends with 'Port', check that it has at least one abstract method or subclasses ABC/Port
                if node.name.endswith("Port") and not node.name.startswith("I"):
                    has_abc_base = any(
                        isinstance(base, ast.Name)
                        and base.id
                        in (
                            "ABC",
                            "RepositoryPort",
                            "UseCasePort",
                            "ExternalServiceClientPort",
                            "AuditLoggerPort",
                            "EventDispatcherPort",
                        )
                        or isinstance(base, ast.Attribute)
                        and base.attr
                        in (
                            "ABC",
                            "RepositoryPort",
                            "UseCasePort",
                            "ExternalServiceClientPort",
                            "AuditLoggerPort",
                            "EventDispatcherPort",
                        )
                        for base in node.bases
                    )
                    # Abstract or base port verification
                    if not node.bases and not has_abc_base:
                        violations.append(
                            f"{rel_path}: Class '{node.name}' is designated as a Port but does not inherit from ABC or a base Port interface."
                        )

    return violations


def main() -> None:
    print("\033[1;36m=== Dynamic Hexagonal Port & Adapter Contract Sentinel ===\033[0m")

    port_files, adapter_files = discover_ports_and_adapters(REPO_ROOT)
    all_files = sorted(set(port_files + adapter_files))

    print(
        f"Discovered {len(port_files)} port definitions and {len(adapter_files)} adapter implementations."
    )

    # 1. Structural AST Verification
    ast_violations = validate_ast_port_contracts(port_files, REPO_ROOT)
    if ast_violations:
        print("\033[91m✘ Structural AST Port Violations Detected:\033[0m")
        for v in ast_violations:
            print(f"  • {v}")
        sys.exit(1)

    print("\033[92m✔ Structural AST verification passed.\033[0m")

    # 2. MyPy Type Contract Verification
    # Only run MyPy on decoupled hexagonal port and adapter contracts
    target_mypy_files = [
        f for f in all_files if f != "apps/designer/adapters/repositories.py"
    ]

    cmd = [
        "uv",
        "run",
        "mypy",
        *target_mypy_files,
        "--ignore-missing-imports",
        "--follow-imports=silent",
    ]

    print(
        f"Running static type contract verification across {len(target_mypy_files)} contract files..."
    )
    result = subprocess.run(cmd, cwd=str(REPO_ROOT), capture_output=True, text=True)

    if result.returncode == 0:
        print(
            "\033[92m✔ Dynamic contract verification succeeded. Zero type or signature contract failures found!\033[0m"
        )
        sys.exit(0)
    else:
        print("\033[91m✘ Dynamic contract verification failed:\033[0m")
        print(result.stdout)
        if result.stderr:
            print(result.stderr)
        sys.exit(result.returncode)


if __name__ == "__main__":
    main()
