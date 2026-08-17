"""Unit tests for verify_contracts.py AST validation.

Requirements: PRD-SYS-001
"""

from scripts.verify_contracts import validate_ast_port_contracts


def test_validate_ast_port_contracts_passes(tmp_path):
    # Port inheriting from RepositoryPort[Any]
    port_dir = tmp_path / "apps" / "dummy"
    port_dir.mkdir(parents=True, exist_ok=True)
    port_file = port_dir / "ports.py"
    port_file.write_text(
        "from packages.hexagonal import RepositoryPort\n"
        "class DummyRepositoryPort(RepositoryPort[str]):\n"
        "    pass\n",
        encoding="utf-8",
    )

    violations = validate_ast_port_contracts(
        [str(port_file.relative_to(tmp_path))], tmp_path
    )
    assert len(violations) == 0


def test_validate_ast_port_contracts_fails(tmp_path):
    # Port not inheriting from allowed bases
    port_dir = tmp_path / "apps" / "dummy"
    port_dir.mkdir(parents=True, exist_ok=True)
    port_file = port_dir / "ports.py"
    port_file.write_text(
        "class DummyBadPort:\n    pass\n",
        encoding="utf-8",
    )

    violations = validate_ast_port_contracts(
        [str(port_file.relative_to(tmp_path))], tmp_path
    )
    assert len(violations) == 1
    assert "is designated as a Port but does not inherit" in violations[0]
