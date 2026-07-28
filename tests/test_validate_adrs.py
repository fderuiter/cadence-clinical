from unittest.mock import MagicMock, patch

from scripts.validate_adrs import (
    check_architectural_changes_require_adr,
    get_changed_files,
    is_architectural_file,
    validate_existing_adrs,
)


def test_is_architectural_file():
    # Architectural files
    assert is_architectural_file("pyproject.toml") is True
    assert is_architectural_file("package.json") is True
    assert is_architectural_file("apps/gateway/main.py") is True
    assert is_architectural_file("packages/security/model.py") is True
    assert is_architectural_file("packages/ui/model.py") is True
    assert is_architectural_file("packages/core-models/model.py") is False
    assert is_architectural_file("apps/execution/database/audit.py") is True
    assert is_architectural_file("apps/execution/models.py") is True
    assert is_architectural_file("apps/execution/migrations/0001_init.py") is True

    # Non-architectural files
    assert is_architectural_file("tests/test_validate_adrs.py") is False
    assert is_architectural_file("docs/SRS.md") is False
    assert is_architectural_file("scripts/validate_adrs.py") is False
    assert is_architectural_file(".github/workflows/ci.yml") is False
    assert is_architectural_file("apps/designer/tests/test_designer.py") is False
    assert is_architectural_file("README.md") is False


def test_check_architectural_changes_require_adr_no_changes():
    # If there are no architectural changes, it should pass
    changed_files = {"tests/test_validate_adrs.py", "docs/SRS.md"}
    assert check_architectural_changes_require_adr(changed_files) is True


def test_check_architectural_changes_require_adr_missing_adr():
    # If there are architectural changes but no ADR, it should fail
    changed_files = {"pyproject.toml", "tests/test_validate_adrs.py"}
    assert check_architectural_changes_require_adr(changed_files) is False


def test_check_architectural_changes_require_adr_with_valid_adr():
    # If there are architectural changes and a new ADR is added (and exists on disk), it should pass
    # We patch os.path.exists to return True for the newly added ADR file
    changed_files = {"pyproject.toml", "docs/adr/2026-07-24-test-new-dependency.md"}

    with patch("os.path.exists", return_value=True):
        assert check_architectural_changes_require_adr(changed_files) is True


def test_check_architectural_changes_require_adr_with_deleted_adr():
    # If an ADR file is listed in changed_files but does not exist on disk (deleted/renamed away), it should fail
    changed_files = {"pyproject.toml", "docs/adr/2026-07-24-test-deleted.md"}

    with patch("os.path.exists", return_value=False):
        assert check_architectural_changes_require_adr(changed_files) is False


def test_get_changed_files_from_txt():
    # If changed_files.txt exists, it should read from it
    mock_content = "pyproject.toml\n\napps/gateway/main.py\n"

    with (
        patch("os.path.exists", return_value=True),
        patch(
            "builtins.open",
            MagicMock(
                return_value=MagicMock(
                    __enter__=MagicMock(return_value=mock_content.splitlines())
                )
            ),
        ),
    ):
        changed = get_changed_files()
        assert "pyproject.toml" in changed
        assert "apps/gateway/main.py" in changed


@patch("scripts.validate_adrs.run_git_command")
def test_get_changed_files_from_git_fallbacks(mock_run_git):
    # Mock fallback to git diff and status porcelain using a robust side effect function
    def mock_run_git_side_effect(args):
        if "HEAD^" in args:
            return "apps/gateway/main.py\n", ""
        elif "origin/main" in args:
            return "", ""
        elif "HEAD~1" in args:
            return "", ""
        elif "--porcelain" in args:
            return "M  pyproject.toml\n", ""
        return "", ""

    mock_run_git.side_effect = mock_run_git_side_effect

    with patch("os.path.exists", return_value=False):
        changed = get_changed_files()
        assert "apps/gateway/main.py" in changed
        assert "pyproject.toml" in changed


def test_validate_existing_adrs_valid_case():
    # Ensure our existing ADR validation runs successfully on the repo's existing ADRs
    assert validate_existing_adrs() is True


def test_compliance_utility_parsing():
    from scripts import compliance_utility

    reqs = compliance_utility.get_valid_requirements()

    # Verify we successfully parsed both PRD and SRS requirements
    assert len(reqs) > 0
    assert "PRD-SYS-001" in reqs
    assert "Trace-1" in reqs


def test_compliance_utility_extraction_and_normalization():
    from scripts import compliance_utility

    content = (
        "Implements trace 1, Trace-2, prd-sys-001 and trace 999. Also PRD-ABC-123."
    )
    refs = compliance_utility.extract_requirement_references(content)

    # Check normalized outcomes
    assert "Trace-1" in refs
    assert "Trace-2" in refs
    assert "PRD-SYS-001" in refs
    assert "Trace-999" in refs
    assert "PRD-ABC-123" in refs


def test_adr_compliance_validation_logic():
    from scripts import compliance_utility

    valid_reqs = {"PRD-SYS-001", "Trace-1"}

    # 1. Pre-2026 legacy ADRs pass without requirement references
    success, err = compliance_utility.validate_adr_compliance(
        "2025-12-31-legacy-adr.md", "Some context without references.", valid_reqs
    )
    assert success is True
    assert err == ""

    # 2. Post-2026 modern ADR with valid requirement reference passes
    success, err = compliance_utility.validate_adr_compliance(
        "2026-01-01-modern-adr.md", "This implements Trace-1. Perfect.", valid_reqs
    )
    assert success is True
    assert err == ""

    # 3. Post-2026 modern ADR with missing requirement reference fails
    success, err = compliance_utility.validate_adr_compliance(
        "2026-01-01-modern-adr.md", "No references anywhere.", valid_reqs
    )
    assert success is False
    assert "lacks a valid requirement reference" in err
    assert "2026-01-01-modern-adr.md" in err

    # 4. Post-2026 modern ADR with invalid/misspelled requirement reference fails
    success, err = compliance_utility.validate_adr_compliance(
        "2026-01-01-modern-adr.md", "Implements Trace-99.", valid_reqs
    )
    assert success is False
    assert "references invalid or misspelled requirement identifier(s)" in err
    assert "Trace-99" in err
    assert "2026-01-01-modern-adr.md" in err
