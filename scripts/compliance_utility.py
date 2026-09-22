import os
import re

# Set of ADR files that exist in the repository baseline (as of July 2026)
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


def _sweep_markdown_files(path_or_dir: str) -> list[str]:
    """Helper to sweep a file or directory recursively for markdown files in deterministic order."""
    if not os.path.exists(path_or_dir):
        return []
    if os.path.isfile(path_or_dir):
        return [path_or_dir]
    files_list = []
    ignored_files = {
        "Requirements_Traceability_Matrix.md",
        "IQ_OQ_PQ_Execution_Report.md",
    }
    for root, dirs, files in os.walk(path_or_dir):
        dirs[:] = [d for d in dirs if d != "runs" and not d.startswith(".")]
        dirs.sort()
        for f in sorted(files):
            if f.endswith(".md") and f not in ignored_files:
                files_list.append(os.path.join(root, f))
    return files_list


def parse_srs(filepath_or_dir: str) -> set[str]:
    """Parses SRS requirements (Trace-X) from a file or directory of fragments."""
    requirements = set()
    files = _sweep_markdown_files(filepath_or_dir)
    pattern = re.compile(r"[-*]\s*\*\*Trace[\s-]*(\d+)")
    general_pattern = re.compile(r"\bTrace-(\d+)\b")

    for file_path in files:
        with open(file_path, encoding="utf-8") as f:
            content = f.read()

        for line in content.splitlines():
            match = pattern.search(line)
            if match:
                num = match.group(1)
                requirements.add(f"Trace-{num}")

        for m in general_pattern.findall(content):
            requirements.add(f"Trace-{m}")

    return requirements


def parse_prd(filepath_or_dir: str) -> set[str]:
    """Parses PRD requirements (PRD-XXX-XXX) from a file or directory of fragments."""
    requirements = set()
    files = _sweep_markdown_files(filepath_or_dir)
    pattern = re.compile(r"####\s*(PRD-[A-Z0-9]+-\d+)")
    general_pattern = re.compile(r"\b(PRD-[A-Z0-9]+-\d+)\b")

    for file_path in files:
        with open(file_path, encoding="utf-8") as f:
            content = f.read()

        for line in content.splitlines():
            match = pattern.search(line)
            if match:
                requirements.add(match.group(1))

        for m in general_pattern.findall(content):
            requirements.add(m)

    return requirements


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
DEFAULT_DOCS_DIR = os.path.join(REPO_ROOT, "docs")


def get_valid_requirements(docs_dir: str = DEFAULT_DOCS_DIR) -> set[str]:
    """Gets the master set of valid requirement identifiers by recursively sweeping SDLC folders."""
    reqs = set()
    if not os.path.exists(docs_dir):
        return reqs

    srs_path = os.path.join(docs_dir, "SRS.md")
    srs_dir = os.path.join(docs_dir, "SRS")
    sdlc_dir = os.path.join(docs_dir, "SDLC")

    if os.path.isfile(srs_path):
        reqs.update(parse_srs(srs_path))
    if os.path.isdir(srs_dir):
        reqs.update(parse_srs(srs_dir))

    if os.path.isdir(sdlc_dir):
        reqs.update(parse_srs(sdlc_dir))
        reqs.update(parse_prd(sdlc_dir))
    elif os.path.isfile(
        os.path.join(docs_dir, "SDLC/01_Product_Requirements_Document_PRD.md")
    ):
        reqs.update(
            parse_prd(
                os.path.join(docs_dir, "SDLC/01_Product_Requirements_Document_PRD.md")
            )
        )

    # Also sweep any other subdirectories or root markdown files under docs_dir
    reqs.update(parse_srs(docs_dir))
    reqs.update(parse_prd(docs_dir))

    return reqs


def is_post_2026_adr(filename: str) -> bool:
    """
    Checks if an ADR filename belongs to the post-2026 category.
    Returns True for any ADR file starting with a year >= 2026,
    EXCEPT for the pre-existing baseline records.
    """
    # Filenames format: YYYY-MM-DD-something.md
    match = re.match(r"^(\d{4})-\d{2}-\d{2}", filename)
    if not match:
        return False
    year = int(match.group(1))
    if year < 2026:
        return False
    return not (year == 2026 and filename in BASELINE_FILES)


def extract_requirement_references(content: str) -> set[str]:
    """
    Extracts all requirement reference strings from file content.
    Finds patterns like PRD-SYS-001, Trace-1, Trace 1 case-insensitively.
    Normalizes them to standard case (e.g. PRD-SYS-001, Trace-1).
    """
    # Match trace X or trace-X case-insensitively
    trace_pattern = re.compile(r"\btrace\s*[-]?\s*(\d+)\b", re.IGNORECASE)
    # Match prd-ABC-123 case-insensitively
    prd_pattern = re.compile(r"\bprd-([a-z0-9]+)-([a-z0-9]+)\b", re.IGNORECASE)

    # We can also support req-X and sys-X case-insensitively
    req_pattern = re.compile(r"\breq-(\d+)\b", re.IGNORECASE)
    sys_pattern = re.compile(r"(?<!prd-)\bsys-(\d+)\b", re.IGNORECASE)

    refs = set()
    for m in trace_pattern.findall(content):
        refs.add(f"Trace-{m}")
    for part1, part2 in prd_pattern.findall(content):
        refs.add(f"PRD-{part1.upper()}-{part2.upper()}")
    for m in req_pattern.findall(content):
        refs.add(f"REQ-{m.upper()}")
    for m in sys_pattern.findall(content):
        refs.add(f"PRD-SYS-{m.upper()}")

    return refs


def validate_adr_compliance(
    filename: str, content: str, valid_requirements: set[str]
) -> tuple[bool, str]:
    """
    Validates ADR compliance against design requirements.
    Bypasses pre-2026 legacy decisions.
    Requires at least one valid requirement for post-2026 decisions.
    Raises errors for misspelled or invalid requirements.
    """
    if not is_post_2026_adr(filename):
        return True, ""

    referenced = extract_requirement_references(content)
    if not referenced:
        return (
            False,
            f"Error: Post-2026 ADR file '{filename}' lacks a valid requirement reference.",
        )

    invalid_refs = sorted(list(referenced - valid_requirements))
    if invalid_refs:
        errors = []
        for inv in invalid_refs:
            errors.append(
                f"Error: Post-2026 ADR file '{filename}' references invalid or misspelled requirement identifier(s): '{inv}'."
            )
        return False, "\n".join(errors)

    return True, ""


WORKSTATION_URL_RE = re.compile(
    r'file:///[^\s\)\"\']*?/cadence-clinical/([^\s\)\"\']+)'
)
PROTECTED_RE = re.compile(r"(```[\s\S]*?```|`[^`\n]+`|\]\([^\)]*\)|\[[^\]]+\]:\s*\S+)")


def sanitize_content(
    content: str, valid_requirements: set[str] = None, docs_dir: str = DEFAULT_DOCS_DIR
) -> tuple[str, int, int]:
    """
    Sanitizes markdown content by:
    1. Stripping workstation file:/// URLs pointing to cadence-clinical.
    2. Normalizing requirement reference strings outside code blocks and link targets.
    Returns (new_content, urls_replaced_count, reqs_normalized_count).
    """
    if valid_requirements is None:
        valid_requirements = get_valid_requirements(docs_dir)

    # Build numeric bare mapping: (CAT, int(NUM)) -> 'PRD-CAT-PADDED_NUM'
    bare_num_map = {}
    for req in valid_requirements:
        if req.startswith("PRD-"):
            parts = req.split("-")
            if len(parts) >= 3:
                cat = parts[1].upper()
                num_str = parts[2]
                try:
                    num_int = int(num_str)
                    bare_num_map[(cat, num_int)] = req
                except ValueError:
                    pass

    # 1. Strip workstation URLs
    url_count = 0

    def url_replacer(match):
        nonlocal url_count
        url_count += 1
        return match.group(1)

    new_content = WORKSTATION_URL_RE.sub(url_replacer, content)

    # 2. Normalize requirements in text sections outside protected blocks
    tokens = PROTECTED_RE.split(new_content)
    req_pattern = re.compile(
        r'\b(?:(PRD)-([A-Za-z0-9]+)-([A-Za-z0-9]+)|(trace)[\s-]*(\d+)|(?<!PRD-)(?<!prd-)([A-Za-z0-9]+)-(\d+))\b',
        re.IGNORECASE,
    )

    req_count = 0
    new_tokens = []

    for i, token in enumerate(tokens):
        # Odd indices are protected blocks (code blocks, inline code, link destinations)
        if i % 2 == 1:
            new_tokens.append(token)
            continue

        def req_replacer(m):
            nonlocal req_count
            full_match = m.group(0)

            # Case 1: PRD-CAT-NUM (normalize case to PRD-CAT-NUM)
            if m.group(1):
                normalized = f"PRD-{m.group(2).upper()}-{m.group(3).upper()}"
                if normalized != full_match:
                    req_count += 1
                    return normalized
                return full_match

            # Case 2: trace-x or trace x
            if m.group(4):
                normalized = f"Trace-{m.group(5)}"
                if normalized != full_match:
                    req_count += 1
                    return normalized
                return full_match

            # Case 3: Bare CAT-NUM
            if m.group(6):
                cat = m.group(6).upper()
                try:
                    num_int = int(m.group(7))
                    key = (cat, num_int)
                    if key in bare_num_map:
                        normalized = bare_num_map[key]
                        if normalized != full_match:
                            req_count += 1
                            return normalized
                except ValueError:
                    pass

            return full_match

        section_normalized = req_pattern.sub(req_replacer, token)
        new_tokens.append(section_normalized)

    final_content = "".join(new_tokens)
    return final_content, url_count, req_count


def sanitize_file(
    file_path: str,
    valid_requirements: set[str] = None,
    docs_dir: str = DEFAULT_DOCS_DIR,
) -> tuple[bool, int, int]:
    """
    Sanitizes a single file in-place if changes are needed.
    Returns (modified: bool, url_count: int, req_count: int).
    """
    if not os.path.isfile(file_path):
        return False, 0, 0

    with open(file_path, "r", encoding="utf-8") as f:
        content = f.read()

    new_content, url_count, req_count = sanitize_content(
        content, valid_requirements=valid_requirements, docs_dir=docs_dir
    )

    if new_content != content:
        with open(file_path, "w", encoding="utf-8") as f:
            f.write(new_content)
        return True, url_count, req_count

    return False, 0, 0


def sanitize_docs(
    docs_dir: str = DEFAULT_DOCS_DIR,
    target_files: list[str] = None,
    valid_requirements: set[str] = None,
) -> tuple[int, int, int]:
    """
    Sanitizes all markdown files in docs_dir or specified target_files.
    Returns (files_modified_count, urls_fixed_count, reqs_normalized_count).
    """
    if valid_requirements is None:
        valid_requirements = get_valid_requirements(docs_dir)

    if target_files:
        files = [f for f in target_files if f.endswith(".md") and os.path.isfile(f)]
    else:
        files = _sweep_markdown_files(docs_dir)

    modified_files = 0
    total_urls = 0
    total_reqs = 0

    for file_path in sorted(files):
        mod, urls, reqs = sanitize_file(
            file_path, valid_requirements=valid_requirements, docs_dir=docs_dir
        )
        if mod:
            modified_files += 1
            total_urls += urls
            total_reqs += reqs

    return modified_files, total_urls, total_reqs


if __name__ == "__main__":
    import argparse
    import sys

    parser = argparse.ArgumentParser(
        description="Compliance utility for path sanitization and requirement normalization."
    )
    parser.add_argument(
        "--sanitize",
        "--fix",
        dest="sanitize",
        action="store_true",
        help="Sanitize workstation file URLs and normalize requirement IDs in markdown files.",
    )
    parser.add_argument(
        "--docs-dir",
        default=DEFAULT_DOCS_DIR,
        help="Directory containing documentation files.",
    )
    parser.add_argument(
        "files",
        nargs="*",
        help="Optional list of specific markdown files to process.",
    )

    args = parser.parse_args()

    if args.sanitize:
        files_mod, urls_fixed, reqs_norm = sanitize_docs(
            docs_dir=args.docs_dir, target_files=args.files
        )
        print(
            f"Sanitization complete: {files_mod} file(s) updated, "
            f"{urls_fixed} workstation URL(s) sanitized, "
            f"{reqs_norm} requirement reference(s) normalized."
        )
        sys.exit(0)

