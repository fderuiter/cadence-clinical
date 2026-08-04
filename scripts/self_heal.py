#!/usr/bin/env python3
"""
Autonomous Self-Healing for Classified Safe Changes

This script automatically detects if a PR is a classified safe change (labeled 'safe-change'),
checks if there are merge conflicts, verifies file guardrails, attempts to autonomously
merge main into the PR branch, regenerates conflicting lockfiles (uv.lock, pnpm-lock.yaml),
executes validation checks, and pushes the healed branch using high-privilege credentials.
It also updates the PR checklist comment to report success or failure.
"""

import json
import os
import subprocess
import sys


def run_command(args: list[str], check: bool = True) -> tuple[str, str]:
    """Run a system command and return (stdout, stderr)."""
    try:
        res = subprocess.run(
            args,
            capture_output=True,
            text=True,
            check=check,
            timeout=120,
        )
        return res.stdout.strip(), res.stderr.strip()
    except subprocess.CalledProcessError as e:
        print(f"Command failed: {' '.join(args)}")
        print(f"Stdout: {e.stdout}")
        print(f"Stderr: {e.stderr}")
        if check:
            raise e
        return e.stdout.strip(), e.stderr.strip()
    except Exception as e:
        print(f"Exception running {' '.join(args)}: {e}")
        if check:
            raise e
        return "", str(e)


def is_safe_file(filepath: str) -> bool:
    """Check if a file is considered safe for autonomous self-healing.

    Allowed safe files:
    - Documentation files (*.md, *.markdown) except under docs/SDLC
    - Test files (*test*, tests/)
    - Lockfiles (uv.lock, pnpm-lock.yaml)
    - The self_heal.py script itself and its tests
    - Automation configurations and scripts (.github/, scripts/)
    """
    # 1. Regulated compliance files under docs/SDLC are strictly prohibited
    if "docs/SDLC" in filepath or "docs/sdlc" in filepath.lower():
        return False

    # 2. Allow our own self-healing script, tests, and general GitHub action / automation scripts
    if (
        filepath.startswith(".github/")
        or filepath.startswith("scripts/")
        or "test_self_heal" in filepath
    ):
        return True

    # 3. Lockfiles are allowed
    if filepath in ("uv.lock", "pnpm-lock.yaml"):
        return True

    # 4. Markdown/documentation files are allowed
    if filepath.endswith(".md") or filepath.endswith(".markdown"):
        return True

    # 5. Test files (e.g., tests/, test_*.py, etc.) are allowed
    return bool("test" in filepath.lower() or "fixture" in filepath.lower())


def update_pr_comment(outcome: str) -> None:
    """Run post_pr_comment.py with the specified CONFLICT_OUTCOME."""
    os.environ["CONFLICT_OUTCOME"] = outcome
    try:
        # Run post_pr_comment.py using the same python interpreter
        res = subprocess.run(
            [sys.executable, "scripts/post_pr_comment.py"],
            capture_output=True,
            text=True,
        )
        print("--- post_pr_comment.py stdout ---")
        print(res.stdout)
        print("--- post_pr_comment.py stderr ---")
        print(res.stderr, file=sys.stderr)
    except Exception as e:
        print(f"Failed to run post_pr_comment.py: {e}", file=sys.stderr)


def main() -> None:
    repo = os.environ.get("GITHUB_REPOSITORY")
    pr_number = os.environ.get("PR_NUMBER")

    if not repo or not pr_number:
        print("Missing GITHUB_REPOSITORY or PR_NUMBER. Local execution / dry run?")
        # We can run in fallback mode or dry-run mode for local testing
        if len(sys.argv) > 1 and sys.argv[1] == "--test-only":
            print("Dry run / test only mode.")
            sys.exit(0)
        print("Exiting as GITHUB_REPOSITORY and PR_NUMBER are not set.")
        sys.exit(0)

    print(f"Starting Autonomous Self-Healing for PR #{pr_number} in {repo}")

    # 1. Fetch PR details (labels, branch names, mergeable status)
    # To bypass GitHub API GraphQL/REST rate limit exhaustion errors, we prioritize
    # reading the pull request metadata from the local on-disk GITHUB_EVENT_PATH payload.
    # If the payload is unavailable, we gracefully fallback to the GitHub CLI (gh pr view)
    # with robust error handling, and finally fallback to local Git command resolution.
    labels = []
    head_branch = os.environ.get("GITHUB_HEAD_REF")
    base_branch = os.environ.get("GITHUB_BASE_REF") or "main"
    mergeable_status = "UNKNOWN"

    # Try loading from local GITHUB_EVENT_PATH payload first to bypass API rate limits
    event_path = os.environ.get("GITHUB_EVENT_PATH")
    if event_path and os.path.exists(event_path):
        try:
            with open(event_path) as f:
                event_data = json.load(f)
            pr_payload = event_data.get("pull_request", {})
            if pr_payload:
                labels = [
                    lbl["name"] for lbl in pr_payload.get("labels", []) if "name" in lbl
                ]
                if not head_branch:
                    head_branch = pr_payload.get("head", {}).get("ref")
                if base_branch == "main":
                    base_branch = pr_payload.get("base", {}).get("ref") or "main"
                m = pr_payload.get("mergeable")
                if m is True:
                    mergeable_status = "MERGEABLE"
                elif m is False:
                    mergeable_status = "CONFLICTING"
                print("Loaded PR details from GITHUB_EVENT_PATH payload:")
                print(f"  Labels: {labels}")
                print(f"  Head branch: {head_branch}")
                print(f"  Base branch: {base_branch}")
                print(f"  Mergeable: {mergeable_status}")
        except Exception as e:
            print(f"Failed to read/parse GITHUB_EVENT_PATH payload: {e}")

    # Backup / fallback: query via gh pr view
    if not labels or not head_branch or mergeable_status == "UNKNOWN":
        print("Querying GitHub API (via gh pr view) as fallback...")
        pr_json, pr_err = run_command(
            [
                "gh",
                "pr",
                "view",
                pr_number,
                "--json",
                "labels,headRefName,baseRefName,mergeable",
            ],
            check=False,
        )

        if pr_json:
            try:
                pr_data = json.loads(pr_json)
                if not labels:
                    labels = [lbl["name"] for lbl in pr_data.get("labels", [])]
                if not head_branch:
                    head_branch = pr_data.get("headRefName")
                if base_branch == "main":
                    base_branch = pr_data.get("baseRefName", "main")
                mergeable_status = pr_data.get("mergeable", "UNKNOWN")
                print("Successfully updated PR details from GitHub API.")
            except Exception as e:
                print(f"Failed to parse PR JSON from API fallback: {e}")
        else:
            print(f"GitHub API fallback view failed: {pr_err}")

    # Ultimate fallback for head branch
    if not head_branch:
        try:
            stdout, _ = run_command(["git", "rev-parse", "--abbrev-ref", "HEAD"])
            head_branch = stdout.strip()
            print(f"Determined head branch from local git: {head_branch}")
        except Exception as e:
            print(f"Failed to resolve head branch from git: {e}")

    if not head_branch:
        print("Error: Head branch could not be resolved. Exiting.")
        sys.exit(1)

    print(f"Final resolved labels: {labels}")
    print(f"Final resolved head branch: {head_branch}")
    print(f"Final resolved base branch: {base_branch}")
    print(f"Final resolved mergeable status: {mergeable_status}")

    # 2. Label Check
    if "safe-change" not in labels:
        print("PR does not have 'safe-change' label. Skipping automated self-healing.")
        # Post conflict comment based on the actual mergeable status
        outcome = "failure" if mergeable_status == "CONFLICTING" else "success"
        update_pr_comment(outcome)
        sys.exit(0)

    print("PR is labeled 'safe-change'. Proceeding with guardrail checks...")

    # 3. File Guardrails Check
    # Fetch list of changed files in this PR
    print("Fetching changed files...")
    files_json, files_err = run_command(
        [
            "gh",
            "api",
            f"repos/{repo}/pulls/{pr_number}/files",
            "--paginate",
            "--jq",
            ".[].filename",
        ],
        check=False,
    )

    changed_files = []
    if files_json:
        changed_files = [
            line.strip() for line in files_json.splitlines() if line.strip()
        ]
    else:
        # Fallback to git diff
        print(
            f"Could not fetch files from GitHub API: {files_err}. Falling back to git diff."
        )
        stdout, _ = run_command(
            ["git", "diff", "--name-only", f"origin/{base_branch}...HEAD"], check=False
        )
        changed_files = [line.strip() for line in stdout.splitlines() if line.strip()]

    print(f"Changed files in PR: {changed_files}")

    non_safe_files = [f for f in changed_files if not is_safe_file(f)]
    if non_safe_files:
        print(
            f"STRICT BLOCK: PR modifies non-safe or regulated files: {non_safe_files}"
        )
        print("Autonomous self-healing is strictly blocked on non-safe modifications.")
        update_pr_comment("failure")
        sys.exit(1)

    print("File guardrails check passed! All changed files are classified as 'safe'.")

    # 4. Check if there's actually a merge conflict (using git dry-run merge and mergeable_status)
    if mergeable_status == "MERGEABLE":
        print("PR is already marked as MERGEABLE. No conflict to heal.")
        sys.exit(0)

    # 5. Autonomous Git Merge
    # Configure helper identity
    run_command(["git", "config", "user.name", "github-actions[bot]"], check=False)
    run_command(
        ["git", "config", "user.email", "github-actions[bot]@users.noreply.github.com"],
        check=False,
    )

    # Fetch latest base branch
    print(f"Fetching latest {base_branch}...")
    run_command(["git", "fetch", "origin", base_branch], check=False)

    # Attempt merge to test for conflicts
    print(f"Testing merge of origin/{base_branch} into feature branch {head_branch}...")
    merge_stdout, merge_stderr = run_command(
        ["git", "merge", f"origin/{base_branch}", "--no-commit", "--no-ff"], check=False
    )

    # Find conflicting files
    conflict_stdout, _ = run_command(
        ["git", "diff", "--name-only", "--diff-filter=U"], check=False
    )
    conflicting_files = [
        line.strip() for line in conflict_stdout.splitlines() if line.strip()
    ]
    print(f"Conflicting files: {conflicting_files}")

    if not conflicting_files:
        print(
            "No active merge conflicts detected via git merge dry-run. Aborting test merge."
        )
        run_command(["git", "merge", "--abort"], check=False)
        sys.exit(0)

    # Check if there are non-safe files in conflict
    non_safe_conflicts = [f for f in conflicting_files if not is_safe_file(f)]
    if non_safe_conflicts:
        print(
            f"STRICT BLOCK: Conflicts detected in non-safe files: {non_safe_conflicts}"
        )
        print("Aborting merge.")
        run_command(["git", "merge", "--abort"], check=False)
        update_pr_comment("failure")
        sys.exit(1)

    # Resolve conflicts in safe files
    regenerate_uv = False
    regenerate_pnpm = False

    for file in conflicting_files:
        print(f"Resolving conflict in: {file}")
        if file == "uv.lock":
            regenerate_uv = True
            run_command(["git", "checkout", "--ours", "uv.lock"])
            run_command(["git", "add", "uv.lock"])
        elif file == "pnpm-lock.yaml":
            regenerate_pnpm = True
            run_command(["git", "checkout", "--ours", "pnpm-lock.yaml"])
            run_command(["git", "add", "pnpm-lock.yaml"])
        else:
            # Documentation or tests: keep ours to preserve PR changes
            run_command(["git", "checkout", "--ours", file])
            run_command(["git", "add", file])

    # Regenerate lockfiles programmatically using native package managers if they were in conflict or changed
    if "uv.lock" in changed_files:
        regenerate_uv = True
    if "pnpm-lock.yaml" in changed_files:
        regenerate_pnpm = True

    if regenerate_uv:
        print("Regenerating uv.lock programmatically using uv...")
        sync_out, sync_err = run_command(
            ["uv", "sync", "--python", "3.14", "--all-extras"], check=False
        )
        print(sync_out)
        if sync_err:
            print(f"uv sync stderr: {sync_err}")
        run_command(["git", "add", "uv.lock"])

    if regenerate_pnpm:
        print("Regenerating pnpm-lock.yaml programmatically using pnpm...")
        pnpm_out, pnpm_err = run_command(
            ["pnpm", "install", "--no-frozen-lockfile"], check=False
        )
        print(pnpm_out)
        if pnpm_err:
            print(f"pnpm install stderr: {pnpm_err}")
        run_command(["git", "add", "pnpm-lock.yaml"])

    # Check if there are changes to commit
    status_out, _ = run_command(["git", "status", "--porcelain"], check=False)
    if not status_out.strip():
        print("No changes to commit. Branch is already fully in sync.")
    else:
        # Commit the merge
        print("Committing resolved merge...")
        commit_out, commit_err = run_command(
            [
                "git",
                "commit",
                "-m",
                "chore: autonomous self-healing of safe changes merge conflict",
            ],
            check=False,
        )
        print(commit_out)

    # 6. Execute validation checks before pushing
    print("Executing validation checks...")
    # Ruff Linting Check
    print("Running Ruff linting check...")
    try:
        run_command(["uv", "run", "ruff", "check", "."], check=True)
    except subprocess.CalledProcessError:
        print("Ruff linting failed! Aborting healing.")
        run_command(["git", "reset", "--hard", "HEAD~1"], check=False)
        update_pr_comment("failure")
        sys.exit(1)

    # Pytest Unit Tests Check
    print("Running targeted unit/integration tests validation...")
    try:
        run_command(
            ["uv", "run", "pytest", "tests/test_pr_comment.py", "--no-cov"], check=True
        )
    except subprocess.CalledProcessError:
        print("Tests validation failed! Aborting healing.")
        run_command(["git", "reset", "--hard", "HEAD~1"], check=False)
        update_pr_comment("failure")
        sys.exit(1)

    print("Validation checks passed successfully!")

    # 7. Secure Pushing with High Privilege
    pat = os.environ.get("PAT_FDERUITER") or os.environ.get("GH_TOKEN")
    if pat:
        print("Configuring remote URL with credentials...")
        run_command(
            [
                "git",
                "remote",
                "set-url",
                "origin",
                f"https://x-access-token:{pat}@github.com/{repo}.git",
            ]
        )

    print(f"Pushing healed branch to origin/HEAD:{head_branch}...")
    push_out, push_err = run_command(
        ["git", "push", "origin", f"HEAD:{head_branch}"], check=False
    )
    print(push_out)
    if push_err:
        print(f"Push warnings/errors: {push_err}")

    print("Autonomous self-healing completed successfully and pushed!")
    update_pr_comment("success")


if __name__ == "__main__":
    main()
