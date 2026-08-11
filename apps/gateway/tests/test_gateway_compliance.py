"""
Compliance tests for the Gateway service.
"""

import sys
from pathlib import Path


def test_environment_integrity():
    """
    GxP Installation Qualification Verification Test:
    Ensures that the execution environment meets structural, system and runtime requirements.
    @req:PRD-SYS-001
    """
    # 1. Verify Python Version (should be 3.14+ as per AGENTS.md)
    assert sys.version_info >= (3, 14), (
        f"Python version {sys.version} is less than 3.14!"
    )

    # 2. Verify Presence of Core Directory Boundaries
    repo_root = Path(__file__).resolve().parent.parent.parent.parent
    expected_dirs = ["apps", "packages", "docs", "tests", "scripts"]
    for d in expected_dirs:
        dir_path = repo_root / d
        assert dir_path.is_dir(), (
            f"Core GxP directory boundary '{d}' is missing at {dir_path}!"
        )

    # 3. Verify Presence of Critical Dependency Manifests
    assert (repo_root / "pyproject.toml").is_file(), (
        "pyproject.toml manifest is missing!"
    )
    assert (repo_root / "uv.lock").is_file(), "uv.lock dependency lockfile is missing!"
