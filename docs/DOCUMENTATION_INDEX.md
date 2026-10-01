# Documentation Index

The single map of Cadence documentation. Every document is listed once, with its status:

- **Normative:** the source of truth; other documents must agree with it.
- **Current:** maintained with the code; updated in the same pull request as the change.
- **Generated:** produced by a command; never edit by hand.
- **Baseline:** written for the pre-kernel, per-service design. Kept for requirement IDs and
  traceability; a banner at the top says what supersedes it.
- **Historical:** records of completed work (the Python-to-.NET port).

When you add a document, add it here. When a change makes a document wrong, fix it in the same PR
([CONTRIBUTING.md §5](../CONTRIBUTING.md#5-documentation-is-part-of-the-change)).

---

## Start Here & Core System Overview

| Document | Path |
| --- | --- |
| [AGENTS - Agent & Contributor Guidelines](../AGENTS.md) | `../AGENTS.md` |
| [ARCHITECTURE - System Architecture](../ARCHITECTURE.md) | `../ARCHITECTURE.md` |
| [CONTRIBUTING - Workflow & Standards](../CONTRIBUTING.md) | `../CONTRIBUTING.md` |
| [LICENSE - Project License](../LICENSE) | `../LICENSE` |
| [README - Cadence Platform Overview](../README.md) | `../README.md` |
| [Continuous Integration](CI.md) | `CI.md` |
| [Cadence Unified Developer CLI Reference](cli.md) | `cli.md` |
| [Cadence Clinical: Competitive Analysis, Market Superiority & Full-Spectrum Feature Blueprint](COMPETITIVE_ANALYSIS_AND_MARKET_SUPERIORITY.md) | `COMPETITIVE_ANALYSIS_AND_MARKET_SUPERIORITY.md` |
| [Data Lifecycle Specification](DATA_LIFECYCLE.md) | `DATA_LIFECYCLE.md` |
| [Documentation Index](DOCUMENTATION_INDEX.md) | `DOCUMENTATION_INDEX.md` |
| [Environment Variables](ENVIRONMENT_VARIABLES.md) | `ENVIRONMENT_VARIABLES.md` |
| [Feature & Compatibility Matrix](FEATURE_MATRIX.md) | `FEATURE_MATRIX.md` |
| [Product Specification & Functional Requirements Document (FRD)](FUNCTIONAL_PRODUCT_SPECIFICATION.md) | `FUNCTIONAL_PRODUCT_SPECIFICATION.md` |
| [Local Development Environment](LOCAL_DEV_ENVIRONMENT.md) | `LOCAL_DEV_ENVIRONMENT.md` |
| [Cadence Clinical Platform: .NET 10 Migration Workflow & Playbook](MIGRATION_WORKFLOW.md) | `MIGRATION_WORKFLOW.md` |
| [Database Layout and Migrations Runbook](OPERATIONAL_DATABASE.md) | `OPERATIONAL_DATABASE.md` |
| [eTMF Inbound-Email Webhook - Operational & Technical Guide](OPERATIONAL_INBOUND_EMAIL.md) | `OPERATIONAL_INBOUND_EMAIL.md` |
| [Lab Reference Range Management - Operational & Technical Guide](OPERATIONAL_LAB_RANGES.md) | `OPERATIONAL_LAB_RANGES.md` |
| [Python Reference Inventory (#4789)](PYTHON_REFERENCE_INVENTORY.md) | `PYTHON_REFERENCE_INVENTORY.md` |
| [Software Requirements Specification (SRS)](SRS.md) | `SRS.md` |
| [Testing and Requirement Qualification](TESTING.md) | `TESTING.md` |

## Clinical Kernel & Roadmap

| Document | Path |
| --- | --- |
| [Cadence Clinical Research OS: Ontology and Kernel Specification v0.2](kernel/CLINICAL_KERNEL_SPECIFICATION.md) | `kernel/CLINICAL_KERNEL_SPECIFICATION.md` |
| [Clock Migration Backlog (K-TIME-3, #4784)](kernel/CLOCK_MIGRATION_BACKLOG.md) | `kernel/CLOCK_MIGRATION_BACKLOG.md` |
| [Execution → Clinical Kernel Migration Inventory](kernel/EXECUTION_MIGRATION_INVENTORY.md) | `kernel/EXECUTION_MIGRATION_INVENTORY.md` |
| [Indicators](kernel/INDICATORS.md) | `kernel/INDICATORS.md` |
| [Mapping Specifications](kernel/MAPPING_SPECIFICATIONS.md) | `kernel/MAPPING_SPECIFICATIONS.md` |
| [Rule Packs](kernel/RULE_PACKS.md) | `kernel/RULE_PACKS.md` |

## Architectural Decision Records (ADRs)

| Document | Path |
| --- | --- |
| [ADR-2262: Neo4j Graph Database for Clinical Metadata](adr/2023-01-01-neo4j-graph-database.md) | `adr/2023-01-01-neo4j-graph-database.md` |
| [ADR-2263: PostgreSQL Relational Database for Execution](adr/2023-01-02-postgresql-relational-database.md) | `adr/2023-01-02-postgresql-relational-database.md` |
| [ADR-2264: Pydantic v2 for Domain Models](adr/2023-01-03-pydantic-v2-domain-models.md) | `adr/2023-01-03-pydantic-v2-domain-models.md` |
| [ADR-2265: Keycloak Identity Management](adr/2023-01-04-keycloak-identity-management.md) | `adr/2023-01-04-keycloak-identity-management.md` |
| [ADR-2266: USDM Pydantic Models](adr/2026-06-06-usdm-pydantic-models.md) | `adr/2026-06-06-usdm-pydantic-models.md` |
| [ADR-042: Decoupled API-First In-Memory Diffing](adr/2026-07-22-api-first-in-memory-diffing.md) | `adr/2026-07-22-api-first-in-memory-diffing.md` |
| [ADR-2267: API-First Validation and USDM Integration](adr/2026-07-22-api-first-validation-and-usdm-integration.md) | `adr/2026-07-22-api-first-validation-and-usdm-integration.md` |
| [ADR-2268: Audit Log Design](adr/2026-07-22-audit-log-design.md) | `adr/2026-07-22-audit-log-design.md` |
| [ADR-014: 2026-07-22 Compliance Tracing and Automated Trial Locks](adr/2026-07-22-compliance-tracing-and-automated-trial-locks.md) | `adr/2026-07-22-compliance-tracing-and-automated-trial-locks.md` |
| [ADR-2269: Database Shadow Triggers](adr/2026-07-22-database-shadow-triggers.md) | `adr/2026-07-22-database-shadow-triggers.md` |
| [ADR-2270: Centralized API Gateway Authentication and Header Propagation](adr/2026-07-22-gateway-authentication-propagation.md) | `adr/2026-07-22-gateway-authentication-propagation.md` |
| [ADR-2271: Dynamic OpenAPI Schema Aggregation at the Gateway](adr/2026-07-22-gateway-openapi-aggregation.md) | `adr/2026-07-22-gateway-openapi-aggregation.md` |
| [ADR-2272: Merkle Root Sealing](adr/2026-07-22-merkle-root-sealing.md) | `adr/2026-07-22-merkle-root-sealing.md` |
| [ADR-2273: Metadata-Driven Grid Layouts via Jinja2 Templating](adr/2026-07-22-metadata-driven-grid-layouts.md) | `adr/2026-07-22-metadata-driven-grid-layouts.md` |
| [ADR-2274: Modular pnpm Workspace for Frontend Development](adr/2026-07-22-pnpm-frontend-workspace.md) | `adr/2026-07-22-pnpm-frontend-workspace.md` |
| [ADR-2275: Schema Mapping Design](adr/2026-07-22-schema-mapping-design.md) | `adr/2026-07-22-schema-mapping-design.md` |
| [ADR-2276: Signature Re-Authentication](adr/2026-07-22-signature-re-authentication.md) | `adr/2026-07-22-signature-re-authentication.md` |
| [ADR-2277: Transactional SQL Engine with Standard GxP Auditing](adr/2026-07-22-transactional-sql-engine-gxp-auditing.md) | `adr/2026-07-22-transactional-sql-engine-gxp-auditing.md` |
| [ADR-2278: Background Translation Job and Layout Engine Data Contract](adr/2026-07-22-translation-job-and-layout-engine.md) | `adr/2026-07-22-translation-job-and-layout-engine.md` |
| [ADR-2279: Unified Database Management and Pre-Boot Migrations](adr/2026-07-22-unified-database-management-and-pre-boot-migrations.md) | `adr/2026-07-22-unified-database-management-and-pre-boot-migrations.md` |
| [ADR-2280: Unified USDM Dynamic Mapper and Terminology Cache](adr/2026-07-22-usdm-dynamic-mapper-and-cache.md) | `adr/2026-07-22-usdm-dynamic-mapper-and-cache.md` |
| [ADR-2281: Secure API Signatures with Canonical JSON (Version 2 Sole Standard)](adr/2026-07-23-canonical-json-signatures-and-rolling-versioning.md) | `adr/2026-07-23-canonical-json-signatures-and-rolling-versioning.md` |
| [ADR-016: Core Service-Oriented Clinical Engine](adr/2026-07-23-core-service-oriented-clinical-engine.md) | `adr/2026-07-23-core-service-oriented-clinical-engine.md` |
| [ADR-2282: Declarative Ruleset and Automerge Integration](adr/2026-07-23-declarative-ruleset-automerge.md) | `adr/2026-07-23-declarative-ruleset-automerge.md` |
| [ADR-2283: Targeted Integration with Path Isolation](adr/2026-07-23-targeted-integration-path-isolation.md) | `adr/2026-07-23-targeted-integration-path-isolation.md` |
| [ADR-054: Clinical Query Workflow and Role Authorization](adr/2026-07-24-clinical-query-workflow-authorization.md) | `adr/2026-07-24-clinical-query-workflow-authorization.md` |
| [ADR-2284: Code Formatting and Style Standardization](adr/2026-07-24-code-formatting-and-style-standardization.md) | `adr/2026-07-24-code-formatting-and-style-standardization.md` |
| [ADR-2285: Continuous FMEA GxP-Aligned Exemption Ledger](adr/2026-07-24-continuous-fmea-gxp-aligned-exemption-ledger.md) | `adr/2026-07-24-continuous-fmea-gxp-aligned-exemption-ledger.md` |
| [ADR-056: Database-Native Pessimistic Locking & Retry](adr/2026-07-24-database-native-pessimistic-locking-retry.md) | `adr/2026-07-24-database-native-pessimistic-locking-retry.md` |
| [ADR-2286: FHIR / eSource & eCOA Sync Gateway](adr/2026-07-24-fhir-esource-ecoa-sync-gateway.md) | `adr/2026-07-24-fhir-esource-ecoa-sync-gateway.md` |
| [ADR-050: Medical Coding Engine Persistence Foundation](adr/2026-07-24-medical-coding-engine-persistence.md) | `adr/2026-07-24-medical-coding-engine-persistence.md` |
| [ADR-2287: Minimizing Merge Friction for Baseline and SDLC Verification Artifacts](adr/2026-07-24-minimizing-merge-friction-for-baselines-and-reports.md) | `adr/2026-07-24-minimizing-merge-friction-for-baselines-and-reports.md` |
| [ADR-057: Rolling Versioning and Transition Support for Canonical JSON Signatures](adr/2026-07-24-rolling-upgrade-and-canonical-json-signatures.md) | `adr/2026-07-24-rolling-upgrade-and-canonical-json-signatures.md` |
| [ADR-2288: Data-Driven Expected Document Lists (EDLs) & Completeness Tracking](adr/2026-07-25-data-driven-edl-model-and-completeness.md) | `adr/2026-07-25-data-driven-edl-model-and-completeness.md` |
| [ADR-2289: eTMF Quality Control Review Lifecycle & Validated State Machine](adr/2026-07-25-etmf-qc-review-workflow.md) | `adr/2026-07-25-etmf-qc-review-workflow.md` |
| [ADR-058: FDA 21 CFR Part 11 Offline Sync Ledger & Digital Signatures](adr/2026-07-25-part11-offline-sync-ledger.md) | `adr/2026-07-25-part11-offline-sync-ledger.md` |
| [ADR-046: CTMS Foundation, Database, Auditing, and RBAC Infrastructure](adr/2026-07-26-ctms-foundation-infrastructure.md) | `adr/2026-07-26-ctms-foundation-infrastructure.md` |
| [ADR-097: API-driven Lock Sync and AST Import Validator for eTMF](adr/2026-07-27-api-driven-lock-sync-and-ast-validator.md) | `adr/2026-07-27-api-driven-lock-sync-and-ast-validator.md` |
| [ADR-085: Backward-Compatible Gateway Signature Verification](adr/2026-07-27-backward-compatible-gateway-signature-verification.md) | `adr/2026-07-27-backward-compatible-gateway-signature-verification.md` |
| [ADR-095: CI Schema Introspection and Gateway Aggregation Resilience](adr/2026-07-27-ci-schema-introspection-and-gateway-aggregation.md) | `adr/2026-07-27-ci-schema-introspection-and-gateway-aggregation.md` |
| [ADR-096: API Gateway Startup Environment Assertions to Prevent Production Auth Bypasses](adr/2026-07-27-gateway-startup-environment-assertions.md) | `adr/2026-07-27-gateway-startup-environment-assertions.md` |
| [ADR-2290: Programmatic Multi-Database Reset CLI Tool with Safety Guardrails](adr/2026-07-27-multi-database-reset-cli-tool.md) | `adr/2026-07-27-multi-database-reset-cli-tool.md` |
| [ADR-104: PostgreSQL-Native Parallel Testing and Unified CI Caching](adr/2026-07-27-postgres-parallel-testing-ci-caching.md) | `adr/2026-07-27-postgres-parallel-testing-ci-caching.md` |
| [ADR-047: Quality & CAPA Foundation and Domain Models](adr/2026-07-27-quality-capa-scaffold.md) | `adr/2026-07-27-quality-capa-scaffold.md` |
| [ADR-2291: Standardize FastAPI Identity Dependency and Site Access Migration](adr/2026-07-27-standardize-fastapi-identity-dependency.md) | `adr/2026-07-27-standardize-fastapi-identity-dependency.md` |
| [ADR-2292: Unified Python-based Documentation Validator](adr/2026-07-27-unified-python-markdown-validator.md) | `adr/2026-07-27-unified-python-markdown-validator.md` |
| [ADR-0091: Dynamic Local Branch Point Detection and Offline ADR Validation](adr/2026-07-28-dynamic-local-branch-point-detection-and-offline-validation.md) | `adr/2026-07-28-dynamic-local-branch-point-detection-and-offline-validation.md` |
| [ADR-0001: Hybrid ESLint and In-Memory Accessibility Auditing](adr/2026-07-28-hybrid-eslint-and-in-memory-accessibility-auditing.md) | `adr/2026-07-28-hybrid-eslint-and-in-memory-accessibility-auditing.md` |
| [ADR-2293: Native Vue 3 Rules Designer with GxP Ledger Sync](adr/2026-07-28-native-vue3-rules-designer-gxp-ledger-sync.md) | `adr/2026-07-28-native-vue3-rules-designer-gxp-ledger-sync.md` |
| [ADR-048: SDV/TSDV Persistence Foundation and Migration Support](adr/2026-07-28-sdv-tsdv-persistence-foundation.md) | `adr/2026-07-28-sdv-tsdv-persistence-foundation.md` |
| [ADR-108: Consolidated eClinical Services and Compliance Enhancements](adr/2026-07-29-consolidated-eclinical-services-and-compliance-enhancements.md) | `adr/2026-07-29-consolidated-eclinical-services-and-compliance-enhancements.md` |
| [ADR-106: Standardize PR Quality Gate Comments and Developer Verification Workflows](adr/2026-07-29-standardize-pr-quality-gate-comments-and-developer-verification-workflows.md) | `adr/2026-07-29-standardize-pr-quality-gate-comments-and-developer-verification-workflows.md` |
| [ADR-049: TMF Reference Model Taxonomy Integration](adr/2026-07-29-tmf-reference-model-taxonomy-integration.md) | `adr/2026-07-29-tmf-reference-model-taxonomy-integration.md` |
| [ADR-114: Deprecate legacy econsent helper in packages ui](adr/2026-07-30-deprecate-legacy-econsent-helper-in-packages-ui.md) | `adr/2026-07-30-deprecate-legacy-econsent-helper-in-packages-ui.md` |
| [ADR-107: Durable and Immutable Subject Enrollment Sequence for TSDV](adr/2026-07-30-durable-and-immutable-subject-enrollment-sequence-for-tsdv.md) | `adr/2026-07-30-durable-and-immutable-subject-enrollment-sequence-for-tsdv.md` |
| [ADR-113: eConsent Visual Editor and Subject Consent Portal UI Architecture](adr/2026-07-30-econsent-visual-editor-and-subject-consent-portal-ui-architecture.md) | `adr/2026-07-30-econsent-visual-editor-and-subject-consent-portal-ui-architecture.md` |
| [ADR-2294: Lab Reference Range Management](adr/2026-07-30-lab-reference-range-management.md) | `adr/2026-07-30-lab-reference-range-management.md` |
| [ADR-109: Protocol Ingestion and CRF Draft Generation Workflow](adr/2026-07-30-protocol-ingestion-and-crf-draft-generation-workflow.md) | `adr/2026-07-30-protocol-ingestion-and-crf-draft-generation-workflow.md` |
| [ADR-2295: Rule Authoring, Validation, and DDF Delivery](adr/2026-07-30-rule-authoring-validation-and-ddf-delivery.md) | `adr/2026-07-30-rule-authoring-validation-and-ddf-delivery.md` |
| [ADR-112: Signatures and Workflow Automation Phase 1 and 2 Primitives](adr/2026-07-30-signatures-and-workflow-automation-phase-1-and-2-primitives.md) | `adr/2026-07-30-signatures-and-workflow-automation-phase-1-and-2-primitives.md` |
| [ADR-140: Clinical Code Lookup Input Helper in Shared UI Package](adr/2026-07-31-clinical-code-lookup-input-helper-in-shared-ui-package.md) | `adr/2026-07-31-clinical-code-lookup-input-helper-in-shared-ui-package.md` |
| [ADR-121: Decoupling Cross-Service Python Imports and Security RBAC Isolation](adr/2026-07-31-decoupling-cross-service-python-imports-and-security-rbac-isolation.md) | `adr/2026-07-31-decoupling-cross-service-python-imports-and-security-rbac-isolation.md` |
| [ADR-051: eCOA Subject Identity, Authorization, and Gateway Routing](adr/2026-07-31-ecoa-subject-identity-and-gateway-routing.md) | `adr/2026-07-31-ecoa-subject-identity-and-gateway-routing.md` |
| [ADR-139: Expose CRA Monitoring SDV Transport Models and Shared Sign-Off Logic](adr/2026-07-31-expose-cra-monitoring-sdv-transport-models-and-shared-sign-off-logic.md) | `adr/2026-07-31-expose-cra-monitoring-sdv-transport-models-and-shared-sign-off-logic.md` |
| [ADR-144: Expression Edit Check Phase 14 Runtime Evaluation Engine](adr/2026-07-31-expression-edit-check-phase-14-runtime-evaluation-engine.md) | `adr/2026-07-31-expression-edit-check-phase-14-runtime-evaluation-engine.md` |
| [ADR-130: Extend Centralized Permission Matrix for Medical Coding](adr/2026-07-31-extend-centralized-permission-matrix-for-medical-coding.md) | `adr/2026-07-31-extend-centralized-permission-matrix-for-medical-coding.md` |
| [ADR-117: GxP 21 CFR Part 11 eConsent Audit Trails](adr/2026-07-31-gxp-21-cfr-part-11-econsent-audit-trails.md) | `adr/2026-07-31-gxp-21-cfr-part-11-econsent-audit-trails.md` |
| [ADR-131: Informed Consent Multi-Language Translation Management](adr/2026-07-31-informed-consent-multi-language-translation-management.md) | `adr/2026-07-31-informed-consent-multi-language-translation-management.md` |
| [ADR-132: Lab Master Catalog and Unit Conversion Data Models](adr/2026-07-31-lab-master-catalog-and-unit-conversion-data-models.md) | `adr/2026-07-31-lab-master-catalog-and-unit-conversion-data-models.md` |
| [ADR-141: Modernize Python 3.10 Type Hint Syntax and Lock Ruff Rule Enforcements](adr/2026-07-31-modernize-python-310-type-hint-syntax-and-lock-ruff-rule-enforcements.md) | `adr/2026-07-31-modernize-python-310-type-hint-syntax-and-lock-ruff-rule-enforcements.md` |
| [ADR-2296: On-Demand Consent Status Validation and Audit Hooks](adr/2026-07-31-on-demand-consent-status-validation-and-audit-hooks.md) | `adr/2026-07-31-on-demand-consent-status-validation-and-audit-hooks.md` |
| [ADR-2297: Reference Range Data Model and Persistence Updates](adr/2026-07-31-reference-range-data-model-and-persistence-updates.md) | `adr/2026-07-31-reference-range-data-model-and-persistence-updates.md` |
| [ADR-2298: Resolve Vitest Peer Dependency Conflict in Shared UI Package](adr/2026-07-31-resolve-vitest-peer-dependency-conflict-in-shared-ui-package.md) | `adr/2026-07-31-resolve-vitest-peer-dependency-conflict-in-shared-ui-package.md` |
| [ADR-2299: SDV & TSDV Metadata and Database Migrations](adr/2026-07-31-sdv-tsdv-metadata-enhancements.md) | `adr/2026-07-31-sdv-tsdv-metadata-enhancements.md` |
| [ADR-145: SDV Verification and Compliance Tests Phase 14](adr/2026-07-31-sdv-verification-and-compliance-tests-phase-14.md) | `adr/2026-07-31-sdv-verification-and-compliance-tests-phase-14.md` |
| [ADR-133: Upgrade Environments to Python 3.12](adr/2026-07-31-upgrade-environments-to-python-312.md) | `adr/2026-07-31-upgrade-environments-to-python-312.md` |
| [ADR-2300: Viewport-Aware Grid Inspector for eCRF designer](adr/2026-07-31-viewport-aware-grid-inspector.md) | `adr/2026-07-31-viewport-aware-grid-inspector.md` |
| [ADR-138: Vue SPA Component Smoke Test and Vitest Pipeline Integration](adr/2026-07-31-vue-spa-component-smoke-test-and-vitest-pipeline-integration.md) | `adr/2026-07-31-vue-spa-component-smoke-test-and-vitest-pipeline-integration.md` |
| [ADR-189: Cryptographic Signature Verification Engine and Multi-Format Support](adr/2026-08-01-cryptographic-signature-verification-engine-and-multi-format-support.md) | `adr/2026-08-01-cryptographic-signature-verification-engine-and-multi-format-support.md` |
| [ADR-120: Cryptographic Signature Verification Engine for Electronic Signatures](adr/2026-08-01-cryptographic-signature-verification-engine-for-esignatures.md) | `adr/2026-08-01-cryptographic-signature-verification-engine-for-esignatures.md` |
| [ADR-142: Define Laboratory Master and Align ORM Models](adr/2026-08-01-define-laboratory-master-and-align-orm-models.md) | `adr/2026-08-01-define-laboratory-master-and-align-orm-models.md` |
| [ADR-2301: Developer-Centric Architecture Alignment and Automated Drift Gating](adr/2026-08-01-dev-centric-alignment-and-automated-drift-gating.md) | `adr/2026-08-01-dev-centric-alignment-and-automated-drift-gating.md` |
| [ADR-190: Explicit Background Task Cancellation in Ledger Sealer](adr/2026-08-01-explicit-background-task-cancellation-in-ledger-sealer.md) | `adr/2026-08-01-explicit-background-task-cancellation-in-ledger-sealer.md` |
| [ADR-2302: In-Process ASGI Routing with Live Databases](adr/2026-08-01-in-process-asgi-routing-with-live-databases.md) | `adr/2026-08-01-in-process-asgi-routing-with-live-databases.md` |
| [ADR-2303: SDV and TSDV Data Model Foundations](adr/2026-08-01-sdv-and-tsdv-data-model-foundations.md) | `adr/2026-08-01-sdv-and-tsdv-data-model-foundations.md` |
| [ADR-2304: Unused ESLint Variable and DEID Exemption in UI Package](adr/2026-08-01-unused-eslint-variable-and-deid-exemption-in-ui-package.md) | `adr/2026-08-01-unused-eslint-variable-and-deid-exemption-in-ui-package.md` |
| [ADR-052: Vue SPA, OIDC, RBAC, and Signing Boundaries](adr/2026-08-01-vue-spa-oidc-rbac-signing-boundaries.md) | `adr/2026-08-01-vue-spa-oidc-rbac-signing-boundaries.md` |
| [ADR-251: Centralized Accessibility Matcher and 80 Percent Coverage Gate](adr/2026-08-02-centralized-accessibility-matcher-and-80-percent-coverage-gate.md) | `adr/2026-08-02-centralized-accessibility-matcher-and-80-percent-coverage-gate.md` |
| [ADR-2305: Dual-Mode Secret Scanner for Pre-Commit and CI Validation](adr/2026-08-02-dual-mode-secret-scanner-for-pre-commit-and-ci-validation.md) | `adr/2026-08-02-dual-mode-secret-scanner-for-pre-commit-and-ci-validation.md` |
| [ADR-053: eConsent Scaffold and Shared Part 11 Audit Schemas](adr/2026-08-02-econsent-scaffold-and-part11-audit.md) | `adr/2026-08-02-econsent-scaffold-and-part11-audit.md` |
| [ADR-2306: Shared Eligibility Criteria Domain Contract, DSL Parser, and Deterministic Evaluator](adr/2026-08-02-eligibility-criteria-evaluation-engine.md) | `adr/2026-08-02-eligibility-criteria-evaluation-engine.md` |
| [ADR-2307: Enforce Ruff Style and Formatting on API Gateway Routers](adr/2026-08-02-enforce-ruff-style-and-formatting-on-api-gateway-routers.md) | `adr/2026-08-02-enforce-ruff-style-and-formatting-on-api-gateway-routers.md` |
| [ADR-2308: Enforce Strict Fail-Closed Secrets Validation and Narrow Scanner Exclusions](adr/2026-08-02-enforce-strict-fail-closed-secrets-validation-and-narrow-scanner-exclusions.md) | `adr/2026-08-02-enforce-strict-fail-closed-secrets-validation-and-narrow-scanner-exclusions.md` |
| [ADR-2309: Implement WCAG compliance in shared clinical UI primitives](adr/2026-08-02-implement-wcag-compliance-in-shared-clinical-ui-primitives.md) | `adr/2026-08-02-implement-wcag-compliance-in-shared-clinical-ui-primitives.md` |
| [ADR-2310: Standalone Notifications Service Foundation](adr/2026-08-02-notifications-service-foundation.md) | `adr/2026-08-02-notifications-service-foundation.md` |
| [ADR-2311: Protocol Document Rendering Architecture and Content Contract](adr/2026-08-02-protocol-rendering-architecture.md) | `adr/2026-08-02-protocol-rendering-architecture.md` |
| [ADR-2312: Refactor Signature Validations and Enforce Ruff S101](adr/2026-08-02-refactor-signature-validations-and-enforce-ruff-s101.md) | `adr/2026-08-02-refactor-signature-validations-and-enforce-ruff-s101.md` |
| [ADR-2313: RTSM Architecture and Randomization Persistence](adr/2026-08-02-rtsm-architecture-and-randomization-persistence.md) | `adr/2026-08-02-rtsm-architecture-and-randomization-persistence.md` |
| [ADR-2314: Shared SDTM Foundation Models & Terminology](adr/2026-08-02-sdtm-foundation-models.md) | `adr/2026-08-02-sdtm-foundation-models.md` |
| [ADR-180: Subject Portal Accessibility Auditing and Verification](adr/2026-08-02-subject-portal-playwright-a11y-verification.md) | `adr/2026-08-02-subject-portal-playwright-a11y-verification.md` |
| [ADR-2315: USDM v2/v3 Canonical Contract and Mapping Coverage Matrix](adr/2026-08-02-usdm-v2-v3-canonical-contract-mapping-matrix.md) | `adr/2026-08-02-usdm-v2-v3-canonical-contract-mapping-matrix.md` |
| [ADR-254: Centralized Clinical Form Layout Wrapper](adr/2026-08-03-centralized-clinical-form-layout-wrapper.md) | `adr/2026-08-03-centralized-clinical-form-layout-wrapper.md` |
| [ADR-255: Clean up duplicate security entries in rbac configuration](adr/2026-08-03-clean-up-duplicate-security-entries-in-rbac-configuration.md) | `adr/2026-08-03-clean-up-duplicate-security-entries-in-rbac-configuration.md` |
| [ADR-2316: Decommission Legacy Standalone Frontend Rendering Engine and Legacy V1 Signatures](adr/2026-08-03-cleanup-legacy-rendering-v1-signatures.md) | `adr/2026-08-03-cleanup-legacy-rendering-v1-signatures.md` |
| [ADR-2317: Decommission legacy V1 signatures and standalone rendering engine](adr/2026-08-03-decommission-legacy-v1-signatures-and-standalone-rendering-engine.md) | `adr/2026-08-03-decommission-legacy-v1-signatures-and-standalone-rendering-engine.md` |
| [ADR-2318: eConsent Gateway Integration and Orchestration Port Alignment](adr/2026-08-03-econsent-gateway-integration-and-orchestration-port-alignment.md) | `adr/2026-08-03-econsent-gateway-integration-and-orchestration-port-alignment.md` |
| [ADR-2319: Fail-Safe GxP Document Generation and Draft Warning Banners](adr/2026-08-03-fail-safe-gxp-document-generation-and-draft-warning-banners.md) | `adr/2026-08-03-fail-safe-gxp-document-generation-and-draft-warning-banners.md` |
| [ADR-2320: Harden Serialization (RFC 8785) and Implement eConsent Schema Guards](adr/2026-08-03-harden-serialization-rfc-8785-and-implement-econsent-schema-guards.md) | `adr/2026-08-03-harden-serialization-rfc-8785-and-implement-econsent-schema-guards.md` |
| [ADR-2321: Implement class-based touch target optimization for dense layouts](adr/2026-08-03-implement-class-based-touch-target-optimization-for-dense-layouts.md) | `adr/2026-08-03-implement-class-based-touch-target-optimization-for-dense-layouts.md` |
| [ADR-2322: Implement native uv workspaces with service-specific Dockerfiles](adr/2026-08-03-implement-native-uv-workspaces-with-service-specific-dockerfiles.md) | `adr/2026-08-03-implement-native-uv-workspaces-with-service-specific-dockerfiles.md` |
| [ADR-253: Lightweight Path Pattern Boundary Linter](adr/2026-08-03-lightweight-path-pattern-boundary-linter.md) | `adr/2026-08-03-lightweight-path-pattern-boundary-linter.md` |
| [ADR-2323: Migrate Clinical Forms to Pre-Compiled Shared UI Library](adr/2026-08-03-migrate-clinical-forms-to-pre-compiled-shared-ui-library.md) | `adr/2026-08-03-migrate-clinical-forms-to-pre-compiled-shared-ui-library.md` |
| [ADR-256: Native Interval Filtering and Custom Name Matching](adr/2026-08-03-native-interval-filtering-and-custom-name-matching.md) | `adr/2026-08-03-native-interval-filtering-and-custom-name-matching.md` |
| [ADR-2324: Optimized eTMF Document Registry with Store Indexing and Client-Side Pagination](adr/2026-08-03-optimized-etmf-document-registry-with-store-indexing-and-client-side-pagination.md) | `adr/2026-08-03-optimized-etmf-document-registry-with-store-indexing-and-client-side-pagination.md` |
| [ADR-135: Resolve RBAC Duplicate Key Static Lint Failures](adr/2026-08-03-resolve-rbac-duplicate-key-static-lint-failures.md) | `adr/2026-08-03-resolve-rbac-duplicate-key-static-lint-failures.md` |
| [ADR-2325: Ruff Lint Alignment and Centralized RBAC Validation](adr/2026-08-03-ruff-lint-alignment-and-centralized-rbac-validation.md) | `adr/2026-08-03-ruff-lint-alignment-and-centralized-rbac-validation.md` |
| [ADR-2326: Subject Randomization Lifecycle and State Guards](adr/2026-08-03-subject-randomization-lifecycle-and-state-guards.md) | `adr/2026-08-03-subject-randomization-lifecycle-and-state-guards.md` |
| [ADR-147: Task-Driven CRA Workspace Routing & Multi-Select Batch SDV](adr/2026-08-03-task-driven-cra-workspace-and-batch-sdv-verification.md) | `adr/2026-08-03-task-driven-cra-workspace-and-batch-sdv-verification.md` |
| [ADR-134: Upgrade Environments to Python 3.14](adr/2026-08-03-upgrade-environments-to-python-3-14.md) | `adr/2026-08-03-upgrade-environments-to-python-3-14.md` |
| [ADR-2327: Upgrade Environments to Python 3.14](adr/2026-08-03-upgrade-environments-to-python-314.md) | `adr/2026-08-03-upgrade-environments-to-python-314.md` |
| [ADR-2328: Centralized layout utilities and 48px touch targets](adr/2026-08-04-centralized-layout-utilities-and-48px-touch-targets.md) | `adr/2026-08-04-centralized-layout-utilities-and-48px-touch-targets.md` |
| [ADR-257: Centralized Redis eSignature Token Replay Prevention](adr/2026-08-04-centralized-redis-esignature-token-replay-prevention.md) | `adr/2026-08-04-centralized-redis-esignature-token-replay-prevention.md` |
| [ADR-2329: Compile-Time Vite Build Mode for Interactive Public Demo](adr/2026-08-04-compile-time-vite-build-mode-for-interactive-public-demo.md) | `adr/2026-08-04-compile-time-vite-build-mode-for-interactive-public-demo.md` |
| [ADR-055: CTMS Service Boundary, Reporting, and Operational Workspace](adr/2026-08-04-ctms-service-boundary-and-reporting.md) | `adr/2026-08-04-ctms-service-boundary-and-reporting.md` |
| [ADR-2158: Decoupled Hexagonal Storage Architecture for Study Designer](adr/2026-08-04-decoupled-hexagonal-storage-architecture-for-study-designer.md) | `adr/2026-08-04-decoupled-hexagonal-storage-architecture-for-study-designer.md` |
| [ADR-2157: Enforce Fail-Fast Environment Validation for GxP Compliance](adr/2026-08-04-enforce-fail-fast-environment-validation-for-gxp-compliance.md) | `adr/2026-08-04-enforce-fail-fast-environment-validation-for-gxp-compliance.md` |
| [ADR-2156: Fail-Fast Environment Validation for Cryptographic Secrets](adr/2026-08-04-fail-fast-environment-validation-for-cryptographic-secrets.md) | `adr/2026-08-04-fail-fast-environment-validation-for-cryptographic-secrets.md` |
| [ADR-2330: Implement Hexagonal Domain Repository Separation](adr/2026-08-04-implement-hexagonal-domain-repository-separation.md) | `adr/2026-08-04-implement-hexagonal-domain-repository-separation.md` |
| [ADR-2331: Implement Native Interval Filtering and Custom Name Matching in Scrubber](adr/2026-08-04-implement-native-interval-filtering-and-custom-name-matching-in-scrubber.md) | `adr/2026-08-04-implement-native-interval-filtering-and-custom-name-matching-in-scrubber.md` |
| [ADR-2155: In Memory Terminology Search Cache](adr/2026-08-04-in-memory-terminology-search-cache.md) | `adr/2026-08-04-in-memory-terminology-search-cache.md` |
| [ADR-2332: Metadata-driven Additional Properties JSON column for ClinicalObservation](adr/2026-08-04-metadata-driven-additional-properties-json-column-for-clinicalobservation.md) | `adr/2026-08-04-metadata-driven-additional-properties-json-column-for-clinicalobservation.md` |
| [ADR-2333: Multi-Node Signature Replay Prevention with Redis Fallback](adr/2026-08-04-multi-node-signature-replay-prevention-with-redis-fallback.md) | `adr/2026-08-04-multi-node-signature-replay-prevention-with-redis-fallback.md` |
| [ADR-2334: Notification Delivery Channels and Retry Dispatcher](adr/2026-08-04-notification-delivery-channels-and-retry-dispatcher.md) | `adr/2026-08-04-notification-delivery-channels-and-retry-dispatcher.md` |
| [ADR-2335: Refactor security context decorator for Python 3.14 compatibility](adr/2026-08-04-refactor-security-context-decorator-for-python-314-compatibility.md) | `adr/2026-08-04-refactor-security-context-decorator-for-python-314-compatibility.md` |
| [ADR-2336: RSA-PSS Cryptographic Padding for Electronic Signatures](adr/2026-08-04-rsa-pss-cryptographic-padding-for-electronic-signatures.md) | `adr/2026-08-04-rsa-pss-cryptographic-padding-for-electronic-signatures.md` |
| [ADR-2337: Scope CSS hover to can hover and add touch tap fallbacks](adr/2026-08-04-scope-css-hover-to-can-hover-and-add-touch-tap-fallbacks.md) | `adr/2026-08-04-scope-css-hover-to-can-hover-and-add-touch-tap-fallbacks.md` |
| [ADR-2338: System-Wide Hexagonal Architecture and Pytest-Archon Enforcement](adr/2026-08-04-system-wide-hexagonal-architecture-and-pytest-archon-enforcement.md) | `adr/2026-08-04-system-wide-hexagonal-architecture-and-pytest-archon-enforcement.md` |
| [ADR-2339: Transition to PSS Padding for RSA Signatures and Verifications](adr/2026-08-04-transition-to-pss-padding-for-rsa-signatures-and-verifications.md) | `adr/2026-08-04-transition-to-pss-padding-for-rsa-signatures-and-verifications.md` |
| [ADR-2340: Viewport-Driven DOM Recycler for Large eCRFs](adr/2026-08-04-viewport-driven-dom-recycler-for-large-ecrfs.md) | `adr/2026-08-04-viewport-driven-dom-recycler-for-large-ecrfs.md` |
| [ADR-2341: Global Prettier Formatting and Pre-commit Checks](adr/2026-08-05-global-prettier-formatting-and-pre-commit-checks.md) | `adr/2026-08-05-global-prettier-formatting-and-pre-commit-checks.md` |
| [ADR-2159: PostgreSQL Migration for Regulatory Services](adr/2026-08-05-postgresql-migration-for-regulatory-services.md) | `adr/2026-08-05-postgresql-migration-for-regulatory-services.md` |
| [ADR-2342: Quality & CAPA Management, Platform Placement, and Traceability](adr/2026-08-05-quality-capa-management.md) | `adr/2026-08-05-quality-capa-management.md` |
| [ADR-2343: Arm-Aware Schedule of Activities (SoA) Matrix Component](adr/2026-08-06-arm-aware-soa-matrix.md) | `adr/2026-08-06-arm-aware-soa-matrix.md` |
| [ADR-2344: Global Monorepo Security Scanning and Fail-Fast Startup Assertions](adr/2026-08-06-monorepo-security-scanning-and-fail-fast-assertions.md) | `adr/2026-08-06-monorepo-security-scanning-and-fail-fast-assertions.md` |
| [ADR-2161: Parameterized Branding and Dynamic Domain Configurations](adr/2026-08-06-parameterized-branding-and-dynamic-domain-configurations.md) | `adr/2026-08-06-parameterized-branding-and-dynamic-domain-configurations.md` |
| [ADR-2160: Part 11 Electronic Signature Compliance Engine Improvements](adr/2026-08-06-part-11-electronic-signature-compliance-engine-improvements.md) | `adr/2026-08-06-part-11-electronic-signature-compliance-engine-improvements.md` |
| [ADR-2345: Purge Legacy Physical Lab Columns for Dynamic JSON Schema](adr/2026-08-06-purge-legacy-physical-lab-columns-for-dynamic-json-schema.md) | `adr/2026-08-06-purge-legacy-physical-lab-columns-for-dynamic-json-schema.md` |
| [ADR-2163: Decentralize Test Directory Structure Across Workspaces](adr/2026-08-07-decentralize-test-directory-structure-across-workspaces.md) | `adr/2026-08-07-decentralize-test-directory-structure-across-workspaces.md` |
| [ADR-116: ePRO Sync Durable Reconciliation, Defeated Record Retention, and Structural Queries](adr/2026-08-07-epro-sync-durable-reconciliation.md) | `adr/2026-08-07-epro-sync-durable-reconciliation.md` |
| [ADR-2164: Eradicate Core Models and Implement Anti-Corruption Layers](adr/2026-08-07-eradicate-core-models-and-implement-anti-corruption-layers.md) | `adr/2026-08-07-eradicate-core-models-and-implement-anti-corruption-layers.md` |
| [ADR-2346: Route terminology APIs through the gateway](adr/2026-08-07-gateway-terminology-routing.md) | `adr/2026-08-07-gateway-terminology-routing.md` |
| [ADR-2347: Organization Directory Service and Persistence Models Scaffold](adr/2026-08-07-organization-directory-service-foundation.md) | `adr/2026-08-07-organization-directory-service-foundation.md` |
| [ADR-2162: Skip to content link and strict layout validation](adr/2026-08-07-skip-to-content-link-and-strict-layout-validation.md) | `adr/2026-08-07-skip-to-content-link-and-strict-layout-validation.md` |
| [ADR-065: Biomedical Concept Locks for Active-Recruiting Studies](adr/2026-08-08-biomedical-concept-locks.md) | `adr/2026-08-08-biomedical-concept-locks.md` |
| [ADR-059: Centralized RBAC Toolkit, Permissions, Principals, and Field Masking](adr/2026-08-08-centralized-rbac-toolkit.md) | `adr/2026-08-08-centralized-rbac-toolkit.md` |
| [ADR-060: eCOA Portal and Interop Deployment Architecture](adr/2026-08-08-ecoa-portal-and-interop-deployment.md) | `adr/2026-08-08-ecoa-portal-and-interop-deployment.md` |
| [ADR-2348: Expose Authenticated SDTM/ADaM Dataset-JSON Export Endpoints](adr/2026-08-08-expose-authenticated-sdtm-adam-dataset-json-export-endpoints.md) | `adr/2026-08-08-expose-authenticated-sdtm-adam-dataset-json-export-endpoints.md` |
| [ADR-063: Gateway Step-Up Re-Authentication and Signature Token Issuance](adr/2026-08-08-gateway-step-up-re-authentication.md) | `adr/2026-08-08-gateway-step-up-re-authentication.md` |
| [ADR-2165: Hexagonal Architecture Standard](adr/2026-08-08-hexagonal-architecture-standard.md) | `adr/2026-08-08-hexagonal-architecture-standard.md` |
| [ADR-062: PI-only Atomic Batch Electronic Sign-Off in Execution](adr/2026-08-08-pi-only-batch-sign-off-execution.md) | `adr/2026-08-08-pi-only-batch-sign-off-execution.md` |
| [ADR-061: RTSM Supply Domain Persistence Models](adr/2026-08-08-rtsm-supply-domain-persistence.md) | `adr/2026-08-08-rtsm-supply-domain-persistence.md` |
| [ADR-2166: Standardize Semantic Token Layer Across Platforms](adr/2026-08-08-standardize-semantic-token-layer-across-platforms.md) | `adr/2026-08-08-standardize-semantic-token-layer-across-platforms.md` |
| [ADR-2349: Automated eTMF Document Redaction](adr/2026-08-09-automated-etmf-document-redaction.md) | `adr/2026-08-09-automated-etmf-document-redaction.md` |
| [ADR-2350: eISF Browse, RBAC, Downloads, and Completeness](adr/2026-08-09-eisf-maintenance-and-formatting.md) | `adr/2026-08-09-eisf-maintenance-and-formatting.md` |
| [ADR-2351: eTMF Filterable and Paginated Audit Log API](adr/2026-08-09-etmf-filterable-paginated-audit-log.md) | `adr/2026-08-09-etmf-filterable-paginated-audit-log.md` |
| [ADR-2352: Global Library Object Instantiation in Clinical Studies](adr/2026-08-09-global-library-object-instantiation.md) | `adr/2026-08-09-global-library-object-instantiation.md` |
| [ADR-2353: Medical Coding Engine and Query Subsystem Integration](adr/2026-08-09-medical-coding-engine-query-integration.md) | `adr/2026-08-09-medical-coding-engine-query-integration.md` |
| [ADR-067: Signed NCI Thesaurus CT Web Client](adr/2026-08-09-nci-thesaurus-signed-web-client.md) | `adr/2026-08-09-nci-thesaurus-signed-web-client.md` |
| [ADR-064: E2B(R3) ICSR XML Export and Structural Validation](adr/2026-08-09-safety-e2b-icsr-xml-export-pipeline.md) | `adr/2026-08-09-safety-e2b-icsr-xml-export-pipeline.md` |
| [ADR-2354: Debounced Clinical Code Lookup UI Primitive](adr/2026-08-10-debounced-clinical-code-lookup-ui-primitive.md) | `adr/2026-08-10-debounced-clinical-code-lookup-ui-primitive.md` |
| [ADR-185: Dynamic Dependency Matching and Clinical De-identification Consolidation](adr/2026-08-10-dynamic-dependency-matching-and-clinical-deid-consolidation.md) | `adr/2026-08-10-dynamic-dependency-matching-and-clinical-deid-consolidation.md` |
| [ADR-2167: AST-based Offline Schema Drift Detection and Babel Parser Dependency](adr/2026-08-11-ast-based-offline-schema-drift-detection.md) | `adr/2026-08-11-ast-based-offline-schema-drift-detection.md` |
| [ADR-086: Bidirectional Field Parity and RFC 7807 Validation Schemas](adr/2026-08-11-bidirectional-field-parity-and-rfc7807-validation-schemas.md) | `adr/2026-08-11-bidirectional-field-parity-and-rfc7807-validation-schemas.md` |
| [ADR-2355: Browser-Native Layout-Skipping for eCRF Virtualization](adr/2026-08-11-browser-native-layout-skipping-for-ecrf-virtualization.md) | `adr/2026-08-11-browser-native-layout-skipping-for-ecrf-virtualization.md` |
| [ADR-2356: Centralized Brand and Domain Validation Guardrail](adr/2026-08-11-centralized-brand-and-domain-validation.md) | `adr/2026-08-11-centralized-brand-and-domain-validation.md` |
| [ADR-118: Chronological Date Validation and Visit Date Constraints in SDTM Models](adr/2026-08-11-chronological-sdtm-date-validation.md) | `adr/2026-08-11-chronological-sdtm-date-validation.md` |
| [ADR-2357: Declarative Route Dependencies and Fallback Signature Verification](adr/2026-08-11-declarative-dependencies-and-signature-verification-fallback.md) | `adr/2026-08-11-declarative-dependencies-and-signature-verification-fallback.md` |
| [ADR-123: Decommissioning Client-Side TS Wrapper SDK and Enforcing Frontend Dependency Gates](adr/2026-08-11-decommissioning-client-side-ts-sdk-and-enforcing-dependency-gates.md) | `adr/2026-08-11-decommissioning-client-side-ts-sdk-and-enforcing-dependency-gates.md` |
| [ADR-122: Decouple Shared Test Signing RBAC Helpers from Gateway](adr/2026-08-11-decoupling-shared-test-signing-rbac-helpers-from-gateway.md) | `adr/2026-08-11-decoupling-shared-test-signing-rbac-helpers-from-gateway.md` |
| [ADR-2358: Gateway Signature Legacy V2 Fallback](adr/2026-08-11-gateway-signature-legacy-v2-fallback.md) | `adr/2026-08-11-gateway-signature-legacy-v2-fallback.md` |
| [ADR-2359: Key-Based Sync Reconciliation](adr/2026-08-11-key-based-sync-reconciliation.md) | `adr/2026-08-11-key-based-sync-reconciliation.md` |
| [ADR-2169: Offline and Synchronous OpenAPI Schema Generation for 15 Platform Services](adr/2026-08-11-offline-and-synchronous-openapi-schema-generation-for-15-platform-services.md) | `adr/2026-08-11-offline-and-synchronous-openapi-schema-generation-for-15-platform-services.md` |
| [ADR-2360: Persistent Local PIN Wrapper and Secure Offline IndexedDB Encryption](adr/2026-08-11-persistent-local-pin-wrapper-and-indexeddb-security.md) | `adr/2026-08-11-persistent-local-pin-wrapper-and-indexeddb-security.md` |
| [ADR-2361: Pool Connection State Eviction and Checkout Resets](adr/2026-08-11-pool-connection-state-eviction-and-checkout-resets.md) | `adr/2026-08-11-pool-connection-state-eviction-and-checkout-resets.md` |
| [ADR-2168: Refined Mock Signature Verification Base64 Collision Prevention](adr/2026-08-11-refine-asymmetric-mock-signature-detection-for-base64-collisions.md) | `adr/2026-08-11-refine-asymmetric-mock-signature-detection-for-base64-collisions.md` |
| [ADR-2362: Relational Outbox System across Execution and eTMF Services](adr/2026-08-11-relational-outbox-system-across-execution-and-etmf-services.md) | `adr/2026-08-11-relational-outbox-system-across-execution-and-etmf-services.md` |
| [ADR-252: Implement slot-driven dynamic accessibility context in Vue 3 SPA components](adr/2026-08-11-slot-driven-dynamic-accessibility-context.md) | `adr/2026-08-11-slot-driven-dynamic-accessibility-context.md` |
| [ADR-2363: Standardize Hatch build configurations for clean namespace wheels](adr/2026-08-11-standardize-hatch-build-configurations-for-clean-namespace-wheels.md) | `adr/2026-08-11-standardize-hatch-build-configurations-for-clean-namespace-wheels.md` |
| [ADR-2364: Standardize PR Templates and Centralize Shared Hashing & Radio Grid Layouts](adr/2026-08-11-standardize-pr-templates-and-centralize-hashing-grid-layout.md) | `adr/2026-08-11-standardize-pr-templates-and-centralize-hashing-grid-layout.md` |
| [ADR-068: Unified Parameterized Relational Database Lifespan Wrapper](adr/2026-08-11-unified-parameterized-relational-database-lifespan-wrapper.md) | `adr/2026-08-11-unified-parameterized-relational-database-lifespan-wrapper.md` |
| [ADR-2365: Unified Ports and Adapters Architecture, Transaction Propagation, and Gateway Connection Pooling](adr/2026-08-11-unified-ports-and-adapters-architecture-transaction-propagation-and-gateway-connection-pooling.md) | `adr/2026-08-11-unified-ports-and-adapters-architecture-transaction-propagation-and-gateway-connection-pooling.md` |
| [ADR-066: VitePress Workspace Documentation Portal](adr/2026-08-11-vitepress-workspace-documentation-portal.md) | `adr/2026-08-11-vitepress-workspace-documentation-portal.md` |
| [ADR-2171: Deconsolidate monolithic database models](adr/2026-08-12-deconsolidate-monolithic-database-models.md) | `adr/2026-08-12-deconsolidate-monolithic-database-models.md` |
| [ADR-2170: Decoupled PostgreSQL Schema Introspection and Database Triggers for GxP Compliance](adr/2026-08-12-decoupled-postgresql-schema-introspection-and-database-triggers-for-gxp-compliance.md) | `adr/2026-08-12-decoupled-postgresql-schema-introspection-and-database-triggers-for-gxp-compliance.md` |
| [ADR-2366: Dedicated Background Processing with PostgreSQL Database-level Coordination](adr/2026-08-12-dedicated-background-processing-with-postgresql-database-level-coordination.md) | `adr/2026-08-12-dedicated-background-processing-with-postgresql-database-level-coordination.md` |
| [ADR-094: Deterministic GxP Report Generation and Signature Verification Rectification](adr/2026-08-12-deterministic-gxp-report-generation-and-signature-verification.md) | `adr/2026-08-12-deterministic-gxp-report-generation-and-signature-verification.md` |
| [ADR-192: Dynamic CDASH type generation and strongly-typed clinical Vue components](adr/2026-08-12-dynamic-cdash-type-generation-and-strongly-typed-clinical-vue-components.md) | `adr/2026-08-12-dynamic-cdash-type-generation-and-strongly-typed-clinical-vue-components.md` |
| [ADR-2367: NestJS API Gateway Promise Coalescing and Eager Key Prefetching](adr/2026-08-12-nestjs-api-gateway-promise-coalescing.md) | `adr/2026-08-12-nestjs-api-gateway-promise-coalescing.md` |
| [ADR-2368: Python-Native Isolated Architecture (Strict Compliance)](adr/2026-08-12-python-native-isolated-architecture-strict-compliance.md) | `adr/2026-08-12-python-native-isolated-architecture-strict-compliance.md` |
| [ADR-2369: Secure Unblinding Signature Fallback Restriction & Scope Normalization](adr/2026-08-12-secure-unblinding-signature-fallback-restriction.md) | `adr/2026-08-12-secure-unblinding-signature-fallback-restriction.md` |
| [ADR-2370: Separate Monolithic Clinical Workflows into Modular Sub-routers](adr/2026-08-12-separate-monolithic-clinical-workflows-into-modular-sub-routers.md) | `adr/2026-08-12-separate-monolithic-clinical-workflows-into-modular-sub-routers.md` |
| [ADR-2371: Unified Cryptographic Fingerprint Verification API](adr/2026-08-12-unified-cryptographic-fingerprint-verification-api.md) | `adr/2026-08-12-unified-cryptographic-fingerprint-verification-api.md` |
| [ADR-2174: AI-Native USDM Protocol Digitization and Automated eCRF Synthesis](adr/2026-08-13-ai-native-usdm-protocol-digitization-and-automated-ecrf-synthesis.md) | `adr/2026-08-13-ai-native-usdm-protocol-digitization-and-automated-ecrf-synthesis.md` |
| [ADR-2372: Decouple ClinicalQueryPanel from Pinia and Auth store](adr/2026-08-13-decouple-clinical-query-panel.md) | `adr/2026-08-13-decouple-clinical-query-panel.md` |
| [ADR-2373: Port Contract Compliance & Backend Static Verification](adr/2026-08-13-port-contract-compliance-and-backend-static-verification.md) | `adr/2026-08-13-port-contract-compliance-and-backend-static-verification.md` |
| [ADR-2172: Standardize on Plural Adapters with Build-time Path Linting](adr/2026-08-13-standardize-on-plural-adapters-with-build-time-path-linting.md) | `adr/2026-08-13-standardize-on-plural-adapters-with-build-time-path-linting.md` |
| [ADR-2374: Standalone In-Application Ticketing Service and Platform Integration](adr/2026-08-13-tickets-service-scaffold-and-gateway-integration.md) | `adr/2026-08-13-tickets-service-scaffold-and-gateway-integration.md` |
| [ADR-2375: Write-Time Soft-Versioning and Active Inactive Cloning for eCRF Form Submissions](adr/2026-08-13-write-time-soft-versioning-and-active-inactive-cloning-for-ecrf-form-submissions.md) | `adr/2026-08-13-write-time-soft-versioning-and-active-inactive-cloning-for-ecrf-form-submissions.md` |
| [ADR-2376: Biostatistical Export Pipeline Interoperability and Architecture](adr/2026-08-14-biostatistical-export-pipeline-interoperability.md) | `adr/2026-08-14-biostatistical-export-pipeline-interoperability.md` |
| [ADR-2179: Comprehensive Hexagonal Architecture Standardization and Frontend Modularization](adr/2026-08-14-comprehensive-hexagonal-architecture-standardization-and-frontend-modularization.md) | `adr/2026-08-14-comprehensive-hexagonal-architecture-standardization-and-frontend-modularization.md` |
| [ADR-071: GitHub Pages Documentation Hosting](adr/2026-08-14-github-pages-documentation-hosting.md) | `adr/2026-08-14-github-pages-documentation-hosting.md` |
| [ADR-2175: Graph-Native Zero-Downtime Protocol Amendment and In-Flight Subject Migration Engine](adr/2026-08-14-graph-native-zero-downtime-protocol-amendment-and-in-flight-subject-migration-engine.md) | `adr/2026-08-14-graph-native-zero-downtime-protocol-amendment-and-in-flight-subject-migration-engine.md` |
| [ADR-2176: Isolated Concurrent Pytest Harness and Worker Lifecycle](adr/2026-08-14-isolated-concurrent-pytest-harness-and-worker-lifecycle.md) | `adr/2026-08-14-isolated-concurrent-pytest-harness-and-worker-lifecycle.md` |
| [ADR-2173: Offline TypeScript Schema Generator using SQLModel Base Metadata](adr/2026-08-14-offline-typescript-schema-generator-using-sqlmodel-base-metadata.md) | `adr/2026-08-14-offline-typescript-schema-generator-using-sqlmodel-base-metadata.md` |
| [ADR-2377: Pluggable Relational SQLModel Database Store for Security Audit Logs](adr/2026-08-14-pluggable-relational-sqlmodel-database-store-for-security-audit-logs.md) | `adr/2026-08-14-pluggable-relational-sqlmodel-database-store-for-security-audit-logs.md` |
| [ADR-2177: Standardized Hexagonal Architecture and Unified Developer CLI](adr/2026-08-14-standardized-hexagonal-architecture-and-unified-developer-cli.md) | `adr/2026-08-14-standardized-hexagonal-architecture-and-unified-developer-cli.md` |
| [ADR-2178: Standardized Hexagonal Architecture and Unified Developer Experience Suite](adr/2026-08-14-standardized-hexagonal-architecture-and-unified-developer-experience-suite.md) | `adr/2026-08-14-standardized-hexagonal-architecture-and-unified-developer-experience-suite.md` |
| [ADR-2378: Bidirectional API Contract Enforcement and Legacy Whitelisting](adr/2026-08-15-bidirectional-api-contract-enforcement.md) | `adr/2026-08-15-bidirectional-api-contract-enforcement.md` |
| [ADR-2183: Comprehensive Clinical Issue and Operations Hub Architecture](adr/2026-08-15-comprehensive-clinical-issue-and-operations-hub-architecture.md) | `adr/2026-08-15-comprehensive-clinical-issue-and-operations-hub-architecture.md` |
| [ADR-2181: Comprehensive Clinical Quality and RBQM eQMS Architecture](adr/2026-08-15-comprehensive-clinical-quality-and-rbqm-eqms-architecture.md) | `adr/2026-08-15-comprehensive-clinical-quality-and-rbqm-eqms-architecture.md` |
| [ADR-2180: Comprehensive CTMS Modular Architecture and Ecosystem Synchronization](adr/2026-08-15-comprehensive-ctms-modular-architecture-and-ecosystem-synchronization.md) | `adr/2026-08-15-comprehensive-ctms-modular-architecture-and-ecosystem-synchronization.md` |
| [ADR-2182: Comprehensive Enterprise eConsent Architecture and Multi-Party Signature Pipeline](adr/2026-08-15-comprehensive-enterprise-econsent-architecture-and-multi-party-signature-pipeline.md) | `adr/2026-08-15-comprehensive-enterprise-econsent-architecture-and-multi-party-signature-pipeline.md` |
| [ADR-2379: Parallel Continuous Integration Workflows and Local Concurrent Verification](adr/2026-08-16-parallel-ci-workflows-and-local-concurrent-execution.md) | `adr/2026-08-16-parallel-ci-workflows-and-local-concurrent-execution.md` |
| [ADR-2380: Centralized Permission-Based Authorization and Dynamic Mappings](adr/2026-08-17-centralized-permission-auth.md) | `adr/2026-08-17-centralized-permission-auth.md` |
| [ADR-2381: Fast Version-Check with Immediate Quarantine and Client Alerting](adr/2026-08-17-fast-version-check-with-immediate-quarantine-and-client-alerting.md) | `adr/2026-08-17-fast-version-check-with-immediate-quarantine-and-client-alerting.md` |
| [ADR-2382: Frontend Standardization, CSS Grid Layouts, and Centralized UI Utilities](adr/2026-08-17-frontend-standardization-css-grid-and-centralized-utilities.md) | `adr/2026-08-17-frontend-standardization-css-grid-and-centralized-utilities.md` |
| [ADR-2383: Interactive Mermaid Diagrams and Responsive Layout Controls](adr/2026-08-17-interactive-mermaid-diagrams-and-zoom-pan-controls.md) | `adr/2026-08-17-interactive-mermaid-diagrams-and-zoom-pan-controls.md` |
| [ADR-2384: Standardized PostgreSQL Write Verification and eTMF Immutability Triggers](adr/2026-08-17-standardized-postgresql-write-verification-and-etmf-immutability-triggers.md) | `adr/2026-08-17-standardized-postgresql-write-verification-and-etmf-immutability-triggers.md` |
| [ADR-2385: Unified Database Schema Introspection and Automated Type Synchronization Gating](adr/2026-08-17-unified-database-introspection-and-type-gating.md) | `adr/2026-08-17-unified-database-introspection-and-type-gating.md` |
| [ADR-2386: Unified De-identification Compliance Scan CLI, Pre-commit Hook, and Blocking CI Gate](adr/2026-08-17-unified-deid-compliance-scan-cli.md) | `adr/2026-08-17-unified-deid-compliance-scan-cli.md` |
| [ADR-098: Document Redaction Architecture, Regulatory Data-Handling, and Compliance Profiles](adr/2026-08-18-document-redaction-architecture.md) | `adr/2026-08-18-document-redaction-architecture.md` |
| [ADR-2184: Localize datetime helpers in security package](adr/2026-08-18-localize-datetime-helpers-in-security-package.md) | `adr/2026-08-18-localize-datetime-helpers-in-security-package.md` |
| [ADR-2387: Metadata-Bound Filtering and Consent Abstraction for Transaction Auditing](adr/2026-08-18-metadata-bound-filtering-and-consent-abstraction-for-transaction-auditing.md) | `adr/2026-08-18-metadata-bound-filtering-and-consent-abstraction-for-transaction-auditing.md` |
| [ADR-2185: Dynamic Subject Enrollment and eCRF Visit Execution with Real Time Edit Checks](adr/2026-08-19-dynamic-subject-enrollment-and-ecrf-visit-execution-with-real-time-edit-checks.md) | `adr/2026-08-19-dynamic-subject-enrollment-and-ecrf-visit-execution-with-real-time-edit-checks.md` |
| [ADR-111: Structured Expiration Metadata and Migration Runners for eTMF/eISF](adr/2026-08-19-etmf-eisf-structured-expiration-metadata-and-migrations.md) | `adr/2026-08-19-etmf-eisf-structured-expiration-metadata-and-migrations.md` |
| [ADR-099: Global Library Architecture, Lifecycle, and Multi-Tenant Isolation](adr/2026-08-19-global-library-architecture-lifecycle-tenancy.md) | `adr/2026-08-19-global-library-architecture-lifecycle-tenancy.md` |
| [ADR-105: Codebase Cleanup, Test Pipeline Fixes, and Documentation Preflight Standardization](adr/2026-08-20-codebase-cleanup-and-test-pipeline-fixes.md) | `adr/2026-08-20-codebase-cleanup-and-test-pipeline-fixes.md` |
| [ADR-2388: Targeted E2E Contrast Enforcement and Style Token Upgrades](adr/2026-08-20-targeted-e2e-contrast-enforcement-and-style-token-upgrades.md) | `adr/2026-08-20-targeted-e2e-contrast-enforcement-and-style-token-upgrades.md` |
| [ADR-2191: AI Gateway Microservice and Three-Tier Clinical Intelligence Architecture](adr/2026-08-21-ai-gateway-microservice-and-three-tier-clinical-intelligence-architecture.md) | `adr/2026-08-21-ai-gateway-microservice-and-three-tier-clinical-intelligence-architecture.md` |
| [ADR-2195: Asynchronous Cross-Domain eCRF Anomaly Detection Worker](adr/2026-08-21-asynchronous-cross-domain-ecrf-anomaly-detection-worker.md) | `adr/2026-08-21-asynchronous-cross-domain-ecrf-anomaly-detection-worker.md` |
| [ADR-2194: Asynchronous Protocol Digitization Stage DAG with USDM Compilation](adr/2026-08-21-asynchronous-protocol-digitization-stage-dag-with-usdm-compilation.md) | `adr/2026-08-21-asynchronous-protocol-digitization-stage-dag-with-usdm-compilation.md` |
| [ADR-2189: Comprehensive Developer Tooling Native MCP Server and Staged Guardrails](adr/2026-08-21-comprehensive-developer-tooling-native-mcp-server-and-staged-guardrails.md) | `adr/2026-08-21-comprehensive-developer-tooling-native-mcp-server-and-staged-guardrails.md` |
| [ADR-2190: Comprehensive Developer Tooling Overhaul](adr/2026-08-21-comprehensive-developer-tooling-overhaul.md) | `adr/2026-08-21-comprehensive-developer-tooling-overhaul.md` |
| [ADR-110: eTMF Inbound-Email Webhook and Binder Routing](adr/2026-08-21-etmf-inbound-email-webhook-and-binder-routing.md) | `adr/2026-08-21-etmf-inbound-email-webhook-and-binder-routing.md` |
| [ADR-2196: Generative Pharmacovigilance Safety Narratives with Part 11 E-Signature](adr/2026-08-21-generative-pharmacovigilance-safety-narratives-with-part-11-e-signature.md) | `adr/2026-08-21-generative-pharmacovigilance-safety-narratives-with-part-11-e-signature.md` |
| [ADR-2192: Grounded Protocol Knowledge Hub and Support Ticket RAG Triage](adr/2026-08-21-grounded-protocol-knowledge-hub-and-support-ticket-rag-triage.md) | `adr/2026-08-21-grounded-protocol-knowledge-hub-and-support-ticket-rag-triage.md` |
| [ADR-2197: Hybrid Semantic Medical Coding Pipeline with pgvector](adr/2026-08-21-hybrid-semantic-medical-coding-pipeline-with-pgvector.md) | `adr/2026-08-21-hybrid-semantic-medical-coding-pipeline-with-pgvector.md` |
| [ADR-2187: Knowledge Hub Multi-Channel Notifications and Preferences Architecture](adr/2026-08-21-knowledge-hub-multi-channel-notifications-and-preferences-architecture.md) | `adr/2026-08-21-knowledge-hub-multi-channel-notifications-and-preferences-architecture.md` |
| [ADR-2188: Knowledge Hub Two-Tier Immutable Data Model](adr/2026-08-21-knowledge-hub-two-tier-immutable-data-model.md) | `adr/2026-08-21-knowledge-hub-two-tier-immutable-data-model.md` |
| [ADR-2193: Multimodal eTMF and eISF Document Intelligence and DIA Reference Model Classifier](adr/2026-08-21-multimodal-etmf-and-eisf-document-intelligence-and-dia-reference-model-classifier.md) | `adr/2026-08-21-multimodal-etmf-and-eisf-document-intelligence-and-dia-reference-model-classifier.md` |
| [ADR-2186: Object Storage Port and Envelope Security Architecture](adr/2026-08-21-object-storage-port-and-envelope-security-architecture.md) | `adr/2026-08-21-object-storage-port-and-envelope-security-architecture.md` |
| [ADR-2389: Shared AES-GCM Offline At-Rest Encryption and Key Derivation Contract](adr/2026-08-22-aes-gcm-offline-at-rest-encryption.md) | `adr/2026-08-22-aes-gcm-offline-at-rest-encryption.md` |
| [ADR-2200: Automated Site Financials Fee Matrix and Real-Time Payable Engine](adr/2026-08-22-automated-site-financials-fee-matrix-and-real-time-payable-engine.md) | `adr/2026-08-22-automated-site-financials-fee-matrix-and-real-time-payable-engine.md` |
| [ADR-2202: Country-Aware Greenlight Site Activation and Execution Gating Engine](adr/2026-08-22-country-aware-greenlight-site-activation-and-execution-gating-engine.md) | `adr/2026-08-22-country-aware-greenlight-site-activation-and-execution-gating-engine.md` |
| [ADR-2204: Cumulative Mean Kinetic Temperature Stability Budget Engine](adr/2026-08-22-cumulative-mean-kinetic-temperature-stability-budget-engine.md) | `adr/2026-08-22-cumulative-mean-kinetic-temperature-stability-budget-engine.md` |
| [ADR-2203: Hermetic Containerized Statistical Computing Environment and Role-Gated Unblinded Data Vault](adr/2026-08-22-hermetic-containerized-statistical-computing-environment-and-role-gated-unblinded-data-vault.md) | `adr/2026-08-22-hermetic-containerized-statistical-computing-environment-and-role-gated-unblinded-data-vault.md` |
| [ADR-2390: Hybrid FHIR-to-CDISC Semantic Interoperability Mapper](adr/2026-08-22-hybrid-fhir-to-cdisc-semantic-interoperability-mapper.md) | `adr/2026-08-22-hybrid-fhir-to-cdisc-semantic-interoperability-mapper.md` |
| [ADR-2391: Shared Semantic Regulated-Action Contract for Step-Up Re-Authentication](adr/2026-08-22-move-to-shared-semantic-regulated-action-contract.md) | `adr/2026-08-22-move-to-shared-semantic-regulated-action-contract.md` |
| [ADR-2201: Reactive Cross-App Clinical Event Spine and Transactional Outbox](adr/2026-08-22-reactive-cross-app-clinical-event-spine-and-transactional-outbox.md) | `adr/2026-08-22-reactive-cross-app-clinical-event-spine-and-transactional-outbox.md` |
| [ADR-2198: Self-Hosted CI Runner and Trunk-Based Branching Lifecycle](adr/2026-08-22-self-hosted-ci-runner-and-trunk-based-branching-lifecycle.md) | `adr/2026-08-22-self-hosted-ci-runner-and-trunk-based-branching-lifecycle.md` |
| [ADR-2199: Sovereign All-in-One eClinical Suite Expansion](adr/2026-08-22-sovereign-all-in-one-eclinical-suite-expansion.md) | `adr/2026-08-22-sovereign-all-in-one-eclinical-suite-expansion.md` |
| [ADR-2392: Automated Aggregate Safety Compiler (DSUR / PSUR / PBRER) and Exposure Calculator](adr/2026-08-23-automated-aggregate-safety-compiler-and-exposure-calculator.md) | `adr/2026-08-23-automated-aggregate-safety-compiler-and-exposure-calculator.md` |
| [ADR-2210: Automated Golden Path Video Recording and GDrive Sync](adr/2026-08-23-automated-golden-path-video-recording-and-gdrive-sync.md) | `adr/2026-08-23-automated-golden-path-video-recording-and-gdrive-sync.md` |
| [ADR-2213: Autonomous Cross-Domain Quality Surveillance Runner Architecture](adr/2026-08-23-autonomous-cross-domain-quality-surveillance-runner-architecture.md) | `adr/2026-08-23-autonomous-cross-domain-quality-surveillance-runner-architecture.md` |
| [ADR-2206: Cell & Gene Therapy Vein-to-Vein Chain of Custody and Identity](adr/2026-08-23-cell-and-gene-therapy-vein-to-vein-chain-of-custody-and-identity.md) | `adr/2026-08-23-cell-and-gene-therapy-vein-to-vein-chain-of-custody-and-identity.md` |
| [ADR-2393: Clinical Endpoint Adjudication Committee (CEC) Dossier & 2+1 Blinded Review Suite](adr/2026-08-23-clinical-endpoint-adjudication-committee-cec-dossier-and-2plus1-blinded-review-suite.md) | `adr/2026-08-23-clinical-endpoint-adjudication-committee-cec-dossier-and-2plus1-blinded-review-suite.md` |
| [ADR-2212: DDF Compiler Pipeline Architecture](adr/2026-08-23-ddf-compiler-pipeline-architecture.md) | `adr/2026-08-23-ddf-compiler-pipeline-architecture.md` |
| [ADR-2215: Decompose Payments Domain Modules](adr/2026-08-23-decompose-payments-domain-modules.md) | `adr/2026-08-23-decompose-payments-domain-modules.md` |
| [ADR-2211: Deep Adjudication Workflow Seam](adr/2026-08-23-deep-adjudication-workflow-seam.md) | `adr/2026-08-23-deep-adjudication-workflow-seam.md` |
| [ADR-2214: Deep Regulatory Transmission Gateway and Multi-Tier Acknowledgement Seam](adr/2026-08-23-deep-regulatory-transmission-gateway-and-multi-tier-acknowledgement-seam.md) | `adr/2026-08-23-deep-regulatory-transmission-gateway-and-multi-tier-acknowledgement-seam.md` |
| [ADR-2216: Deepen CDISC Study Submission Compiler](adr/2026-08-23-deepen-cdisc-study-submission-compiler.md) | `adr/2026-08-23-deepen-cdisc-study-submission-compiler.md` |
| [ADR-2394: Delegation of Authority Log Archive Destination and Taxonomy Mapping Decisions](adr/2026-08-23-doa-archive-destination-and-taxonomy.md) | `adr/2026-08-23-doa-archive-destination-and-taxonomy.md` |
| [ADR-2207: GxP Telehealth Video Consultation, Direct-to-Patient Logistics, and Tiered Pediatric Assent](adr/2026-08-23-gxp-telehealth-video-consultation-direct-to-patient-logistics-and-tiered-pediatric-assent.md) | `adr/2026-08-23-gxp-telehealth-video-consultation-direct-to-patient-logistics-and-tiered-pediatric-assent.md` |
| [ADR-2209: Medical Device and IVD Investigation Suite with UDI and Malfunction Quarantine](adr/2026-08-23-medical-device-and-ivd-investigation-suite-with-udi-and-malfunction-quarantine.md) | `adr/2026-08-23-medical-device-and-ivd-investigation-suite-with-udi-and-malfunction-quarantine.md` |
| [ADR-2205: Multi-Echelon RTSM Supply Hierarchy and Predictive Resupply](adr/2026-08-23-multi-echelon-rtsm-supply-hierarchy-and-predictive-resupply.md) | `adr/2026-08-23-multi-echelon-rtsm-supply-hierarchy-and-predictive-resupply.md` |
| [ADR-2208: Participant Financial Wallet and EU CTR Lay Summary Portal](adr/2026-08-23-participant-financial-wallet-and-eu-ctr-lay-summary-portal.md) | `adr/2026-08-23-participant-financial-wallet-and-eu-ctr-lay-summary-portal.md` |
| [ADR-2395: SMART on FHIR Ingestion Pipeline & CRC Source Data Reconciliation Drawer](adr/2026-08-23-smart-backend-services-and-crc-reconciliation-drawer.md) | `adr/2026-08-23-smart-backend-services-and-crc-reconciliation-drawer.md` |
| [ADR-2396: Ticket Read Audit Policy and Paginated Audit Log Retrieval](adr/2026-08-23-ticket-read-audit-policy-and-pagination.md) | `adr/2026-08-23-ticket-read-audit-policy-and-pagination.md` |
| [ADR-100: Tickets-to-Notifications Integration Architecture](adr/2026-08-23-tickets-notifications-integration.md) | `adr/2026-08-23-tickets-notifications-integration.md` |
| [ADR-2397: Antigravity GxP Lifecycle Hooks and AI Coding Guardrails](adr/2026-08-24-antigravity-gxp-lifecycle-hooks-and-agent-guardrails.md) | `adr/2026-08-24-antigravity-gxp-lifecycle-hooks-and-agent-guardrails.md` |
| [ADR-2218: ClinicalIssueHub Module-Based Routing and Dual-Target SLAs](adr/2026-08-24-clinicalissuehub-module-based-routing-and-dual-target-slas.md) | `adr/2026-08-24-clinicalissuehub-module-based-routing-and-dual-target-slas.md` |
| [ADR-2398: Domain-Specific MCP Tool Extensions, Graph Introspection, and Autonomous Agent Sidecars](adr/2026-08-24-domain-mcp-tools-graph-introspection-and-agent-sidecars.md) | `adr/2026-08-24-domain-mcp-tools-graph-introspection-and-agent-sidecars.md` |
| [ADR-2217: Dynamic Consent Engine Re-Consent Gating and Lifecycle](adr/2026-08-24-dynamic-consent-engine-re-consent-gating-and-lifecycle.md) | `adr/2026-08-24-dynamic-consent-engine-re-consent-gating-and-lifecycle.md` |
| [ADR-101: Role Authorization for Granular Data-Lock Actions and Trust Boundary Verification](adr/2026-08-24-role-authorization-for-granular-data-lock-actions.md) | `adr/2026-08-24-role-authorization-for-granular-data-lock-actions.md` |
| [ADR-2399: Support Tickets SLA/Overdue Escalation and Notification De-Duplication](adr/2026-08-24-tickets-escalation-and-deduplication.md) | `adr/2026-08-24-tickets-escalation-and-deduplication.md` |
| [ADR-2400: Autonomous Multi Criteria Cohort Reasoning and Synthetic Feasibility Engine](adr/2026-08-25-autonomous-multi-criteria-cohort-reasoning-and-synthetic-feasibility-engine.md) | `adr/2026-08-25-autonomous-multi-criteria-cohort-reasoning-and-synthetic-feasibility-engine.md` |
| [ADR-2401: CTMS Offline Sync Reconciliation and Structural Clinical Queries](adr/2026-08-25-ctms-offline-sync-reconciliation-and-structural-clinical-queries.md) | `adr/2026-08-25-ctms-offline-sync-reconciliation-and-structural-clinical-queries.md` |
| [ADR-2402: Hybrid Multi-Step Clinical Reasoning Core and Protocol Grounding Gate](adr/2026-08-25-hybrid-multi-step-clinical-reasoning-core-and-protocol-grounding-gate.md) | `adr/2026-08-25-hybrid-multi-step-clinical-reasoning-core-and-protocol-grounding-gate.md` |
| [ADR-2403: Standardize Microservice Domain Exceptions RFC 7807 Handlers and Repository Layer](adr/2026-08-25-standardize-microservice-domain-exceptions-rfc-7807-handlers-and-repository-layer.md) | `adr/2026-08-25-standardize-microservice-domain-exceptions-rfc-7807-handlers-and-repository-layer.md` |
| [ADR-2404: Targeted Schema Provenance and Post Synthesis Faithfulness Scoring](adr/2026-08-25-targeted-schema-provenance-and-post-synthesis-faithfulness-scoring.md) | `adr/2026-08-25-targeted-schema-provenance-and-post-synthesis-faithfulness-scoring.md` |
| [ADR-115: Durable Dictionary-Import Worker and Recovery Contract](adr/2026-08-26-durable-dictionary-import-worker.md) | `adr/2026-08-26-durable-dictionary-import-worker.md` |
| [ADR-2405: Durable and Idempotent eTMF Expiration Scanning Service](adr/2026-08-26-durable-idempotent-etmf-expiration-scanner.md) | `adr/2026-08-26-durable-idempotent-etmf-expiration-scanner.md` |
| [ADR-2406: eISF Gateway Integration, Docker-Compose Wiring, and eTMF Sync Topology](adr/2026-08-26-eisf-gateway-compose-and-etmf-sync-topology.md) | `adr/2026-08-26-eisf-gateway-compose-and-etmf-sync-topology.md` |
| [ADR-2407: eSignature-Backed Delegation of Authority (DOA) Log and Task Delegation Service](adr/2026-08-26-esignature-backed-doa-task-delegation-service.md) | `adr/2026-08-26-esignature-backed-doa-task-delegation-service.md` |
| [ADR-2408: GxP Compliance Change Request Workflow](adr/2026-08-26-gxp-compliance-change-request-workflow.md) | `adr/2026-08-26-gxp-compliance-change-request-workflow.md` |
| [ADR-2409: SAE Reconciliation Architecture](adr/2026-08-26-sae-reconciliation-architecture.md) | `adr/2026-08-26-sae-reconciliation-architecture.md` |
| [ADR-2410: SDTM/ADaM Export Privacy: Deterministic Pseudonymization and Date De-identification](adr/2026-08-26-sdtm-adam-export-privacy.md) | `adr/2026-08-26-sdtm-adam-export-privacy.md` |
| [ADR-2411: Site-Aware Synchronized Document Deduplication](adr/2026-08-26-site-aware-synchronized-document-deduplication.md) | `adr/2026-08-26-site-aware-synchronized-document-deduplication.md` |
| [ADR-2412: Add SQLModel Dependency for eISF Structured Section Taxonomy and Versioning](adr/2026-08-27-add-sqlmodel-dependency-for-eisf-taxonomy-and-versioning.md) | `adr/2026-08-27-add-sqlmodel-dependency-for-eisf-taxonomy-and-versioning.md` |
| [ADR-2413: CDASH-to-SDTM Domain Mapping & Mapped Record Database Persistence](adr/2026-08-27-cdash-to-sdtm-domain-mapping-and-persistence.md) | `adr/2026-08-27-cdash-to-sdtm-domain-mapping-and-persistence.md` |
| [ADR-2414: Exposing Document Storage and Study Archival REST Endpoints](adr/2026-08-27-document-management-and-study-archival-endpoints.md) | `adr/2026-08-27-document-management-and-study-archival-endpoints.md` |
| [ADR-2415: eTMF Expiration Alert Notification Dispatch and Recipient Routing](adr/2026-08-27-etmf-expiration-notifications-dispatch.md) | `adr/2026-08-27-etmf-expiration-notifications-dispatch.md` |
| [ADR-2416: Offline Data Ingestion & Sync Engine](adr/2026-08-27-offline-data-reconciliation-integrity.md) | `adr/2026-08-27-offline-data-reconciliation-integrity.md` |
| [ADR-2417: 21 CFR Part 11 Batch Electronic Signatures for PI Casebook Sign-Off](adr/2026-08-27-prd-sys-001-compliance-batch-e-signatures.md) | `adr/2026-08-27-prd-sys-001-compliance-batch-e-signatures.md` |
| [ADR-2231: AI-Enhanced CDISC-Native Visual Metadata IDE and Tripartite eCRF Designer Standard](adr/2026-08-28-ai-enhanced-cdisc-native-visual-metadata-ide-and-tripartite-ecrf-designer-standard.md) | `adr/2026-08-28-ai-enhanced-cdisc-native-visual-metadata-ide-and-tripartite-ecrf-designer-standard.md` |
| [ADR-2219: Antigravity Workspace Plugin and Skill Architecture](adr/2026-08-28-antigravity-workspace-plugin-and-skill-architecture.md) | `adr/2026-08-28-antigravity-workspace-plugin-and-skill-architecture.md` |
| [ADR-2418: Centralized RBAC Matrix for Metadata Designer Mutations](adr/2026-08-28-centralized-rbac-matrix-for-metadata-designer-mutations.md) | `adr/2026-08-28-centralized-rbac-matrix-for-metadata-designer-mutations.md` |
| [ADR-2227: Decoupled Persona Composable and Capability Pattern for eClinical Workspaces](adr/2026-08-28-decoupled-persona-composable-and-capability-pattern-for-eclinical-workspaces.md) | `adr/2026-08-28-decoupled-persona-composable-and-capability-pattern-for-eclinical-workspaces.md` |
| [ADR-2221: Discrepancy Event Outbox Clinical Issue Hub and Multi Channel Notifications](adr/2026-08-28-discrepancy-event-outbox-clinical-issue-hub-and-multi-channel-notifications.md) | `adr/2026-08-28-discrepancy-event-outbox-clinical-issue-hub-and-multi-channel-notifications.md` |
| [ADR-2154: Dynamic Hover Pointer Capability Detection & Touch-Safe Glossary Fallbacks](adr/2026-08-28-dynamic-hover-detection-and-touch-safe-glossary-fallbacks.md) | `adr/2026-08-28-dynamic-hover-detection-and-touch-safe-glossary-fallbacks.md` |
| [ADR-119: Extend Centralized Permission Matrix for Clinical Execution](adr/2026-08-28-extend-centralized-permission-matrix-for-execution.md) | `adr/2026-08-28-extend-centralized-permission-matrix-for-execution.md` |
| [ADR-2225: Gateway Proxy Dispatch Routing and RFC 7807 Error Boundaries](adr/2026-08-28-gateway-proxy-dispatch-routing-and-rfc-7807-error-boundaries.md) | `adr/2026-08-28-gateway-proxy-dispatch-routing-and-rfc-7807-error-boundaries.md` |
| [ADR-2229: Hybrid CQRS Unit of Work and Dual-Defense GxP Audit Architecture](adr/2026-08-28-hybrid-cqrs-unit-of-work-and-dual-defense-gxp-audit-architecture.md) | `adr/2026-08-28-hybrid-cqrs-unit-of-work-and-dual-defense-gxp-audit-architecture.md` |
| [ADR-2228: Metadata-Driven Engine Architecture and Regulated Domain DSL Standard](adr/2026-08-28-metadata-driven-engine-architecture-and-regulated-domain-dsl-standard.md) | `adr/2026-08-28-metadata-driven-engine-architecture-and-regulated-domain-dsl-standard.md` |
| [ADR-2224: Multi Persona Golden Path Suite Video Capture and RTM Synchronization](adr/2026-08-28-multi-persona-golden-path-suite-video-capture-and-rtm-synchronization.md) | `adr/2026-08-28-multi-persona-golden-path-suite-video-capture-and-rtm-synchronization.md` |
| [ADR-2226: Programmatic VitePress API Documentation Engine](adr/2026-08-28-programmatic-vitepress-api-documentation-engine.md) | `adr/2026-08-28-programmatic-vitepress-api-documentation-engine.md` |
| [ADR-2222: Real Time Payable Ledger Holdbacks and Patient Compensation Wallet](adr/2026-08-28-real-time-payable-ledger-holdbacks-and-patient-compensation-wallet.md) | `adr/2026-08-28-real-time-payable-ledger-holdbacks-and-patient-compensation-wallet.md` |
| [ADR-2419: Reference Range Data Model & Persistence](adr/2026-08-28-reference-range-data-model-and-persistence.md) | `adr/2026-08-28-reference-range-data-model-and-persistence.md` |
| [ADR-2230: RTSM Stochastic Randomization and Predictive Supply Chain Engine Standard](adr/2026-08-28-rtsm-stochastic-randomization-and-predictive-supply-chain-engine-standard.md) | `adr/2026-08-28-rtsm-stochastic-randomization-and-predictive-supply-chain-engine-standard.md` |
| [ADR-2420: SDV Sign-Off Workflow & Automatic Verification Drop](adr/2026-08-28-sdv-sign-off-workflow-and-automatic-verification-drop.md) | `adr/2026-08-28-sdv-sign-off-workflow-and-automatic-verification-drop.md` |
| [ADR-2421: Shared Domain Vocabulary and Authorization Foundation](adr/2026-08-28-shared-domain-vocabulary-and-authorization-foundation.md) | `adr/2026-08-28-shared-domain-vocabulary-and-authorization-foundation.md` |
| [ADR-129: Shared eSignature Foundations](adr/2026-08-28-shared-esignature-foundations.md) | `adr/2026-08-28-shared-esignature-foundations.md` |
| [ADR-2220: Site Greenlight Governance Regulatory Binders and DOA Signing](adr/2026-08-28-site-greenlight-governance-regulatory-binders-and-doa-signing.md) | `adr/2026-08-28-site-greenlight-governance-regulatory-binders-and-doa-signing.md` |
| [ADR-2223: Specialized Science Safety Submissions CSM Forensics and AI Air Gap](adr/2026-08-28-specialized-science-safety-submissions-csm-forensics-and-ai-air-gap.md) | `adr/2026-08-28-specialized-science-safety-submissions-csm-forensics-and-ai-air-gap.md` |
| [ADR-2236: E2B R3 Suspect Drug WHODrug Active Ingredient Validation and AS2 PreFlight Gating](adr/2026-08-29-e2b-r3-suspect-drug-whodrug-active-ingredient-validation-and-as2-preflight-gating.md) | `adr/2026-08-29-e2b-r3-suspect-drug-whodrug-active-ingredient-validation-and-as2-preflight-gating.md` |
| [ADR-2232: Financial and Site Operations Engines Standard](adr/2026-08-29-financial-and-site-operations-engines-standard.md) | `adr/2026-08-29-financial-and-site-operations-engines-standard.md` |
| [ADR-2237: Multi-Tenant Database Schema Provisioning and Automated Migration Pipeline](adr/2026-08-29-multi-tenant-database-schema-provisioning-and-automated-migration-pipeline.md) | `adr/2026-08-29-multi-tenant-database-schema-provisioning-and-automated-migration-pipeline.md` |
| [ADR-2234: Pure JSONB Observation Storage and Columnar Dataset JSON Exporter](adr/2026-08-29-pure-jsonb-observation-storage-and-columnar-dataset-json-exporter.md) | `adr/2026-08-29-pure-jsonb-observation-storage-and-columnar-dataset-json-exporter.md` |
| [ADR-2235: Schedule of Activities Matrix WCAG 2.1 AA 2D Grid Navigation and Background Sync Hardening](adr/2026-08-29-schedule-of-activities-matrix-wcag-21-aa-2d-grid-navigation-and-background-sync-hardening.md) | `adr/2026-08-29-schedule-of-activities-matrix-wcag-21-aa-2d-grid-navigation-and-background-sync-hardening.md` |
| [ADR-2422: SDV and TSDV Endpoint Hardening, RBAC Permission Mapping, and Local Study Scope Enforcement](adr/2026-08-29-sdv-tsdv-endpoint-hardening-rbac-study-scope.md) | `adr/2026-08-29-sdv-tsdv-endpoint-hardening-rbac-study-scope.md` |
| [ADR-2423: Graph Schema Extensions for Arm-Aware Schedule of Activities (SoA)](adr/2026-08-30-soa-graph-schema-extensions.md) | `adr/2026-08-30-soa-graph-schema-extensions.md` |
| [ADR-2424: Vue SPA Foundation and Tooling Setup](adr/2026-08-30-vue-spa-foundation-tooling.md) | `adr/2026-08-30-vue-spa-foundation-tooling.md` |
| [ADR-2425: Randomization and Trial Supply Management](adr/2026-08-31-randomization-and-trial-supply-management.md) | `adr/2026-08-31-randomization-and-trial-supply-management.md` |
| [ADR-2426: SAE Reconciliation & Safety Gateway (E2B)](adr/2026-09-01-sae-reconciliation-safety-gateway-e2b.md) | `adr/2026-09-01-sae-reconciliation-safety-gateway-e2b.md` |
| [ADR-2427: Bypass Unused Parameter ESLint in Shared UI](adr/2026-09-02-bypass-unused-parameter-eslint-in-shared-ui.md) | `adr/2026-09-02-bypass-unused-parameter-eslint-in-shared-ui.md` |
| [ADR-188: Clinical Rules and Query Designer Tabs](adr/2026-09-02-clinical-rules-and-query-designer-tabs.md) | `adr/2026-09-02-clinical-rules-and-query-designer-tabs.md` |
| [ADR-2428: Core Service Architecture CQRS Transactional Outbox and GxP Interceptor Standard](adr/2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md) | `adr/2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md` |
| [ADR-2233: Migration to .NET 10 Architecture and Standards](adr/2026-09-02-migration-to-dotnet-10-architecture-and-standards.md) | `adr/2026-09-02-migration-to-dotnet-10-architecture-and-standards.md` |
| [ADR-2429: Unified GxP Traceability and Tooling Pipeline for .NET 10 Migration](adr/2026-09-02-unified-gxp-traceability-and-tooling-pipeline-for-net-10-migration.md) | `adr/2026-09-02-unified-gxp-traceability-and-tooling-pipeline-for-net-10-migration.md` |
| [ADR-2430: Centralized Study Scope and Lab Range Permissions](adr/2026-09-03-centralized-study-scope-and-lab-range-permissions.md) | `adr/2026-09-03-centralized-study-scope-and-lab-range-permissions.md` |
| [ADR-2240: Full-Stack Environment Setup Wizard and Python Workspace Glob Hardening](adr/2026-09-03-full-stack-environment-setup-wizard-and-python-workspace-glob-hardening.md) | `adr/2026-09-03-full-stack-environment-setup-wizard-and-python-workspace-glob-hardening.md` |
| [ADR-2239: Universal Decimal Financial Precision Dynamic OpenAPI Gateway Discovery and Tier 2 Quality Realignment](adr/2026-09-03-universal-decimal-financial-precision-dynamic-openapi-gateway-discovery-and-tier-2-quality-realignment.md) | `adr/2026-09-03-universal-decimal-financial-precision-dynamic-openapi-gateway-discovery-and-tier-2-quality-realignment.md` |
| [ADR-2238: Universal PostgreSQL Persistence Standard and Hybrid JSONB Record Projection](adr/2026-09-03-universal-postgresql-persistence-standard-and-hybrid-jsonb-record-projection.md) | `adr/2026-09-03-universal-postgresql-persistence-standard-and-hybrid-jsonb-record-projection.md` |
| [ADR-146: Dynamic Port Diagnostics and Fallback Realignment](adr/2026-09-04-dynamic-port-diagnostics-and-fallback-realignment.md) | `adr/2026-09-04-dynamic-port-diagnostics-and-fallback-realignment.md` |
| [ADR-2431: Global Library Governance & Release Management](adr/2026-09-04-global-library-governance-release-management.md) | `adr/2026-09-04-global-library-governance-release-management.md` |
| [ADR-2432: Interactive SoA Builder Web Client (Phase 19)](adr/2026-09-04-interactive-soa-builder-web-client-phase-19.md) | `adr/2026-09-04-interactive-soa-builder-web-client-phase-19.md` |
| [ADR-2241: Pin uv Python Preference to System for Python 3.14](adr/2026-09-04-pin-uv-python-preference-to-system-for-python-314.md) | `adr/2026-09-04-pin-uv-python-preference-to-system-for-python-314.md` |
| [ADR-250: Extend Centralized Permission Matrix for eCOA and ePRO](adr/2026-09-05-extend-centralized-permission-matrix-for-ecoa.md) | `adr/2026-09-05-extend-centralized-permission-matrix-for-ecoa.md` |
| [ADR-2433: Enforce Strict Fail-Closed Environment Rules and Narrow Scanner Exclusions](adr/2026-09-06-enforce-strict-fail-closed-environment-rules.md) | `adr/2026-09-06-enforce-strict-fail-closed-environment-rules.md` |
| [ADR-2434: GxP Compliance Reporting CI Alignment and Local Dependency Configuration](adr/2026-09-06-gxp-compliance-reporting-ci-alignment.md) | `adr/2026-09-06-gxp-compliance-reporting-ci-alignment.md` |
| [ADR-2435: Extend Centralized Permission Matrix for eCOA Diary Alert Actions](adr/2026-09-07-extend-ecoa-diary-alert-permissions.md) | `adr/2026-09-07-extend-ecoa-diary-alert-permissions.md` |
| [ADR-2436: Decommission legacy rendering and v1 signatures](adr/2026-09-08-decommission-legacy-rendering-and-v1-signatures.md) | `adr/2026-09-08-decommission-legacy-rendering-and-v1-signatures.md` |
| [ADR-2437: Decommission Legacy V1 Signatures and Standalone Rendering Engine](adr/2026-09-08-decommission-legacy-v1-signatures-and-rendering-engine.md) | `adr/2026-09-08-decommission-legacy-v1-signatures-and-rendering-engine.md` |
| [ADR-2438: RSA Signature Verification Fallback to PKCS1v15](adr/2026-09-09-rsa-signature-verification-fallback-pkcs1v15.md) | `adr/2026-09-09-rsa-signature-verification-fallback-pkcs1v15.md` |
| [ADR-193: Align Ledger Block Signature and Deduplicate Pinia Dependency](adr/2026-09-10-align-ledger-block-signature-and-deduplicate-pinia-dependency.md) | `adr/2026-09-10-align-ledger-block-signature-and-deduplicate-pinia-dependency.md` |
| [ADR-2439: Shared Zod Schemas and Backend Immutability](adr/2026-09-10-shared-zod-schemas-and-backend-immutability.md) | `adr/2026-09-10-shared-zod-schemas-and-backend-immutability.md` |
| [ADR-328: Preservation of CDASH Type Definition Pristine State](adr/2026-09-11-preservation-of-cdash-type-definition-pristine-state.md) | `adr/2026-09-11-preservation-of-cdash-type-definition-pristine-state.md` |
| [ADR-2440: Site Compliance Cache and Transition Gating](adr/2026-09-11-site-compliance-cache-and-transition-gating.md) | `adr/2026-09-11-site-compliance-cache-and-transition-gating.md` |
| [ADR-2441: In-Process Clinical De-Identification and Gateway USDM Validation Router](adr/2026-09-12-in-process-clinical-deid-and-gateway-usdm-router.md) | `adr/2026-09-12-in-process-clinical-deid-and-gateway-usdm-router.md` |
| [ADR-2442: MSW Mock Gateway Simulator and Browser HMAC Verification Engine](adr/2026-09-12-msw-mock-gateway-and-browser-hmac-verification.md) | `adr/2026-09-12-msw-mock-gateway-and-browser-hmac-verification.md` |
| [ADR-2443: Decommission Legacy Python Compose Services and Set .NET 10 as Default Runtime](adr/2026-09-25-decommission-legacy-python-services-and-default-dotnet10-runtime.md) | `adr/2026-09-25-decommission-legacy-python-services-and-default-dotnet10-runtime.md` |
| [ADR-2444: Decommission Python Scripts to Native .NET 10 Tooling Services and Cadence.Cli Commands](adr/2026-09-25-decommission-python-scripts-to-native-dotnet-10-tooling.md) | `adr/2026-09-25-decommission-python-scripts-to-native-dotnet-10-tooling.md` |
| [ADR-2445: Payments Domain Primitive Decimal Conversion & EF Core Explicit Precision Mapping](adr/2026-09-25-payments-primitive-decimal-conversion-and-explicit-ef-core-precision.md) | `adr/2026-09-25-payments-primitive-decimal-conversion-and-explicit-ef-core-precision.md` |
| [ADR-2242: Phased Workspace Member Unlinking and Toolchain Decommissioning Protocol](adr/2026-09-25-phased-workspace-member-unlinking-and-toolchain-decommissioning-protocol.md) | `adr/2026-09-25-phased-workspace-member-unlinking-and-toolchain-decommissioning-protocol.md` |
| [ADR-2243: Topological Tier-Based .NET 10 Migration and Legacy Python Decommissioning](adr/2026-09-28-topological-tier-based-dotnet-10-migration-and-python-decommissioning.md) | `adr/2026-09-28-topological-tier-based-dotnet-10-migration-and-python-decommissioning.md` |
| [ADR-2257: Attributable Audit Actors and Named Service Accounts](adr/2026-09-29-attributable-audit-actors-and-named-service-accounts.md) | `adr/2026-09-29-attributable-audit-actors-and-named-service-accounts.md` |
| [ADR-2248: Cadence Security Formatting and Query Style Alignment](adr/2026-09-29-cadence-security-formatting-and-query-style-alignment.md) | `adr/2026-09-29-cadence-security-formatting-and-query-style-alignment.md` |
| [ADR-2244: Clinical Kernel Modular Monolith Architecture](adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) | `adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md` |
| [ADR-2247: Clinical Kernel Single Database Schema and Architecture Sentinel Hardening](adr/2026-09-29-clinical-kernel-single-database-schema-and-architecture-sentinel-hardening.md) | `adr/2026-09-29-clinical-kernel-single-database-schema-and-architecture-sentinel-hardening.md` |
| [ADR-2252: Concept Registry with CDISC Biomedical Concepts as the Semantic Key (K005)](adr/2026-09-29-concept-registry-cdisc-biomedical-concepts-as-semantic-key.md) | `adr/2026-09-29-concept-registry-cdisc-biomedical-concepts-as-semantic-key.md` |
| [ADR-2259: Electronic Signature Step-Up Re-Authentication](adr/2026-09-29-electronic-signature-step-up-re-authentication.md) | `adr/2026-09-29-electronic-signature-step-up-re-authentication.md` |
| [ADR-2256: Fail-Closed Gateway Identity and Signed Caller Authentication](adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md) | `adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md` |
| [ADR-2448: Gateway Routes Verified Against Service Endpoints](adr/2026-09-29-gateway-routes-verified-against-service-endpoints.md) | `adr/2026-09-29-gateway-routes-verified-against-service-endpoints.md` |
| [ADR-2451: Gateway Timeouts and Health-Checked Clusters](adr/2026-09-29-gateway-timeouts-and-health-checked-clusters.md) | `adr/2026-09-29-gateway-timeouts-and-health-checked-clusters.md` |
| [ADR-2253: Identity Vault and Layered Participant Identity (K006)](adr/2026-09-29-identity-vault-and-layered-participant-identity.md) | `adr/2026-09-29-identity-vault-and-layered-participant-identity.md` |
| [ADR-2251: Kernel Authority Computed from Evidence via a Hand-Coded Policy Evaluator (K004)](adr/2026-09-29-kernel-authority-computed-from-evidence-policy-engine.md) | `adr/2026-09-29-kernel-authority-computed-from-evidence-policy-engine.md` |
| [ADR-2449: Kernel Command Pipeline, Transactional Write Set and Per-Tenant Audit Chain](adr/2026-09-29-kernel-command-pipeline-transactional-write-set-and-per-tenant-audit-chain.md) | `adr/2026-09-29-kernel-command-pipeline-transactional-write-set-and-per-tenant-audit-chain.md` |
| [ADR-2255: Kernel Lineage Graph and End-to-End Scientific Traceability (K009)](adr/2026-09-29-kernel-lineage-graph-and-scientific-traceability.md) | `adr/2026-09-29-kernel-lineage-graph-and-scientific-traceability.md` |
| [ADR-2250: Kernel Tenant Isolation via Row-Level Security in a Shared Schema (K003)](adr/2026-09-29-kernel-tenant-isolation-via-row-level-security.md) | `adr/2026-09-29-kernel-tenant-isolation-via-row-level-security.md` |
| [ADR-2447: Module Schemas via Search Path and Migration Deployment Modes](adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md) | `adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md` |
| [ADR-2249: Native AOT CLI Compilation and Makefile Toolchain Binding](adr/2026-09-29-native-aot-cli-compilation-and-makefile-binding.md) | `adr/2026-09-29-native-aot-cli-compilation-and-makefile-binding.md` |
| [ADR-2261: Native C# De-Identification Engine and Cryptographic Canonical Signing Foundation](adr/2026-09-29-native-csharp-deid-engine-and-canonical-signing.md) | `adr/2026-09-29-native-csharp-deid-engine-and-canonical-signing.md` |
| [ADR-2245: Native PostgreSQL ProtocolVersion Snapshots and Neo4j Retirement](adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md) | `adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md` |
| [ADR-2260: No Default Credentials and Fail-Closed Secret Resolution](adr/2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md) | `adr/2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md` |
| [ADR-2246: PostgreSQL Native Bitemporal Interval Standard via tstzrange and GiST](adr/2026-09-29-postgresql-native-bitemporal-interval-standard-via-tstzrange-and-gist.md) | `adr/2026-09-29-postgresql-native-bitemporal-interval-standard-via-tstzrange-and-gist.md` |
| [ADR-2450: Reject Concurrency Conflicts, Retry Only Transient Failures](adr/2026-09-29-reject-concurrency-conflicts-retry-only-transient-failures.md) | `adr/2026-09-29-reject-concurrency-conflicts-retry-only-transient-failures.md` |
| [ADR-2258: Restrict Cascade Deletes onto Audit Trails and Regulated Child Records](adr/2026-09-29-restrict-cascade-deletes-onto-audit-trails-and-regulated-children.md) | `adr/2026-09-29-restrict-cascade-deletes-onto-audit-trails-and-regulated-children.md` |
| [ADR-2254: Rules as Versioned Data, Continuous Standards Conformance and a Unified Rules Engine (K008)](adr/2026-09-29-rules-as-versioned-data-and-unified-rules-engine.md) | `adr/2026-09-29-rules-as-versioned-data-and-unified-rules-engine.md` |
| [ADR-2446: Single Audit Writer and One Chain per Aggregate Type](adr/2026-09-29-single-audit-writer-and-one-chain-per-aggregate.md) | `adr/2026-09-29-single-audit-writer-and-one-chain-per-aggregate.md` |
| [ADR-2541: Consolidate GxPAuditInterceptor on Cadence.Database and Remove Obsolete Security Interceptor](adr/2026-09-30-consolidate-gxp-audit-interceptor-on-database-package.md) | `adr/2026-09-30-consolidate-gxp-audit-interceptor-on-database-package.md` |
| [ADR-2590: Counts Limited to the Reader's Visible Scope](adr/2026-09-30-counts-limited-to-the-readers-visible-scope.md) | `adr/2026-09-30-counts-limited-to-the-readers-visible-scope.md` |
| [ADR-2502: DDF Item Rules Use the One Rule Language](adr/2026-09-30-ddf-item-rules-use-the-one-rule-language.md) | `adr/2026-09-30-ddf-item-rules-use-the-one-rule-language.md` |
| [ADR-2483: Dry-Run Authorization and the Command Catalogue](adr/2026-09-30-dry-run-authorization-and-command-catalogue.md) | `adr/2026-09-30-dry-run-authorization-and-command-catalogue.md` |
| [ADR-2592: Dual Ecosystem Vulnerability Auditor and CEC Chair Adjudications Persistence](adr/2026-09-30-dual-ecosystem-vulnerability-auditor-and-cec-chair-adjudications-persistence.md) | `adr/2026-09-30-dual-ecosystem-vulnerability-auditor-and-cec-chair-adjudications-persistence.md` |
| [ADR-2456: Effectivity and Study-Site Activation](adr/2026-09-30-effectivity-and-study-site-activation.md) | `adr/2026-09-30-effectivity-and-study-site-activation.md` |
| [ADR-2510: Endpoint Permissions from the Permission Matrix](adr/2026-09-30-endpoint-permissions-from-the-permission-matrix.md) | `adr/2026-09-30-endpoint-permissions-from-the-permission-matrix.md` |
| [ADR-2480: Evaluate-Only Simulation over Candidate Versions](adr/2026-09-30-evaluate-only-simulation-over-candidate-versions.md) | `adr/2026-09-30-evaluate-only-simulation-over-candidate-versions.md` |
| [ADR-2454: Evidence Artifacts as Immutable, Hash-Addressed Versions](adr/2026-09-30-evidence-artifacts-as-immutable-hash-addressed-versions.md) | `adr/2026-09-30-evidence-artifacts-as-immutable-hash-addressed-versions.md` |
| [ADR-2472: Executable Protocol Design Document with Stable Element IDs](adr/2026-09-30-executable-protocol-design-document.md) | `adr/2026-09-30-executable-protocol-design-document.md` |
| [ADR-2487: Expected Schedule as a Pure Function of the Protocol Design](adr/2026-09-30-expected-schedule-as-a-pure-function.md) | `adr/2026-09-30-expected-schedule-as-a-pure-function.md` |
| [ADR-2482: Explanation Model Produced by the Rule Owner](adr/2026-09-30-explanation-model-produced-by-the-rule-owner.md) | `adr/2026-09-30-explanation-model-produced-by-the-rule-owner.md` |
| [ADR-2490: Fail-Fast Startup Validation of Required Settings](adr/2026-09-30-fail-fast-startup-validation-of-required-settings.md) | `adr/2026-09-30-fail-fast-startup-validation-of-required-settings.md` |
| [ADR-2494: Field-Level Encryption and Keyed Lookup Hashes for Secrets at Rest](adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md) | `adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md` |
| [ADR-2513: Form Approval Hashes the RFC 8785 Canonical Payload](adr/2026-09-30-form-approval-hashes-the-rfc-8785-canonical-payload.md) | `adr/2026-09-30-form-approval-hashes-the-rfc-8785-canonical-payload.md` |
| [ADR-2457: Grant-Backed Node Visibility](adr/2026-09-30-grant-backed-node-visibility.md) | `adr/2026-09-30-grant-backed-node-visibility.md` |
| [ADR-2521: The Grant Payables Ledger Is Ported to Payments in Decimal with Kernel IDs](adr/2026-09-30-grant-payables-ledger-in-decimal-with-kernel-ids.md) | `adr/2026-09-30-grant-payables-ledger-in-decimal-with-kernel-ids.md` |
| [ADR-2455: Grants, Roles and Delegation as Bitemporal Kernel Authority](adr/2026-09-30-grants-roles-and-delegation-as-bitemporal-kernel-authority.md) | `adr/2026-09-30-grants-roles-and-delegation-as-bitemporal-kernel-authority.md` |
| [ADR-2491: HTTP Edge Hardening: Trusted Forwarded Headers, HSTS and Per-Caller Rate Limits](adr/2026-09-30-http-edge-hardening-forwarded-headers-hsts-rate-limits.md) | `adr/2026-09-30-http-edge-hardening-forwarded-headers-hsts-rate-limits.md` |
| [ADR-2458: The IdentityVault](adr/2026-09-30-identity-vault.md) | `adr/2026-09-30-identity-vault.md` |
| [ADR-2540: Kernel API Host and Read Endpoints for the Clinical Canvas](adr/2026-09-30-kernel-api-host-and-read-endpoints-for-the-canvas.md) | `adr/2026-09-30-kernel-api-host-and-read-endpoints-for-the-canvas.md` |
| [ADR-2493: Keycloak-Only Web Sign-In](adr/2026-09-30-keycloak-only-web-sign-in.md) | `adr/2026-09-30-keycloak-only-web-sign-in.md` |
| [ADR-2471: Lineage Store and Masked Read Contract](adr/2026-09-30-lineage-store-and-masked-read-contract.md) | `adr/2026-09-30-lineage-store-and-masked-read-contract.md` |
| [ADR-2463: Mapping Specifications as Governed Concept-to-Variable Maps (K028)](adr/2026-09-30-mapping-specifications-as-governed-concept-to-variable-maps.md) | `adr/2026-09-30-mapping-specifications-as-governed-concept-to-variable-maps.md` |
| [ADR-2530: Measure Web Coverage over All Source with a Ratchet Floor](adr/2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md) | `adr/2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md` |
| [ADR-2500: One Folder per Test Project and a Separate Integration Test Project](adr/2026-09-30-one-folder-per-test-project-and-a-separate-integration-project.md) | `adr/2026-09-30-one-folder-per-test-project-and-a-separate-integration-project.md` |
| [ADR-2496: Parse the CLI with System.CommandLine and Publish It as Native AOT Without Suppressions](adr/2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md) | `adr/2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md` |
| [ADR-2453: Participants, Bitemporal Enrollment Lifecycle and Gap-Free Enrollment Numbering](adr/2026-09-30-participants-enrollment-lifecycle-and-gap-free-numbering.md) | `adr/2026-09-30-participants-enrollment-lifecycle-and-gap-free-numbering.md` |
| [ADR-2470: Per-Tenant Terminology, Pinned Code-System Versions and Versioned Concepts](adr/2026-09-30-per-tenant-terminology-and-pinned-code-system-versions.md) | `adr/2026-09-30-per-tenant-terminology-and-pinned-code-system-versions.md` |
| [ADR-2474: Protocol Elements as Lineage Nodes](adr/2026-09-30-protocol-elements-as-lineage-nodes.md) | `adr/2026-09-30-protocol-elements-as-lineage-nodes.md` |
| [ADR-2452: Protocol Version Lifecycle, Content-Hash Freezing and Attested Approval](adr/2026-09-30-protocol-version-lifecycle-content-hash-and-attested-approval.md) | `adr/2026-09-30-protocol-version-lifecycle-content-hash-and-attested-approval.md` |
| [ADR-2481: Public NodeRef and Kernel-Computed Zoom Path](adr/2026-09-30-public-noderef-and-kernel-computed-zoom-path.md) | `adr/2026-09-30-public-noderef-and-kernel-computed-zoom-path.md` |
| [ADR-2459: Qualifications Gate Capabilities](adr/2026-09-30-qualifications-gate-capabilities.md) | `adr/2026-09-30-qualifications-gate-capabilities.md` |
| [ADR-2485: Reader-Facing Activity Stream Projection](adr/2026-09-30-reader-facing-activity-stream-projection.md) | `adr/2026-09-30-reader-facing-activity-stream-projection.md` |
| [ADR-2495: One Encoding for the Reason-for-Change Header](adr/2026-09-30-reason-for-change-header-encoding.md) | `adr/2026-09-30-reason-for-change-header-encoding.md` |
| [ADR-2505: Recording Evidence Goes to Controlled Object Storage, Not Google Drive](adr/2026-09-30-recording-evidence-goes-to-controlled-object-storage.md) | `adr/2026-09-30-recording-evidence-goes-to-controlled-object-storage.md` |
| [ADR-2492: Retire Python Reference Sources Once Their C# Ports Are Tested](adr/2026-09-30-retire-python-reference-sources-once-ports-are-tested.md) | `adr/2026-09-30-retire-python-reference-sources-once-ports-are-tested.md` |
| [ADR-2503: Reusable Form Blocks Are Collection Specification Library Items](adr/2026-09-30-reusable-form-blocks-are-collection-specification-library-items.md) | `adr/2026-09-30-reusable-form-blocks-are-collection-specification-library-items.md` |
| [ADR-2461: Rule Expression Language, Rule-Pack Format and the IRuleEvaluator Port](adr/2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md) | `adr/2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md` |
| [ADR-2475: Schedule of Activities as a Rendering, with Typed Amendment Directives](adr/2026-09-30-schedule-of-activities-as-a-rendering-with-typed-amendment-directives.md) | `adr/2026-09-30-schedule-of-activities-as-a-rendering-with-typed-amendment-directives.md` |
| [ADR-2512: Security and Compliance Take Time From IClock](adr/2026-09-30-security-and-compliance-take-time-from-iclock.md) | `adr/2026-09-30-security-and-compliance-take-time-from-iclock.md` |
| [ADR-2460: Standards Registry and Metadata Registry as Governed, Tenant-Scoped Reference Data](adr/2026-09-30-standards-registry-and-mdr-as-governed-tenant-reference-data.md) | `adr/2026-09-30-standards-registry-and-mdr-as-governed-tenant-reference-data.md` |
| [ADR-2488: Study Pulse Metrics and Attention Rules as Versioned Indicators](adr/2026-09-30-study-pulse-metrics-as-versioned-indicators.md) | `adr/2026-09-30-study-pulse-metrics-as-versioned-indicators.md` |
| [ADR-2520: The Supply Unblinding Engine and Facade Are Not Carried Forward](adr/2026-09-30-supply-unblinding-and-facade-not-carried-forward.md) | `adr/2026-09-30-supply-unblinding-and-facade-not-carried-forward.md` |
| [ADR-2591: Synchronous In-Process DSUR Compilation and Cryptographic Signing Engine](adr/2026-09-30-synchronous-in-process-dsur-compilation-and-cryptographic-signing-engine.md) | `adr/2026-09-30-synchronous-in-process-dsur-compilation-and-cryptographic-signing-engine.md` |
| [ADR-2511: Template Render Routes Need a Permission](adr/2026-09-30-template-render-routes-need-a-permission.md) | `adr/2026-09-30-template-render-routes-need-a-permission.md` |
| [ADR-2484: Tenant-Scoped Search over the Source Tables](adr/2026-09-30-tenant-scoped-search-over-source-tables.md) | `adr/2026-09-30-tenant-scoped-search-over-source-tables.md` |
| [ADR-2501: The DDF Compiler Refuses Forms That Fail the Accessibility Check](adr/2026-09-30-the-ddf-compiler-refuses-forms-that-fail-the-accessibility-check.md) | `adr/2026-09-30-the-ddf-compiler-refuses-forms-that-fail-the-accessibility-check.md` |
| [ADR-2506: The engine_core Rule AST Is Superseded by the One Rule Language](adr/2026-09-30-the-engine-core-rule-ast-is-superseded-by-the-one-rule-language.md) | `adr/2026-09-30-the-engine-core-rule-ast-is-superseded-by-the-one-rule-language.md` |
| [ADR-2514: The Tenant Comes From Sign-In, With No Fallback](adr/2026-09-30-the-tenant-comes-from-sign-in-with-no-fallback.md) | `adr/2026-09-30-the-tenant-comes-from-sign-in-with-no-fallback.md` |
| [ADR-2504: The Tripartite Form Definition Is Not Carried Forward](adr/2026-09-30-the-tripartite-form-definition-is-not-carried-forward.md) | `adr/2026-09-30-the-tripartite-form-definition-is-not-carried-forward.md` |
| [ADR-2473: CDISC USDM 4 Import into the Protocol Design](adr/2026-09-30-usdm-4-import-into-the-protocol-design.md) | `adr/2026-09-30-usdm-4-import-into-the-protocol-design.md` |
| [ADR-2462: Validation Findings Derived from Immutable Runs and Attested Decisions](adr/2026-09-30-validation-findings-derived-from-immutable-runs-and-attested-decisions.md) | `adr/2026-09-30-validation-findings-derived-from-immutable-runs-and-attested-decisions.md` |
| [ADR-2507: Visit Timing, Anchored Visits and Cycles on the Expected Schedule](adr/2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md) | `adr/2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md` |
| [ADR-2486: Work Queues as Capability-Keyed Definitions](adr/2026-09-30-work-queues-as-capability-keyed-definitions.md) | `adr/2026-09-30-work-queues-as-capability-keyed-definitions.md` |
| [ADR-2801: Automated Stipend Disbursement Threshold Rules for Participant Wallet](adr/2026-10-01-automated-stipend-disbursement-threshold-rules.md) | `adr/2026-10-01-automated-stipend-disbursement-threshold-rules.md` |
| [Architectural Decision Records (ADRs) Index](adr/index.md) | `adr/index.md` |
| [ADR-[NUMBER]: [Short, Descriptive Title]](adr/TEMPLATE.md) | `adr/TEMPLATE.md` |

## System & Feature Specifications

| Document | Path |
| --- | --- |
| [Specification: Antigravity GxP Lifecycle Hooks & AI Coding Guardrails](specs/01-antigravity-gxp-lifecycle-hooks.md) | `specs/01-antigravity-gxp-lifecycle-hooks.md` |
| [Specification: Domain-Specific MCP Tool Extensions, Graph Introspection & Autonomous Agent Sidecars](specs/02-antigravity-domain-mcp-tools-and-agent-sidecars.md) | `specs/02-antigravity-domain-mcp-tools-and-agent-sidecars.md` |
| [Specification: Unified Developer Tooling, Native MCP Server & Staged Guardrails](specs/developer-tooling-mcp-guardrails.md) | `specs/developer-tooling-mcp-guardrails.md` |
| [System Specification: AI-Enhanced CDISC-Native Visual Metadata IDE & Tripartite eCRF Designer](specs/ecrf_designer_ide_spec.md) | `specs/ecrf_designer_ide_spec.md` |

## Python-to-.NET Port & Migration

| Document | Path |
| --- | --- |
| [BOUNDED_CONTEXT_MIGRATION_MATRIX.md: Domain Decomposition & Migration Sequencing](migration/BOUNDED_CONTEXT_MIGRATION_MATRIX.md) | `migration/BOUNDED_CONTEXT_MIGRATION_MATRIX.md` |
| [DEPENDENCY_AND_PARITY_CATALOG.md: Phase 0 Ecosystem Mapping & Test Parity Baseline](migration/DEPENDENCY_AND_PARITY_CATALOG.md) | `migration/DEPENDENCY_AND_PARITY_CATALOG.md` |
| [.NET 10 Modernization Hub: Phase 0 Architectural Baseline](migration/index.md) | `migration/index.md` |
| [Forensic Gap Analysis & Phase 0 Migration Readiness Audit](migration/Migration_Phase0_Readiness_Audit.md) | `migration/Migration_Phase0_Readiness_Audit.md` |
| [Phase 0 Executive Discovery: Domain Decomposition Matrix & .NET 10 Solution Scaffold](migration/PHASE0_DOMAIN_DECOMPOSITION_AND_SCAFFOLD.md) | `migration/PHASE0_DOMAIN_DECOMPOSITION_AND_SCAFFOLD.md` |
| [00. Phase 0 Validation Gate — Audit Report](migration/phase0/00_validation_gate_report.md) | `migration/phase0/00_validation_gate_report.md` |
| [00a. Gate 5 Remediation — Clean dotnet build Cadence.sln Restored](migration/phase0/00a_build_remediation.md) | `migration/phase0/00a_build_remediation.md` |
| [01. System Topology & Entry Point Audit](migration/phase0/01_system_topology.md) | `migration/phase0/01_system_topology.md` |
| [02. Bounded Context Decomposition & Coupling Analysis](migration/phase0/02_bounded_contexts.md) | `migration/phase0/02_bounded_contexts.md` |
| [03. Migration Sequencing Plan & Strangler Fig Design](migration/phase0/03_migration_plan.md) | `migration/phase0/03_migration_plan.md` |
| [05. Orphan Audit Gate — Zero-Orphan Verification](migration/phase0/05_orphan_audit_gate.md) | `migration/phase0/05_orphan_audit_gate.md` |
| [.NET 10 Solution Scaffold Guide](migration/SCAFFOLD_GUIDE.md) | `migration/SCAFFOLD_GUIDE.md` |
| [Behavioral & Invariant Specification: Clinical Observations, Form Submissions & Dynamic Projections](migration/slices/execution-observations/01_spec.md) | `migration/slices/execution-observations/01_spec.md` |
| [Technical Design: EXECUTION-OBSERVATIONS (.NET 10 / C# 14)](migration/slices/execution-observations/02_technical_design.md) | `migration/slices/execution-observations/02_technical_design.md` |
| [Tracer-Bullet Development Tickets: EXECUTION-OBSERVATIONS](migration/slices/execution-observations/03_tickets.md) | `migration/slices/execution-observations/03_tickets.md` |
| [Implementation Sign-Off: execution-observations](migration/slices/execution-observations/04_implementation_signoff.md) | `migration/slices/execution-observations/04_implementation_signoff.md` |
| [Phase 1 Validation Gate Report: execution-subjects](migration/slices/execution-subjects/00_phase1_validation_report.md) | `migration/slices/execution-subjects/00_phase1_validation_report.md` |
| [Phase 2 Validation Gate Report: EXECUTION-SUBJECTS](migration/slices/execution-subjects/00_phase2_validation_report.md) | `migration/slices/execution-subjects/00_phase2_validation_report.md` |
| [Phase 3 Validation Gate Report: EXECUTION-SUBJECTS](migration/slices/execution-subjects/00_phase3_validation_report.md) | `migration/slices/execution-subjects/00_phase3_validation_report.md` |
| [Behavioral & Invariant Specification: Subject Lifecycle & Enrollment](migration/slices/execution-subjects/01_spec.md) | `migration/slices/execution-subjects/01_spec.md` |
| [Target .NET 10 Architecture: Subject Lifecycle & Enrollment](migration/slices/execution-subjects/02_architecture.md) | `migration/slices/execution-subjects/02_architecture.md` |
| [Technical Design: EXECUTION-SUBJECTS (.NET 10 / C# 14)](migration/slices/execution-subjects/02_technical_design.md) | `migration/slices/execution-subjects/02_technical_design.md` |
| [Migration Backlog: EXECUTION-SUBJECTS](migration/slices/execution-subjects/03_tickets.md) | `migration/slices/execution-subjects/03_tickets.md` |
| [Implementation Sign-Off: execution-subjects](migration/slices/execution-subjects/04_implementation_signoff.md) | `migration/slices/execution-subjects/04_implementation_signoff.md` |
| [Functional Specification: Participant Compensation](migration/slices/participant-compensation/Participant_Compensation_Functional_Spec.md) | `migration/slices/participant-compensation/Participant_Compensation_Functional_Spec.md` |
| [SYSTEM_TOPOLOGY_AUDIT.md: Phase 0 Macro Architectural Baseline](migration/SYSTEM_TOPOLOGY_AUDIT.md) | `migration/SYSTEM_TOPOLOGY_AUDIT.md` |

## SDLC Compliance & Regulatory Documentation

| Document | Path |
| --- | --- |
| [Product Requirements Document (PRD) - Cadence Clinical Platform](SDLC/01_Product_Requirements_Document_PRD.md) | `SDLC/01_Product_Requirements_Document_PRD.md` |
| [Technical Design Document (TDD) & Architecture Specification](SDLC/02_Technical_Design_Document_TDD.md) | `SDLC/02_Technical_Design_Document_TDD.md` |
| [Cadence Clinical - API & Integration Specification](SDLC/03_API_Integration_Specification.md) | `SDLC/03_API_Integration_Specification.md` |
| [Data Standards & Interoperability Blueprint](SDLC/04_Data_Standards_Interoperability_Blueprint.md) | `SDLC/04_Data_Standards_Interoperability_Blueprint.md` |
| [Security, Compliance & Audit Trail Spec](SDLC/05_Security_Compliance_Audit_Spec.md) | `SDLC/05_Security_Compliance_Audit_Spec.md` |
| [Quality Assurance (QA) & Validation Plan](SDLC/06_QA_Validation_Plan.md) | `SDLC/06_QA_Validation_Plan.md` |
| [Operations & Deployment Guide (Production Playbook)](SDLC/07_Operations_Deployment_Guide.md) | `SDLC/07_Operations_Deployment_Guide.md` |
| [Cadence Clinical: Global Enterprise Implementation Roadmap & Competitive Superiority Blueprint](SDLC/08_Implementation_Roadmap.md) | `SDLC/08_Implementation_Roadmap.md` |
| [GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report](SDLC/IQ_OQ_PQ_Execution_Report.md) | `SDLC/IQ_OQ_PQ_Execution_Report.md` |
| [GitHub Issue Structure, Project Board & Parallel Work Stream Guide](SDLC/ISSUE_STRUCTURE_GUIDE.md) | `SDLC/ISSUE_STRUCTURE_GUIDE.md` |
| [Requirements Traceability Matrix (RTM)](SDLC/Requirements_Traceability_Matrix.md) | `SDLC/Requirements_Traceability_Matrix.md` |
| [Software Development Lifecycle (SDLC) Documentation Suite](SDLC/SDLC_Documentation_Suite.md) | `SDLC/SDLC_Documentation_Suite.md` |
| [Gateway-to-Execution Cryptographic Signature-Token Contract](SDLC/Signature_Token_Cryptographic_Contract.md) | `SDLC/Signature_Token_Cryptographic_Contract.md` |
| [Feature Specification: Cadence Knowledge & Support Hub](SDLC/SPEC_Knowledge_Support_Hub.md) | `SDLC/SPEC_Knowledge_Support_Hub.md` |
| [Specification: Mathematical Relational & Tensor-Algebraic MDR (Neo4j Decommissioning)](SDLC/SPEC_Neo4j_to_Relational_Algebraic_MDR.md) | `SDLC/SPEC_Neo4j_to_Relational_Algebraic_MDR.md` |

## Clinical Standards, Schemas & Reference Data

| Document | Path |
| --- | --- |
| [CDISC Standards & Reference Documentation Guide](CDISC/README.md) | `CDISC/README.md` |
| [OpenAPI specifications](openapi/README.md) | `openapi/README.md` |
| [ctms database schema](schema/ctms.md) | `schema/ctms.md` |
| [econsent database schema](schema/econsent.md) | `schema/econsent.md` |
| [eisf database schema](schema/eisf.md) | `schema/eisf.md` |
| [etmf database schema](schema/etmf.md) | `schema/etmf.md` |
| [execution database schema](schema/execution.md) | `schema/execution.md` |
| [fileshare database schema](schema/fileshare.md) | `schema/fileshare.md` |
| [imaging database schema](schema/imaging.md) | `schema/imaging.md` |
| [interop database schema](schema/interop.md) | `schema/interop.md` |
| [kernel database schema](schema/kernel.md) | `schema/kernel.md` |
| [knowledge database schema](schema/knowledge.md) | `schema/knowledge.md` |
| [notifications database schema](schema/notifications.md) | `schema/notifications.md` |
| [org database schema](schema/org.md) | `schema/org.md` |
| [payments database schema](schema/payments.md) | `schema/payments.md` |
| [quality database schema](schema/quality.md) | `schema/quality.md` |
| [Database schema](schema/README.md) | `schema/README.md` |
| [safety database schema](schema/safety.md) | `schema/safety.md` |
| [tickets database schema](schema/tickets.md) | `schema/tickets.md` |

## REST API Reference

| Document | Path |
| --- | --- |
| [ai_gateway API](api/ai_gateway.md) | `api/ai_gateway.md` |
| [ctms API](api/ctms.md) | `api/ctms.md` |
| [designer API](api/designer.md) | `api/designer.md` |
| [econsent API](api/econsent.md) | `api/econsent.md` |
| [eisf API](api/eisf.md) | `api/eisf.md` |
| [etmf API](api/etmf.md) | `api/etmf.md` |
| [execution API](api/execution.md) | `api/execution.md` |
| [fileshare API](api/fileshare.md) | `api/fileshare.md` |
| [imaging API](api/imaging.md) | `api/imaging.md` |
| [API Reference](api/index.md) | `api/index.md` |
| [interop API](api/interop.md) | `api/interop.md` |
| [kernel API](api/kernel.md) | `api/kernel.md` |
| [knowledge API](api/knowledge.md) | `api/knowledge.md` |
| [notifications API](api/notifications.md) | `api/notifications.md` |
| [org API](api/org.md) | `api/org.md` |
| [payments API](api/payments.md) | `api/payments.md` |
| [quality API](api/quality.md) | `api/quality.md` |
| [safety API](api/safety.md) | `api/safety.md` |
| [tickets API](api/tickets.md) | `api/tickets.md` |

## UI Components & User Experience

| Document | Path |
| --- | --- |
| [AuditLogViewer](components/audit-viewer.md) | `components/audit-viewer.md` |
| [ClinicalDataTable](components/data-table.md) | `components/data-table.md` |
| [Cadence Enterprise UI Components & Design System](components/index.md) | `components/index.md` |
| [ClinicalModal](components/modal.md) | `components/modal.md` |
| [PersonaSwitcher](components/persona-switcher.md) | `components/persona-switcher.md` |
| [Clinical Canvas: Interaction Model and Platform Requirements v0.1](ux/CLINICAL_CANVAS.md) | `ux/CLINICAL_CANVAS.md` |
| [Moving the web app onto the .NET API](web/API_MIGRATION.md) | `web/API_MIGRATION.md` |

## Agent Guidelines & Working Notes

| Document | Path |
| --- | --- |
| [Domain Docs](agents/domain.md) | `agents/domain.md` |
| [Issue tracker: GitHub](agents/issue-tracker.md) | `agents/issue-tracker.md` |
| [Triage Labels](agents/triage-labels.md) | `agents/triage-labels.md` |

