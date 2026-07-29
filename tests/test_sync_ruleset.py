import json
import os
import sys
from pathlib import Path
from unittest.mock import MagicMock, mock_open, patch

import pytest

sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from scripts.sync_ruleset import RULESET_NAME, get_repository, sync_ruleset


def test_get_repository_from_env():
    """Verify that the repository name is retrieved correctly from GITHUB_REPOSITORY environment variable."""
    with patch.dict(os.environ, {"GITHUB_REPOSITORY": "testowner/testrepo"}):
        repo = get_repository()
        assert repo == "testowner/testrepo"


def test_get_repository_from_git_https():
    """Verify repository identifier parsing from git remote config with an HTTPS URL format."""
    with patch.dict(os.environ, {}, clear=True):
        mock_run = MagicMock()
        mock_run.return_value.stdout = "https://github.com/someowner/somerepo.git\n"
        mock_run.return_value.stderr = ""
        mock_run.return_value.returncode = 0

        with patch("subprocess.run", mock_run):
            repo = get_repository()
            assert repo == "someowner/somerepo"


def test_get_repository_from_git_ssh():
    """Verify repository identifier parsing from git remote config with an SSH URL format."""
    with patch.dict(os.environ, {}, clear=True):
        mock_run = MagicMock()
        mock_run.return_value.stdout = "git@github.com:sshowner/sshrepo.git\n"
        mock_run.return_value.stderr = ""
        mock_run.return_value.returncode = 0

        with patch("subprocess.run", mock_run):
            repo = get_repository()
            assert repo == "sshowner/sshrepo"


def test_get_repository_fallback():
    """Verify that the default fallback repository is returned if GITHUB_REPOSITORY is missing and git command fails."""
    with patch.dict(os.environ, {}, clear=True):
        mock_run = MagicMock(side_effect=Exception("git command failed"))

        with patch("subprocess.run", mock_run):
            repo = get_repository()
            assert repo == "fderuiter/cadence-clinical"


def test_sync_ruleset_dry_run():
    """Verify that sync_ruleset runs in dry-run mode and exits early when GITHUB_TOKEN is not provided."""
    with patch.dict(os.environ, {"TESTING_RULES_SYNC": "true"}, clear=True):
        with patch("pathlib.Path.exists", return_value=True):
            with patch(
                "pathlib.Path.glob", return_value=[Path(".github/rulesets/main.json")]
            ):
                with patch(
                    "builtins.open",
                    mock_open(read_data='{"name": "main-branch-protection"}'),
                ):
                    # Should return early without calling gh api because GITHUB_TOKEN/GH_TOKEN is missing
                    sync_ruleset()


def test_sync_ruleset_create_new():
    """Verify that a new ruleset is created via a POST API call when it does not already exist on GitHub."""
    with patch.dict(
        os.environ, {"GITHUB_REPOSITORY": "owner/repo", "GITHUB_TOKEN": "token"}
    ):
        mock_run = MagicMock()
        # First call (gh api to fetch list of rulesets) returns an empty list
        # Second call (gh api to create ruleset) returns success
        mock_run.side_effect = [
            MagicMock(stdout="[]", stderr="", returncode=0),
            MagicMock(stdout="{}", stderr="", returncode=0),
        ]

        with patch("subprocess.run", mock_run):
            with patch("pathlib.Path.exists", return_value=True):
                with patch(
                    "pathlib.Path.glob",
                    return_value=[Path(".github/rulesets/main.json")],
                ):
                    with patch(
                        "builtins.open",
                        mock_open(read_data='{"name": "main-branch-protection"}'),
                    ):
                        sync_ruleset()

        assert mock_run.call_count == 2
        # Check that second call was POST
        args = mock_run.call_args_list[1][0][0]
        assert "POST" in args
        assert "repos/owner/repo/rulesets" in args


def test_sync_ruleset_update_existing():
    """Verify that an existing ruleset is updated via a PUT API call matching its resolved ruleset ID."""
    with patch.dict(
        os.environ, {"GITHUB_REPOSITORY": "owner/repo", "GITHUB_TOKEN": "token"}
    ):
        mock_run = MagicMock()
        # First call (gh api to fetch list of rulesets) returns a list with our ruleset
        # Second call (gh api to update ruleset) returns success
        mock_run.side_effect = [
            MagicMock(
                stdout=json.dumps([{"name": RULESET_NAME, "id": 12345}]),
                stderr="",
                returncode=0,
            ),
            MagicMock(stdout="{}", stderr="", returncode=0),
        ]

        with patch("subprocess.run", mock_run):
            with patch("pathlib.Path.exists", return_value=True):
                with patch(
                    "pathlib.Path.glob",
                    return_value=[Path(".github/rulesets/main.json")],
                ):
                    with patch(
                        "builtins.open",
                        mock_open(read_data='{"name": "main-branch-protection"}'),
                    ):
                        sync_ruleset()

        assert mock_run.call_count == 2
        # Check that second call was PUT to update the existing ruleset
        args = mock_run.call_args_list[1][0][0]
        assert "PUT" in args
        assert "repos/owner/repo/rulesets/12345" in args


def test_sync_ruleset_permission_denied_403():
    """Verify that a permission denied (HTTP 403) from GitHub API raises SystemExit when FAIL_ON_RULESET_SYNC_ERROR is true."""
    import subprocess

    with patch.dict(
        os.environ,
        {
            "GITHUB_REPOSITORY": "owner/repo",
            "GITHUB_TOKEN": "token",
            "FAIL_ON_RULESET_SYNC_ERROR": "true",
        },
    ):
        mock_run = MagicMock()
        # First call: GET existing rulesets -> empty list
        # Second call: POST to create ruleset -> fails with 403
        err = subprocess.CalledProcessError(
            returncode=1,
            cmd=["gh", "api", "--method", "POST", "repos/owner/repo/rulesets"],
            output="",
            stderr="Resource not accessible by integration",
        )
        mock_run.side_effect = [
            MagicMock(stdout="[]", stderr="", returncode=0),
            err,
        ]

        with patch("subprocess.run", mock_run):
            with patch("pathlib.Path.exists", return_value=True):
                with patch(
                    "pathlib.Path.glob",
                    return_value=[Path(".github/rulesets/main.json")],
                ):
                    with patch(
                        "builtins.open",
                        mock_open(read_data='{"name": "main-branch-protection"}'),
                    ):
                        with pytest.raises(SystemExit) as exc_info:
                            sync_ruleset()
                        assert exc_info.value.code == 1


def test_sync_ruleset_permission_denied_403_graceful():
    """Verify that a permission denied (HTTP 403) logs a warning and returns cleanly without raising SystemExit when FAIL_ON_RULESET_SYNC_ERROR is not set."""
    import subprocess

    with patch.dict(
        os.environ,
        {"GITHUB_REPOSITORY": "owner/repo", "GITHUB_TOKEN": "token"},
        clear=True,
    ):
        mock_run = MagicMock()
        err = subprocess.CalledProcessError(
            returncode=1,
            cmd=["gh", "api", "--method", "POST", "repos/owner/repo/rulesets"],
            output="",
            stderr="Resource not accessible by integration",
        )
        mock_run.side_effect = [
            MagicMock(stdout="[]", stderr="", returncode=0),
            err,
        ]

        with patch("subprocess.run", mock_run):
            with patch("pathlib.Path.exists", return_value=True):
                with patch(
                    "pathlib.Path.glob",
                    return_value=[Path(".github/rulesets/main.json")],
                ):
                    with patch(
                        "builtins.open",
                        mock_open(read_data='{"name": "main-branch-protection"}'),
                    ):
                        # Should complete without raising SystemExit
                        sync_ruleset()


def test_sync_ruleset_multiple_files_integration():
    """Verify that multiple configuration files are detected and handled properly."""
    with patch.dict(
        os.environ, {"GITHUB_REPOSITORY": "owner/repo", "GITHUB_TOKEN": "token"}
    ):
        mock_run = MagicMock()
        # First call (gh api to fetch list of rulesets) returns main-branch-protection as existing,
        # but release-branches-protection and version-tags-protection as missing.
        mock_run.side_effect = [
            MagicMock(
                stdout=json.dumps([{"name": "main-branch-protection", "id": 55555}]),
                stderr="",
                returncode=0,
            ),
            MagicMock(
                stdout="{}", stderr="", returncode=0
            ),  # PUT main-branch-protection
            MagicMock(
                stdout="{}", stderr="", returncode=0
            ),  # POST release-branches-protection
            MagicMock(
                stdout="{}", stderr="", returncode=0
            ),  # POST version-tags-protection
        ]

        # Mocking the filesystem glob and file reads to test sync_ruleset with all three files
        mock_glob = [
            Path(".github/rulesets/main.json"),
            Path(".github/rulesets/release_branches.json"),
            Path(".github/rulesets/version_tags.json"),
        ]

        def mock_open_file(file_path, *args, **kwargs):
            path_str = str(file_path)
            if "main.json" in path_str:
                return mock_open(read_data='{"name": "main-branch-protection"}')()
            elif "release_branches.json" in path_str:
                return mock_open(read_data='{"name": "release-branches-protection"}')()
            elif "version_tags.json" in path_str:
                return mock_open(read_data='{"name": "version-tags-protection"}')()
            return mock_open()()

        with patch("subprocess.run", mock_run):
            with patch("pathlib.Path.exists", return_value=True):
                with patch("pathlib.Path.is_dir", return_value=True):
                    with patch("pathlib.Path.glob", return_value=mock_glob):
                        with patch("builtins.open", side_effect=mock_open_file):
                            sync_ruleset()

        assert mock_run.call_count == 4
        # Verify the API calls were made correctly
        calls = [call[0][0] for call in mock_run.call_args_list]
        # First call: GET existing rulesets
        assert "repos/owner/repo/rulesets" in calls[0]
        # Second call: PUT for main-branch-protection
        assert "PUT" in calls[1]
        assert "repos/owner/repo/rulesets/55555" in calls[1]
        # Third call: POST for release-branches-protection
        assert "POST" in calls[2]
        assert "repos/owner/repo/rulesets" in calls[2]
        # Fourth call: POST for version-tags-protection
        assert "POST" in calls[3]
        assert "repos/owner/repo/rulesets" in calls[3]


def test_gxp_ruleset_file_structures():
    """Verify that the actual release branches and version tags JSON files meet GxP compliance rules (no bypass allowed, blocking deletion & force-pushes)."""
    base_dir = Path(__file__).resolve().parent.parent
    release_path = base_dir / ".github" / "rulesets" / "release_branches.json"
    tags_path = base_dir / ".github" / "rulesets" / "version_tags.json"

    assert release_path.exists(), "release_branches.json does not exist"
    assert tags_path.exists(), "version_tags.json does not exist"

    with open(release_path, "r") as f:
        release_data = json.load(f)
    with open(tags_path, "r") as f:
        tags_data = json.load(f)

    # 1. Assert correct target and enforcement
    assert release_data["name"] == "release-branches-protection"
    assert release_data["target"] == "branch"
    assert release_data["enforcement"] == "active"
    assert "refs/heads/release/*" in release_data["conditions"]["ref_name"]["include"]

    assert tags_data["name"] == "version-tags-protection"
    assert tags_data["target"] == "tag"
    assert tags_data["enforcement"] == "active"
    assert "refs/tags/v*" in tags_data["conditions"]["ref_name"]["include"]

    # 2. Strict compliance: bypass_actors is omitted or empty
    assert "bypass_actors" not in release_data or not release_data["bypass_actors"]
    assert "bypass_actors" not in tags_data or not tags_data["bypass_actors"]

    # 3. Blocking deletions and non-fast-forward pushes
    release_rules = {r["type"] for r in release_data["rules"]}
    tags_rules = {r["type"] for r in tags_data["rules"]}

    assert "deletion" in release_rules
    assert "non_fast_forward" in release_rules
    assert "deletion" in tags_rules
    assert "non_fast_forward" in tags_rules
