"""Unit tests verifying the GitHub CI PR classification logic.

These tests ensure that the file classification behavior implemented in the
ci.yml workflow properly guards vulnerability exemptions ledger files and
validation scripts from being classified as safe, while allowing standard,
non-vulnerability markdown documentation and tests/scripts to proceed.
"""

import fnmatch


def classify_file(filepath: str) -> bool:
    """Python simulation of the bash classification logic in `.github/workflows/ci.yml`."""
    # Explicitly check for vulnerability ledger and validation scripts
    if filepath == "docs/SDLC/vulnerability_exclusions_ledger.json" or "validate_vulnerabilities.py" in filepath:
        return False

    # Check for safe patterns
    if (
        filepath.startswith("docs/")
        or filepath.endswith(".md")
        or filepath.startswith("tests/")
        or filepath.endswith(".sh")
        or filepath.startswith("scripts/")
    ):
        return True

    return False


def test_classify_vulnerability_ledger():
    """Assert that the vulnerability ledger is always classified as unsafe."""
    assert classify_file("docs/SDLC/vulnerability_exclusions_ledger.json") is False


def test_classify_vulnerability_script():
    """Assert that vulnerability validation scripts are always classified as unsafe."""
    assert classify_file("scripts/validate_vulnerabilities.py") is False
    assert classify_file("scripts/validate_vulnerabilities.py") is False


def test_classify_standard_markdown():
    """Assert that standard markdown files are always classified as safe."""
    assert classify_file("docs/SDLC/01_Product_Requirements_Document_PRD.md") is True
    assert classify_file("README.md") is True
    assert classify_file("docs/some_doc.md") is True
    assert classify_file("CONTRIBUTING.md") is True


def test_classify_safe_scripts_and_tests():
    """Assert that safe scripts and tests are classified as safe."""
    assert classify_file("scripts/validate_adrs.py") is True
    assert classify_file("tests/test_vulnerabilities.py") is True
    assert classify_file("scripts/clean_secrets_baseline.py") is True


def test_classify_unsafe_other_files():
    """Assert that general source code and configuration files are unsafe."""
    assert classify_file("package.json") is False
    assert classify_file("pyproject.toml") is False
    assert classify_file("apps/execution/main.py") is False
