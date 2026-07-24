import json
import os
import sys
from unittest.mock import MagicMock, mock_open, patch

sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from scripts.sync_ruleset import RULESET_NAME, get_repository, sync_ruleset


def test_get_repository_from_env():
    with patch.dict(os.environ, {"GITHUB_REPOSITORY": "testowner/testrepo"}):
        repo = get_repository()
        assert repo == "testowner/testrepo"


def test_get_repository_from_git_https():
    with patch.dict(os.environ, {}, clear=True):
        mock_run = MagicMock()
        mock_run.return_value.stdout = "https://github.com/someowner/somerepo.git\n"
        mock_run.return_value.stderr = ""
        mock_run.return_value.returncode = 0

        with patch("subprocess.run", mock_run):
            repo = get_repository()
            assert repo == "someowner/somerepo"


def test_get_repository_from_git_ssh():
    with patch.dict(os.environ, {}, clear=True):
        mock_run = MagicMock()
        mock_run.return_value.stdout = "git@github.com:sshowner/sshrepo.git\n"
        mock_run.return_value.stderr = ""
        mock_run.return_value.returncode = 0

        with patch("subprocess.run", mock_run):
            repo = get_repository()
            assert repo == "sshowner/sshrepo"


def test_get_repository_fallback():
    with patch.dict(os.environ, {}, clear=True):
        mock_run = MagicMock(side_effect=Exception("git command failed"))

        with patch("subprocess.run", mock_run):
            repo = get_repository()
            assert repo == "fderuiter/cadence-clinical"


def test_sync_ruleset_dry_run():
    with patch.dict(os.environ, {"TESTING_RULES_SYNC": "true"}, clear=True):
        with patch("pathlib.Path.exists", return_value=True):
            with patch(
                "builtins.open",
                mock_open(read_data='{"name": "main-branch-protection"}'),
            ):
                # Should return early without calling gh api because GITHUB_TOKEN/GH_TOKEN is missing
                sync_ruleset()


def test_sync_ruleset_create_new():
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
                    "builtins.open",
                    mock_open(read_data='{"name": "main-branch-protection"}'),
                ):
                    sync_ruleset()

        assert mock_run.call_count == 2
        # Check that second call was PUT to update the existing ruleset
        args = mock_run.call_args_list[1][0][0]
        assert "PUT" in args
        assert "repos/owner/repo/rulesets/12345" in args
