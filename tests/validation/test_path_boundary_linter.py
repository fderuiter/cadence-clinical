"""
Unit and integration tests for the path-pattern boundary linter.
"""

from pathlib import Path

import pytest  # type: ignore[import-not-found]

from scripts.validate_path_patterns import run_layout_assertions, validate_file


@pytest.fixture
def repo_root():
    return Path(__file__).resolve().parent.parent.parent


def test_linter_positive_cases(repo_root):
    """
    Test that compliant file paths are successfully validated.
    """
    # Correct python files
    is_valid, err = validate_file("apps/gateway/main.py", repo_root)
    assert is_valid is True, f"Expected apps/gateway/main.py to be valid but got: {err}"

    is_valid, err = validate_file("packages/core-models/audit.py", repo_root)
    assert is_valid is True, (
        f"Expected packages/core-models/audit.py to be valid but got: {err}"
    )

    is_valid, err = validate_file("tests/test_foo.py", repo_root)
    assert is_valid is True, f"Expected tests/test_foo.py to be valid but got: {err}"

    # Correct vue files
    is_valid, err = validate_file("apps/web/src/App.vue", repo_root)
    assert is_valid is True, f"Expected apps/web/src/App.vue to be valid but got: {err}"

    is_valid, err = validate_file("packages/ui/components/Button.vue", repo_root)
    assert is_valid is True, (
        f"Expected packages/ui/components/Button.vue to be valid but got: {err}"
    )

    # Correct root files
    is_valid, err = validate_file("pyproject.toml", repo_root)
    assert is_valid is True, f"Expected pyproject.toml to be valid but got: {err}"

    is_valid, err = validate_file("eslint.config.mjs", repo_root)
    assert is_valid is True, f"Expected eslint.config.mjs to be valid but got: {err}"


def test_linter_negative_cases(repo_root):
    """
    Test that invalid or misplaced file paths correctly trigger validation failures.
    """
    # Misplaced python file in root
    is_valid, err = validate_file("malicious.py", repo_root)
    assert is_valid is False
    assert (
        "File resides outside permitted root-level directories" in err
        or "must reside inside" in err
    )

    # Misplaced vue file
    is_valid, err = validate_file("scripts/confused.vue", repo_root)
    assert is_valid is False
    assert "Vue components (*.vue) must reside inside frontend structures" in err

    # Misplaced sh file
    is_valid, err = validate_file("apps/web/bad.sh", repo_root)
    assert is_valid is False
    assert "Shell scripts (*.sh) must reside in scripts" in err

    # Misplaced unknown file type in root
    is_valid, err = validate_file("random_file.xyz", repo_root)
    assert is_valid is False
    assert "File resides outside permitted root-level directories" in err


def test_environment_integrity_assertions(tmp_path):
    """
    Test that the layout integrity checks verify the required GxP structure correctly.
    """
    # Setup temporary layout that satisfies conditions
    (tmp_path / "apps").mkdir()
    (tmp_path / "packages").mkdir()
    (tmp_path / "docs").mkdir()
    (tmp_path / "tests").mkdir()
    (tmp_path / "scripts").mkdir()
    (tmp_path / "pyproject.toml").touch()
    (tmp_path / "uv.lock").touch()

    # Should pass without assertion errors
    run_layout_assertions(tmp_path)

    # Missing a core folder should fail
    (tmp_path / "apps").rmdir()
    with pytest.raises(
        AssertionError, match="Core GxP directory boundary 'apps' is missing"
    ):
        run_layout_assertions(tmp_path)
