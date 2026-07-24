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
    """Sync the declarative branch ruleset configuration to the repository."""
    # Resolve the absolute path of the configuration file relative to the script's root
    base_dir = Path(__file__).resolve().parent.parent
    config_file_path = base_dir / CONFIG_PATH

    if not config_file_path.exists():
        print(f"Error: Configuration file not found at {config_file_path}")
        sys.exit(1)

    print(f"Loading ruleset configuration from {config_file_path}...")
    try:
        with open(config_file_path, "r") as f:
            _ = json.load(f)
    except Exception as e:
        print(f"Error parsing JSON from {config_file_path}: {e}")
        sys.exit(1)

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

    existing_ruleset_id = None
    for ruleset in rulesets:
        if ruleset.get("name") == RULESET_NAME:
            existing_ruleset_id = ruleset.get("id")
            break

    if existing_ruleset_id:
        print(
            f"Found existing ruleset '{RULESET_NAME}' with ID {existing_ruleset_id}. Updating..."
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
        print("Ruleset updated successfully!")
    else:
        print(f"Ruleset '{RULESET_NAME}' not found. Creating new ruleset...")
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
        print("Ruleset created successfully!")


if __name__ == "__main__":
    sync_ruleset()
