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


def parse_srs(filepath: str) -> set[str]:
    """Parses requirements from SRS file."""
    requirements = set()
    if not os.path.exists(filepath):
        return requirements

    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    # Pattern used in generate_rtm.py: e.g. * **Trace 1: Shadow Schema Retention:**
    pattern = re.compile(r"\*\s*\*\*Trace\s*(\d+)")
    for line in content.splitlines():
        match = pattern.search(line)
        if match:
            num = match.group(1)
            requirements.add(f"Trace-{num}")

    # Fallback to general Trace-\d+ parsing
    general_pattern = re.compile(r"\bTrace-(\d+)\b")
    for m in general_pattern.findall(content):
        requirements.add(f"Trace-{m}")

    return requirements


def parse_prd(filepath: str) -> set[str]:
    """Parses requirements from PRD/product design files."""
    requirements = set()
    if not os.path.exists(filepath):
        return requirements

    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    # Pattern used in generate_rtm.py: e.g. #### PRD-SYS-001:
    pattern = re.compile(r"####\s*(PRD-[A-Z]+-\d+)")
    for line in content.splitlines():
        match = pattern.search(line)
        if match:
            requirements.add(match.group(1))

    # General regex pattern for any PRD-[A-Z]+-\d+ reference
    general_pattern = re.compile(r"\b(PRD-[A-Z]+-\d+)\b")
    for m in general_pattern.findall(content):
        requirements.add(m)

    return requirements


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
DEFAULT_DOCS_DIR = os.path.join(REPO_ROOT, "docs")


def get_valid_requirements(docs_dir: str = DEFAULT_DOCS_DIR) -> set[str]:
    """Gets the master set of valid requirement identifiers."""
    srs_path = os.path.join(docs_dir, "SRS.md")
    prd_path = os.path.join(docs_dir, "SDLC/01_Product_Requirements_Document_PRD.md")

    reqs = set()
    reqs.update(parse_srs(srs_path))
    reqs.update(parse_prd(prd_path))

    # Support other requirement prefixes just in case
    # Walker through docs to ensure complete safety
    sdlc_dir = os.path.join(docs_dir, "SDLC")
    if os.path.isdir(sdlc_dir):
        for f in os.listdir(sdlc_dir):
            if f.endswith(".md"):
                file_path = os.path.join(sdlc_dir, f)
                reqs.update(parse_prd(file_path))

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
    if year == 2026 and filename in BASELINE_FILES:
        return False
    return True


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

    # We can also support req-X and sys-X case-insensitively just in case
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
        refs.add(f"SYS-{m.upper()}")

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
