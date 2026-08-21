"""Unit and behavioral tests for the two-tier staged pre-commit hook runner.

@req:PRD-SYS-049
"""

import stat
import subprocess
from pathlib import Path

from scripts.pre_commit import (
    get_staged_files,
    install_pre_commit_hook,
    run_tier1_staged_checks,
)


def test_install_pre_commit_hook(tmp_path: Path):
    """Verify pre-commit hook installer creates executable hook file.

    @req:PRD-SYS-049
    """
    git_dir = tmp_path / ".git"
    hooks_dir = git_dir / "hooks"
    hooks_dir.mkdir(parents=True, exist_ok=True)

    success, msg = install_pre_commit_hook(tmp_path)
    assert success is True
    assert "installed" in msg.lower() or "updated" in msg.lower()

    hook_file = hooks_dir / "pre-commit"
    assert hook_file.exists()
    assert "scripts/pre_commit.py" in hook_file.read_text()
    mode = hook_file.stat().st_mode
    assert bool(mode & stat.S_IXUSR)


def test_get_staged_files(monkeypatch):
    """Verify get_staged_files correctly parses git diff output.

    @req:PRD-SYS-049
    """

    def mock_run(*args, **kwargs):
        class MockResult:
            returncode = 0
            stdout = "apps/execution/main.py\npackages/cli/formatting.py\nREADME.md\n"
            stderr = ""

        return MockResult()

    monkeypatch.setattr(subprocess, "run", mock_run)
    files = get_staged_files(Path("/mock/repo"))
    assert files == [
        "apps/execution/main.py",
        "packages/cli/formatting.py",
        "README.md",
    ]


def test_run_tier1_staged_checks_no_files():
    """Verify tier1 passes immediately when no Python files are staged.

    @req:PRD-SYS-049
    """
    res = run_tier1_staged_checks([], repo_root=Path("."))
    assert res["passed"] is True
    assert res["staged_count"] == 0
