#!/usr/bin/env python3
"""GxP Compliance Sync Script — Cadence Clinical.

This script is the single-command solution for keeping GxP compliance
documentation in sync with the current test state. It automates the
three-step workflow that the CI ``compliance`` job enforces:

    1. Run the .NET test suite and emit ``report_dotnet.trx``.
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

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

# Add repository root to sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

# Enforce Python 3.14+ runtime before loading standard modules or packages
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]} ({sys.executable}).\n"
            "Cadence Clinical requires Python 3.14+.\n"
            "Please run: uv run python scripts/sync_gxp.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info

# Mark that the GxP sync process is running, so spawned scripts know they aren't run directly.
# Triggered CI/CD compliance validation run.
os.environ["GXP_SYNC_RUNNING"] = "1"
os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "test-gxp-audit-secret-key-placeholder-abc"
)
os.environ.setdefault("GATEWAY_SECRET", "internal-gateway-secret-12345")
os.environ.setdefault("SIGNING_SECRET", "designer-amendment-secure-key-12345")
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)
os.environ.setdefault("TERMINOLOGY_OFFLINE", "true")
os.environ.setdefault("ALLOW_MOCK_SIGNATURES", "1")

if "GXP_SIGNING_PRIVATE_KEY" not in os.environ or "GXP_SIGNING_CERT" not in os.environ:
    from packages.compliance.services.gxp_signer import generate_gxp_signing_credentials

    _priv, _cert = generate_gxp_signing_credentials("cadence-gxp-runner")
    os.environ["GXP_SIGNING_PRIVATE_KEY"] = _priv
    os.environ["GXP_SIGNING_CERT"] = _cert

# Files that must be committed to keep CI green and compliant.
GXP_DOCS = [
    "docs/SDLC/Requirements_Traceability_Matrix.md",
    "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
    "docs/SDLC/runs",
]
TRX_REPORT = "report_dotnet.trx"
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
    import shutil

    resolved_cmd = list(cmd)
    if resolved_cmd and resolved_cmd[0] == "uv" and shutil.which("uv") is None:
        # Fallback to current virtual environment python
        if len(resolved_cmd) > 2 and resolved_cmd[1] == "run":
            if resolved_cmd[2] == "--all-extras":
                rest = resolved_cmd[3:]
            else:
                rest = resolved_cmd[2:]
            if rest and rest[0] == "python":
                resolved_cmd = [sys.executable] + rest[1:]
            elif rest and rest[0] == "pytest":
                resolved_cmd = [sys.executable, "-m", "pytest"] + rest[1:]
            else:
                resolved_cmd = [sys.executable, "-m"] + rest

    print(f"\n▶  {' '.join(resolved_cmd)}")
    return subprocess.run(
        resolved_cmd,
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


def get_required_test_assemblies(
    repo_root: Path = REPO_ROOT, configuration: str = "Release"
) -> list[Path]:
    """Inspect the repository for required .NET test project output assemblies.

    Args:
        repo_root: Path to repository root.
        configuration: Build configuration (e.g. 'Release' or 'Debug').

    Returns:
        List of absolute file paths to expected test assembly DLLs.
    """
    import xml.etree.ElementTree as ET

    assemblies = []
    tests_dir = repo_root / "tests"
    if not tests_dir.exists():
        return assemblies

    for csproj in sorted(tests_dir.glob("*.csproj")):
        try:
            tree = ET.parse(csproj)  # nosec B314
            root = tree.getroot()

            is_test = False
            for is_test_elem in root.findall(".//IsTestProject"):
                if is_test_elem.text and is_test_elem.text.strip().lower() == "true":
                    is_test = True
                    break

            if not is_test:
                for pkg in root.findall(".//PackageReference"):
                    if "xunit" in (pkg.get("Include") or "").lower():
                        is_test = True
                        break

            if is_test:
                assembly_name = csproj.stem
                for elem in root.findall(".//AssemblyName"):
                    if elem.text and elem.text.strip():
                        assembly_name = elem.text.strip()
                        break

                tfm = "net10.0"
                for elem in root.findall(".//TargetFramework"):
                    if elem.text and elem.text.strip():
                        tfm = elem.text.strip()
                        break

                base_output_path = "bin/"
                for elem in root.findall(".//BaseOutputPath"):
                    if elem.text and elem.text.strip():
                        base_output_path = elem.text.strip()
                        break

                dll_path = (
                    csproj.parent
                    / base_output_path
                    / configuration
                    / tfm
                    / f"{assembly_name}.dll"
                )
                assemblies.append(dll_path.resolve())
        except Exception as e:
            print(f"⚠ Failed to parse project file {csproj}: {e}")

    return assemblies


def check_test_assemblies_status(
    repo_root: Path = REPO_ROOT, configuration: str = "Release"
) -> tuple[bool, bool, list[Path]]:
    """Check if required .NET test assembly binaries exist and are up to date.

    Args:
        repo_root: Path to repository root.
        configuration: Build configuration (e.g. 'Release').

    Returns:
        Tuple of (missing: bool, stale: bool, required_assemblies: list[Path]).
    """
    required = get_required_test_assemblies(repo_root, configuration)
    if not required:
        default_unit = (
            repo_root
            / "tests"
            / "bin"
            / "UnitTests"
            / configuration
            / "net10.0"
            / "Cadence.UnitTests.dll"
        )
        default_arch = (
            repo_root
            / "tests"
            / "bin"
            / "ArchitectureTests"
            / configuration
            / "net10.0"
            / "Cadence.ArchitectureTests.dll"
        )
        default_e2e = (
            repo_root
            / "tests"
            / "bin"
            / "E2ETests"
            / configuration
            / "net10.0"
            / "Cadence.E2ETests.dll"
        )
        required = [default_unit, default_arch, default_e2e]

    missing_assemblies = [p for p in required if not p.is_file()]
    if missing_assemblies:
        return True, False, required

    min_assembly_mtime = min(p.stat().st_mtime for p in required)

    stale = False
    source_dirs = [
        repo_root / "apps",
        repo_root / "packages",
        repo_root / "tests",
    ]
    source_extensions = {".cs", ".csproj", ".props", ".targets", ".sln"}

    for s_dir in source_dirs:
        if not s_dir.exists():
            continue
        for root, dirs, files in os.walk(s_dir):
            dirs[:] = [
                d
                for d in dirs
                if d not in ("bin", "obj", ".venv", "node_modules", ".git")
            ]
            for f in files:
                ext = Path(f).suffix.lower()
                if ext in source_extensions:
                    full_p = Path(root) / f
                    try:
                        if full_p.stat().st_mtime > min_assembly_mtime:
                            stale = True
                            break
                    except OSError:
                        pass
            if stale:
                break
        if stale:
            break

    if not stale:
        for root_file in [
            "Cadence.sln",
            "Directory.Build.props",
            "Directory.Packages.props",
        ]:
            p = repo_root / root_file
            if p.is_file():
                try:
                    if p.stat().st_mtime > min_assembly_mtime:
                        stale = True
                        break
                except OSError:
                    pass

    return False, stale, required


def step_run_tests(dry_run: bool, full: bool = False, draft: bool = False) -> None:
    """Execute the .NET test suite and emit report_dotnet.trx.

    Args:
        dry_run: If True, skip running tests.
        full: If True, force full test execution.
        draft: If True, allow tests to fail or skip in offline mode.

    Raises:
        SystemExit: If tests fail and not in draft mode.
    """
    if dry_run or draft:
        print("⏭  [draft/dry-run] Skipping live test execution.")
        return

    print("\n" + "=" * 60)
    print("STEP 1 / 3 — Running test suite")
    print("=" * 60)

    configuration = "Release"
    missing, stale, _required = check_test_assemblies_status(REPO_ROOT, configuration)
    should_build = full or missing or stale

    if should_build:
        if missing:
            print("ℹ  [Preflight] Missing compiled test assemblies detected.")
        elif stale:
            print("ℹ  [Preflight] Test assemblies are stale relative to source files.")
        elif full:
            print("ℹ  [Preflight] Full rebuild requested.")

        print(
            "▶  Compiling solution binaries: dotnet build Cadence.sln --configuration Release --no-restore"
        )
        try:
            _run(
                [
                    "dotnet",
                    "build",
                    "Cadence.sln",
                    "--configuration",
                    configuration,
                    "--no-restore",
                ]
            )
            print("✔  [Preflight] Solution build succeeded.")
        except subprocess.CalledProcessError:
            print("\n" + "!" * 70, file=sys.stderr)
            print(
                "✘  [PREFLIGHT ERROR] Failed to compile .NET test assemblies.",
                file=sys.stderr,
            )
            print(
                "   Command: dotnet build Cadence.sln --configuration Release --no-restore",
                file=sys.stderr,
            )
            print(
                "   If working in an offline environment without pre-restored packages,",
                file=sys.stderr,
            )
            print(
                "   run compliance synchronization with '--draft' mode to bypass strict test execution:",
                file=sys.stderr,
            )
            print("     uv run python scripts/sync_gxp.py --draft", file=sys.stderr)
            print("!" * 70 + "\n", file=sys.stderr)
            if draft:
                print("⚠  Build failed; continuing in draft mode.")
                return
            sys.exit(1)
    else:
        print(
            "✔  [Preflight] Test assemblies present and up-to-date. Skipping build step."
        )

    try:
        _run(
            [
                "dotnet",
                "test",
                "Cadence.sln",
                "--no-restore",
                "--no-build",
                "--configuration",
                configuration,
                "--results-directory",
                ".",
                "--logger",
                "trx;LogFileName=report_dotnet.trx",
            ]
        )
    except subprocess.CalledProcessError:
        if draft:
            print("\n⚠  Dotnet test failed or offline; continuing in draft mode.")
        else:
            print("\n" + "!" * 70, file=sys.stderr)
            print("✘  Dotnet test execution failed.", file=sys.stderr)
            print(
                "   Fix failing tests before syncing GxP docs, or run in '--draft' mode:",
                file=sys.stderr,
            )
            print("     uv run python scripts/sync_gxp.py --draft", file=sys.stderr)
            print("!" * 70 + "\n", file=sys.stderr)
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

    cmd = [
        "uv",
        "run",
        "--all-extras",
        "python",
        "scripts/generate_rtm.py",
        "--report",
        TRX_REPORT,
    ]
    if dry_run:
        cmd.append("--validate")
        print("⏭  [dry-run] Running generate_rtm.py --validate (read-only).")
    if draft or (dry_run and not os.path.exists(TRX_REPORT)):
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
    parser.add_argument(
        "--full",
        action="store_true",
        help="Force full test suite execution bypassing incremental cache.",
    )
    args = parser.parse_args()

    if args.commit and args.dry_run:
        parser.error("--commit and --dry-run are mutually exclusive.")

    print_runtime_info("sync_gxp.py")
    print("Cadence Clinical — GxP Compliance Sync")
    print("=" * 60)
    if args.dry_run:
        print("Mode: DRY RUN (no files will be modified or staged)")
    elif args.commit:
        print("Mode: FULL SYNC + AUTO COMMIT")
    else:
        print("Mode: SYNC (will stage docs; commit manually)")

    step_run_tests(dry_run=args.dry_run, full=args.full, draft=args.draft)
    step_generate_rtm(dry_run=args.dry_run, draft=args.draft)
    step_stage_and_report(dry_run=args.dry_run, auto_commit=args.commit)

    print("\n" + "=" * 60)
    print("✔  GxP sync complete.")
    print("=" * 60)


if __name__ == "__main__":
    main()
