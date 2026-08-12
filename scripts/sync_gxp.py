#!/usr/bin/env python3
"""GxP Compliance Sync Script — Cadence Clinical.

This script is the single-command solution for keeping GxP compliance
documentation in sync with the current test state. It automates the
three-step workflow that the CI ``compliance`` job enforces:

    1. Run the full pytest suite and emit ``report.xml``.
    2. Regenerate ``docs/SDLC/Requirements_Traceability_Matrix.md`` and
       ``docs/SDLC/IQ_OQ_PQ_Execution_Report.md`` from the test results.
    3. Stage the updated docs files ready for commit.

Usage
-----
Run from the repo root::

    uv run python scripts/sync_gxp.py            # full sync + git add
    uv run python scripts/sync_gxp.py --commit    # sync + auto-commit
    uv run python scripts/sync_gxp.py --dry-run   # check only, no changes

Or via the convenience aliases::

    pnpm sync-gxp
    make sync-gxp

When to run
-----------
Run this script whenever the CI ``compliance`` job fails with::

    GxP compliance documentation is out of sync with the current system state!

This happens when:
  * New tests were added that map to requirement IDs.
  * Existing tests were renamed or removed.
  * Test pass/fail outcomes changed since the last RTM commit.

The script will exit non-zero if tests fail, keeping it safe to use in
automated pipelines.
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path

# Mark that the GxP sync process is running, so spawned scripts know they aren't run directly.
os.environ["GXP_SYNC_RUNNING"] = "1"

# Files that must be committed to keep CI green and compliant.
GXP_DOCS = [
    "docs/SDLC/Requirements_Traceability_Matrix.md",
    "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
]
JUNIT_REPORT = "report.xml"
REPO_ROOT = Path(__file__).parent.parent


def _run(
    cmd: list[str], *, check: bool = True, capture: bool = False
) -> subprocess.CompletedProcess:
    """Run a subprocess command with consistent error reporting.

    Args:
        cmd: Command and arguments to execute.
        check: If True, raise on non-zero exit code.
        capture: If True, capture stdout/stderr instead of streaming.

    Returns:
        The completed process result.
    """
    print(f"\n▶  {' '.join(cmd)}")
    return subprocess.run(
        cmd,
        cwd=REPO_ROOT,
        check=check,
        capture_output=capture,
        text=True,
    )


def _git_diff_docs() -> list[str]:
    """Return a list of GxP doc paths that differ from HEAD.

    Returns:
        List of file paths (relative to repo root) that have uncommitted changes.
    """
    result = subprocess.run(
        ["git", "diff", "--name-only", "--", *GXP_DOCS],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        check=True,
    )
    # Also check for untracked / new files
    result_new = subprocess.run(
        ["git", "status", "--porcelain", "--", *GXP_DOCS],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        check=True,
    )
    changed = set()
    for line in result.stdout.splitlines():
        if line.strip():
            changed.add(line.strip())
    for line in result_new.stdout.splitlines():
        if line.strip():
            changed.add(line.strip().split(None, 1)[-1])
    return sorted(changed)


def _merge_reports(main_path: str, notif_path: str, dest_path: str) -> None:
    import xml.etree.ElementTree as ET

    try:
        main_tree = ET.parse(main_path)  # nosec B314
        main_root = main_tree.getroot()

        main_suite = main_root.find("testsuite")
        if main_suite is None:
            main_suite = main_root

        notif_tree = ET.parse(notif_path)  # nosec B314
        notif_root = notif_tree.getroot()
        notif_suite = notif_root.find("testsuite")
        if notif_suite is None:
            notif_suite = notif_root

        for testcase in list(notif_suite.findall("testcase")):
            main_suite.append(testcase)

        for attr in ["tests", "failures", "errors", "skipped"]:
            main_val = int(main_suite.get(attr) or 0)
            notif_val = int(notif_suite.get(attr) or 0)
            main_suite.set(attr, str(main_val + notif_val))

        for attr in ["tests", "failures", "errors", "skipped"]:
            main_val = int(main_root.get(attr) or 0)
            notif_val = int(notif_root.get(attr) or 0)
            main_root.set(attr, str(main_val + notif_val))

        main_tree.write(dest_path, encoding="utf-8", xml_declaration=True)
        print("✔  Successfully merged JUnit XML reports.")
    except Exception as e:
        print(f"⚠  Failed to merge reports: {e}")


def step_run_tests(dry_run: bool) -> None:
    """Execute the pytest suite and emit report.xml.

    Args:
        dry_run: If True, skip running tests.

    Raises:
        SystemExit: If tests fail.
    """
    if dry_run:
        print("⏭  [dry-run] Skipping test execution.")
        return

    print("\n" + "=" * 60)
    print("STEP 1 / 3 — Running test suite")
    print("=" * 60)
    try:
        # Run main tests with concurrency
        _run(
            [
                "uv",
                "run",
                "--all-extras",
                "pytest",
                "-n",
                "auto",
                "--junitxml",
                "report_main.xml",
                "-q",
                "--no-cov",
                "--ignore=scripts/tests/test_layout_validator.py",
                "--ignore=apps/notifications/tests/test_notifications.py",
                "--ignore=apps/notifications/tests/test_notification_worker.py",
                "--ignore=tests/validation",
                "--ignore=scripts/tests/test_translation_recovery.py",
                "--ignore=scripts/tests/test_translator.py",
                "--ignore=apps/designer/tests/test_crf_builder_compliance.py",
                "--ignore=apps/designer/tests/test_crf_requirements_mapping.py",
            ]
        )
        # Run translation and compliance layout tests sequentially
        _run(
            [
                "uv",
                "run",
                "--all-extras",
                "pytest",
                "--junitxml",
                "report_sequential.xml",
                "-q",
                "--no-cov",
                "scripts/tests/test_translation_recovery.py",
                "scripts/tests/test_translator.py",
                "apps/designer/tests/test_crf_builder_compliance.py",
                "apps/designer/tests/test_crf_requirements_mapping.py",
            ]
        )
        # Run notification tests sequentially
        _run(
            [
                "uv",
                "run",
                "--all-extras",
                "pytest",
                "--junitxml",
                "report_notif.xml",
                "-q",
                "--no-cov",
                "apps/notifications/tests/test_notifications.py",
                "apps/notifications/tests/test_notification_worker.py",
            ]
        )
        # Run layout validator tests sequentially
        _run(
            [
                "uv",
                "run",
                "--all-extras",
                "pytest",
                "--junitxml",
                "report_integration.xml",
                "-q",
                "--no-cov",
                "scripts/tests/test_layout_validator.py",
            ]
        )
        # Run GxP qualification validation suite sequentially
        _run(
            [
                "uv",
                "run",
                "--all-extras",
                "pytest",
                "tests/validation",
                "-o",
                "python_files=*_suite.py *_tests.py test_*.py",
                "--junitxml",
                "report_qualification.xml",
                "-q",
                "--no-cov",
            ]
        )
        # Merge reports
        _run(
            [
                "python3",
                "scripts/merge_junit.py",
                JUNIT_REPORT,
                "report_main.xml",
                "report_sequential.xml",
                "report_notif.xml",
                "report_integration.xml",
                "report_qualification.xml",
            ]
        )
    except subprocess.CalledProcessError:
        print("\n✘  Tests failed. Fix failing tests before syncing GxP docs.")
        sys.exit(1)


def step_generate_rtm(dry_run: bool, draft: bool = False) -> None:
    """Regenerate Requirements Traceability Matrix and Qualification Report.

    Args:
        dry_run: If True, run in --validate mode only (read-only).
        draft: If True, bypass fail-fast check and allow draft generation.

    Raises:
        SystemExit: If RTM generation fails.
    """
    print("\n" + "=" * 60)
    print("STEP 2 / 3 — Generating GxP compliance docs")
    print("=" * 60)

    cmd = ["uv", "run", "--all-extras", "python", "scripts/generate_rtm.py"]
    if dry_run:
        cmd.append("--validate")
        print("⏭  [dry-run] Running generate_rtm.py --validate (read-only).")
    if draft or (dry_run and not os.path.exists("report.xml")):
        cmd.append("--draft")

    try:
        _run(cmd)
    except subprocess.CalledProcessError:
        print("\n✘  RTM generation failed.")
        sys.exit(1)


def step_stage_and_report(dry_run: bool, auto_commit: bool) -> None:
    """Stage updated docs and optionally commit them.

    Args:
        dry_run: If True, only report the diff without staging.
        auto_commit: If True, automatically create a commit for the staged docs.
    """
    print("\n" + "=" * 60)
    print("STEP 3 / 3 — Staging compliance docs")
    print("=" * 60)

    changed = _git_diff_docs()

    if not changed:
        print("\n✔  GxP docs are already up to date — no commit needed.")
        return

    print("\nChanged files:\n  " + "\n  ".join(changed))

    if dry_run:
        print(
            "\n⚠  [dry-run] Docs are out of sync. "
            "Run without --dry-run to stage and commit."
        )
        sys.exit(1)

    _run(["git", "add", "--", *GXP_DOCS])
    print("\n✔  Files staged.")

    if auto_commit:
        _run(
            [
                "git",
                "commit",
                "-m",
                "docs(rtm): sync GxP compliance docs with current test state\n\n"
                "Regenerated by scripts/sync_gxp.py",
            ]
        )
        print("\n✔  Committed. Push with: git push")
    else:
        print(
            "\n  Files are staged. Commit them with:\n"
            "    git commit -m 'docs(rtm): sync GxP compliance docs with current test state'"
        )


def main() -> None:
    """Entry point for the GxP sync script."""
    parser = argparse.ArgumentParser(
        description=(
            "Sync GxP compliance documentation with the current test state.\n\n"
            "Runs: pytest → generate_rtm.py → git add docs/SDLC/\n\n"
            "Use this to resolve the CI error:\n"
            "  'GxP compliance documentation is out of sync with the current system state!'"
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Validate only — skip tests, run RTM in --validate mode, report diff without staging.",
    )
    parser.add_argument(
        "--commit",
        action="store_true",
        help="Automatically commit staged docs after generation (implies no --dry-run).",
    )
    parser.add_argument(
        "--draft",
        action="store_true",
        help="Generate draft compliance documents (bypasses fail-fast checks).",
    )
    args = parser.parse_args()

    if args.commit and args.dry_run:
        parser.error("--commit and --dry-run are mutually exclusive.")

    print("Cadence Clinical — GxP Compliance Sync")
    print("=" * 60)
    if args.dry_run:
        print("Mode: DRY RUN (no files will be modified or staged)")
    elif args.commit:
        print("Mode: FULL SYNC + AUTO COMMIT")
    else:
        print("Mode: FULL SYNC (will stage docs; commit manually)")

    step_run_tests(dry_run=args.dry_run)
    step_generate_rtm(dry_run=args.dry_run, draft=args.draft)
    step_stage_and_report(dry_run=args.dry_run, auto_commit=args.commit)

    print("\n" + "=" * 60)
    print("✔  GxP sync complete.")
    print("=" * 60)


if __name__ == "__main__":
    main()
