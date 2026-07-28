#!/usr/bin/env python3
"""
Synchronization script to update branch protection rulesets via the GitHub API.
This script reads the declarative ruleset configuration from .github/rulesets/main.json
and syncs it to the target repository.
"""

import json
import os
import subprocess
import sys
from pathlib import Path

CONFIG_PATH = Path(".github/rulesets/main.json")
RULESET_NAME = "main-branch-protection"


def run_command(args: list[str], check: bool = True) -> tuple[str, str]:
    """Run a system command and return output."""
    try:
        res = subprocess.run(
            args,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=check,
            timeout=30,
        )
        return res.stdout.strip(), res.stderr.strip()
    except subprocess.TimeoutExpired as e:
        print(f"Command timed out: {' '.join(args)}")
        if check:
            raise e
        return "", "Timeout expired"
    except subprocess.CalledProcessError as e:
        print(f"Command failed: {' '.join(args)}")
        print(f"Stdout: {e.stdout}")
        print(f"Stderr: {e.stderr}")
        if check:
            raise e
        return "", e.stderr.strip()


def get_repository() -> str:
    """Determine the GitHub repository identifier (owner/repo)."""
    repo = os.environ.get("GITHUB_REPOSITORY")
    if repo:
        return repo

    # Fallback to parsing from git remote if GITHUB_REPOSITORY is not set
    try:
        stdout, _ = run_command(["git", "remote", "get-url", "origin"])
        if stdout:
            # Handle formats like:
            # https://github.com/owner/repo.git
            # git@github.com:owner/repo.git
            parts = stdout.replace(":", "/").split("/")
            if len(parts) >= 2:
                owner = parts[-2]
                repo_name = parts[-1].replace(".git", "")
                return f"{owner}/{repo_name}"
    except Exception as e:
        print(f"Warning: Could not resolve repository from git remote: {e}")

    return "fderuiter/cadence-clinical"  # Default fallback if all else fails


def sync_ruleset():
    """Sync the declarative branch ruleset configurations to the repository."""
    # Resolve the absolute path of the configuration files relative to the script's root
    base_dir = Path(__file__).resolve().parent.parent
    rulesets_dir = base_dir / ".github" / "rulesets"

    if not rulesets_dir.exists() or not rulesets_dir.is_dir():
        print(f"Error: Rulesets directory not found at {rulesets_dir}")
        sys.exit(1)

    # Scanning strictly designated configurations folder
    config_files = sorted(list(rulesets_dir.glob("*.json")))
    if not config_files:
        print(f"Error: No JSON ruleset configurations found in {rulesets_dir}")
        sys.exit(1)

    configs_to_sync = []
    for config_file_path in config_files:
        print(f"Loading ruleset configuration from {config_file_path}...")
        try:
            with open(config_file_path, "r") as f:
                config_data = json.load(f)
        except Exception as e:
            print(f"Error parsing JSON from {config_file_path}: {e}")
            sys.exit(1)

        ruleset_name = config_data.get("name")
        if not ruleset_name:
            print(
                f"Error: Ruleset configuration {config_file_path} is missing 'name' property."
            )
            sys.exit(1)
        configs_to_sync.append((config_file_path, ruleset_name))

    repo = get_repository()
    print(f"Target repository: {repo}")

    # Check for GITHUB_TOKEN or GH_TOKEN presence
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if not token:
        # Check if we are in a headless/mock test environment
        if os.environ.get("TESTING_RULES_SYNC") == "true":
            print("TESTING_RULES_SYNC is true. Running in dry-run/mock mode.")
            return

    # Fetch existing rulesets
    print("Fetching existing repository rulesets...")
    stdout, stderr = run_command(["gh", "api", f"repos/{repo}/rulesets"], check=False)

    if not stdout:
        print(
            f"Warning: Failed to fetch rulesets or repository doesn't have rulesets access (or gh not authenticated). Stderr: {stderr}"
        )
        if os.environ.get("GITHUB_ACTIONS") == "true" and not os.environ.get(
            "TEST_SUITE_RUN"
        ):
            print(
                "Error: Running in GitHub Actions but gh api returned empty output. Is GH_TOKEN configured?"
            )
            sys.exit(1)
        print("Exiting dry-run sync successfully.")
        return

    try:
        rulesets = json.loads(stdout)
    except Exception as e:
        print(f"Error parsing rulesets JSON from API: {e}. Output was: {stdout}")
        sys.exit(1)

    for config_file_path, ruleset_name in configs_to_sync:
        existing_ruleset_id = None
        for ruleset in rulesets:
            if ruleset.get("name") == ruleset_name:
                existing_ruleset_id = ruleset.get("id")
                break

        try:
            if existing_ruleset_id:
                print(
                    f"Found existing ruleset '{ruleset_name}' with ID {existing_ruleset_id}. Updating..."
                )
                update_url = f"repos/{repo}/rulesets/{existing_ruleset_id}"
                stdout, stderr = run_command(
                    [
                        "gh",
                        "api",
                        "--method",
                        "PUT",
                        update_url,
                        "--input",
                        str(config_file_path),
                    ],
                    check=True,
                )
                print(f"Ruleset '{ruleset_name}' updated successfully!")
            else:
                print(f"Ruleset '{ruleset_name}' not found. Creating new ruleset...")
                create_url = f"repos/{repo}/rulesets"
                stdout, stderr = run_command(
                    [
                        "gh",
                        "api",
                        "--method",
                        "POST",
                        create_url,
                        "--input",
                        str(config_file_path),
                    ],
                    check=True,
                )
                print(f"Ruleset '{ruleset_name}' created successfully!")
        except subprocess.CalledProcessError as e:
            stderr_msg = e.stderr or ""
            stdout_msg = e.stdout or ""
            combined_output = f"{stderr_msg}\n{stdout_msg}"
            if (
                "Resource not accessible by integration" in combined_output
                or "403" in combined_output
            ):
                print(
                    "Error: Permission denied (HTTP 403) during ruleset administration.\n"
                    "Ruleset administration requires a token with 'Administration: write' permissions. "
                    "Please verify that the GITHUB_TOKEN has the required permissions or that a dedicated admin-capable PAT is supplied."
                )
            else:
                print(
                    f"Error: Command failed with exit code {e.returncode}.\nStderr: {stderr_msg}\nStdout: {stdout_msg}"
                )
            sys.exit(1)


if __name__ == "__main__":
    sync_ruleset()
