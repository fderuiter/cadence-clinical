#!/usr/bin/env python3
"""
Post PR Quality Checklist Comment Generator

This script runs in GitHub Actions to generate a natural, actionable, and PR-specific
quality gate comment on Pull Requests. It inspects PR metadata and changed files,
provides exact local terminal commands to resolve any failing checks, and wraps static
GxP compliance checklists in a clean collapsible reference section.
"""

import json
import os
import re
import subprocess
import sys

ROW_KEYS: dict[str, str] = {
    "Linting & Formatting": "lint",
    "Backend Tests & Coverage": "test",
    "Frontend Checks": "frontend",
    "ADR Validation": "adr",
    "Dependency & Static Audit": "audit",
    "Dependency, Static Audit & Secrets Scan": "audit",
    "DEID Compliance Scan": "deid",
    "Git Merge Conflicts": "conflict",
    "Code Duplication Scan": "duplication",
    "Requirements Traceability": "traceability",
}

FIX_COMMANDS: dict[str, str] = {
    "lint": "`uv run ruff check . --fix && uv run ruff format .`",
    "test": "`uv run pytest -n auto`",
    "frontend": "`pnpm -r format && pnpm -r lint`",
    "adr": "`python3 scripts/validate_adrs.py`",
    "audit": "`uv run pre-commit run detect-secrets --all-files`",
    "conflict": "`git fetch origin main && git merge origin/main`",
    "deid": "`uv run python -m packages.deid.cli`",
    "duplication": "`python3 scripts/detect_duplication.py`",
    "traceability": "`python3 scripts/generate_rtm.py --validate`",
}


def run_command(args: list[str], check: bool = True) -> tuple[str, str]:
    """Run a system command and return output with a finite timeout."""
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
        print(f"Command timed out (30s limit): {' '.join(args)}")
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


def get_status_emoji(outcome: str | None) -> str:
    if not outcome:
        return "⚪ Skip/Unknown"
    outcome = outcome.lower()
    if outcome in ("success", "passed", "true", "yes"):
        return "✅ Passed"
    elif outcome in ("failure", "failed", "false", "no"):
        return "❌ Failed"
    elif outcome in ("skipped", "skip"):
        return "⚪ Skipped"
    elif outcome in ("warning", "warn"):
        return "⚠️ Warning"
    else:
        return f"⚪ {outcome.capitalize()}"


def parse_existing_outcomes(comment_body: str) -> dict[str, str]:
    """Parse existing comment body to extract previously stored outcomes."""
    outcomes: dict[str, str] = {}
    pattern = re.compile(r"\|\s*\*(.*?)\*\*.*?\s*\|\s*(.*?)\s*\|")
    for match in pattern.finditer(comment_body):
        raw_key = match.group(1).strip()
        raw_status = match.group(2).strip()

        key: str | None = None
        for rk, k in ROW_KEYS.items():
            if rk in raw_key:
                key = k
                break

        if key:
            if "Passed" in raw_status or "No Conflicts" in raw_status:
                outcomes[key] = "success"
            elif "Failed" in raw_status or "Conflicts Detected" in raw_status:
                outcomes[key] = "failure"
            elif "Skipped" in raw_status or "Skip" in raw_status:
                outcomes[key] = "skipped"
            elif "Warning" in raw_status:
                outcomes[key] = "warning"
            else:
                outcomes[key] = "skipped"
    return outcomes


def merge_outcomes(
    new_outcomes: dict[str, str], existing_outcomes: dict[str, str]
) -> dict[str, str]:
    """Merge newly supplied outcomes with existing ones to avoid state erasure."""
    merged: dict[str, str] = {}
    for key in [
        "lint",
        "test",
        "frontend",
        "adr",
        "audit",
        "conflict",
        "deid",
        "duplication",
        "traceability",
    ]:
        new_val = new_outcomes.get(key)
        existing_val = existing_outcomes.get(key)

        if new_val and new_val.lower() not in ("skipped", "skip", "unknown", ""):
            merged[key] = new_val
        elif existing_val:
            merged[key] = existing_val
        else:
            merged[key] = new_val or "skipped"
    return merged


def get_pr_metadata(repo: str, pr_number: str) -> tuple[str, list[str]]:
    """Fetch PR title and list of changed file paths using gh API."""
    pr_title = f"PR #{pr_number}"
    changed_files: list[str] = []

    # Fetch PR title
    title_json, _ = run_command(
        ["gh", "api", f"repos/{repo}/pulls/{pr_number}", "--jq", ".title"],
        check=False,
    )
    if title_json:
        pr_title = title_json.strip()

    # Fetch changed files
    files_json, _ = run_command(
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
    if files_json:
        changed_files = [
            line.strip() for line in files_json.splitlines() if line.strip()
        ]

    return pr_title, changed_files


def summarize_components(changed_files: list[str]) -> str:
    """Categorize changed files into human-readable functional components."""
    if not changed_files:
        return "Workspace & Repository Configurations"

    component_map: dict[str, str] = {
        "apps/execution": "`apps/execution/` (Electronic Data Capture & Audit Engine)",
        "apps/designer": "`apps/designer/` (Study Designer & Clinical MDR)",
        "apps/gateway": "`apps/gateway/` (API Gateway & OIDC Authentication)",
        "apps/web": "`apps/web/` (Frontend Vue 3 SPA)",
        "apps/subject-portal": "`apps/subject-portal/` (Patient-Facing eCOA/ePRO Portal)",
        "apps/etmf": "`apps/etmf/` (eTMF Document Management)",
        "apps/ctms": "`apps/ctms/` (Clinical Trial Management System)",
        "apps/quality": "`apps/quality/` (Clinical Quality & CAPA Logging)",
        "apps/interop": "`apps/interop/` (FHIR & Interoperability Gateway)",
        "apps/notifications": "`apps/notifications/` (Notifications & Webhooks)",
        "apps/tickets": "`apps/tickets/` (Clinical Queries & Issues)",
        "apps/safety": "`apps/safety/` (Safety & ICSR / SAE Management)",
        "apps/org": "`apps/org/` (Organization & Personnel Management)",
        "packages/security": "`packages/security/` (Security & RBAC Package)",
        "packages/ui": "`packages/ui/` (Shared UI Component Library)",
        "packages/core-models": "`packages/core-models/` (CDISC USDM, ODM & SDTM Core Models)",
        "packages/deid": "`packages/deid/` (DEID Anonymization Service)",
        "docs": "`docs/` (Workspace Specifications & ADR Documentation)",
        "scripts": "`scripts/` (Automation, Validation & CI Tooling)",
        ".github": "`.github/` (GitHub Workflows & Automation Configs)",
    }

    matched: set[str] = set()
    for file in changed_files:
        for prefix, label in component_map.items():
            if file.startswith(prefix):
                matched.add(label)
                break

    if not matched:
        return "Workspace & System Configurations"

    return "\n".join(f"- {item}" for item in sorted(matched))


def build_comment_body(
    outcomes: dict[str, str],
    has_failures: bool,
    repo: str = "owner/repo",
    pr_number: str = "123",
) -> str:
    pr_title, changed_files = get_pr_metadata(repo, pr_number)
    component_summary = summarize_components(changed_files)

    checked_traceability = (
        "[x]" if outcomes.get("traceability") in ("success", "passed") else "[ ]"
    )

    conflict_val = outcomes.get("conflict", "success").lower()
    if conflict_val in ("failure", "failed", "true", "yes"):
        emoji_conflict = "❌ Conflicts Detected"
    elif conflict_val in ("success", "passed", "false", "no"):
        emoji_conflict = "✅ No Conflicts"
    else:
        emoji_conflict = get_status_emoji(conflict_val)

    # Header and Summary Message
    if has_failures:
        header_message = (
            f"### ⚠️ Action Required: Quality Gate Verification Issues\n\n"
            f"Automated quality gates detected issues on PR **#{pr_number}** (`{pr_title}`). "
            f"Please review the status table below and run the recommended local fix commands before merging."
        )
    else:
        header_message = (
            f"### ✅ PR Quality Verification Passed\n\n"
            f"Great job! All automated quality gates passed successfully for PR **#{pr_number}** (`{pr_title}`). "
            f"No merge conflicts or compliance policy violations were detected."
        )

    # Build Quality Gate Status Table with Actionable Fix Guidance
    status_table = (
        "| Quality Gate / Check | Status | Action / Recommended Local Fix |\n"
        "| :--- | :--- | :--- |\n"
    )

    checks = [
        ("Linting & Formatting (Ruff)", "lint"),
        ("Backend Tests & Coverage (pytest)", "test"),
        ("Requirements Traceability", "traceability"),
        ("Frontend Checks (pnpm check)", "frontend"),
        ("ADR Validation (validate_adrs.py)", "adr"),
        ("Dependency, Static Audit & Secrets Scan", "audit"),
        ("DEID Compliance Scan", "deid"),
        ("Code Duplication Scan", "detect_duplication.py"),
        ("Git Merge Conflicts", "conflict"),
    ]

    for label, key in checks:
        if key == "conflict":
            status_str = emoji_conflict
            is_failed = "Conflicts Detected" in emoji_conflict
        else:
            status_str = get_status_emoji(outcomes.get(key))
            is_failed = outcomes.get(key, "").lower() in (
                "failure",
                "failed",
                "false",
                "no",
            )

        if is_failed:
            fix_guidance = FIX_COMMANDS.get(key, "Inspect CI logs for details")
        else:
            fix_guidance = "—"

        status_table += f"| **{label}** | {status_str} | {fix_guidance} |\n"

    # Read vulnerability summary if present
    vulnerability_table = ""
    v_summary_path = "/tmp/vulnerability_summary.json"  # nosec B108
    if os.path.exists(v_summary_path):
        try:
            with open(v_summary_path, "r", encoding="utf-8") as f:
                v_summary = json.load(f)
            vulns = v_summary.get("vulnerabilities", [])
            inline_violations = v_summary.get("inline_violations", [])
            ledger_errors = v_summary.get("ledger_errors", [])

            vulnerability_table = "\n\n#### 🛡️ GxP Security Exemption Ledger Status\n"
            if not vulns and not inline_violations and not ledger_errors:
                vulnerability_table += "✅ No active or proposed security exemptions detected in this build.\n"
            else:
                vulnerability_table += "| Vulnerability ID | Package | RPN | Status | Justification / Error |\n"
                vulnerability_table += "| :--- | :--- | :--- | :--- | :--- |\n"

                for v in vulns:
                    v_id = v.get("vulnerability_id")
                    pkg = v.get("package_name")
                    rpn_val = v.get("rpn", "N/A")
                    status_raw = v.get("status")
                    just = v.get("justification", "No justification provided")

                    status_str = (
                        "✅ Approved"
                        if status_raw == "Approved"
                        else (
                            "❌ Blocked"
                            if status_raw == "Blocked"
                            else f"⚠️ {status_raw}"
                        )
                    )
                    vulnerability_table += (
                        f"| **{v_id}** | {pkg} | {rpn_val} | {status_str} | {just} |\n"
                    )

                for viol in inline_violations:
                    v_file, v_line, v_text = viol
                    v_file_short = os.path.basename(v_file)
                    vulnerability_table += f"| **Inline Bypass** | {v_file_short}:{v_line} | N/A | ❌ Blocked | Inline configuration flag detected: `{v_text}` |\n"

                for err in ledger_errors:
                    vulnerability_table += (
                        f"| **Ledger Error** | N/A | N/A | ❌ Blocked | {err} |\n"
                    )
        except Exception as e:
            vulnerability_table = f"\n\n#### 🛡️ GxP Security Exemption Ledger Status\n⚠️ Error reading vulnerability summary: {e}\n"

    # Read duplication summary if present
    duplication_table = ""
    dup_summary_path = "duplication_summary.json"
    if os.path.exists(dup_summary_path):
        try:
            with open(dup_summary_path, "r", encoding="utf-8") as f:
                dup_summary = json.load(f)
            duplicates = dup_summary.get("duplicates", [])
            if duplicates:
                duplication_table = "\n\n#### ⚠️ Code Duplication Scanner Warnings\n"
                duplication_table += "| Block 1 | Block 2 | Code Preview |\n"
                duplication_table += "| :--- | :--- | :--- |\n"
                for dup in duplicates:
                    loc1 = dup["loc1"]
                    loc2 = dup["loc2"]
                    preview = dup["preview"].replace("\n", "<br>").replace("|", "\\|")
                    duplication_table += (
                        f"| `{loc1['file']}` (Lines {loc1['start']}-{loc1['end']}) | "
                        f"`{loc2['file']}` (Lines {loc2['start']}-{loc2['end']}) | "
                        f"`{preview}` |\n"
                    )
        except Exception as e:
            duplication_table = f"\n\n#### ⚠️ Code Duplication Scanner Warnings\n⚠️ Error reading duplication summary: {e}\n"

    body = f"""<!-- ID: CADENCE_PR_QUALITY_GATE_CHECKLIST -->
{header_message}

#### 📊 Quality Gate Status Summary
{status_table}
{vulnerability_table}
{duplication_table}

#### 📦 Target Modules & Files Changed
{component_summary}

---

<details>
<summary>📖 View PR Compliance Reference Checklist & System Boundaries</summary>

### Part 1: System Boundaries & Architecture Standards
Ensure your contribution strictly adheres to the **Cadence Clinical Platform** architecture:
*   **Product Mission & Scope:** Standalone eClinical platform synthesizing upstream Metadata Management (MDR) with downstream Electronic Data Capture (EDC) into an automated Digital Data Flow (DDF) platform.
*   **Stack & Guardrails:** Adhere strictly to language versions (Python 3.11+), core frameworks (FastAPI, Pydantic v2 strict typing), linters/formatters (Ruff/Black), and database patterns (SQLAlchemy/SQLModel for PostgreSQL, Neo4j Python Driver for Graph DB).
*   **Compliance & GxP Standards:** Maintain CDISC USDM, CDISC ODM, and 21 CFR Part 11 compliant audit fields (`created_at`, `created_by`, `reason_for_change`, `version_index`).
*   **Directory Routing Rules:**
    *   Security, RBAC, cryptographic signing, audit context ──► `packages/security/`
    *   Shared Vue 3 UI components, layout helpers, widgets ──► `packages/ui/`
    *   CDISC USDM, ODM, SDTM schemas & domain models ──► `packages/core-models/`
    *   Patient DEID anonymization & masking engine ──► `packages/deid/`
    *   Study authoring, MDR & USDM graph logic ──► `apps/designer/`
    *   eCRF Data capture, PostgreSQL audit ledger, TSDV, & SDTM mapping ──► `apps/execution/`
    *   API Gateway routers & OIDC auth controllers ──► `apps/gateway/`
    *   Web User Interface SPA application ──► `apps/web/`
    *   Patient-facing eCOA/ePRO portal ──► `apps/subject-portal/`
    *   eTMF document taxonomy & GCP archiving ──► `apps/etmf/`
    *   CTMS site monitoring visits & trial tracking ──► `apps/ctms/`
    *   Quality deviations & CAPA logging ──► `apps/quality/`
    *   FHIR adapters & interop registries ──► `apps/interop/`
    *   Notification dispatch & email relays ──► `apps/notifications/`
    *   Clinical query tickets & site messaging ──► `apps/tickets/`
    *   Safety SAE icsr XML rendering & validation ──► `apps/safety/`
    *   Organization, site & personnel allocations ──► `apps/org/`

### Part 2: Pull Request Verification Gates
Every Pull Request must satisfy three mandatory verification gates before merging:

#### Gate 1: Comprehensive Documentation & Docstrings
*   **Source Codebases:** All modules, classes, functions, and public APIs must include clear, standardized Google or NumPy style docstrings. Complex or non-obvious business logic must include inline comments explaining *why* a pattern is applied.
*   **Workspace Documentation:** If a PR introduces a new service boundary, modifies an existing data flow, or alters public contracts, the corresponding markdown documentation in `docs/` (e.g., `docs/SRS.md`, `docs/DATA_LIFECYCLE.md`) must be updated.

#### Gate 2: Architecture Decision Records (ADRs)
Enforce a strict **"Code + Context"** design policy. Any PR that introduces significant architectural changes must include an Architecture Decision Record.
*   **When is an ADR required?**
    *   Adding significant third-party dependencies, new database engines, or core infrastructure shifts.
    *   Modifying inter-service data contracts, public APIs, or integration gateways.
    *   Altering underlying data storage models or executing major database schema migrations.
*   *Format:* Create a new markdown file inside `docs/adr/` using the chronological naming convention `YYYY-MM-DD-short-title.md` and register it in `docs/adr/index.md`.

#### Gate 3: Mandatory Test Coverage & Verification Passes
*   **Test Location:** All unit, integration, and end-to-end tests must reside inside the `tests/` directory.
*   **Framework Requirements:** Tests must execute successfully via `pytest` and `pytest-asyncio`, with external dependencies mocked or spun up via containerized test environments where appropriate.
*   **Automated Validation:** CI/CD execution environments automatically enforce the project's test suite and linting/type-checking pipelines prior to merge.

### Part 3: Intelligent Merge Conflict Resolution Protocol
When merge conflicts occur, execute the following resolution sequence:
1.  **Pre-Resolution Assessment:** Locate all conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`). Categorize conflict scope: Code/Logic, Schema/Data Model, Documentation, or Dependency Configuration.
2.  **Domain-Aware Resolution Rules:**
    *   *Core Models & Schemas:* Prioritize strict typing, schema backward compatibility, and immutability/audit rules.
    *   *Separation of Concerns:* Ensure cross-contamination between isolated modules or layers does not occur.
    *   *Non-Overlapping Logic:* Integrate both capabilities safely while ensuring type safety and formatting compliance remain intact.
3.  **Dependency & Lockfile Integrity:** Never manually text-merge automated dependency lockfiles (`uv.lock`, `pnpm-lock.yaml`). Cleanly merge the primary configuration manifest (`pyproject.toml`, `package.json`), then regenerate the lockfile cleanly using the project's native package manager (`uv sync`, `pnpm install`).
4.  **Artifact Cleanup:** Ensure absolute removal of all conflict markers, duplicate imports, and orphaned code blocks.

### Part 4: Principal-Level PR Summary Checklist
Before approving a PR or signing off on a merged state, verify completion of this checklist:
*   [ ] **Type Safety & Linting:** Code strictly complies with the project's type-checking and linting configurations.
*   [ ] **Documentation:** Comprehensive docstrings exist on all public functions/classes, and workspace docs reflect any data flow changes.
*   [ ] **Test Coverage:** Unit and/or integration tests are added under the appropriate test directory, maintaining the 80% coverage threshold.
*   {checked_traceability} **Requirements Traceability:** SRS and PRD requirements are fully mapped to automated verification tests.
*   [ ] **Architectural Intent:** An ADR is added to the architecture logs if major new design patterns or dependencies were introduced.
*   [ ] **Clean Verification Suite:** All local checks (test runner, linter, type-checker) pass successfully without warnings or errors.
*   [ ] **Conflict-Free:** All Git conflict markers and lockfile discrepancies are fully resolved.
</details>
"""
    return body


def main() -> None:
    repo = os.environ.get("GITHUB_REPOSITORY")
    pr_number = os.environ.get("PR_NUMBER")

    if not repo or not pr_number:
        print(
            "Missing GITHUB_REPOSITORY or PR_NUMBER environment variables. Skipping PR comment posting."
        )
        sys.exit(0)

    audit_outcome = os.environ.get("AUDIT_OUTCOME", "").lower()
    static_outcome = os.environ.get("STATIC_OUTCOME", "").lower()
    secrets_outcome = os.environ.get(
        "SECRETS_OUTCOME",
        "",  # pragma: allowlist secret
    ).lower()  # pragma: allowlist secret
    combined_audit = ""
    if "failure" in (
        audit_outcome,
        static_outcome,
        secrets_outcome,  # pragma: allowlist secret
    ):
        combined_audit = "failure"
    elif (
        audit_outcome == "success"
        and static_outcome == "success"
        and secrets_outcome == "success"  # pragma: allowlist secret
    ):
        combined_audit = "success"
    else:
        combined_audit = next(
            (
                val
                for val in (
                    audit_outcome,
                    static_outcome,
                    secrets_outcome,  # pragma: allowlist secret
                )
                if val
            ),
            "",
        )

    raw_new_outcomes: dict[str, str] = {
        "lint": os.environ.get("LINTING_OUTCOME", ""),
        "test": os.environ.get("TEST_OUTCOME", ""),
        "frontend": os.environ.get("FRONTEND_OUTCOME", ""),
        "adr": os.environ.get("ADR_OUTCOME", ""),
        "audit": combined_audit,
        "conflict": os.environ.get("CONFLICT_OUTCOME", ""),
        "deid": os.environ.get("DEID_OUTCOME", ""),
        "duplication": os.environ.get("DUPLICATION_OUTCOME", ""),
        "traceability": os.environ.get("TRACEABILITY_OUTCOME", ""),
    }

    # Fetch existing comments to see if we have an existing checklist comment
    comments_json, _ = run_command(
        [
            "gh",
            "api",
            f"repos/{repo}/issues/{pr_number}/comments",
            "--paginate",
        ],
        check=False,
    )

    existing_comment_id: str | None = None
    existing_outcomes: dict[str, str] = {}
    if comments_json:
        try:
            comments = json.loads(comments_json)
            for comment in comments:
                body = comment.get("body", "")
                if "<!-- ID: CADENCE_PR_QUALITY_GATE_CHECKLIST -->" in body:
                    existing_comment_id = comment["id"]
                    existing_outcomes = parse_existing_outcomes(body)
                    break
        except Exception as e:
            print(f"Error parsing comments JSON: {e}")

    merged_outcomes = merge_outcomes(raw_new_outcomes, existing_outcomes)

    job_status = os.environ.get("JOB_STATUS", "success")
    has_failures = job_status.lower() == "failure" or any(
        val.lower() in ("failure", "failed", "true", "yes")
        for val in merged_outcomes.values()
    )

    comment_body = build_comment_body(merged_outcomes, has_failures, repo, pr_number)

    if has_failures or existing_comment_id:
        if existing_comment_id:
            print(f"Updating existing comment {existing_comment_id}...")
            run_command(
                [
                    "gh",
                    "api",
                    f"repos/{repo}/issues/comments/{existing_comment_id}",
                    "-X",
                    "PATCH",
                    "-F",
                    f"body={comment_body}",
                ]
            )
        else:
            print("Creating a new PR comment...")
            run_command(
                [
                    "gh",
                    "api",
                    f"repos/{repo}/issues/{pr_number}/comments",
                    "-X",
                    "POST",
                    "-F",
                    f"body={comment_body}",
                ]
            )
    else:
        print("All checks passed and no existing comment found. No action needed.")


if __name__ == "__main__":
    main()
