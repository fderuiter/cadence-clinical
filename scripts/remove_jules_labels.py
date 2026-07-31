#!/usr/bin/env python3
"""Cadence Clinical — Remove Jules Labels Script.

Scans all GitHub issues and pull requests in the repository and removes any
labels that match 'jules' (case-insensitive) or start with 'jules:', 'jules-', or 'jules/'.
"""

import argparse
import json
import os
import subprocess
import sys
import urllib.parse


def handle_permission_error(stderr_msg: str) -> None:
    """Handle API authorization and permission errors gracefully."""
    combined = stderr_msg.lower()
    patterns = [
        "resource not accessible by integration",
        "403",
        "http 403",
        "must have admin rights",
        "viewer can't make query",
        "not logged in",
        "unauthorized",
        "forbidden",
        "permission",
        "rate limit",
        "rate_limit",
        "exceeded",
    ]
    if any(p in combined for p in patterns):
        print(
            "WARNING: GitHub API permission or authentication error occurred.\n"
            f"Error details: {stderr_msg.strip()}\n"
            "Skipping label removal.",
            file=sys.stderr,
        )
        if os.environ.get("FAIL_ON_REMOVE_JULES_LABELS_ERROR") == "true":
            sys.exit(1)
        else:
            sys.exit(0)


def run_gh_cmd(args: list[str]) -> tuple[int, str, str]:
    """Run a `gh` command and return (returncode, stdout, stderr)."""
    try:
        res = subprocess.run(
            args, capture_output=True, text=True, check=False, timeout=60
        )
        return res.returncode, res.stdout.strip(), res.stderr.strip()
    except Exception as e:
        return 1, "", str(e)


def is_jules_label(label_name: str) -> bool:
    """Check if a label name matches 'jules' case-insensitively or starts with 'jules' prefix."""
    lname = label_name.lower()
    return (
        lname == "jules"
        or lname.startswith("jules:")
        or lname.startswith("jules-")
        or lname.startswith("jules/")
    )


def fetch_all_issues(repo: str) -> list[dict]:
    """Fetch all issues (open and closed) from GitHub REST API."""
    issues: list[dict] = []
    page = 1
    while True:
        url_path = f"repos/{repo}/issues?state=all&per_page=100&page={page}"
        code, stdout, stderr = run_gh_cmd(["gh", "api", url_path])
        if code != 0:
            handle_permission_error(stderr)
            print(f"Error fetching issues on page {page}: {stderr}", file=sys.stderr)
            break

        page_issues = json.loads(stdout) if stdout else []
        if not page_issues:
            break

        issues.extend(page_issues)
        if len(page_issues) < 100:
            break
        page += 1

    return issues


def remove_label(
    repo: str, issue_number: int, label_name: str, dry_run: bool = False
) -> bool:
    """Remove a label from a specific issue or pull request."""
    encoded_label = urllib.parse.quote(label_name)
    if dry_run:
        print(f"[DRY-RUN] Would remove label '{label_name}' from Issue #{issue_number}")
        return True

    url_path = f"repos/{repo}/issues/{issue_number}/labels/{encoded_label}"
    code, stdout, stderr = run_gh_cmd(["gh", "api", "-X", "DELETE", url_path])
    if code == 0:
        print(f"Successfully removed label '{label_name}' from Issue #{issue_number}")
        return True
    print(
        f"Failed to remove label '{label_name}' from Issue #{issue_number}: {stderr}",
        file=sys.stderr,
    )
    return False


def main() -> None:
    """Main execution entry point."""
    parser = argparse.ArgumentParser(
        description="Remove 'jules' labels from GitHub issues."
    )
    parser.add_argument(
        "--repo",
        default=os.environ.get("GITHUB_REPOSITORY", "fderuiter/cadence-clinical"),
        help="GitHub repo (owner/repo)",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Perform dry run without deleting labels",
    )
    args = parser.parse_args()

    print(f"Scanning issues in repository: {args.repo}...")
    issues = fetch_all_issues(args.repo)
    print(f"Fetched {len(issues)} total issues and pull requests.")

    removed_count = 0
    issue_count = 0

    for item in issues:
        number = item.get("number")
        labels = item.get("labels", [])

        jules_labels = [
            label_item["name"]
            for label_item in labels
            if is_jules_label(label_item.get("name", ""))
        ]
        if jules_labels and number:
            issue_count += 1
            for lbl in jules_labels:
                if remove_label(args.repo, number, lbl, dry_run=args.dry_run):
                    removed_count += 1

    mode = " (DRY-RUN)" if args.dry_run else ""
    print(
        f"Done{mode}. Processed {issue_count} items with jules labels. Total labels removed: {removed_count}."
    )


if __name__ == "__main__":
    main()
