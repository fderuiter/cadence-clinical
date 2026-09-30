#!/usr/bin/env python3
"""
Script: validate_adrs.py
Description: Validates Architectural Decision Records (ADRs) formatting, requirement traceability,
             and master index synchronization across docs/adr/index.md, docs/adr/README.md, and docs/ADR_INDEX.md.
             Supports --fix to automatically synchronize and generate master index files.
"""

import argparse
import os
import re
import sys
from pathlib import Path

# Files to ignore in docs/adr/ directory
IGNORE_FILES = {"TEMPLATE.md", "index.md", "README.md"}

# Post-2026 baseline files that are exempt from requirement reference validation
BASELINE_FILES = {
    "2026-06-06-usdm-pydantic-models.md",
    "2026-07-22-api-first-in-memory-diffing.md",
    "2026-07-22-api-first-validation-and-usdm-integration.md",
    "2026-07-22-audit-log-design.md",
    "2026-07-22-compliance-tracing-and-automated-trial-locks.md",
    "2026-07-22-database-shadow-triggers.md",
    "2026-07-22-gateway-authentication-propagation.md",
    "2026-07-22-gateway-openapi-aggregation.md",
    "2026-07-22-merkle-root-sealing.md",
    "2026-07-22-metadata-driven-grid-layouts.md",
    "2026-07-22-pnpm-frontend-workspace.md",
    "2026-07-22-schema-mapping-design.md",
    "2026-07-22-signature-re-authentication.md",
    "2026-07-22-transactional-sql-engine-gxp-auditing.md",
    "2026-07-22-translation-job-and-layout-engine.md",
    "2026-07-22-unified-database-management-and-pre-boot-migrations.md",
    "2026-07-22-usdm-dynamic-mapper-and-cache.md",
    "2026-07-23-canonical-json-signatures-and-rolling-versioning.md",
    "2026-07-23-core-service-oriented-clinical-engine.md",
    "2026-07-23-declarative-ruleset-automerge.md",
    "2026-07-23-targeted-integration-path-isolation.md",
    "2026-07-24-clinical-query-workflow-authorization.md",
    "2026-07-24-code-formatting-and-style-standardization.md",
    "2026-07-24-continuous-fmea-gxp-aligned-exemption-ledger.md",
    "2026-07-24-database-native-pessimistic-locking-retry.md",
    "2026-07-24-fhir-esource-ecoa-sync-gateway.md",
    "2026-07-24-medical-coding-engine-persistence.md",
    "2026-07-24-minimizing-merge-friction-for-baselines-and-reports.md",
    "2026-07-24-rolling-upgrade-and-canonical-json-signatures.md",
    "2026-07-25-data-driven-edl-model-and-completeness.md",
    "2026-07-25-etmf-qc-review-workflow.md",
    "2026-07-25-part11-offline-sync-ledger.md",
    "2026-07-26-ctms-foundation-infrastructure.md",
    "2026-07-27-backward-compatible-gateway-signature-verification.md",
    "2026-07-27-ci-schema-introspection-and-gateway-aggregation.md",
    "2026-07-27-multi-database-reset-cli-tool.md",
    "2026-07-27-quality-capa-scaffold.md",
    "2026-07-27-standardize-fastapi-identity-dependency.md",
    "2026-07-28-sdv-tsdv-persistence-foundation.md",
    "2026-07-29-tmf-reference-model-taxonomy-integration.md",
    "2026-07-30-lab-reference-range-management.md",
    "2026-07-30-rule-authoring-validation-and-ddf-delivery.md",
    "2026-07-31-ecoa-subject-identity-and-gateway-routing.md",
    "2026-08-01-vue-spa-oidc-rbac-signing-boundaries.md",
    "2026-08-02-econsent-scaffold-and-part11-audit.md",
    "2026-08-02-eligibility-criteria-evaluation-engine.md",
    "2026-08-02-notifications-service-foundation.md",
    "2026-08-02-protocol-rendering-architecture.md",
    "2026-08-02-rtsm-architecture-and-randomization-persistence.md",
    "2026-08-02-sdtm-foundation-models.md",
    "2026-08-02-usdm-v2-v3-canonical-contract-mapping-matrix.md",
    "2026-08-03-subject-randomization-lifecycle-and-state-guards.md",
    "2026-08-04-ctms-service-boundary-and-reporting.md",
    "2026-08-04-notification-delivery-channels-and-retry-dispatcher.md",
    "2026-08-05-quality-capa-management.md",
    "2026-08-06-arm-aware-soa-matrix.md",
    "2026-08-07-epro-sync-durable-reconciliation.md",
    "2026-08-07-gateway-terminology-routing.md",
    "2026-08-07-organization-directory-service-foundation.md",
    "2026-08-08-biomedical-concept-locks.md",
    "2026-08-08-centralized-rbac-toolkit.md",
    "2026-08-08-ecoa-portal-and-interop-deployment.md",
    "2026-08-08-expose-authenticated-sdtm-adam-dataset-json-export-endpoints.md",
    "2026-08-08-gateway-step-up-re-authentication.md",
    "2026-08-08-pi-only-batch-sign-off-execution.md",
    "2026-08-08-rtsm-supply-domain-persistence.md",
    "2026-08-09-automated-etmf-document-redaction.md",
    "2026-08-09-eisf-maintenance-and-formatting.md",
    "2026-08-09-etmf-filterable-paginated-audit-log.md",
    "2026-08-09-global-library-object-instantiation.md",
    "2026-08-09-medical-coding-engine-query-integration.md",
    "2026-08-09-nci-thesaurus-signed-web-client.md",
    "2026-08-09-safety-e2b-icsr-xml-export-pipeline.md",
    "2026-08-10-debounced-clinical-code-lookup-ui-primitive.md",
    "2026-08-11-bidirectional-field-parity-and-rfc7807-validation-schemas.md",
    "2026-08-11-declarative-dependencies-and-signature-verification-fallback.md",
    "2026-08-11-gateway-signature-legacy-v2-fallback.md",
    "2026-08-11-standardize-pr-templates-and-centralize-hashing-grid-layout.md",
    "2026-08-11-unified-parameterized-relational-database-lifespan-wrapper.md",
    "2026-08-11-vitepress-workspace-documentation-portal.md",
    "2026-08-12-deterministic-gxp-report-generation-and-signature-verification.md",
    "2026-08-12-secure-unblinding-signature-fallback-restriction.md",
    "2026-08-13-tickets-service-scaffold-and-gateway-integration.md",
    "2026-08-14-biostatistical-export-pipeline-interoperability.md",
    "2026-08-15-bidirectional-api-contract-enforcement.md",
    "2026-08-16-parallel-ci-workflows-and-local-concurrent-execution.md",
    "2026-07-27-unified-python-markdown-validator.md",
    "2026-07-28-native-vue3-rules-designer-gxp-ledger-sync.md",
    "2026-08-17-centralized-permission-auth.md",
    "2026-08-17-frontend-standardization-css-grid-and-centralized-utilities.md",
    "2026-08-17-interactive-mermaid-diagrams-and-zoom-pan-controls.md",
    "2026-07-27-api-driven-lock-sync-and-ast-validator.md",
}

FILENAME_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}-.+\.md$")
ADR_HEADING_PATTERN = re.compile(r"^# ADR-(\d{3,}):\s*(.+)$")
MARKDOWN_LINK_PATTERN = re.compile(r"\]\(([^)\s]+)\)")
INDEX_ENTRY_PATTERN = re.compile(r"^\s*[-*]\s+\[([^\]]+)\]\(([^)\s]+)\)(?:\s+\((\d{4}-\d{2}-\d{2})\))?(?:\s+(.*))?")

REQUIRED_SECTIONS_OLD = [
    "## Status",
    "## Context",
    "## Decision",
    "## Alternatives Considered",
    "## Trade-offs",
]

REQUIRED_SECTIONS_NEW = [
    "## 1. Context & Problem Statement",
    "## 2. Decision Drivers & Constraints",
    "## 3. Options Considered",
    "## 4. Decision Outcome",
    "## 5. Consequences & Trade-offs",
    "## 6. Implementation & Verification",
]

DOMAIN_HEADINGS = [
    "### 0. Clinical Kernel (K001–K032)",
    "### 1. Core Platform & Execution Engine",
    "### 2. API Gateway, Security & Identity",
    "### 3. Clinical Data Interoperability & Standards",
    "### 4. Clinical Operations & Business Modules",
    "### 5. Compliance, Audit & Governance",
    "### 6. Frontend & Design System",
    "### 7. DevOps, Tooling & CI/CD",
]


def find_repo_root() -> Path:
    current = Path(__file__).resolve().parent
    while current != current.parent:
        if (current / "Cadence.sln").exists() or (current / "package.json").exists():
            return current
        current = current.parent
    return Path.cwd()


def get_valid_requirements(docs_dir: Path) -> set[str]:
    valid_reqs = set()
    if not docs_dir.exists():
        return valid_reqs

    prd_regex = re.compile(r"\b(PRD-[A-Z0-9]+-\d+)\b", re.IGNORECASE)
    trace_regex = re.compile(r"\bTrace-(\d+)\b", re.IGNORECASE)

    for md_file in docs_dir.glob("**/*.md"):
        if md_file.name in ("Requirements_Traceability_Matrix.md", "IQ_OQ_PQ_Execution_Report.md"):
            continue
        try:
            content = md_file.read_text(encoding="utf-8")
            for m in prd_regex.finditer(content):
                valid_reqs.add(m.group(1).upper())
            for m in trace_regex.finditer(content):
                valid_reqs.add(f"Trace-{m.group(1)}")
        except Exception:
            pass

    return valid_reqs


def extract_requirement_references(content: str) -> set[str]:
    prd_regex = re.compile(r"\bprd-([a-z0-9]+)-([a-z0-9]+)\b", re.IGNORECASE)
    trace_regex = re.compile(r"\btrace\s*[-]?\s*(\d+)\b", re.IGNORECASE)

    prd_refs = {
        f"PRD-{m.group(1).upper()}-{m.group(2).upper()}"
        for m in prd_regex.finditer(content)
    }
    trace_refs = {f"Trace-{m.group(1)}" for m in trace_regex.finditer(content)}
    return prd_refs | trace_refs


def is_post_2026_adr(filename: str) -> bool:
    match = re.match(r"^(\d{4})-\d{2}-\d{2}", filename)
    if not match:
        return False
    year = int(match.group(1))
    if year < 2026:
        return False
    return not (year == 2026 and filename in BASELINE_FILES)


def parse_adr_metadata(file_path: Path) -> tuple[int | None, str, str]:
    filename = file_path.name
    date_match = re.match(r"^(\d{4}-\d{2}-\d{2})", filename)
    date_str = date_match.group(1) if date_match else "2026-01-01"

    try:
        content = file_path.read_text(encoding="utf-8")
        lines = content.splitlines()
        first_line = lines[0].strip() if lines else ""
        match = ADR_HEADING_PATTERN.match(first_line)
        if match:
            adr_num = int(match.group(1))
            title = match.group(2).strip()
            return adr_num, title, date_str
    except Exception:
        pass

    return None, filename, date_str


def parse_existing_index(index_path: Path) -> tuple[dict[str, list[dict]], list[str]]:
    if not index_path.exists():
        return {}, []

    content = index_path.read_text(encoding="utf-8")
    lines = content.splitlines()

    domain_entries = {heading: [] for heading in DOMAIN_HEADINGS}
    preamble = []
    current_domain = None
    in_domain_section = False

    for line in lines:
        stripped = line.strip()
        if stripped.startswith("### "):
            in_domain_section = True
            for h in DOMAIN_HEADINGS:
                if stripped.startswith(h):
                    current_domain = h
                    break
            else:
                current_domain = stripped
                if current_domain not in domain_entries:
                    domain_entries[current_domain] = []
            continue

        if not in_domain_section:
            preamble.append(line)
            continue

        entry_match = INDEX_ENTRY_PATTERN.match(line)
        if entry_match and current_domain:
            title_text = entry_match.group(1)
            filename = entry_match.group(2)
            date_str = entry_match.group(3) or ""
            extra_suffix = entry_match.group(4) or ""

            clean_filename = filename.replace("docs/adr/", "").replace("adr/", "")

            domain_entries[current_domain].append({
                "title_text": title_text,
                "filename": clean_filename,
                "date_str": date_str,
                "extra_suffix": extra_suffix,
                "raw_line": line,
            })

    return domain_entries, preamble


def transform_link_for_root_docs(match: re.Match) -> str:
    target = match.group(1)
    if target.startswith("http://") or target.startswith("https://") or target.startswith("#") or target.startswith("mailto:"):
        return match.group(0)

    if target.startswith("../"):
        new_target = target[3:]
    elif not target.startswith("adr/"):
        new_target = f"adr/{target}"
    else:
        new_target = target

    return f"]({new_target})"


def sync_master_indices(repo_root: Path) -> None:
    adr_dir = repo_root / "docs" / "adr"
    index_md_path = adr_dir / "index.md"
    readme_md_path = adr_dir / "README.md"
    adr_index_path = repo_root / "docs" / "ADR_INDEX.md"

    adr_files = sorted([
        f for f in adr_dir.glob("*.md")
        if f.name not in IGNORE_FILES
    ])

    existing_domains, preamble = parse_existing_index(index_md_path)

    indexed_filenames = set()
    for entries in existing_domains.values():
        for e in entries:
            indexed_filenames.add(e["filename"])

    unindexed_files = [f for f in adr_files if f.name not in indexed_filenames]

    for file_path in unindexed_files:
        adr_num, title, date_str = parse_adr_metadata(file_path)
        display_title = f"ADR-{adr_num:03d}: {title}" if adr_num is not None else title
        entry = {
            "title_text": display_title,
            "filename": file_path.name,
            "date_str": date_str,
            "extra_suffix": "",
            "raw_line": f"- [{display_title}]({file_path.name}) ({date_str})",
        }

        target_domain = "### 1. Core Platform & Execution Engine"
        if adr_num is not None and ((adr_num >= 2244 and adr_num <= 2255) or (adr_num >= 2449 and adr_num <= 2590) or file_path.name.startswith("2026-09-29-clinical-kernel")):
            target_domain = "### 0. Clinical Kernel (K001–K032)"

        if target_domain not in existing_domains:
            existing_domains[target_domain] = []
        existing_domains[target_domain].append(entry)

    if not preamble:
        preamble = [
            "# Architectural Decision Records (ADRs) Index",
            "",
            "This document tracks all Architectural Decision Records for the Cadence Clinical platform categorized by functional domain.",
            "",
            "## Standard ADR Template & Scaffolding",
            "",
            "- [ADR Template](TEMPLATE.md)",
            '- Scaffold a new ADR with `pnpm adr:new "<title>"` or `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- scaffold adr "<title>"`. It writes `docs/adr/YYYY-MM-DD-<slug>.md` headed `# ADR-<next free number>: <title>` and appends an entry to the end of this index; move that entry into the matching domain section.',
            "- Validate with `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check adrs` or `python3 scripts/validate_adrs.py`.",
            "",
            "## Numbering Rules",
            "",
            "Every ADR has exactly one unique number, given on its first line as `# ADR-NNNN: Title`.",
            "",
            "## Domain-Based Decisions Log",
            "",
        ]

    index_lines = list(preamble)
    if index_lines and index_lines[-1] != "":
        index_lines.append("")

    for domain_heading in DOMAIN_HEADINGS:
        index_lines.append(domain_heading)
        index_lines.append("")
        entries = existing_domains.get(domain_heading, [])
        for entry in entries:
            date_part = f" ({entry['date_str']})" if entry.get("date_str") else ""
            suffix_part = f" {entry['extra_suffix']}" if entry.get("extra_suffix") else ""
            index_lines.append(f"- [{entry['title_text']}]({entry['filename']}){date_part}{suffix_part}")
        index_lines.append("")

    index_content = "\n".join(index_lines).strip() + "\n"

    index_md_path.write_text(index_content, encoding="utf-8")
    readme_md_path.write_text(index_content, encoding="utf-8")

    # Generate content for docs/ADR_INDEX.md
    adr_index_lines = []
    for line in index_lines:
        transformed_line = MARKDOWN_LINK_PATTERN.sub(transform_link_for_root_docs, line)
        adr_index_lines.append(transformed_line)

    adr_index_content = "\n".join(adr_index_lines).strip() + "\n"
    adr_index_path.write_text(adr_index_content, encoding="utf-8")


def validate_adrs(repo_root: Path) -> tuple[bool, list[str]]:
    adr_dir = repo_root / "docs" / "adr"
    docs_dir = repo_root / "docs"

    errors = []

    if not adr_dir.exists():
        return False, [f"Directory '{adr_dir}' not found."]

    valid_reqs = get_valid_requirements(docs_dir)

    index_files = {
        "index.md": adr_dir / "index.md",
        "README.md": adr_dir / "README.md",
        "ADR_INDEX.md": repo_root / "docs" / "ADR_INDEX.md",
    }

    for name, path in index_files.items():
        if not path.exists():
            errors.append(f"Master index file '{name}' ({path}) not found.")

    adr_files = sorted([
        f for f in adr_dir.glob("*.md")
        if f.name not in IGNORE_FILES
    ])

    files_by_number: dict[int, list[str]] = {}

    for file_path in adr_files:
        filename = file_path.name

        if not FILENAME_PATTERN.match(filename):
            errors.append(f"File '{filename}' does not follow standard date pattern YYYY-MM-DD-*.md.")

        try:
            content = file_path.read_text(encoding="utf-8")
        except Exception as ex:
            errors.append(f"Error reading '{filename}': {ex}")
            continue

        lines = content.splitlines()
        first_line = lines[0].strip() if lines else ""

        match = ADR_HEADING_PATTERN.match(first_line)
        if not match:
            errors.append(f"File '{filename}' missing '# ADR-NNNN: Title' heading on line 1 (found: '{first_line}').")
        else:
            adr_num = int(match.group(1))
            files_by_number.setdefault(adr_num, []).append(filename)

        is_legacy = "## Status" in content and "## 1. Context & Problem Statement" not in content
        req_sections = REQUIRED_SECTIONS_OLD if is_legacy else REQUIRED_SECTIONS_NEW
        missing = [s for s in req_sections if s not in content]
        if missing:
            errors.append(f"File '{filename}' missing required section(s): {', '.join(missing)}.")

        if is_post_2026_adr(filename):
            refs = extract_requirement_references(content)
            if not refs:
                errors.append(f"Post-2026 ADR '{filename}' lacks a requirement reference.")
            else:
                invalid_refs = [r for r in refs if r not in valid_reqs]
                if invalid_refs:
                    errors.append(f"Post-2026 ADR '{filename}' references invalid requirement(s): {', '.join(invalid_refs)}.")

    for num, holders in files_by_number.items():
        if len(holders) > 1:
            errors.append(f"ADR-{num:03d} is used by multiple files: {', '.join(holders)}.")

    for index_name, index_path in index_files.items():
        if not index_path.exists():
            continue

        index_content = index_path.read_text(encoding="utf-8")
        index_lines = index_content.splitlines()

        for dh in DOMAIN_HEADINGS:
            if dh not in index_content:
                errors.append(f"Master index '{index_name}' missing domain section '{dh}'.")

        listed_counts: dict[str, int] = {}
        for line_idx, line in enumerate(index_lines, start=1):
            for link_match in MARKDOWN_LINK_PATTERN.finditer(line):
                target = link_match.group(1)
                if target.startswith("http://") or target.startswith("https://") or target.startswith("#") or target.startswith("mailto:"):
                    continue

                target_path = target.split("#")[0]
                if index_name == "ADR_INDEX.md":
                    resolved_path = docs_dir / target_path
                else:
                    resolved_path = adr_dir / target_path

                if not resolved_path.exists():
                    errors.append(f"{index_name} line {line_idx} links to '{target}', which does not exist.")

            entry_match = INDEX_ENTRY_PATTERN.match(line)
            if entry_match:
                filename = entry_match.group(2).split("#")[0].replace("docs/adr/", "").replace("adr/", "")
                listed_counts[filename] = listed_counts.get(filename, 0) + 1

        for adr_file in adr_files:
            count = listed_counts.get(adr_file.name, 0)
            if count == 0:
                errors.append(f"File '{adr_file.name}' is missing from '{index_name}'.")
            elif count > 1:
                errors.append(f"File '{adr_file.name}' is listed {count} times in '{index_name}'; must be listed exactly once.")

    return len(errors) == 0, errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate and synchronize Architectural Decision Records (ADRs).")
    parser.add_argument("--fix", action="store_true", help="Automatically generate and update all master index files.")
    args = parser.parse_args()

    repo_root = find_repo_root()

    if args.fix:
        print("Synchronizing ADR master index files (docs/adr/index.md, docs/adr/README.md, docs/ADR_INDEX.md)...")
        sync_master_indices(repo_root)
        print("Master index synchronization complete.")

    success, errors = validate_adrs(repo_root)

    if not success:
        print("\n[ERROR] ADR Validation Failed with the following issues:\n", file=sys.stderr)
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        return 1

    print("[OK] All ADRs and master index files passed validation.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
