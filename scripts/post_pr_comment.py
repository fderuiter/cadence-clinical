#!/usr/bin/env python3
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
    "Git Merge Conflicts": "conflict",
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
            timeout=30,  # Prevent hanging runs
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
    pattern = re.compile(r"\|\s*\*\*(.*?)\*\*.*?\s*\|\s*(.*?)\s*\|")
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
    for key in ["lint", "test", "frontend", "adr", "audit", "conflict"]:
        new_val = new_outcomes.get(key)
        existing_val = existing_outcomes.get(key)

        # Only overwrite existing status if we got a fresh, non-skipped run
        if new_val and new_val.lower() not in ("skipped", "skip", "unknown", ""):
            merged[key] = new_val
        elif existing_val:
            merged[key] = existing_val
        else:
            merged[key] = new_val or "skipped"
    return merged


def build_comment_body(outcomes: dict[str, str], has_failures: bool) -> str:
    emoji_lint = get_status_emoji(outcomes.get("lint"))
    emoji_test = get_status_emoji(outcomes.get("test"))
    emoji_frontend = get_status_emoji(outcomes.get("frontend"))
    emoji_adr = get_status_emoji(outcomes.get("adr"))
    emoji_audit = get_status_emoji(outcomes.get("audit"))

    # Turn the conflict emoji into a positive "No Conflict" if it's success (Passed), or "Conflict Detected" if it's failure
    conflict_val = outcomes.get("conflict", "success").lower()
    if conflict_val in ("failure", "failed", "true", "yes"):
        emoji_conflict = "❌ Conflicts Detected"
    elif conflict_val in ("success", "passed", "false", "no"):
        emoji_conflict = "✅ No Conflicts"
    else:
        emoji_conflict = get_status_emoji(conflict_val)

    # Header message based on failures
    if has_failures:
        header_message = (
            "### ⚠️ Quality Gate Alerts & Review Checklist Required\n\n"
            "One or more automated quality gates have failed, or merge conflicts "
            "have been detected on this Pull Request. Please review the checklist "
            "and status below to resolve these issues before merging."
        )
    else:
        header_message = (
            "### ✅ All Quality Gates Passed Successfully\n\n"
            "Great job! All automated quality gates have passed successfully, "
            "and no merge conflicts were detected. The review checklist below "
            "is provided for final compliance verification."
        )

    body = f"""<!-- ID: CADENCE_PR_QUALITY_GATE_CHECKLIST -->
{header_message}

#### 📊 Quality Gate Status Summary
| Quality Gate / Check | Status |
| :--- | :--- |
| **Linting & Formatting** (Ruff) | {emoji_lint} |
| **Backend Tests & Coverage** (pytest) | {emoji_test} |
| **Frontend Checks** (pnpm check) | {emoji_frontend} |
| **ADR Validation** (validate_adrs.py) | {emoji_adr} |
| **Dependency, Static Audit & Secrets Scan** (pip-audit/bandit/detect-secrets) | {emoji_audit} |
| **Git Merge Conflicts** | {emoji_conflict} |

---

# Universal Task & PR Review Checklist: Intelligent Code Review & Merge Validation

## Part 1: System Boundaries & Architecture Standards
Ensure your contribution strictly adheres to the **Cadence Clinical Platform** architecture:
*   **Product Mission & Scope:** Standalone eClinical platform synthesizing upstream Metadata Management (MDR) with downstream Electronic Data Capture (EDC) into an automated Digital Data Flow (DDF) platform.
*   **Stack & Guardrails:** Adhere strictly to language versions (Python 3.11+), core frameworks (FastAPI, Pydantic v2 strict typing), linters/formatters (Ruff/Black), and database patterns (SQLAlchemy/SQLModel for PostgreSQL, Neo4j Python Driver for Graph DB).
*   **Compliance & GxP Standards:** Maintain CDISC USDM, CDISC ODM, and 21 CFR Part 11 compliant audit fields (`created_at`, `created_by`, `reason_for_change`, `version_index`).
*   **Directory Routing Rules:**
    *   Security, utilities, and global helpers ──► `packages/security/`
    *   UI components, layout utilities, forms ──► `packages/ui/`
    *   Study authoring & MDR validation logic ──► `apps/designer/`
    *   Data capture, translation, & execution logic ──► `apps/execution/`
    *   Gateway routers, OIDC auth controllers ──► `apps/gateway/`
    *   Web User Interface application ──► `apps/web/`

## Part 2: Pull Request Verification Gates
Every Pull Request must satisfy three mandatory verification gates before merging:

### Gate 1: Comprehensive Documentation & Docstrings
*   **Source Codebases:** All modules, classes, functions, and public APIs must include clear, standardized Google or NumPy style docstrings. Complex or non-obvious business logic must include inline comments explaining *why* a pattern is applied.
*   **Workspace Documentation:** If a PR introduces a new service boundary, modifies an existing data flow, or alters public contracts, the corresponding markdown documentation in `docs/` (e.g., `docs/SRS.md`, `docs/DATA_LIFECYCLE.md`) must be updated.

### Gate 2: Architecture Decision Records (ADRs)
Enforce a strict **"Code + Context"** design policy. Any PR that introduces significant architectural changes must include an Architecture Decision Record.
*   **When is an ADR required?**
    *   Adding significant third-party dependencies, new database engines, or core infrastructure shifts.
    *   Modifying inter-service data contracts, public APIs, or integration gateways.
    *   Altering underlying data storage models or executing major database schema migrations.
*   *Format:* Create a new markdown file inside `docs/adr/` using the chronological naming convention `YYYY-MM-DD-short-title.md` and register it in `docs/adr/index.md`.

### Gate 3: Mandatory Test Coverage & Verification Passes
*   **Test Location:** All unit, integration, and end-to-end tests must reside inside the `tests/` directory.
*   **Framework Requirements:** Tests must execute successfully via `pytest` and `pytest-asyncio`, with external dependencies mocked or spun up via containerized test environments where appropriate.
*   **Automated Validation:** CI/CD execution environments automatically enforce the project's test suite and linting/type-checking pipelines prior to merge.

## Part 3: Intelligent Merge Conflict Resolution Protocol
When merge conflicts occur, execute the following resolution sequence:
1.  **Pre-Resolution Assessment:** Locate all conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`). Categorize conflict scope: Code/Logic, Schema/Data Model, Documentation, or Dependency Configuration.
2.  **Domain-Aware Resolution Rules:**
    *   *Core Models & Schemas:* Prioritize strict typing, schema backward compatibility, and immutability/audit rules.
    *   *Separation of Concerns:* Ensure cross-contamination between isolated modules or layers does not occur.
    *   *Non-Overlapping Logic:* Integrate both capabilities safely while ensuring type safety and formatting compliance remain intact.
3.  **Dependency & Lockfile Integrity:** Never manually text-merge automated dependency lockfiles (`uv.lock`, `pnpm-lock.yaml`). Cleanly merge the primary configuration manifest (`pyproject.toml`, `package.json`), then regenerate the lockfile cleanly using the project's native package manager (`uv sync`, `pnpm install`).
4.  **Artifact Cleanup:** Ensure absolute removal of all conflict markers, duplicate imports, and orphaned code blocks.

## Part 4: Principal-Level PR Summary Checklist
Before approving a PR or signing off on a merged state, verify completion of this checklist:
*   [ ] **Type Safety & Linting:** Code strictly complies with the project's type-checking and linting configurations.
*   [ ] **Documentation:** Comprehensive docstrings exist on all public functions/classes, and workspace docs reflect any data flow changes.
*   [ ] **Test Coverage:** Unit and/or integration tests are added under the appropriate test directory, maintaining the 80% coverage threshold.
*   [ ] **Architectural Intent:** An ADR is added to the architecture logs if major new design patterns or dependencies were introduced.
*   [ ] **Clean Verification Suite:** All local checks (test runner, linter, type-checker) pass successfully without warnings or errors.
*   [ ] **Conflict-Free:** All Git conflict markers and lockfile discrepancies are fully resolved.
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
    secrets_outcome = os.environ.get("SECRETS_OUTCOME", "").lower()
    combined_audit = ""
    if "failure" in (audit_outcome, static_outcome, secrets_outcome):
        combined_audit = "failure"
    elif (
        audit_outcome == "success"
        and static_outcome == "success"
        and secrets_outcome == "success"  # pragma: allowlist secret
    ):
        combined_audit = "success"
    else:
        # Fallback to whichever non-empty outcome is present
        combined_audit = next(
            (val for val in (audit_outcome, static_outcome, secrets_outcome) if val), ""
        )

    raw_new_outcomes: dict[str, str] = {
        "lint": os.environ.get("LINTING_OUTCOME", ""),
        "test": os.environ.get("TEST_OUTCOME", ""),
        "frontend": os.environ.get("FRONTEND_OUTCOME", ""),
        "adr": os.environ.get("ADR_OUTCOME", ""),
        "audit": combined_audit,
        "conflict": os.environ.get("CONFLICT_OUTCOME", ""),
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

    # Merge fresh outcomes with existing ones
    merged_outcomes = merge_outcomes(raw_new_outcomes, existing_outcomes)

    # Compute overall job failure status
    job_status = os.environ.get("JOB_STATUS", "success")
    has_failures = job_status.lower() == "failure" or any(
        val.lower() in ("failure", "failed", "true", "yes")
        for val in merged_outcomes.values()
    )

    comment_body = build_comment_body(merged_outcomes, has_failures)

    # We post/update if we have failures/conflicts, OR if we already had a comment before
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
