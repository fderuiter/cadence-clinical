# Documentation Index

The single map of Cadence documentation. Every document is listed once, with its status:

- **Normative:** the source of truth; other documents must agree with it.
- **Current:** maintained with the code; updated in the same pull request as the change.
- **Generated:** produced by a command; never edit by hand.
- **Baseline:** written for the pre-kernel, per-service design. Kept for requirement IDs and
  traceability; a banner at the top says what supersedes it.
- **Historical:** records of earlier decisions and completed or retired work.

When you add a document, add it here. When a change makes a document wrong, fix it in the same PR
([CONTRIBUTING.md §5](../CONTRIBUTING.md#5-documentation-is-part-of-the-change)).

---

## Start here

| Document | Status | What it is |
| --- | --- | --- |
| [README](../README.md) | Current | What Cadence is, where the code is today, quickstart |
| [Kernel specification v0.2](kernel/CLINICAL_KERNEL_SPECIFICATION.md) | Normative | Clinical Research OS: engines, kernel concepts, invariants (`K-…`), roadmap (§15) |
| [ARCHITECTURE.md](../ARCHITECTURE.md) | Current | Target architecture, key decisions, running services, security, persistence |
| [CONTEXT.md](../CONTEXT.md) | Current | Domain glossary: kernel vocabulary and application terms |
| [AGENTS.md](../AGENTS.md) | Current | Coding rules for humans and AI agents |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | Current | Setup, workflow, local checks, documentation rules |
| [Project plan](PROJECT_PLAN.md) | Current | Single-repository delivery scope, module ownership, milestones, and engineering practices |
| [Goals and priorities](GOALS_AND_PRIORITIES.md) | Current | Code-level objectives and measurable outcomes, in priority order |
| [Issue management](ISSUE_MANAGEMENT.md) | Current | Canonical issue taxonomy, readiness, horizon caps, and maintenance cadence |
| [Backlog insights archive](BACKLOG_INSIGHTS_ARCHIVE.md) | Historical | Source-to-owner provenance and constraints preserved during the November 2026 reset |

## Engineering blog

| Document | Status | What it is |
| --- | --- | --- |
| [Cadence Engineering Blog](blog/index.md) | Current | Curated architectural milestones, platform engineering narratives, and governance updates |
| [Clinical Kernel v0.2: One Canonical Body of Clinical Truth](blog/2026-09-29-clinical-kernel-v02-ontology-and-invariants.md) | Current | Why Cadence inverted the multi-product eClinical stack around one PostgreSQL clinical kernel, six domain engines, bit... |
| [From Python Microservices to .NET 10 and Disciplined Python Retirement](blog/2026-09-30-dotnet-10-modernization-and-python-retirement.md) | Current | How we migrated Cadence's backend and developer tooling to .NET 10 / C# 14 with Native AOT quality gates and retired ... |
| [Governed AI in Clinical Research: Proposals, Grounding, and Human Attestation](blog/2026-10-20-governed-ai-proposals-in-one-runtime.md) | Current | How ADR-2630 (K037) embeds AI agents across clinical workflows through one governed runtime where every output is a c... |
| [Keeping Cadence in One Repository: Consolidating on a Modular Monolith](blog/2026-11-19-keeping-cadence-in-one-repository.md) | Current | Why ADR-2603 ended our multi-repository split plan so kernel contracts, application modules, and end-to-end GxP evide... |

## Kernel and roadmap

| Document | Status | What it is |
| --- | --- | --- |
| [Kernel specification v0.2](kernel/CLINICAL_KERNEL_SPECIFICATION.md) | Normative | The model every capability builds on |
| [Roadmap (spec §15)](kernel/CLINICAL_KERNEL_SPECIFICATION.md#15-roadmap) | Normative | Phases 0–5, tracked in epic [#4719](https://github.com/fderuiter/cadence-clinical/issues/4719) |
| [Kernel decisions K001–K032](adr/index.md) | Current | Kernel decisions and their ADRs; [ARCHITECTURE §2](../ARCHITECTURE.md#2-key-decisions) summarises them with the platform decisions |
| [Kernel package guide](https://github.com/fderuiter/cadence-clinical/blob/main/packages/kernel/README.md) | Current | How the kernel projects are built and used: command pipeline, tenancy, NodeRef, explanations, authorization dry runs, search, protocol versions, Concept and Standards Registries, rule packs, validation findings, activity stream, expected schedule, IdentityVault, grants and qualifications |
| [Rule packs](kernel/RULE_PACKS.md) | Current | Writing, releasing and benchmarking rule packs (spec §10.1, ADR-2461) |
| [Indicators](kernel/INDICATORS.md) | Current | Writing, releasing and evaluating Study Pulse indicators, KRIs and QTLs (spec §10.4, ADR-2488) |
| [Mapping specifications](kernel/MAPPING_SPECIFICATIONS.md) | Current | Writing, pinning and applying mapping specifications; K-STD-1 (spec §6.3, ADR-2463) |
| [Clock migration backlog](kernel/CLOCK_MIGRATION_BACKLOG.md) | Current | Remaining `DateTime.Now/UtcNow` call sites to move to `IClock` (K-TIME-3) |
| [Clinical Canvas](ux/CLINICAL_CANVAS.md) | Current | UI interaction model (study-centric navigation, three panes, six verbs) and the kernel read APIs the UI needs |
| [AI agents](ai/AI_AGENTS.md) | Current | The AI agent catalogue by lifecycle stage, research-type packs, the autonomy ladder and the regulatory map (ADR-2630) |
| [Agent tools, grounding and the MCP server](ai/AGENT_TOOLS.md) | Current | The read tools agents call as their requester, tool allow-lists, the citation verifier, the Proposal writer and the MCP endpoint (ADR-2633) |
| [Evaluating AI task configurations](ai/EVALUATION.md) | Current | Evaluation sets, deterministic checks, the calibrated judge, run records for qualification and production signals (ADR-2634) |

## Former repository migration program

| Document | Status | What it is |
| --- | --- | --- |
| [Controlled migration plan](architecture/repository-migration/MIGRATION_PLAN.md) | Historical | Former ten-repository program and proposed extraction phases |
| [Repository registry](architecture/repository-migration/REPOSITORY_REGISTRY.md) | Historical | Former destination repository and product mapping |
| [Capability ownership matrix](architecture/repository-migration/CAPABILITY_OWNERSHIP_MATRIX.md) | Historical | Prior mapping and analysis of overlapping module responsibilities |
| [Source inventory](architecture/repository-migration/SOURCE_INVENTORY.md) | Historical | Dated dependency and ownership inventory used in the migration review |
| [Kernel extraction plan](architecture/repository-migration/KERNEL_EXTRACTION_PLAN.md) | Historical | Prior SDK extraction preparation and verification analysis |
| [Multi-repository handoff](architecture/repository-migration/AGENT_HANDOFF.md) | Historical | Former migration workflow and synchronization conditions |

## Architecture decision records

| Document | Status | What it is |
| --- | --- | --- |
| [ADR index](adr/index.md) | Current | Every ADR by domain, with supersession marked |
| [ADR template](adr/TEMPLATE.md) | Current | Create new ADRs with `pnpm cadence scaffold adr "<title>"` |

| [ADR-2262: Neo4j Graph Database for Clinical Metadata](adr/2023-01-01-neo4j-graph-database.md) | Historical | Superseded by [ADR-2245](adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md). Neo4j is re... |
| [ADR-2263: PostgreSQL Relational Database for Execution](adr/2023-01-02-postgresql-relational-database.md) | Historical | Accepted |
| [ADR-2264: Pydantic v2 for Domain Models](adr/2023-01-03-pydantic-v2-domain-models.md) | Historical | Accepted |
| [ADR-2265: Keycloak Identity Management](adr/2023-01-04-keycloak-identity-management.md) | Current | Accepted |
| [ADR-2266: USDM Pydantic Models](adr/2026-06-06-usdm-pydantic-models.md) | Historical | Accepted |
| [ADR-042: Decoupled API-First In-Memory Diffing](adr/2026-07-22-api-first-in-memory-diffing.md) | Historical | - Status: Accepted |
| [ADR-2267: API-First Validation and USDM Integration](adr/2026-07-22-api-first-validation-and-usdm-integration.md) | Historical | Accepted |
| [ADR-2268: Audit Log Design](adr/2026-07-22-audit-log-design.md) | Current | Accepted |
| [ADR-014: 2026-07-22 Compliance Tracing and Automated Trial Locks](adr/2026-07-22-compliance-tracing-and-automated-trial-locks.md) | Historical | - Status: Accepted |
| [ADR-2269: Database Shadow Triggers](adr/2026-07-22-database-shadow-triggers.md) | Current | Accepted |
| [ADR-2270: Centralized API Gateway Authentication and Header Propagation](adr/2026-07-22-gateway-authentication-propagation.md) | Historical | * Status: Superseded by [ADR-2256](adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md) |
| [ADR-2271: Dynamic OpenAPI Schema Aggregation at the Gateway](adr/2026-07-22-gateway-openapi-aggregation.md) | Historical | Accepted |
| [ADR-2272: Merkle Root Sealing](adr/2026-07-22-merkle-root-sealing.md) | Current | Accepted |
| [ADR-2273: Metadata-Driven Grid Layouts via Jinja2 Templating](adr/2026-07-22-metadata-driven-grid-layouts.md) | Historical | Accepted |
| [ADR-2274: Modular pnpm Workspace for Frontend Development](adr/2026-07-22-pnpm-frontend-workspace.md) | Historical | Accepted |
| [ADR-2275: Schema Mapping Design](adr/2026-07-22-schema-mapping-design.md) | Current | Accepted |
| [ADR-2276: Signature Re-Authentication](adr/2026-07-22-signature-re-authentication.md) | Current | Accepted |
| [ADR-2277: Transactional SQL Engine with Standard GxP Auditing](adr/2026-07-22-transactional-sql-engine-gxp-auditing.md) | Current | Accepted |
| [ADR-2278: Background Translation Job and Layout Engine Data Contract](adr/2026-07-22-translation-job-and-layout-engine.md) | Historical | Accepted |
| [ADR-2279: Unified Database Management and Pre-Boot Migrations](adr/2026-07-22-unified-database-management-and-pre-boot-migrations.md) | Historical | Accepted |
| [ADR-2280: Unified USDM Dynamic Mapper and Terminology Cache](adr/2026-07-22-usdm-dynamic-mapper-and-cache.md) | Historical | Accepted |
| [ADR-2281: Secure API Signatures with Canonical JSON (Version 2 Sole Standard)](adr/2026-07-23-canonical-json-signatures-and-rolling-versioning.md) | Current | Accepted / Standardized |
| [ADR-016: Core Service-Oriented Clinical Engine](adr/2026-07-23-core-service-oriented-clinical-engine.md) | Historical | - Status: Accepted |
| [ADR-2282: Declarative Ruleset and Automerge Integration](adr/2026-07-23-declarative-ruleset-automerge.md) | Historical | Accepted (Amended August 2026) |
| [ADR-2283: Targeted Integration with Path Isolation](adr/2026-07-23-targeted-integration-path-isolation.md) | Current | Accepted |
| [ADR-054: Clinical Query Workflow and Role Authorization](adr/2026-07-24-clinical-query-workflow-authorization.md) | Historical | - Status: Accepted |
| [ADR-2284: Code Formatting and Style Standardization](adr/2026-07-24-code-formatting-and-style-standardization.md) | Historical | Accepted |
| [ADR-2285: Continuous FMEA GxP-Aligned Exemption Ledger](adr/2026-07-24-continuous-fmea-gxp-aligned-exemption-ledger.md) | Historical | Accepted |
| [ADR-056: Database-Native Pessimistic Locking & Retry](adr/2026-07-24-database-native-pessimistic-locking-retry.md) | Historical | - Status: Accepted |
| [ADR-2286: FHIR / eSource & eCOA Sync Gateway](adr/2026-07-24-fhir-esource-ecoa-sync-gateway.md) | Historical | Accepted |
| [ADR-050: Medical Coding Engine Persistence Foundation](adr/2026-07-24-medical-coding-engine-persistence.md) | Historical | - Status: Accepted |
| [ADR-2287: Minimizing Merge Friction for Baseline and SDLC Verification Artifacts](adr/2026-07-24-minimizing-merge-friction-for-baselines-and-reports.md) | Historical | Accepted |
| [ADR-057: Rolling Versioning and Transition Support for Canonical JSON Signatures](adr/2026-07-24-rolling-upgrade-and-canonical-json-signatures.md) | Historical | - Status: Accepted |
| [ADR-2288: Data-Driven Expected Document Lists (EDLs) & Completeness Tracking](adr/2026-07-25-data-driven-edl-model-and-completeness.md) | Historical | Accepted |
| [ADR-2289: eTMF Quality Control Review Lifecycle & Validated State Machine](adr/2026-07-25-etmf-qc-review-workflow.md) | Current | Accepted |
| [ADR-058: FDA 21 CFR Part 11 Offline Sync Ledger & Digital Signatures](adr/2026-07-25-part11-offline-sync-ledger.md) | Historical | - Status: Accepted |
| [ADR-046: CTMS Foundation, Database, Auditing, and RBAC Infrastructure](adr/2026-07-26-ctms-foundation-infrastructure.md) | Historical | - Status: Accepted |
| [ADR-097: API-driven Lock Sync and AST Import Validator for eTMF](adr/2026-07-27-api-driven-lock-sync-and-ast-validator.md) | Historical | - Status: Accepted |
| [ADR-085: Backward-Compatible Gateway Signature Verification](adr/2026-07-27-backward-compatible-gateway-signature-verification.md) | Historical | - Status: Accepted |
| [ADR-095: CI Schema Introspection and Gateway Aggregation Resilience](adr/2026-07-27-ci-schema-introspection-and-gateway-aggregation.md) | Historical | - Status: Accepted |
| [ADR-096: API Gateway Startup Environment Assertions to Prevent Production Auth Bypasses](adr/2026-07-27-gateway-startup-environment-assertions.md) | Historical | - Status: Accepted |
| [ADR-2290: Programmatic Multi-Database Reset CLI Tool with Safety Guardrails](adr/2026-07-27-multi-database-reset-cli-tool.md) | Historical | Also known as: ADR-096 (former number) |
| [ADR-104: PostgreSQL-Native Parallel Testing and Unified CI Caching](adr/2026-07-27-postgres-parallel-testing-ci-caching.md) | Historical | - Status: Accepted |
| [ADR-047: Quality & CAPA Foundation and Domain Models](adr/2026-07-27-quality-capa-scaffold.md) | Historical | - Status: Accepted |
| [ADR-2291: Standardize FastAPI Identity Dependency and Site Access Migration](adr/2026-07-27-standardize-fastapi-identity-dependency.md) | Historical | - Status: Accepted |
| [ADR-2292: Unified Python-based Documentation Validator](adr/2026-07-27-unified-python-markdown-validator.md) | Historical | Also known as: ADR-104 (former number) |
| [ADR-0091: Dynamic Local Branch Point Detection and Offline ADR Validation](adr/2026-07-28-dynamic-local-branch-point-detection-and-offline-validation.md) | Historical | - Status: Accepted |
| [ADR-0001: Hybrid ESLint and In-Memory Accessibility Auditing](adr/2026-07-28-hybrid-eslint-and-in-memory-accessibility-auditing.md) | Current | - Status: Accepted |
| [ADR-2293: Native Vue 3 Rules Designer with GxP Ledger Sync](adr/2026-07-28-native-vue3-rules-designer-gxp-ledger-sync.md) | Historical | Accepted |
| [ADR-048: SDV/TSDV Persistence Foundation and Migration Support](adr/2026-07-28-sdv-tsdv-persistence-foundation.md) | Historical | - Status: Accepted |
| [ADR-108: Consolidated eClinical Services and Compliance Enhancements](adr/2026-07-29-consolidated-eclinical-services-and-compliance-enhancements.md) | Historical | - Status: Accepted |
| [ADR-106: Standardize PR Quality Gate Comments and Developer Verification Workflows](adr/2026-07-29-standardize-pr-quality-gate-comments-and-developer-verification-workflows.md) | Current | - Status: Accepted |
| [ADR-049: TMF Reference Model Taxonomy Integration](adr/2026-07-29-tmf-reference-model-taxonomy-integration.md) | Historical | - Status: Accepted |
| [ADR-114: Deprecate legacy econsent helper in packages ui](adr/2026-07-30-deprecate-legacy-econsent-helper-in-packages-ui.md) | Historical | - Status: Accepted |
| [ADR-107: Durable and Immutable Subject Enrollment Sequence for TSDV](adr/2026-07-30-durable-and-immutable-subject-enrollment-sequence-for-tsdv.md) | Historical | - Status: Accepted |
| [ADR-113: eConsent Visual Editor and Subject Consent Portal UI Architecture](adr/2026-07-30-econsent-visual-editor-and-subject-consent-portal-ui-architecture.md) | Current | - Status: Accepted |
| [ADR-2294: Lab Reference Range Management](adr/2026-07-30-lab-reference-range-management.md) | Historical | Also known as: ADR-050 (former number) |
| [ADR-109: Protocol Ingestion and CRF Draft Generation Workflow](adr/2026-07-30-protocol-ingestion-and-crf-draft-generation-workflow.md) | Historical | - Status: Accepted |
| [ADR-2295: Rule Authoring, Validation, and DDF Delivery](adr/2026-07-30-rule-authoring-validation-and-ddf-delivery.md) | Historical | Accepted |
| [ADR-112: Signatures and Workflow Automation Phase 1 and 2 Primitives](adr/2026-07-30-signatures-and-workflow-automation-phase-1-and-2-primitives.md) | Historical | - Status: Accepted |
| [ADR-140: Clinical Code Lookup Input Helper in Shared UI Package](adr/2026-07-31-clinical-code-lookup-input-helper-in-shared-ui-package.md) | Current | - Status: Accepted |
| [ADR-121: Decoupling Cross-Service Python Imports and Security RBAC Isolation](adr/2026-07-31-decoupling-cross-service-python-imports-and-security-rbac-isolation.md) | Historical | - Status: Accepted |
| [ADR-051: eCOA Subject Identity, Authorization, and Gateway Routing](adr/2026-07-31-ecoa-subject-identity-and-gateway-routing.md) | Historical | - Status: Accepted |
| [ADR-139: Expose CRA Monitoring SDV Transport Models and Shared Sign-Off Logic](adr/2026-07-31-expose-cra-monitoring-sdv-transport-models-and-shared-sign-off-logic.md) | Historical | - Status: Accepted |
| [ADR-144: Expression Edit Check Phase 14 Runtime Evaluation Engine](adr/2026-07-31-expression-edit-check-phase-14-runtime-evaluation-engine.md) | Current | - Status: Accepted |
| [ADR-130: Extend Centralized Permission Matrix for Medical Coding](adr/2026-07-31-extend-centralized-permission-matrix-for-medical-coding.md) | Historical | - Status: Accepted |
| [ADR-117: GxP 21 CFR Part 11 eConsent Audit Trails](adr/2026-07-31-gxp-21-cfr-part-11-econsent-audit-trails.md) | Historical | - Status: Accepted |
| [ADR-131: Informed Consent Multi-Language Translation Management](adr/2026-07-31-informed-consent-multi-language-translation-management.md) | Historical | - Status: Accepted |
| [ADR-132: Lab Master Catalog and Unit Conversion Data Models](adr/2026-07-31-lab-master-catalog-and-unit-conversion-data-models.md) | Current | - Status: Accepted |
| [ADR-141: Modernize Python 3.10 Type Hint Syntax and Lock Ruff Rule Enforcements](adr/2026-07-31-modernize-python-310-type-hint-syntax-and-lock-ruff-rule-enforcements.md) | Historical | - Status: Accepted |
| [ADR-2296: On-Demand Consent Status Validation and Audit Hooks](adr/2026-07-31-on-demand-consent-status-validation-and-audit-hooks.md) | Historical | Also known as: ADR-121 (former number) |
| [ADR-2297: Reference Range Data Model and Persistence Updates](adr/2026-07-31-reference-range-data-model-and-persistence-updates.md) | Historical | - Status: Accepted |
| [ADR-2298: Resolve Vitest Peer Dependency Conflict in Shared UI Package](adr/2026-07-31-resolve-vitest-peer-dependency-conflict-in-shared-ui-package.md) | Current | Also known as: ADR-121 (former number) |
| [ADR-2299: SDV & TSDV Metadata and Database Migrations](adr/2026-07-31-sdv-tsdv-metadata-enhancements.md) | Historical | Also known as: ADR-049 (former number) |
| [ADR-145: SDV Verification and Compliance Tests Phase 14](adr/2026-07-31-sdv-verification-and-compliance-tests-phase-14.md) | Current | - Status: Accepted |
| [ADR-133: Upgrade Environments to Python 3.12](adr/2026-07-31-upgrade-environments-to-python-312.md) | Historical | - Status: Accepted |
| [ADR-2300: Viewport-Aware Grid Inspector for eCRF designer](adr/2026-07-31-viewport-aware-grid-inspector.md) | Current | Accepted |
| [ADR-138: Vue SPA Component Smoke Test and Vitest Pipeline Integration](adr/2026-07-31-vue-spa-component-smoke-test-and-vitest-pipeline-integration.md) | Current | - Status: Accepted |
| [ADR-189: Cryptographic Signature Verification Engine and Multi-Format Support](adr/2026-08-01-cryptographic-signature-verification-engine-and-multi-format-support.md) | Historical | - Status: Accepted |
| [ADR-120: Cryptographic Signature Verification Engine for Electronic Signatures](adr/2026-08-01-cryptographic-signature-verification-engine-for-esignatures.md) | Historical | - Status: Accepted |
| [ADR-142: Define Laboratory Master and Align ORM Models](adr/2026-08-01-define-laboratory-master-and-align-orm-models.md) | Historical | - Status: Accepted |
| [ADR-2301: Developer-Centric Architecture Alignment and Automated Drift Gating](adr/2026-08-01-dev-centric-alignment-and-automated-drift-gating.md) | Historical | Also known as: ADR-112 (former number) |
| [ADR-190: Explicit Background Task Cancellation in Ledger Sealer](adr/2026-08-01-explicit-background-task-cancellation-in-ledger-sealer.md) | Historical | - Status: Accepted |
| [ADR-2302: In-Process ASGI Routing with Live Databases](adr/2026-08-01-in-process-asgi-routing-with-live-databases.md) | Historical | Also known as: ADR-189 (former number) |
| [ADR-2303: SDV and TSDV Data Model Foundations](adr/2026-08-01-sdv-and-tsdv-data-model-foundations.md) | Historical | Also known as: ADR-189 (former number) |
| [ADR-2304: Unused ESLint Variable and DEID Exemption in UI Package](adr/2026-08-01-unused-eslint-variable-and-deid-exemption-in-ui-package.md) | Current | Also known as: ADR-145 (former number) |
| [ADR-052: Vue SPA, OIDC, RBAC, and Signing Boundaries](adr/2026-08-01-vue-spa-oidc-rbac-signing-boundaries.md) | Current | - Status: Accepted |
| [ADR-251: Centralized Accessibility Matcher and 80 Percent Coverage Gate](adr/2026-08-02-centralized-accessibility-matcher-and-80-percent-coverage-gate.md) | Historical | Amended by [ADR-2530](adr/2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md). The 80 percent |
| [ADR-2305: Dual-Mode Secret Scanner for Pre-Commit and CI Validation](adr/2026-08-02-dual-mode-secret-scanner-for-pre-commit-and-ci-validation.md) | Historical | Also known as: ADR-251 (former number) |
| [ADR-053: eConsent Scaffold and Shared Part 11 Audit Schemas](adr/2026-08-02-econsent-scaffold-and-part11-audit.md) | Historical | - Status: Accepted |
| [ADR-2306: Shared Eligibility Criteria Domain Contract, DSL Parser, and Deterministic Evaluator](adr/2026-08-02-eligibility-criteria-evaluation-engine.md) | Historical | Also known as: ADR-053 (former number) |
| [ADR-2307: Enforce Ruff Style and Formatting on API Gateway Routers](adr/2026-08-02-enforce-ruff-style-and-formatting-on-api-gateway-routers.md) | Historical | Also known as: ADR-251 (former number) |
| [ADR-2308: Enforce Strict Fail-Closed Secrets Validation and Narrow Scanner Exclusions](adr/2026-08-02-enforce-strict-fail-closed-secrets-validation-and-narrow-scanner-exclusions.md) | Historical | - Status: Accepted |
| [ADR-2309: Implement WCAG compliance in shared clinical UI primitives](adr/2026-08-02-implement-wcag-compliance-in-shared-clinical-ui-primitives.md) | Current | Also known as: ADR-251 (former number) |
| [ADR-2310: Standalone Notifications Service Foundation](adr/2026-08-02-notifications-service-foundation.md) | Historical | Also known as: ADR-053 (former number) |
| [ADR-2311: Protocol Document Rendering Architecture and Content Contract](adr/2026-08-02-protocol-rendering-architecture.md) | Historical | Also known as: ADR-053 (former number) |
| [ADR-2312: Refactor Signature Validations and Enforce Ruff S101](adr/2026-08-02-refactor-signature-validations-and-enforce-ruff-s101.md) | Historical | Also known as: ADR-120 (former number) |
| [ADR-2313: RTSM Architecture and Randomization Persistence](adr/2026-08-02-rtsm-architecture-and-randomization-persistence.md) | Historical | Also known as: ADR-053 (former number) |
| [ADR-2314: Shared SDTM Foundation Models & Terminology](adr/2026-08-02-sdtm-foundation-models.md) | Historical | Also known as: ADR-053 (former number) |
| [ADR-180: Subject Portal Accessibility Auditing and Verification](adr/2026-08-02-subject-portal-playwright-a11y-verification.md) | Historical | - Status: Accepted |
| [ADR-2315: USDM v2/v3 Canonical Contract and Mapping Coverage Matrix](adr/2026-08-02-usdm-v2-v3-canonical-contract-mapping-matrix.md) | Historical | Also known as: ADR-053 (former number) |
| [ADR-254: Centralized Clinical Form Layout Wrapper](adr/2026-08-03-centralized-clinical-form-layout-wrapper.md) | Historical | - Status: Accepted |
| [ADR-255: Clean up duplicate security entries in rbac configuration](adr/2026-08-03-clean-up-duplicate-security-entries-in-rbac-configuration.md) | Historical | - Status: Accepted |
| [ADR-2316: Decommission Legacy Standalone Frontend Rendering Engine and Legacy V1 Signatures](adr/2026-08-03-cleanup-legacy-rendering-v1-signatures.md) | Current | Also known as: ADR-254 (former number) |
| [ADR-2317: Decommission legacy V1 signatures and standalone rendering engine](adr/2026-08-03-decommission-legacy-v1-signatures-and-standalone-rendering-engine.md) | Current | Also known as: ADR-255 (former number) |
| [ADR-2318: eConsent Gateway Integration and Orchestration Port Alignment](adr/2026-08-03-econsent-gateway-integration-and-orchestration-port-alignment.md) | Historical | Also known as: ADR-254 (former number) |
| [ADR-2319: Fail-Safe GxP Document Generation and Draft Warning Banners](adr/2026-08-03-fail-safe-gxp-document-generation-and-draft-warning-banners.md) | Historical | Also known as: ADR-254 (former number) |
| [ADR-2320: Harden Serialization (RFC 8785) and Implement eConsent Schema Guards](adr/2026-08-03-harden-serialization-rfc-8785-and-implement-econsent-schema-guards.md) | Historical | Also known as: ADR-254 (former number) |
| [ADR-2321: Implement class-based touch target optimization for dense layouts](adr/2026-08-03-implement-class-based-touch-target-optimization-for-dense-layouts.md) | Current | Also known as: ADR-254 (former number) |
| [ADR-2322: Implement native uv workspaces with service-specific Dockerfiles](adr/2026-08-03-implement-native-uv-workspaces-with-service-specific-dockerfiles.md) | Historical | Also known as: ADR-255 (former number) |
| [ADR-253: Lightweight Path Pattern Boundary Linter](adr/2026-08-03-lightweight-path-pattern-boundary-linter.md) | Historical | - Status: Accepted |
| [ADR-2323: Migrate Clinical Forms to Pre-Compiled Shared UI Library](adr/2026-08-03-migrate-clinical-forms-to-pre-compiled-shared-ui-library.md) | Historical | Also known as: ADR-254 (former number) |
| [ADR-256: Native Interval Filtering and Custom Name Matching](adr/2026-08-03-native-interval-filtering-and-custom-name-matching.md) | Historical | - Status: Accepted |
| [ADR-2324: Optimized eTMF Document Registry with Store Indexing and Client-Side Pagination](adr/2026-08-03-optimized-etmf-document-registry-with-store-indexing-and-client-side-pagination.md) | Current | Also known as: ADR-254 (former number) |
| [ADR-135: Resolve RBAC Duplicate Key Static Lint Failures](adr/2026-08-03-resolve-rbac-duplicate-key-static-lint-failures.md) | Historical | - Status: Accepted |
| [ADR-2325: Ruff Lint Alignment and Centralized RBAC Validation](adr/2026-08-03-ruff-lint-alignment-and-centralized-rbac-validation.md) | Historical | Also known as: ADR-255 (former number) |
| [ADR-2326: Subject Randomization Lifecycle and State Guards](adr/2026-08-03-subject-randomization-lifecycle-and-state-guards.md) | Historical | Also known as: ADR-054 (former number) |
| [ADR-147: Task-Driven CRA Workspace Routing & Multi-Select Batch SDV](adr/2026-08-03-task-driven-cra-workspace-and-batch-sdv-verification.md) | Current | - Status: Accepted |
| [ADR-134: Upgrade Environments to Python 3.14](adr/2026-08-03-upgrade-environments-to-python-3-14.md) | Historical | - Status: Accepted |
| [ADR-2327: Upgrade Environments to Python 3.14](adr/2026-08-03-upgrade-environments-to-python-314.md) | Historical | Also known as: ADR-253 (former number) |
| [ADR-2328: Centralized layout utilities and 48px touch targets](adr/2026-08-04-centralized-layout-utilities-and-48px-touch-targets.md) | Historical | Also known as: ADR-256 (former number) |
| [ADR-257: Centralized Redis eSignature Token Replay Prevention](adr/2026-08-04-centralized-redis-esignature-token-replay-prevention.md) | Historical | - Status: Accepted |
| [ADR-2329: Compile-Time Vite Build Mode for Interactive Public Demo](adr/2026-08-04-compile-time-vite-build-mode-for-interactive-public-demo.md) | Current | Also known as: ADR-253 (former number) |
| [ADR-055: CTMS Service Boundary, Reporting, and Operational Workspace](adr/2026-08-04-ctms-service-boundary-and-reporting.md) | Historical | - Status: Accepted |
| [ADR-2158: Decoupled Hexagonal Storage Architecture for Study Designer](adr/2026-08-04-decoupled-hexagonal-storage-architecture-for-study-designer.md) | Historical | - Status: Accepted |
| [ADR-2157: Enforce Fail-Fast Environment Validation for GxP Compliance](adr/2026-08-04-enforce-fail-fast-environment-validation-for-gxp-compliance.md) | Historical | - Status: Accepted |
| [ADR-2156: Fail-Fast Environment Validation for Cryptographic Secrets](adr/2026-08-04-fail-fast-environment-validation-for-cryptographic-secrets.md) | Historical | - Status: Accepted |
| [ADR-2330: Implement Hexagonal Domain Repository Separation](adr/2026-08-04-implement-hexagonal-domain-repository-separation.md) | Historical | Also known as: ADR-2156 (former number) |
| [ADR-2331: Implement Native Interval Filtering and Custom Name Matching in Scrubber](adr/2026-08-04-implement-native-interval-filtering-and-custom-name-matching-in-scrubber.md) | Historical | Also known as: ADR-256 (former number) |
| [ADR-2155: In Memory Terminology Search Cache](adr/2026-08-04-in-memory-terminology-search-cache.md) | Historical | - Status: Accepted |
| [ADR-2332: Metadata-driven Additional Properties JSON column for ClinicalObservation](adr/2026-08-04-metadata-driven-additional-properties-json-column-for-clinicalobservation.md) | Historical | Also known as: ADR-117 (former number) |
| [ADR-2333: Multi-Node Signature Replay Prevention with Redis Fallback](adr/2026-08-04-multi-node-signature-replay-prevention-with-redis-fallback.md) | Historical | - Status: Accepted |
| [ADR-2334: Notification Delivery Channels and Retry Dispatcher](adr/2026-08-04-notification-delivery-channels-and-retry-dispatcher.md) | Historical | Also known as: ADR-054 (former number) |
| [ADR-2335: Refactor security context decorator for Python 3.14 compatibility](adr/2026-08-04-refactor-security-context-decorator-for-python-314-compatibility.md) | Historical | Also known as: ADR-256 (former number) |
| [ADR-2336: RSA-PSS Cryptographic Padding for Electronic Signatures](adr/2026-08-04-rsa-pss-cryptographic-padding-for-electronic-signatures.md) | Historical | Also known as: ADR-2157 (former number) |
| [ADR-2337: Scope CSS hover to can hover and add touch tap fallbacks](adr/2026-08-04-scope-css-hover-to-can-hover-and-add-touch-tap-fallbacks.md) | Current | Also known as: ADR-256 (former number) |
| [ADR-2338: System-Wide Hexagonal Architecture and Pytest-Archon Enforcement](adr/2026-08-04-system-wide-hexagonal-architecture-and-pytest-archon-enforcement.md) | Historical | Also known as: ADR-2156 (former number) |
| [ADR-2339: Transition to PSS Padding for RSA Signatures and Verifications](adr/2026-08-04-transition-to-pss-padding-for-rsa-signatures-and-verifications.md) | Historical | Also known as: ADR-2157 (former number) |
| [ADR-2340: Viewport-Driven DOM Recycler for Large eCRFs](adr/2026-08-04-viewport-driven-dom-recycler-for-large-ecrfs.md) | Current | Also known as: ADR-256 (former number) |
| [ADR-2341: Global Prettier Formatting and Pre-commit Checks](adr/2026-08-05-global-prettier-formatting-and-pre-commit-checks.md) | Historical | Accepted |
| [ADR-2159: PostgreSQL Migration for Regulatory Services](adr/2026-08-05-postgresql-migration-for-regulatory-services.md) | Historical | - Status: Accepted |
| [ADR-2342: Quality & CAPA Management, Platform Placement, and Traceability](adr/2026-08-05-quality-capa-management.md) | Historical | Also known as: ADR-056 (former number) |
| [ADR-2343: Arm-Aware Schedule of Activities (SoA) Matrix Component](adr/2026-08-06-arm-aware-soa-matrix.md) | Current | Also known as: ADR-057 (former number) |
| [ADR-2344: Global Monorepo Security Scanning and Fail-Fast Startup Assertions](adr/2026-08-06-monorepo-security-scanning-and-fail-fast-assertions.md) | Historical | - Status: Accepted |
| [ADR-2161: Parameterized Branding and Dynamic Domain Configurations](adr/2026-08-06-parameterized-branding-and-dynamic-domain-configurations.md) | Historical | - Status: Accepted |
| [ADR-2160: Part 11 Electronic Signature Compliance Engine Improvements](adr/2026-08-06-part-11-electronic-signature-compliance-engine-improvements.md) | Historical | - Status: Accepted |
| [ADR-2345: Purge Legacy Physical Lab Columns for Dynamic JSON Schema](adr/2026-08-06-purge-legacy-physical-lab-columns-for-dynamic-json-schema.md) | Historical | Also known as: ADR-2159 (former number) |
| [ADR-2163: Decentralize Test Directory Structure Across Workspaces](adr/2026-08-07-decentralize-test-directory-structure-across-workspaces.md) | Historical | - Status: Accepted |
| [ADR-116: ePRO Sync Durable Reconciliation, Defeated Record Retention, and Structural Queries](adr/2026-08-07-epro-sync-durable-reconciliation.md) | Historical | - Status: Accepted |
| [ADR-2164: Eradicate Core Models and Implement Anti-Corruption Layers](adr/2026-08-07-eradicate-core-models-and-implement-anti-corruption-layers.md) | Historical | - Status: Accepted |
| [ADR-2346: Route terminology APIs through the gateway](adr/2026-08-07-gateway-terminology-routing.md) | Historical | Also known as: ADR-058 (former number) |
| [ADR-2347: Organization Directory Service and Persistence Models Scaffold](adr/2026-08-07-organization-directory-service-foundation.md) | Historical | Also known as: ADR-058 (former number) |
| [ADR-2162: Skip to content link and strict layout validation](adr/2026-08-07-skip-to-content-link-and-strict-layout-validation.md) | Current | - Status: Accepted |
| [ADR-065: Biomedical Concept Locks for Active-Recruiting Studies](adr/2026-08-08-biomedical-concept-locks.md) | Historical | - Status: Accepted |
| [ADR-059: Centralized RBAC Toolkit, Permissions, Principals, and Field Masking](adr/2026-08-08-centralized-rbac-toolkit.md) | Historical | - Status: Accepted |
| [ADR-060: eCOA Portal and Interop Deployment Architecture](adr/2026-08-08-ecoa-portal-and-interop-deployment.md) | Historical | - Status: Accepted |
| [ADR-2348: Expose Authenticated SDTM/ADaM Dataset-JSON Export Endpoints](adr/2026-08-08-expose-authenticated-sdtm-adam-dataset-json-export-endpoints.md) | Historical | Also known as: ADR-059 (former number) |
| [ADR-063: Gateway Step-Up Re-Authentication and Signature Token Issuance](adr/2026-08-08-gateway-step-up-re-authentication.md) | Historical | - Status: Accepted |
| [ADR-2165: Hexagonal Architecture Standard](adr/2026-08-08-hexagonal-architecture-standard.md) | Historical | - Status: Accepted |
| [ADR-062: PI-only Atomic Batch Electronic Sign-Off in Execution](adr/2026-08-08-pi-only-batch-sign-off-execution.md) | Historical | - Status: Accepted |
| [ADR-061: RTSM Supply Domain Persistence Models](adr/2026-08-08-rtsm-supply-domain-persistence.md) | Historical | - Status: Accepted |
| [ADR-2166: Standardize Semantic Token Layer Across Platforms](adr/2026-08-08-standardize-semantic-token-layer-across-platforms.md) | Current | - Status: Accepted |
| [ADR-2349: Automated eTMF Document Redaction](adr/2026-08-09-automated-etmf-document-redaction.md) | Historical | Also known as: ADR-065 (former number) |
| [ADR-2350: eISF Browse, RBAC, Downloads, and Completeness](adr/2026-08-09-eisf-maintenance-and-formatting.md) | Historical | Also known as: ADR-065 (former number) |
| [ADR-2351: eTMF Filterable and Paginated Audit Log API](adr/2026-08-09-etmf-filterable-paginated-audit-log.md) | Historical | Also known as: ADR-062 (former number) |
| [ADR-2352: Global Library Object Instantiation in Clinical Studies](adr/2026-08-09-global-library-object-instantiation.md) | Historical | Also known as: ADR-062 (former number) |
| [ADR-2353: Medical Coding Engine and Query Subsystem Integration](adr/2026-08-09-medical-coding-engine-query-integration.md) | Historical | Also known as: ADR-065 (former number) |
| [ADR-067: Signed NCI Thesaurus CT Web Client](adr/2026-08-09-nci-thesaurus-signed-web-client.md) | Historical | - Status: Accepted |
| [ADR-064: E2B(R3) ICSR XML Export and Structural Validation](adr/2026-08-09-safety-e2b-icsr-xml-export-pipeline.md) | Historical | - Status: Superseded by [ADR-2464](adr/2026-10-20-icsr-render-emits-ich-e2br3-validated-against-the-ich-schema.md) |
| [ADR-2354: Debounced Clinical Code Lookup UI Primitive](adr/2026-08-10-debounced-clinical-code-lookup-ui-primitive.md) | Historical | Also known as: ADR-065 (former number) |
| [ADR-185: Dynamic Dependency Matching and Clinical De-identification Consolidation](adr/2026-08-10-dynamic-dependency-matching-and-clinical-deid-consolidation.md) | Historical | - Status: Accepted |
| [ADR-2167: AST-based Offline Schema Drift Detection and Babel Parser Dependency](adr/2026-08-11-ast-based-offline-schema-drift-detection.md) | Historical | - Status: Accepted |
| [ADR-086: Bidirectional Field Parity and RFC 7807 Validation Schemas](adr/2026-08-11-bidirectional-field-parity-and-rfc7807-validation-schemas.md) | Historical | - Status: Accepted |
| [ADR-2355: Browser-Native Layout-Skipping for eCRF Virtualization](adr/2026-08-11-browser-native-layout-skipping-for-ecrf-virtualization.md) | Current | Also known as: ADR-2167 (former number) |
| [ADR-2356: Centralized Brand and Domain Validation Guardrail](adr/2026-08-11-centralized-brand-and-domain-validation.md) | Historical | Also known as: ADR-189 (former number) |
| [ADR-118: Chronological Date Validation and Visit Date Constraints in SDTM Models](adr/2026-08-11-chronological-sdtm-date-validation.md) | Historical | - Status: Accepted |
| [ADR-2357: Declarative Route Dependencies and Fallback Signature Verification](adr/2026-08-11-declarative-dependencies-and-signature-verification-fallback.md) | Historical | Also known as: ADR-085 (former number) |
| [ADR-123: Decommissioning Client-Side TS Wrapper SDK and Enforcing Frontend Dependency Gates](adr/2026-08-11-decommissioning-client-side-ts-sdk-and-enforcing-dependency-gates.md) | Current | - Status: Accepted |
| [ADR-122: Decouple Shared Test Signing RBAC Helpers from Gateway](adr/2026-08-11-decoupling-shared-test-signing-rbac-helpers-from-gateway.md) | Historical | - Status: Accepted |
| [ADR-2358: Gateway Signature Legacy V2 Fallback](adr/2026-08-11-gateway-signature-legacy-v2-fallback.md) | Historical | Also known as: ADR-85 (former number) |
| [ADR-2359: Key-Based Sync Reconciliation](adr/2026-08-11-key-based-sync-reconciliation.md) | Historical | Also known as: ADR-2167 (former number) |
| [ADR-2169: Offline and Synchronous OpenAPI Schema Generation for 15 Platform Services](adr/2026-08-11-offline-and-synchronous-openapi-schema-generation-for-15-platform-services.md) | Historical | - Status: Accepted |
| [ADR-2360: Persistent Local PIN Wrapper and Secure Offline IndexedDB Encryption](adr/2026-08-11-persistent-local-pin-wrapper-and-indexeddb-security.md) | Current | - Status: Accepted |
| [ADR-2361: Pool Connection State Eviction and Checkout Resets](adr/2026-08-11-pool-connection-state-eviction-and-checkout-resets.md) | Historical | Also known as: ADR-2169 (former number) |
| [ADR-2168: Refined Mock Signature Verification Base64 Collision Prevention](adr/2026-08-11-refine-asymmetric-mock-signature-detection-for-base64-collisions.md) | Historical | - Status: Accepted |
| [ADR-2362: Relational Outbox System across Execution and eTMF Services](adr/2026-08-11-relational-outbox-system-across-execution-and-etmf-services.md) | Historical | Also known as: ADR-2169 (former number) |
| [ADR-252: Implement slot-driven dynamic accessibility context in Vue 3 SPA components](adr/2026-08-11-slot-driven-dynamic-accessibility-context.md) | Current | - Status: Accepted |
| [ADR-2363: Standardize Hatch build configurations for clean namespace wheels](adr/2026-08-11-standardize-hatch-build-configurations-for-clean-namespace-wheels.md) | Historical | Also known as: ADR-2167 (former number) |
| [ADR-2364: Standardize PR Templates and Centralize Shared Hashing & Radio Grid Layouts](adr/2026-08-11-standardize-pr-templates-and-centralize-hashing-grid-layout.md) | Historical | Also known as: ADR-85 (former number) |
| [ADR-068: Unified Parameterized Relational Database Lifespan Wrapper](adr/2026-08-11-unified-parameterized-relational-database-lifespan-wrapper.md) | Historical | - Status: Accepted |
| [ADR-2365: Unified Ports and Adapters Architecture, Transaction Propagation, and Gateway Connection Pooling](adr/2026-08-11-unified-ports-and-adapters-architecture-transaction-propagation-and-gateway-connection-pooling.md) | Historical | Also known as: ADR-2167 (former number) |
| [ADR-066: VitePress Workspace Documentation Portal](adr/2026-08-11-vitepress-workspace-documentation-portal.md) | Current | - Status: Accepted |
| [ADR-2171: Deconsolidate monolithic database models](adr/2026-08-12-deconsolidate-monolithic-database-models.md) | Historical | - Status: Accepted |
| [ADR-2170: Decoupled PostgreSQL Schema Introspection and Database Triggers for GxP Compliance](adr/2026-08-12-decoupled-postgresql-schema-introspection-and-database-triggers-for-gxp-compliance.md) | Historical | - Status: Accepted |
| [ADR-2366: Dedicated Background Processing with PostgreSQL Database-level Coordination](adr/2026-08-12-dedicated-background-processing-with-postgresql-database-level-coordination.md) | Historical | Also known as: ADR-2171 (former number) |
| [ADR-094: Deterministic GxP Report Generation and Signature Verification Rectification](adr/2026-08-12-deterministic-gxp-report-generation-and-signature-verification.md) | Historical | - Status: Accepted |
| [ADR-192: Dynamic CDASH type generation and strongly-typed clinical Vue components](adr/2026-08-12-dynamic-cdash-type-generation-and-strongly-typed-clinical-vue-components.md) | Historical | - Status: Accepted |
| [ADR-2367: NestJS API Gateway Promise Coalescing and Eager Key Prefetching](adr/2026-08-12-nestjs-api-gateway-promise-coalescing.md) | Historical | * Status: Superseded by [ADR-2256](adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md) |
| [ADR-2368: Python-Native Isolated Architecture (Strict Compliance)](adr/2026-08-12-python-native-isolated-architecture-strict-compliance.md) | Historical | - Status: Accepted |
| [ADR-2369: Secure Unblinding Signature Fallback Restriction & Scope Normalization](adr/2026-08-12-secure-unblinding-signature-fallback-restriction.md) | Historical | Also known as: ADR-86 (former number) |
| [ADR-2370: Separate Monolithic Clinical Workflows into Modular Sub-routers](adr/2026-08-12-separate-monolithic-clinical-workflows-into-modular-sub-routers.md) | Historical | - Status: Accepted |
| [ADR-2371: Unified Cryptographic Fingerprint Verification API](adr/2026-08-12-unified-cryptographic-fingerprint-verification-api.md) | Historical | Also known as: ADR-2170 (former number) |
| [ADR-2174: AI-Native USDM Protocol Digitization and Automated eCRF Synthesis](adr/2026-08-13-ai-native-usdm-protocol-digitization-and-automated-ecrf-synthesis.md) | Historical | - Status: Accepted |
| [ADR-2372: Decouple ClinicalQueryPanel from Pinia and Auth store](adr/2026-08-13-decouple-clinical-query-panel.md) | Current | Also known as: ADR-068 (former number) |
| [ADR-2373: Port Contract Compliance & Backend Static Verification](adr/2026-08-13-port-contract-compliance-and-backend-static-verification.md) | Historical | - Status: Accepted |
| [ADR-2172: Standardize on Plural Adapters with Build-time Path Linting](adr/2026-08-13-standardize-on-plural-adapters-with-build-time-path-linting.md) | Historical | - Status: Accepted |
| [ADR-2374: Standalone In-Application Ticketing Service and Platform Integration](adr/2026-08-13-tickets-service-scaffold-and-gateway-integration.md) | Historical | Also known as: ADR-067 (former number) |
| [ADR-2375: Write-Time Soft-Versioning and Active Inactive Cloning for eCRF Form Submissions](adr/2026-08-13-write-time-soft-versioning-and-active-inactive-cloning-for-ecrf-form-submissions.md) | Historical | Also known as: ADR-2172 (former number) |
| [ADR-2376: Biostatistical Export Pipeline Interoperability and Architecture](adr/2026-08-14-biostatistical-export-pipeline-interoperability.md) | Historical | Also known as: ADR-094 (former number) |
| [ADR-2179: Comprehensive Hexagonal Architecture Standardization and Frontend Modularization](adr/2026-08-14-comprehensive-hexagonal-architecture-standardization-and-frontend-modularization.md) | Historical | - Status: Accepted |
| [ADR-071: GitHub Pages Documentation Hosting](adr/2026-08-14-github-pages-documentation-hosting.md) | Historical | - Status: Superseded by ADR-2550 |
| [ADR-2175: Graph-Native Zero-Downtime Protocol Amendment and In-Flight Subject Migration Engine](adr/2026-08-14-graph-native-zero-downtime-protocol-amendment-and-in-flight-subject-migration-engine.md) | Historical | - Status: Accepted |
| [ADR-2176: Isolated Concurrent Pytest Harness and Worker Lifecycle](adr/2026-08-14-isolated-concurrent-pytest-harness-and-worker-lifecycle.md) | Historical | - Status: Accepted |
| [ADR-2173: Offline TypeScript Schema Generator using SQLModel Base Metadata](adr/2026-08-14-offline-typescript-schema-generator-using-sqlmodel-base-metadata.md) | Historical | - Status: Accepted |
| [ADR-2377: Pluggable Relational SQLModel Database Store for Security Audit Logs](adr/2026-08-14-pluggable-relational-sqlmodel-database-store-for-security-audit-logs.md) | Historical | Also known as: ADR-2173 (former number) |
| [ADR-2177: Standardized Hexagonal Architecture and Unified Developer CLI](adr/2026-08-14-standardized-hexagonal-architecture-and-unified-developer-cli.md) | Historical | - Status: Accepted |
| [ADR-2178: Standardized Hexagonal Architecture and Unified Developer Experience Suite](adr/2026-08-14-standardized-hexagonal-architecture-and-unified-developer-experience-suite.md) | Historical | - Status: Accepted |
| [ADR-2378: Bidirectional API Contract Enforcement and Legacy Whitelisting](adr/2026-08-15-bidirectional-api-contract-enforcement.md) | Historical | Also known as: ADR-095 (former number) |
| [ADR-2183: Comprehensive Clinical Issue and Operations Hub Architecture](adr/2026-08-15-comprehensive-clinical-issue-and-operations-hub-architecture.md) | Historical | - Status: Accepted |
| [ADR-2181: Comprehensive Clinical Quality and RBQM eQMS Architecture](adr/2026-08-15-comprehensive-clinical-quality-and-rbqm-eqms-architecture.md) | Historical | - Status: Accepted |
| [ADR-2180: Comprehensive CTMS Modular Architecture and Ecosystem Synchronization](adr/2026-08-15-comprehensive-ctms-modular-architecture-and-ecosystem-synchronization.md) | Historical | - Status: Accepted |
| [ADR-2182: Comprehensive Enterprise eConsent Architecture and Multi-Party Signature Pipeline](adr/2026-08-15-comprehensive-enterprise-econsent-architecture-and-multi-party-signature-pipeline.md) | Historical | - Status: Accepted |
| [ADR-2379: Parallel Continuous Integration Workflows and Local Concurrent Verification](adr/2026-08-16-parallel-ci-workflows-and-local-concurrent-execution.md) | Current | Also known as: ADR-096 (former number) |
| [ADR-2380: Centralized Permission-Based Authorization and Dynamic Mappings](adr/2026-08-17-centralized-permission-auth.md) | Historical | Also known as: ADR-097 (former number) |
| [ADR-2381: Fast Version-Check with Immediate Quarantine and Client Alerting](adr/2026-08-17-fast-version-check-with-immediate-quarantine-and-client-alerting.md) | Current | Also known as: ADR-2179 (former number) |
| [ADR-2382: Frontend Standardization, CSS Grid Layouts, and Centralized UI Utilities](adr/2026-08-17-frontend-standardization-css-grid-and-centralized-utilities.md) | Historical | Also known as: ADR-097 (former number) |
| [ADR-2383: Interactive Mermaid Diagrams and Responsive Layout Controls](adr/2026-08-17-interactive-mermaid-diagrams-and-zoom-pan-controls.md) | Historical | Also known as: ADR-097 (former number) |
| [ADR-2384: Standardized PostgreSQL Write Verification and eTMF Immutability Triggers](adr/2026-08-17-standardized-postgresql-write-verification-and-etmf-immutability-triggers.md) | Historical | - Status: Accepted |
| [ADR-2385: Unified Database Schema Introspection and Automated Type Synchronization Gating](adr/2026-08-17-unified-database-introspection-and-type-gating.md) | Historical | Also known as: ADR-2174 (former number) |
| [ADR-2386: Unified De-identification Compliance Scan CLI, Pre-commit Hook, and Blocking CI Gate](adr/2026-08-17-unified-deid-compliance-scan-cli.md) | Historical | Also known as: ADR-097 (former number) |
| [ADR-098: Document Redaction Architecture, Regulatory Data-Handling, and Compliance Profiles](adr/2026-08-18-document-redaction-architecture.md) | Historical | - Status: Accepted |
| [ADR-2184: Localize datetime helpers in security package](adr/2026-08-18-localize-datetime-helpers-in-security-package.md) | Historical | - Status: Accepted |
| [ADR-2387: Metadata-Bound Filtering and Consent Abstraction for Transaction Auditing](adr/2026-08-18-metadata-bound-filtering-and-consent-abstraction-for-transaction-auditing.md) | Historical | Also known as: ADR-2184 (former number) |
| [ADR-2185: Dynamic Subject Enrollment and eCRF Visit Execution with Real Time Edit Checks](adr/2026-08-19-dynamic-subject-enrollment-and-ecrf-visit-execution-with-real-time-edit-checks.md) | Historical | - Status: Accepted |
| [ADR-111: Structured Expiration Metadata and Migration Runners for eTMF/eISF](adr/2026-08-19-etmf-eisf-structured-expiration-metadata-and-migrations.md) | Historical | - Status: Accepted |
| [ADR-099: Global Library Architecture, Lifecycle, and Multi-Tenant Isolation](adr/2026-08-19-global-library-architecture-lifecycle-tenancy.md) | Historical | - Status: Accepted |
| [ADR-105: Codebase Cleanup, Test Pipeline Fixes, and Documentation Preflight Standardization](adr/2026-08-20-codebase-cleanup-and-test-pipeline-fixes.md) | Historical | - Status: Accepted |
| [ADR-2388: Targeted E2E Contrast Enforcement and Style Token Upgrades](adr/2026-08-20-targeted-e2e-contrast-enforcement-and-style-token-upgrades.md) | Historical | Also known as: ADR-120 (former number) |
| [ADR-2191: AI Gateway Microservice and Three-Tier Clinical Intelligence Architecture](adr/2026-08-21-ai-gateway-microservice-and-three-tier-clinical-intelligence-architecture.md) | Historical | - Status: Accepted |
| [ADR-2195: Asynchronous Cross-Domain eCRF Anomaly Detection Worker](adr/2026-08-21-asynchronous-cross-domain-ecrf-anomaly-detection-worker.md) | Historical | - Status: Accepted |
| [ADR-2194: Asynchronous Protocol Digitization Stage DAG with USDM Compilation](adr/2026-08-21-asynchronous-protocol-digitization-stage-dag-with-usdm-compilation.md) | Historical | - Status: Accepted |
| [ADR-2189: Comprehensive Developer Tooling Native MCP Server and Staged Guardrails](adr/2026-08-21-comprehensive-developer-tooling-native-mcp-server-and-staged-guardrails.md) | Historical | - Status: Accepted |
| [ADR-2190: Comprehensive Developer Tooling Overhaul](adr/2026-08-21-comprehensive-developer-tooling-overhaul.md) | Historical | - Status: Accepted |
| [ADR-110: eTMF Inbound-Email Webhook and Binder Routing](adr/2026-08-21-etmf-inbound-email-webhook-and-binder-routing.md) | Historical | - Status: Accepted |
| [ADR-2196: Generative Pharmacovigilance Safety Narratives with Part 11 E-Signature](adr/2026-08-21-generative-pharmacovigilance-safety-narratives-with-part-11-e-signature.md) | Historical | - Status: Accepted |
| [ADR-2192: Grounded Protocol Knowledge Hub and Support Ticket RAG Triage](adr/2026-08-21-grounded-protocol-knowledge-hub-and-support-ticket-rag-triage.md) | Historical | - Status: Accepted |
| [ADR-2197: Hybrid Semantic Medical Coding Pipeline with pgvector](adr/2026-08-21-hybrid-semantic-medical-coding-pipeline-with-pgvector.md) | Historical | - Status: Accepted |
| [ADR-2187: Knowledge Hub Multi-Channel Notifications and Preferences Architecture](adr/2026-08-21-knowledge-hub-multi-channel-notifications-and-preferences-architecture.md) | Current | - Status: Accepted |
| [ADR-2188: Knowledge Hub Two-Tier Immutable Data Model](adr/2026-08-21-knowledge-hub-two-tier-immutable-data-model.md) | Historical | - Status: Accepted |
| [ADR-2193: Multimodal eTMF and eISF Document Intelligence and DIA Reference Model Classifier](adr/2026-08-21-multimodal-etmf-and-eisf-document-intelligence-and-dia-reference-model-classifier.md) | Historical | - Status: Accepted |
| [ADR-2186: Object Storage Port and Envelope Security Architecture](adr/2026-08-21-object-storage-port-and-envelope-security-architecture.md) | Historical | - Status: Accepted |
| [ADR-2389: Shared AES-GCM Offline At-Rest Encryption and Key Derivation Contract](adr/2026-08-22-aes-gcm-offline-at-rest-encryption.md) | Historical | Also known as: ADR-099 (former number) |
| [ADR-2200: Automated Site Financials Fee Matrix and Real-Time Payable Engine](adr/2026-08-22-automated-site-financials-fee-matrix-and-real-time-payable-engine.md) | Current | - Status: Accepted |
| [ADR-2202: Country-Aware Greenlight Site Activation and Execution Gating Engine](adr/2026-08-22-country-aware-greenlight-site-activation-and-execution-gating-engine.md) | Current | - Status: Accepted |
| [ADR-2204: Cumulative Mean Kinetic Temperature Stability Budget Engine](adr/2026-08-22-cumulative-mean-kinetic-temperature-stability-budget-engine.md) | Historical | - Status: Accepted |
| [ADR-2203: Hermetic Containerized Statistical Computing Environment and Role-Gated Unblinded Data Vault](adr/2026-08-22-hermetic-containerized-statistical-computing-environment-and-role-gated-unblinded-data-vault.md) | Historical | - Status: Accepted |
| [ADR-2390: Hybrid FHIR-to-CDISC Semantic Interoperability Mapper](adr/2026-08-22-hybrid-fhir-to-cdisc-semantic-interoperability-mapper.md) | Current | Accepted |
| [ADR-2391: Shared Semantic Regulated-Action Contract for Step-Up Re-Authentication](adr/2026-08-22-move-to-shared-semantic-regulated-action-contract.md) | Historical | Also known as: ADR-099 (former number) |
| [ADR-2201: Reactive Cross-App Clinical Event Spine and Transactional Outbox](adr/2026-08-22-reactive-cross-app-clinical-event-spine-and-transactional-outbox.md) | Historical | - Status: Superseded by ADR-2428 |
| [ADR-2198: Self-Hosted CI Runner and Trunk-Based Branching Lifecycle](adr/2026-08-22-self-hosted-ci-runner-and-trunk-based-branching-lifecycle.md) | Historical | - Status: Accepted |
| [ADR-2199: Sovereign All-in-One eClinical Suite Expansion](adr/2026-08-22-sovereign-all-in-one-eclinical-suite-expansion.md) | Historical | - Status: Accepted |
| [ADR-2392: Automated Aggregate Safety Compiler (DSUR / PSUR / PBRER) and Exposure Calculator](adr/2026-08-23-automated-aggregate-safety-compiler-and-exposure-calculator.md) | Historical | Also known as: ADR-2199 (former number) |
| [ADR-2210: Automated Golden Path Video Recording and GDrive Sync](adr/2026-08-23-automated-golden-path-video-recording-and-gdrive-sync.md) | Historical | Amended by [ADR-2505](adr/2026-09-30-recording-evidence-goes-to-controlled-object-storage.md). Recording evidence is |
| [ADR-2213: Autonomous Cross-Domain Quality Surveillance Runner Architecture](adr/2026-08-23-autonomous-cross-domain-quality-surveillance-runner-architecture.md) | Historical | - Status: Accepted |
| [ADR-2206: Cell & Gene Therapy Vein-to-Vein Chain of Custody and Identity](adr/2026-08-23-cell-and-gene-therapy-vein-to-vein-chain-of-custody-and-identity.md) | Historical | - Status: Accepted |
| [ADR-2393: Clinical Endpoint Adjudication Committee (CEC) Dossier & 2+1 Blinded Review Suite](adr/2026-08-23-clinical-endpoint-adjudication-committee-cec-dossier-and-2plus1-blinded-review-suite.md) | Historical | Also known as: ADR-2199 (former number) |
| [ADR-2212: DDF Compiler Pipeline Architecture](adr/2026-08-23-ddf-compiler-pipeline-architecture.md) | Historical | - Status: Accepted |
| [ADR-2215: Decompose Payments Domain Modules](adr/2026-08-23-decompose-payments-domain-modules.md) | Historical | - Status: Accepted |
| [ADR-2211: Deep Adjudication Workflow Seam](adr/2026-08-23-deep-adjudication-workflow-seam.md) | Historical | - Status: Accepted |
| [ADR-2214: Deep Regulatory Transmission Gateway and Multi-Tier Acknowledgement Seam](adr/2026-08-23-deep-regulatory-transmission-gateway-and-multi-tier-acknowledgement-seam.md) | Historical | - Status: Accepted |
| [ADR-2216: Deepen CDISC Study Submission Compiler](adr/2026-08-23-deepen-cdisc-study-submission-compiler.md) | Historical | - Status: Accepted |
| [ADR-2394: Delegation of Authority Log Archive Destination and Taxonomy Mapping Decisions](adr/2026-08-23-doa-archive-destination-and-taxonomy.md) | Historical | Also known as: ADR-099 (former number) |
| [ADR-2207: GxP Telehealth Video Consultation, Direct-to-Patient Logistics, and Tiered Pediatric Assent](adr/2026-08-23-gxp-telehealth-video-consultation-direct-to-patient-logistics-and-tiered-pediatric-assent.md) | Historical | - Status: Accepted |
| [ADR-2209: Medical Device and IVD Investigation Suite with UDI and Malfunction Quarantine](adr/2026-08-23-medical-device-and-ivd-investigation-suite-with-udi-and-malfunction-quarantine.md) | Historical | - Status: Accepted |
| [ADR-2205: Multi-Echelon RTSM Supply Hierarchy and Predictive Resupply](adr/2026-08-23-multi-echelon-rtsm-supply-hierarchy-and-predictive-resupply.md) | Historical | - Status: Accepted |
| [ADR-2208: Participant Financial Wallet and EU CTR Lay Summary Portal](adr/2026-08-23-participant-financial-wallet-and-eu-ctr-lay-summary-portal.md) | Current | - Status: Accepted |
| [ADR-2395: SMART on FHIR Ingestion Pipeline & CRC Source Data Reconciliation Drawer](adr/2026-08-23-smart-backend-services-and-crc-reconciliation-drawer.md) | Historical | Also known as: ADR-2199 (former number) |
| [ADR-2396: Ticket Read Audit Policy and Paginated Audit Log Retrieval](adr/2026-08-23-ticket-read-audit-policy-and-pagination.md) | Historical | Also known as: ADR-099 (former number) |
| [ADR-100: Tickets-to-Notifications Integration Architecture](adr/2026-08-23-tickets-notifications-integration.md) | Historical | - Status: Accepted |
| [ADR-2397: Antigravity GxP Lifecycle Hooks and AI Coding Guardrails](adr/2026-08-24-antigravity-gxp-lifecycle-hooks-and-agent-guardrails.md) | Historical | Also known as: ADR-2192 (former number) |
| [ADR-2218: ClinicalIssueHub Module-Based Routing and Dual-Target SLAs](adr/2026-08-24-clinicalissuehub-module-based-routing-and-dual-target-slas.md) | Historical | - Status: Accepted |
| [ADR-2398: Domain-Specific MCP Tool Extensions, Graph Introspection, and Autonomous Agent Sidecars](adr/2026-08-24-domain-mcp-tools-graph-introspection-and-agent-sidecars.md) | Historical | Also known as: ADR-2193 (former number) |
| [ADR-2217: Dynamic Consent Engine Re-Consent Gating and Lifecycle](adr/2026-08-24-dynamic-consent-engine-re-consent-gating-and-lifecycle.md) | Historical | - Status: Accepted |
| [ADR-101: Role Authorization for Granular Data-Lock Actions and Trust Boundary Verification](adr/2026-08-24-role-authorization-for-granular-data-lock-actions.md) | Historical | - Status: Accepted |
| [ADR-2399: Support Tickets SLA/Overdue Escalation and Notification De-Duplication](adr/2026-08-24-tickets-escalation-and-deduplication.md) | Historical | Also known as: ADR-101 (former number) |
| [ADR-2400: Autonomous Multi Criteria Cohort Reasoning and Synthetic Feasibility Engine](adr/2026-08-25-autonomous-multi-criteria-cohort-reasoning-and-synthetic-feasibility-engine.md) | Historical | Also known as: ADR-2218 (former number) |
| [ADR-2401: CTMS Offline Sync Reconciliation and Structural Clinical Queries](adr/2026-08-25-ctms-offline-sync-reconciliation-and-structural-clinical-queries.md) | Historical | Also known as: ADR-107 (former number) |
| [ADR-2402: Hybrid Multi-Step Clinical Reasoning Core and Protocol Grounding Gate](adr/2026-08-25-hybrid-multi-step-clinical-reasoning-core-and-protocol-grounding-gate.md) | Historical | Also known as: ADR-2217 (former number) |
| [ADR-2403: Standardize Microservice Domain Exceptions RFC 7807 Handlers and Repository Layer](adr/2026-08-25-standardize-microservice-domain-exceptions-rfc-7807-handlers-and-repository-layer.md) | Historical | Also known as: ADR-2217 (former number) |
| [ADR-2404: Targeted Schema Provenance and Post Synthesis Faithfulness Scoring](adr/2026-08-25-targeted-schema-provenance-and-post-synthesis-faithfulness-scoring.md) | Historical | Also known as: ADR-2217 (former number) |
| [ADR-115: Durable Dictionary-Import Worker and Recovery Contract](adr/2026-08-26-durable-dictionary-import-worker.md) | Historical | - Status: Proposed |
| [ADR-2405: Durable and Idempotent eTMF Expiration Scanning Service](adr/2026-08-26-durable-idempotent-etmf-expiration-scanner.md) | Historical | Also known as: ADR-112 (former number) |
| [ADR-2406: eISF Gateway Integration, Docker-Compose Wiring, and eTMF Sync Topology](adr/2026-08-26-eisf-gateway-compose-and-etmf-sync-topology.md) | Historical | Also known as: ADR-111 (former number) |
| [ADR-2407: eSignature-Backed Delegation of Authority (DOA) Log and Task Delegation Service](adr/2026-08-26-esignature-backed-doa-task-delegation-service.md) | Historical | Also known as: ADR-114 (former number) |
| [ADR-2408: GxP Compliance Change Request Workflow](adr/2026-08-26-gxp-compliance-change-request-workflow.md) | Historical | Also known as: ADR-117 (former number) |
| [ADR-2409: SAE Reconciliation Architecture](adr/2026-08-26-sae-reconciliation-architecture.md) | Historical | Also known as: ADR-111 (former number) |
| [ADR-2410: SDTM/ADaM Export Privacy: Deterministic Pseudonymization and Date De-identification](adr/2026-08-26-sdtm-adam-export-privacy.md) | Historical | Also known as: ADR-108 (former number) |
| [ADR-2411: Site-Aware Synchronized Document Deduplication](adr/2026-08-26-site-aware-synchronized-document-deduplication.md) | Historical | Also known as: ADR-112 (former number) |
| [ADR-2412: Add SQLModel Dependency for eISF Structured Section Taxonomy and Versioning](adr/2026-08-27-add-sqlmodel-dependency-for-eisf-taxonomy-and-versioning.md) | Historical | Also known as: ADR-117 (former number) |
| [ADR-2413: CDASH-to-SDTM Domain Mapping & Mapped Record Database Persistence](adr/2026-08-27-cdash-to-sdtm-domain-mapping-and-persistence.md) | Historical | Also known as: ADR-117 (former number) |
| [ADR-2414: Exposing Document Storage and Study Archival REST Endpoints](adr/2026-08-27-document-management-and-study-archival-endpoints.md) | Historical | Also known as: ADR-118 (former number) |
| [ADR-2415: eTMF Expiration Alert Notification Dispatch and Recipient Routing](adr/2026-08-27-etmf-expiration-notifications-dispatch.md) | Historical | Also known as: ADR-117 (former number) |
| [ADR-2416: Offline Data Ingestion & Sync Engine](adr/2026-08-27-offline-data-reconciliation-integrity.md) | Historical | Also known as: ADR-117 (former number) |
| [ADR-2417: 21 CFR Part 11 Batch Electronic Signatures for PI Casebook Sign-Off](adr/2026-08-27-prd-sys-001-compliance-batch-e-signatures.md) | Current | Also known as: ADR-118 (former number) |
| [ADR-2231: AI-Enhanced CDISC-Native Visual Metadata IDE and Tripartite eCRF Designer Standard](adr/2026-08-28-ai-enhanced-cdisc-native-visual-metadata-ide-and-tripartite-ecrf-designer-standard.md) | Historical | Amended by [ADR-2504](adr/2026-09-30-the-tripartite-form-definition-is-not-carried-forward.md). The tripartite JSON docum... |
| [ADR-2219: Antigravity Workspace Plugin and Skill Architecture](adr/2026-08-28-antigravity-workspace-plugin-and-skill-architecture.md) | Historical | - Status: Accepted |
| [ADR-2418: Centralized RBAC Matrix for Metadata Designer Mutations](adr/2026-08-28-centralized-rbac-matrix-for-metadata-designer-mutations.md) | Historical | Also known as: ADR-120 (former number) |
| [ADR-2227: Decoupled Persona Composable and Capability Pattern for eClinical Workspaces](adr/2026-08-28-decoupled-persona-composable-and-capability-pattern-for-eclinical-workspaces.md) | Historical | - Status: Accepted |
| [ADR-2221: Discrepancy Event Outbox Clinical Issue Hub and Multi Channel Notifications](adr/2026-08-28-discrepancy-event-outbox-clinical-issue-hub-and-multi-channel-notifications.md) | Historical | - Status: Accepted |
| [ADR-2154: Dynamic Hover Pointer Capability Detection & Touch-Safe Glossary Fallbacks](adr/2026-08-28-dynamic-hover-detection-and-touch-safe-glossary-fallbacks.md) | Historical | - Status: Accepted |
| [ADR-119: Extend Centralized Permission Matrix for Clinical Execution](adr/2026-08-28-extend-centralized-permission-matrix-for-execution.md) | Historical | - Status: Accepted |
| [ADR-2225: Gateway Proxy Dispatch Routing and RFC 7807 Error Boundaries](adr/2026-08-28-gateway-proxy-dispatch-routing-and-rfc-7807-error-boundaries.md) | Historical | - Status: Accepted |
| [ADR-2229: Hybrid CQRS Unit of Work and Dual-Defense GxP Audit Architecture](adr/2026-08-28-hybrid-cqrs-unit-of-work-and-dual-defense-gxp-audit-architecture.md) | Historical | - Status: Superseded by ADR-2428 |
| [ADR-2228: Metadata-Driven Engine Architecture and Regulated Domain DSL Standard](adr/2026-08-28-metadata-driven-engine-architecture-and-regulated-domain-dsl-standard.md) | Historical | - Status: Accepted |
| [ADR-2224: Multi Persona Golden Path Suite Video Capture and RTM Synchronization](adr/2026-08-28-multi-persona-golden-path-suite-video-capture-and-rtm-synchronization.md) | Historical | - Status: Accepted |
| [ADR-2226: Programmatic VitePress API Documentation Engine](adr/2026-08-28-programmatic-vitepress-api-documentation-engine.md) | Historical | - Status: Accepted |
| [ADR-2222: Real Time Payable Ledger Holdbacks and Patient Compensation Wallet](adr/2026-08-28-real-time-payable-ledger-holdbacks-and-patient-compensation-wallet.md) | Historical | - Status: Accepted |
| [ADR-2419: Reference Range Data Model & Persistence](adr/2026-08-28-reference-range-data-model-and-persistence.md) | Historical | Also known as: ADR-061 (former number) |
| [ADR-2230: RTSM Stochastic Randomization and Predictive Supply Chain Engine Standard](adr/2026-08-28-rtsm-stochastic-randomization-and-predictive-supply-chain-engine-standard.md) | Historical | - Status: Accepted |
| [ADR-2420: SDV Sign-Off Workflow & Automatic Verification Drop](adr/2026-08-28-sdv-sign-off-workflow-and-automatic-verification-drop.md) | Historical | Also known as: ADR-119 (former number) |
| [ADR-2421: Shared Domain Vocabulary and Authorization Foundation](adr/2026-08-28-shared-domain-vocabulary-and-authorization-foundation.md) | Historical | Also known as: ADR-120 (former number) |
| [ADR-129: Shared eSignature Foundations](adr/2026-08-28-shared-esignature-foundations.md) | Historical | - Status: Accepted |
| [ADR-2220: Site Greenlight Governance Regulatory Binders and DOA Signing](adr/2026-08-28-site-greenlight-governance-regulatory-binders-and-doa-signing.md) | Historical | - Status: Accepted |
| [ADR-2223: Specialized Science Safety Submissions CSM Forensics and AI Air Gap](adr/2026-08-28-specialized-science-safety-submissions-csm-forensics-and-ai-air-gap.md) | Current | - Status: Accepted |
| [ADR-2236: E2B R3 Suspect Drug WHODrug Active Ingredient Validation and AS2 PreFlight Gating](adr/2026-08-29-e2b-r3-suspect-drug-whodrug-active-ingredient-validation-and-as2-preflight-gating.md) | Historical | - Status: Accepted |
| [ADR-2232: Financial and Site Operations Engines Standard](adr/2026-08-29-financial-and-site-operations-engines-standard.md) | Historical | - Status: Accepted |
| [ADR-2237: Multi-Tenant Database Schema Provisioning and Automated Migration Pipeline](adr/2026-08-29-multi-tenant-database-schema-provisioning-and-automated-migration-pipeline.md) | Historical | - Status: Superseded by [ADR-2250](adr/2026-09-29-kernel-tenant-isolation-via-row-level-security.md) |
| [ADR-2234: Pure JSONB Observation Storage and Columnar Dataset JSON Exporter](adr/2026-08-29-pure-jsonb-observation-storage-and-columnar-dataset-json-exporter.md) | Historical | - Status: Accepted |
| [ADR-2235: Schedule of Activities Matrix WCAG 2.1 AA 2D Grid Navigation and Background Sync Hardening](adr/2026-08-29-schedule-of-activities-matrix-wcag-21-aa-2d-grid-navigation-and-background-sync-hardening.md) | Current | - Status: Accepted |
| [ADR-2422: SDV and TSDV Endpoint Hardening, RBAC Permission Mapping, and Local Study Scope Enforcement](adr/2026-08-29-sdv-tsdv-endpoint-hardening-rbac-study-scope.md) | Historical | Also known as: ADR-120 (former number) |
| [ADR-2423: Graph Schema Extensions for Arm-Aware Schedule of Activities (SoA)](adr/2026-08-30-soa-graph-schema-extensions.md) | Historical | Accepted |
| [ADR-2424: Vue SPA Foundation and Tooling Setup](adr/2026-08-30-vue-spa-foundation-tooling.md) | Current | Also known as: ADR-132 (former number) |
| [ADR-2425: Randomization and Trial Supply Management](adr/2026-08-31-randomization-and-trial-supply-management.md) | Historical | Also known as: ADR-132 (former number) |
| [ADR-2426: SAE Reconciliation & Safety Gateway (E2B)](adr/2026-09-01-sae-reconciliation-safety-gateway-e2b.md) | Historical | Accepted |
| [ADR-2427: Bypass Unused Parameter ESLint in Shared UI](adr/2026-09-02-bypass-unused-parameter-eslint-in-shared-ui.md) | Current | Accepted |
| [ADR-188: Clinical Rules and Query Designer Tabs](adr/2026-09-02-clinical-rules-and-query-designer-tabs.md) | Historical | - Status: Accepted |
| [ADR-2428: Core Service Architecture CQRS Transactional Outbox and GxP Interceptor Standard](adr/2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md) | Current | Also known as: ADR-2234 (former number) |
| [ADR-2233: Migration to .NET 10 Architecture and Standards](adr/2026-09-02-migration-to-dotnet-10-architecture-and-standards.md) | Current | - Status: Accepted |
| [ADR-2429: Unified GxP Traceability and Tooling Pipeline for .NET 10 Migration](adr/2026-09-02-unified-gxp-traceability-and-tooling-pipeline-for-net-10-migration.md) | Historical | Also known as: ADR-2235 (former number) |
| [ADR-2430: Centralized Study Scope and Lab Range Permissions](adr/2026-09-03-centralized-study-scope-and-lab-range-permissions.md) | Historical | Also known as: ADR-145 (former number) |
| [ADR-2240: Full-Stack Environment Setup Wizard and Python Workspace Glob Hardening](adr/2026-09-03-full-stack-environment-setup-wizard-and-python-workspace-glob-hardening.md) | Historical | - Status: Accepted |
| [ADR-2239: Universal Decimal Financial Precision Dynamic OpenAPI Gateway Discovery and Tier 2 Quality Realignment](adr/2026-09-03-universal-decimal-financial-precision-dynamic-openapi-gateway-discovery-and-tier-2-quality-realignment.md) | Historical | - Status: Accepted |
| [ADR-2238: Universal PostgreSQL Persistence Standard and Hybrid JSONB Record Projection](adr/2026-09-03-universal-postgresql-persistence-standard-and-hybrid-jsonb-record-projection.md) | Historical | - Status: Accepted |
| [ADR-146: Dynamic Port Diagnostics and Fallback Realignment](adr/2026-09-04-dynamic-port-diagnostics-and-fallback-realignment.md) | Current | - Status: Accepted |
| [ADR-2431: Global Library Governance & Release Management](adr/2026-09-04-global-library-governance-release-management.md) | Historical | Also known as: ADR-146 (former number) |
| [ADR-2432: Interactive SoA Builder Web Client (Phase 19)](adr/2026-09-04-interactive-soa-builder-web-client-phase-19.md) | Current | Also known as: ADR-134 (former number) |
| [ADR-2241: Pin uv Python Preference to System for Python 3.14](adr/2026-09-04-pin-uv-python-preference-to-system-for-python-314.md) | Historical | - Status: Accepted |
| [ADR-250: Extend Centralized Permission Matrix for eCOA and ePRO](adr/2026-09-05-extend-centralized-permission-matrix-for-ecoa.md) | Historical | - Status: Accepted |
| [ADR-2433: Enforce Strict Fail-Closed Environment Rules and Narrow Scanner Exclusions](adr/2026-09-06-enforce-strict-fail-closed-environment-rules.md) | Historical | Also known as: ADR-120 (former number) |
| [ADR-2434: GxP Compliance Reporting CI Alignment and Local Dependency Configuration](adr/2026-09-06-gxp-compliance-reporting-ci-alignment.md) | Historical | Also known as: ADR-252 (former number) |
| [ADR-2435: Extend Centralized Permission Matrix for eCOA Diary Alert Actions](adr/2026-09-07-extend-ecoa-diary-alert-permissions.md) | Historical | Also known as: ADR-251 (former number) |
| [ADR-2436: Decommission legacy rendering and v1 signatures](adr/2026-09-08-decommission-legacy-rendering-and-v1-signatures.md) | Current | Also known as: ADR-252 (former number) |
| [ADR-2437: Decommission Legacy V1 Signatures and Standalone Rendering Engine](adr/2026-09-08-decommission-legacy-v1-signatures-and-rendering-engine.md) | Current | Also known as: ADR-252 (former number) |
| [ADR-2438: RSA Signature Verification Fallback to PKCS1v15](adr/2026-09-09-rsa-signature-verification-fallback-pkcs1v15.md) | Historical | Also known as: ADR-2158 (former number) |
| [ADR-193: Align Ledger Block Signature and Deduplicate Pinia Dependency](adr/2026-09-10-align-ledger-block-signature-and-deduplicate-pinia-dependency.md) | Current | - Status: Accepted |
| [ADR-2439: Shared Zod Schemas and Backend Immutability](adr/2026-09-10-shared-zod-schemas-and-backend-immutability.md) | Historical | Also known as: ADR-2160 (former number) |
| [ADR-328: Preservation of CDASH Type Definition Pristine State](adr/2026-09-11-preservation-of-cdash-type-definition-pristine-state.md) | Current | - Status: Accepted |
| [ADR-2440: Site Compliance Cache and Transition Gating](adr/2026-09-11-site-compliance-cache-and-transition-gating.md) | Historical | Also known as: ADR-2162 (former number) |
| [ADR-2441: In-Process Clinical De-Identification and Gateway USDM Validation Router](adr/2026-09-12-in-process-clinical-deid-and-gateway-usdm-router.md) | Historical | Also known as: ADR-2161 (former number) |
| [ADR-2442: MSW Mock Gateway Simulator and Browser HMAC Verification Engine](adr/2026-09-12-msw-mock-gateway-and-browser-hmac-verification.md) | Historical | Also known as: ADR-2185 (former number) |
| [ADR-2443: Decommission Legacy Python Compose Services and Set .NET 10 as Default Runtime](adr/2026-09-25-decommission-legacy-python-services-and-default-dotnet10-runtime.md) | Current | Also known as: ADR-2240 (former number) |
| [ADR-2444: Decommission Python Scripts to Native .NET 10 Tooling Services and Cadence.Cli Commands](adr/2026-09-25-decommission-python-scripts-to-native-dotnet-10-tooling.md) | Current | - Status: Accepted |
| [ADR-2445: Payments Domain Primitive Decimal Conversion & EF Core Explicit Precision Mapping](adr/2026-09-25-payments-primitive-decimal-conversion-and-explicit-ef-core-precision.md) | Current | Also known as: ADR-2241 (former number) |
| [ADR-2242: Phased Workspace Member Unlinking and Toolchain Decommissioning Protocol](adr/2026-09-25-phased-workspace-member-unlinking-and-toolchain-decommissioning-protocol.md) | Current | - Status: Accepted |
| [ADR-2243: Topological Tier-Based .NET 10 Migration and Legacy Python Decommissioning](adr/2026-09-28-topological-tier-based-dotnet-10-migration-and-python-decommissioning.md) | Current | - Status: Accepted |
| [ADR-2257: Attributable Audit Actors and Named Service Accounts](adr/2026-09-29-attributable-audit-actors-and-named-service-accounts.md) | Current | - Status: Accepted |
| [ADR-2248: Cadence Security Formatting and Query Style Alignment](adr/2026-09-29-cadence-security-formatting-and-query-style-alignment.md) | Current | - Status: Accepted |
| [ADR-2244: Clinical Kernel Modular Monolith Architecture](adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) | Current | - Status: Accepted |
| [ADR-2247: Clinical Kernel Single Database Schema and Architecture Sentinel Hardening](adr/2026-09-29-clinical-kernel-single-database-schema-and-architecture-sentinel-hardening.md) | Current | - Status: Accepted |
| [ADR-2252: Concept Registry with CDISC Biomedical Concepts as the Semantic Key (K005)](adr/2026-09-29-concept-registry-cdisc-biomedical-concepts-as-semantic-key.md) | Current | - Status: Accepted |
| [ADR-2259: Electronic Signature Step-Up Re-Authentication](adr/2026-09-29-electronic-signature-step-up-re-authentication.md) | Current | - Status: Accepted |
| [ADR-2256: Fail-Closed Gateway Identity and Signed Caller Authentication](adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md) | Current | Also known as: ADR-2250 (former number) |
| [ADR-2448: Gateway Routes Verified Against Service Endpoints](adr/2026-09-29-gateway-routes-verified-against-service-endpoints.md) | Current | - Status: Accepted |
| [ADR-2451: Gateway Timeouts and Health-Checked Clusters](adr/2026-09-29-gateway-timeouts-and-health-checked-clusters.md) | Current | - Status: Accepted |
| [ADR-2253: Identity Vault and Layered Participant Identity (K006)](adr/2026-09-29-identity-vault-and-layered-participant-identity.md) | Current | - Status: Accepted |
| [ADR-2251: Kernel Authority Computed from Evidence via a Hand-Coded Policy Evaluator (K004)](adr/2026-09-29-kernel-authority-computed-from-evidence-policy-engine.md) | Current | - Status: Accepted |
| [ADR-2449: Kernel Command Pipeline, Transactional Write Set and Per-Tenant Audit Chain](adr/2026-09-29-kernel-command-pipeline-transactional-write-set-and-per-tenant-audit-chain.md) | Current | - Status: Accepted |
| [ADR-2255: Kernel Lineage Graph and End-to-End Scientific Traceability (K009)](adr/2026-09-29-kernel-lineage-graph-and-scientific-traceability.md) | Current | - Status: Accepted |
| [ADR-2250: Kernel Tenant Isolation via Row-Level Security in a Shared Schema (K003)](adr/2026-09-29-kernel-tenant-isolation-via-row-level-security.md) | Current | - Status: Accepted |
| [ADR-2447: Module Schemas via Search Path and Migration Deployment Modes](adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md) | Current | - Status: Accepted |
| [ADR-2249: Native AOT CLI Compilation and Makefile Toolchain Binding](adr/2026-09-29-native-aot-cli-compilation-and-makefile-binding.md) | Current | - Status: Accepted |
| [ADR-2261: Native C# De-Identification Engine and Cryptographic Canonical Signing Foundation](adr/2026-09-29-native-csharp-deid-engine-and-canonical-signing.md) | Current | Also known as: ADR-20260929 (former number) |
| [ADR-2245: Native PostgreSQL ProtocolVersion Snapshots and Neo4j Retirement](adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md) | Current | - Status: Accepted |
| [ADR-2260: No Default Credentials and Fail-Closed Secret Resolution](adr/2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md) | Current | - Status: Accepted |
| [ADR-2246: PostgreSQL Native Bitemporal Interval Standard via tstzrange and GiST](adr/2026-09-29-postgresql-native-bitemporal-interval-standard-via-tstzrange-and-gist.md) | Current | - Status: Accepted |
| [ADR-2450: Reject Concurrency Conflicts, Retry Only Transient Failures](adr/2026-09-29-reject-concurrency-conflicts-retry-only-transient-failures.md) | Current | Also known as: ADR-2448 (former number; it collided with the gateway routes ADR) |
| [ADR-2258: Restrict Cascade Deletes onto Audit Trails and Regulated Child Records](adr/2026-09-29-restrict-cascade-deletes-onto-audit-trails-and-regulated-children.md) | Current | - Status: Accepted |
| [ADR-2254: Rules as Versioned Data, Continuous Standards Conformance and a Unified Rules Engine (K008)](adr/2026-09-29-rules-as-versioned-data-and-unified-rules-engine.md) | Current | - Status: Accepted |
| [ADR-2446: Single Audit Writer and One Chain per Aggregate Type](adr/2026-09-29-single-audit-writer-and-one-chain-per-aggregate.md) | Current | - Status: Accepted |
| [ADR-2489: AI Proposals and Automation Agents](adr/2026-09-30-ai-proposals-and-automation-agents.md) | Current | - Status: Accepted |
| [ADR-2541: Consolidate GxPAuditInterceptor on Cadence.Database and Remove Obsolete Security Interceptor](adr/2026-09-30-consolidate-gxp-audit-interceptor-on-database-package.md) | Current | - Status: Accepted |
| [ADR-2590: Counts Limited to the Reader's Visible Scope](adr/2026-09-30-counts-limited-to-the-readers-visible-scope.md) | Current | - Status: Accepted |
| [ADR-2502: DDF Item Rules Use the One Rule Language](adr/2026-09-30-ddf-item-rules-use-the-one-rule-language.md) | Current | - Status: Accepted |
| [ADR-2522: Derived Variables Are Ported as Fixed Standard Formulas, and Custom Formulas Use the One Rule Language](adr/2026-09-30-derived-variables-ported-as-fixed-standard-formulas.md) | Current | - Status: Accepted |
| [ADR-2483: Dry-Run Authorization and the Command Catalogue](adr/2026-09-30-dry-run-authorization-and-command-catalogue.md) | Current | - Status: Accepted |
| [ADR-2592: Dual Ecosystem Vulnerability Auditor and CEC Chair Adjudications Persistence](adr/2026-09-30-dual-ecosystem-vulnerability-auditor-and-cec-chair-adjudications-persistence.md) | Current | - Status: Accepted |
| [ADR-2456: Effectivity and Study-Site Activation](adr/2026-09-30-effectivity-and-study-site-activation.md) | Current | - Status: Accepted |
| [ADR-2510: Endpoint Permissions from the Permission Matrix](adr/2026-09-30-endpoint-permissions-from-the-permission-matrix.md) | Current | - Status: Accepted |
| [ADR-2480: Evaluate-Only Simulation over Candidate Versions](adr/2026-09-30-evaluate-only-simulation-over-candidate-versions.md) | Current | - Status: Accepted |
| [ADR-2454: Evidence Artifacts as Immutable, Hash-Addressed Versions](adr/2026-09-30-evidence-artifacts-as-immutable-hash-addressed-versions.md) | Current | - Status: Accepted |
| [ADR-2472: Executable Protocol Design Document with Stable Element IDs](adr/2026-09-30-executable-protocol-design-document.md) | Current | - Status: Accepted |
| [ADR-2487: Expected Schedule as a Pure Function of the Protocol Design](adr/2026-09-30-expected-schedule-as-a-pure-function.md) | Current | - Status: Accepted |
| [ADR-2482: Explanation Model Produced by the Rule Owner](adr/2026-09-30-explanation-model-produced-by-the-rule-owner.md) | Current | - Status: Accepted |
| [ADR-2490: Fail-Fast Startup Validation of Required Settings](adr/2026-09-30-fail-fast-startup-validation-of-required-settings.md) | Current | - Status: Accepted |
| [ADR-2494: Field-Level Encryption and Keyed Lookup Hashes for Secrets at Rest](adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md) | Current | - Status: Accepted |
| [ADR-2513: Form Approval Hashes the RFC 8785 Canonical Payload](adr/2026-09-30-form-approval-hashes-the-rfc-8785-canonical-payload.md) | Current | - Status: Accepted |
| [ADR-2457: Grant-Backed Node Visibility](adr/2026-09-30-grant-backed-node-visibility.md) | Current | - Status: Accepted |
| [ADR-2521: The Grant Payables Ledger Is Ported to Payments in Decimal with Kernel IDs](adr/2026-09-30-grant-payables-ledger-in-decimal-with-kernel-ids.md) | Current | - Status: Accepted |
| [ADR-2455: Grants, Roles and Delegation as Bitemporal Kernel Authority](adr/2026-09-30-grants-roles-and-delegation-as-bitemporal-kernel-authority.md) | Current | - Status: Accepted |
| [ADR-2491: HTTP Edge Hardening: Trusted Forwarded Headers, HSTS and Per-Caller Rate Limits](adr/2026-09-30-http-edge-hardening-forwarded-headers-hsts-rate-limits.md) | Current | - Status: Accepted |
| [ADR-2458: The IdentityVault](adr/2026-09-30-identity-vault.md) | Current | - Status: Accepted |
| [ADR-2540: Kernel API Host and Read Endpoints for the Clinical Canvas](adr/2026-09-30-kernel-api-host-and-read-endpoints-for-the-canvas.md) | Current | - Status: Accepted |
| [ADR-2493: Keycloak-Only Web Sign-In](adr/2026-09-30-keycloak-only-web-sign-in.md) | Current | - Status: Accepted |
| [ADR-2471: Lineage Store and Masked Read Contract](adr/2026-09-30-lineage-store-and-masked-read-contract.md) | Current | - Status: Accepted |
| [ADR-2463: Mapping Specifications as Governed Concept-to-Variable Maps (K028)](adr/2026-09-30-mapping-specifications-as-governed-concept-to-variable-maps.md) | Current | - Status: Accepted |
| [ADR-2530: Measure Web Coverage over All Source with a Ratchet Floor](adr/2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md) | Current | - Status: Accepted |
| [ADR-2500: One Folder per Test Project and a Separate Integration Test Project](adr/2026-09-30-one-folder-per-test-project-and-a-separate-integration-project.md) | Current | - Status: Accepted |
| [ADR-2496: Parse the CLI with System.CommandLine and Publish It as Native AOT Without Suppressions](adr/2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md) | Current | - Status: Accepted |
| [ADR-2453: Participants, Bitemporal Enrollment Lifecycle and Gap-Free Enrollment Numbering](adr/2026-09-30-participants-enrollment-lifecycle-and-gap-free-numbering.md) | Current | - Status: Accepted |
| [ADR-2470: Per-Tenant Terminology, Pinned Code-System Versions and Versioned Concepts](adr/2026-09-30-per-tenant-terminology-and-pinned-code-system-versions.md) | Current | - Status: Accepted |
| [ADR-2474: Protocol Elements as Lineage Nodes](adr/2026-09-30-protocol-elements-as-lineage-nodes.md) | Current | - Status: Accepted |
| [ADR-2452: Protocol Version Lifecycle, Content-Hash Freezing and Attested Approval](adr/2026-09-30-protocol-version-lifecycle-content-hash-and-attested-approval.md) | Current | - Status: Accepted |
| [ADR-2481: Public NodeRef and Kernel-Computed Zoom Path](adr/2026-09-30-public-noderef-and-kernel-computed-zoom-path.md) | Current | - Status: Accepted |
| [ADR-2459: Qualifications Gate Capabilities](adr/2026-09-30-qualifications-gate-capabilities.md) | Current | - Status: Accepted |
| [ADR-2485: Reader-Facing Activity Stream Projection](adr/2026-09-30-reader-facing-activity-stream-projection.md) | Current | - Status: Accepted |
| [ADR-2495: One Encoding for the Reason-for-Change Header](adr/2026-09-30-reason-for-change-header-encoding.md) | Current | - Status: Accepted |
| [ADR-2505: Recording Evidence Goes to Controlled Object Storage, Not Google Drive](adr/2026-09-30-recording-evidence-goes-to-controlled-object-storage.md) | Current | - Status: Accepted |
| [ADR-2492: Retire Python Reference Sources Once Their C# Ports Are Tested](adr/2026-09-30-retire-python-reference-sources-once-ports-are-tested.md) | Current | - Status: Accepted |
| [ADR-2503: Reusable Form Blocks Are Collection Specification Library Items](adr/2026-09-30-reusable-form-blocks-are-collection-specification-library-items.md) | Current | - Status: Accepted |
| [ADR-2461: Rule Expression Language, Rule-Pack Format and the IRuleEvaluator Port](adr/2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md) | Current | - Status: Accepted |
| [ADR-2475: Schedule of Activities as a Rendering, with Typed Amendment Directives](adr/2026-09-30-schedule-of-activities-as-a-rendering-with-typed-amendment-directives.md) | Current | - Status: Accepted |
| [ADR-2512: Security and Compliance Take Time From IClock](adr/2026-09-30-security-and-compliance-take-time-from-iclock.md) | Current | - Status: Accepted |
| [ADR-2460: Standards Registry and Metadata Registry as Governed, Tenant-Scoped Reference Data](adr/2026-09-30-standards-registry-and-mdr-as-governed-tenant-reference-data.md) | Current | - Status: Accepted |
| [ADR-2488: Study Pulse Metrics and Attention Rules as Versioned Indicators](adr/2026-09-30-study-pulse-metrics-as-versioned-indicators.md) | Current | - Status: Accepted |
| [ADR-2520: The Supply Unblinding Engine and Facade Are Not Carried Forward](adr/2026-09-30-supply-unblinding-and-facade-not-carried-forward.md) | Current | - Status: Accepted |
| [ADR-2591: Synchronous In-Process DSUR Compilation and Cryptographic Signing Engine](adr/2026-09-30-synchronous-in-process-dsur-compilation-and-cryptographic-signing-engine.md) | Current | - Status: Accepted |
| [ADR-2511: Template Render Routes Need a Permission](adr/2026-09-30-template-render-routes-need-a-permission.md) | Current | - Status: Accepted |
| [ADR-2484: Tenant-Scoped Search over the Source Tables](adr/2026-09-30-tenant-scoped-search-over-source-tables.md) | Current | - Status: Accepted |
| [ADR-2501: The DDF Compiler Refuses Forms That Fail the Accessibility Check](adr/2026-09-30-the-ddf-compiler-refuses-forms-that-fail-the-accessibility-check.md) | Current | - Status: Accepted |
| [ADR-2506: The engine_core Rule AST Is Superseded by the One Rule Language](adr/2026-09-30-the-engine-core-rule-ast-is-superseded-by-the-one-rule-language.md) | Current | - Status: Accepted |
| [ADR-2514: The Tenant Comes From Sign-In, With No Fallback](adr/2026-09-30-the-tenant-comes-from-sign-in-with-no-fallback.md) | Current | - Status: Accepted |
| [ADR-2504: The Tripartite Form Definition Is Not Carried Forward](adr/2026-09-30-the-tripartite-form-definition-is-not-carried-forward.md) | Current | - Status: Accepted |
| [ADR-2473: CDISC USDM 4 Import into the Protocol Design](adr/2026-09-30-usdm-4-import-into-the-protocol-design.md) | Current | - Status: Accepted |
| [ADR-2462: Validation Findings Derived from Immutable Runs and Attested Decisions](adr/2026-09-30-validation-findings-derived-from-immutable-runs-and-attested-decisions.md) | Current | - Status: Accepted |
| [ADR-2507: Visit Timing, Anchored Visits and Cycles on the Expected Schedule](adr/2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md) | Current | - Status: Accepted |
| [ADR-2486: Work Queues as Capability-Keyed Definitions](adr/2026-09-30-work-queues-as-capability-keyed-definitions.md) | Current | - Status: Accepted |
| [ADR-2801: Automated Stipend Disbursement Threshold Rules for Participant Wallet](adr/2026-10-01-automated-stipend-disbursement-threshold-rules.md) | Current | - Status: Accepted |
| [ADR-2802: CTMS Visit Letter PDF Compilation and Automated Dispatch Pipeline](adr/2026-10-01-ctms-visit-letter-pdf-compilation-and-automated-dispatch-pipeline.md) | Current | - Status: Accepted |
| [ADR-2803: Cell and Gene Therapy (CGT) Domain Module Integration](adr/2026-10-02-cell-and-gene-therapy-cgt-domain-module.md) | Current | - Status: Accepted |
| [ADR-2806: Country Multi-Site Greenlight Matrix Persistence](adr/2026-10-02-country-multi-site-greenlight-matrix-persistence.md) | Current | - Status: Accepted |
| [ADR-2807: CTMS Site Greenlight Gates Persistence Schema](adr/2026-10-02-ctms-site-greenlight-gates-persistence-schema.md) | Current | - Status: Accepted |
| [ADR-2805: CTMS Site Startup Greenlight Activation Matrix](adr/2026-10-02-ctms-site-startup-greenlight-matrix.md) | Current | - Status: Accepted |
| [ADR-2808: ModalErrorAlert Component and useAsyncRetry Composable Standard](adr/2026-10-02-modalerroralert-component-and-useasyncretry-composable-standard.md) | Current | - Status: Accepted |
| [ADR-2804: Soft Delete and Invalidation of eCRF Form Observations](adr/2026-10-02-soft-delete-and-invalidation-of-ecrf-form-observations.md) | Current | - Status: Accepted |
| [ADR-2810: ACH Auto-Reimbursement and Expense Claim Disbursement Reference](adr/2026-10-06-ach-auto-reimbursement-and-expense-claim-disbursement.md) | Current | - Status: Accepted |
| [ADR-2811: Bulk Synonym Import Service & Hierarchy Rule Store](adr/2026-10-06-bulk-synonym-import-service-and-rule-store.md) | Current | - Status: Accepted |
| [ADR-2809: Published Form Version Registry and Schema Hash Validation](adr/2026-10-06-published-form-version-registry-and-schema-hash-validation.md) | Historical | - Status: Accepted |
| [ADR-2812: Integrated GS1 and HIBCC UDI Parser with Automated RTSM Quarantine Engine](adr/2026-10-07-integrated-udi-parser-and-rtsm-quarantine-engine.md) | Current | - Status: Accepted |
| [ADR-2813: Standardize MassTransit EF Core Transactional Outbox Across Core Services](adr/2026-10-07-standardize-masstransit-ef-core-transactional-outbox-across-core-services.md) | Current | - Status: Accepted |
| [ADR-2813: Synchronous Batch Assignment and Multi Role Delegation Sign Off Architecture](adr/2026-10-07-synchronous-batch-assignment-and-multi-role-delegation-sign-off-architecture.md) | Current | - Status: Accepted |
| [ADR-2815: Bulk Laboratory Reference Range Import & Persistent Audit Store](adr/2026-10-12-bulk-laboratory-reference-range-import-persistent-audit-store.md) | Current | - Status: Accepted |
| [ADR-2610: Scaffold Services Become Kernel Modules or Merge](adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md) | Current | Disposition of the 17 scaffold services (#4787): fourteen modules, three merges, nothing deleted |
| [ADR-2620: The eCRF Correction Transaction Boundary](adr/2026-10-15-ecrf-correction-transaction-boundary.md) | Current | - Status: Accepted |
| [ADR-2515: Step-Up Tokens Are Single Use Across Instances and Restarts](adr/2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md) | Current | - Status: Accepted |
| [ADR-2516: Routes Are Denied Unless They Declare Who May Call Them](adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md) | Current | - Status: Accepted |
| [ADR-2531: Every Mapped Test Is Mandatory Evidence, and Reports Name What They Tested](adr/2026-10-15-every-mapped-test-is-mandatory-evidence.md) | Current | - Status: Accepted |
| [ADR-2532: Feature Branches Never Commit the GxP Snapshot](adr/2026-10-16-feature-branches-never-commit-the-gxp-snapshot.md) | Current | Accepted |
| [ADR-2508: Pin pnpm and Node with packageManager and engines](adr/2026-10-16-pin-pnpm-and-node-with-packagemanager-and-engines.md) | Current | - Status: Accepted |
| [ADR-2523: Explicit Transactions Run Inside the Execution Strategy](adr/2026-10-19-explicit-transactions-run-inside-the-execution-strategy.md) | Current | - Status: Accepted |
| [ADR-2542: The Kernel Host Provisions the Development Tenant](adr/2026-10-19-the-kernel-host-provisions-the-development-tenant.md) | Current | - Status: Accepted |
| [ADR-2600: Modules Write Through the Kernel Command Path](adr/2026-10-19-modules-write-through-the-kernel-command-path.md) | Current | - Status: Accepted |
| [ADR-2601: One Composition Host for the Kernel and Its Modules](adr/2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md) | Current | - Status: Accepted |
| [ADR-2621: Observation Corrections Are Revisions; Locks Are Scoped Assertions](adr/2026-10-19-observation-corrections-are-revisions-locks-are-scoped-assertions.md) | Current | - Status: Accepted |
| [ADR-2622: eCRF Forms Are Kernel Collection Instances Saved in One Command](adr/2026-10-19-ecrf-forms-are-collection-instances-saved-in-one-command.md) | Current | - Status: Accepted |
| [ADR-2533: Tests, CI and Compose Run One Pinned PostgreSQL Major](adr/2026-10-19-tests-ci-and-compose-run-one-pinned-postgresql-major.md) | Current | - Status: Accepted |
| [ADR-2524: Messaging Stays on the Apache-Licensed MassTransit 8 Line](adr/2026-10-19-messaging-stays-on-the-apache-licensed-masstransit-8-line.md) | Current | - Status: Accepted |
| [ADR-2543: The Kernel Host Seeds the Local Demo Study](adr/2026-10-19-the-kernel-host-seeds-the-local-demo-study.md) | Current | - Status: Accepted |
| [ADR-2525: Dependabot Bumps Merge as One Locally Validated Batch](adr/2026-10-20-dependabot-bumps-merge-as-one-locally-validated-batch.md) | Current | - Status: Accepted |
| [ADR-2623: Observation Values Are Typed](adr/2026-10-19-observation-values-are-typed.md) | Current | - Status: Accepted |
| [ADR-2517: SMART Client Assertions Are Signed Only With Server-Side Endpoint Keys](adr/2026-10-19-smart-client-assertions-are-signed-only-with-server-side-endpoint-keys.md) | Current | - Status: Accepted |
| [ADR-2630: AI Agents Act Through Proposals in One Governed Runtime](adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md) | Current | - Status: Accepted |
| [ADR-2632: Agents Act for One Person, and Approved Proposals Execute as Their Approver](adr/2026-10-20-agents-act-for-one-person-and-approved-proposals-execute-as-their-approver.md) | Current | - Status: Accepted |
| [ADR-2518: DSUR Approval Is Its Own Capability, Signed by the Verified Caller](adr/2026-10-19-dsur-approval-is-its-own-capability-signed-by-the-verified-caller.md) | Current | - Status: Accepted |
| [ADR-2631: Agent Task Configurations Are Versioned and Qualified per Tenant](adr/2026-10-20-agent-task-configurations-are-versioned-and-qualified-per-tenant.md) | Current | - Status: Accepted |
| [ADR-2633: Agent Tools Read as the Requester, and Every Claim Cites What Was Read](adr/2026-10-20-agent-tools-read-as-the-requester-and-every-claim-cites-what-was-read.md) | Current | - Status: Accepted |
| [ADR-2634: AI Task Configurations Are Measured by Versioned Evaluations and Production Signals](adr/2026-10-20-ai-task-configurations-are-measured-by-versioned-evaluations-and-production-signals.md) | Current | - Status: Accepted |
| [ADR-2534: Quality Gates Live in One Registry and Each Must Be Able to Fail](adr/2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md) | Current | - Status: Accepted |
| [ADR-2519: Safety Signature Manifests Are Stored as Text](adr/2026-10-20-safety-signature-manifests-are-stored-as-text.md) | Current | - Status: Accepted |
| [ADR-2640: Randomisation Allocates from Approved, Encrypted Scheme Versions](adr/2026-10-20-randomisation-allocates-from-approved-encrypted-scheme-versions.md) | Current | - Status: Accepted |
| [ADR-2464: ICSR Render Emits ICH E2B(R3) Validated Against the ICH Schema](adr/2026-10-20-icsr-render-emits-ich-e2br3-validated-against-the-ich-schema.md) | Current | - Status: Accepted |
| [ADR-2635: AI in the Canvas Is Labelled, Scoped to the Selection and Ends in a Proposal](adr/2026-10-20-ai-in-the-canvas-is-labelled-scoped-and-ends-in-a-proposal.md) | Current | - Status: Accepted |
| [ADR-2636: Inference Is Pluggable, Residency-Bound and Sandboxed](adr/2026-10-20-inference-is-pluggable-residency-bound-and-sandboxed.md) | Current | - Status: Accepted |
| [ADR-2602: Multi-Repository Ownership and Kernel Package Boundaries](adr/2026-10-28-multi-repository-ownership-and-kernel-package-boundaries.md) | Current | Kernel boundaries and accepted ownership decisions. Source repository split superseded by ADR-2603. |
| [ADR-2603: Keep Cadence in One Repository](adr/2026-11-19-keep-cadence-in-one-repository.md) | Current | Single-repository development and integration for the modular monolith |
| [ADR-2544: Shared Reactive Focus Containment and Global Reduced-Motion Tokens](adr/2026-11-19-shared-reactive-focus-containment-and-reduced-motion-tokens.md) | Current | - Status: Accepted |
| [ADR-2545: Centralize Focus Containment Primitives](adr/2026-11-19-centralize-focus-containment-primitives.md) | Current | - Status: Accepted |
| [ADR-2816: Retire Orphaned Legacy Web Type Schemas](adr/2026-11-19-retire-orphaned-legacy-web-type-schemas.md) | Current | - Status: Accepted |
| [ADR-2546: Isolate Static Demo Builds with Stateful MSW Handlers](adr/2026-11-19-isolate-static-demo-builds-with-stateful-msw-handlers.md) | Current | - Status: Accepted |
| [ADR-2817: Caller-Bound Participant Self-Service Wallet and Receipt OCR Capabilities](adr/2026-11-19-caller-bound-participant-self-service-wallet-and-receipt-ocr-capabilities.md) | Current | - Status: Accepted |
| [ADR-2818: Clinical and Gateway Endpoints Take Time From IClock and Ban TimeProvider](adr/2026-11-20-clinical-and-gateway-endpoints-take-time-from-iclock-and-ban-timeprovider.md) | Current | - Status: Accepted |
| [ADR-2820: Recalibrate Shared Color Tokens and WCAG 2.2 AA Contrast Gates](adr/2026-11-19-recalibrate-shared-color-tokens-and-wcag-22-aa-contrast-gates.md) | Current | - Status: Accepted |
| [ADR-2476: Synchronous CDISC ODM v1.3.2 and USDM v4 Protocol Draft Import and Self-Service Study Registration](adr/2026-11-20-synchronous-odm-usdm-protocol-draft-import.md) | Current | - Status: Accepted |
| [ADR-2550: Co-Host Interactive Demos on GitHub Pages Subpaths](adr/2026-11-20-co-host-interactive-demos-on-github-pages-subpaths.md) | Current | - Status: Accepted |
| [ADR-2547: Keycloak Self-Registration and Clinical Kernel Trial Bootstrap](adr/2026-11-20-keycloak-self-registration-and-clinical-kernel-trial-bootstrap.md) | Current | - Status: Accepted |
| [ADR-2819: Focus Active eCRF View on Backed Kernel Forms Outside Demo Mode](adr/2026-11-20-focus-active-ecrf-view-on-backed-kernel-forms-outside-demo-mode.md) | Current | - Status: Accepted |
| [ADR-2641: Contextual SOP Specificity Resolution and Audited Support Ticket Escalation](adr/2026-11-20-contextual-sop-resolution-and-audited-ticket-escalation.md) | Current | - Status: Accepted |
| [ADR-2526: Idempotent Self-Service Participant Expense Claims and Wallet Disbursements](adr/2026-11-20-idempotent-self-service-participant-expense-claims-and-wallet-disbursements.md) | Current | - Status: Accepted |
| [ADR-2535: Forward Verified CI GxP Evidence to Releases and Synchronize Service Migration Snapshots](adr/2026-11-20-forward-verified-ci-gxp-evidence-and-sync-service-migrations.md) | Current | - Status: Accepted |
| [ADR-2548: Enforce PostgreSQL Forced RLS and Shared Clinical Scope Guards Across Standalone Services](adr/2026-11-20-standalone-postgres-rls-and-shared-scope-guards.md) | Current | - Status: Accepted |
| [ADR-2549: Export Canonical Demo Manifest from Kernel Seed](adr/2026-11-20-export-canonical-demo-manifest-from-kernel-seed.md) | Current | - Status: Accepted |
| [ADR-2551: Share Session Resilience, Draft-Safe Sign-Out, and Part 11 Signature Primitives](adr/2026-11-20-share-session-logout-and-signature-primitives.md) | Current | - Status: Accepted |
| [ADR-2497: Dual-Layer Gates for Source and Public Bundles](adr/2026-11-20-dual-layer-gates-for-source-and-public-bundles.md) | Current | - Status: Accepted |
| [ADR-2642: CTMS Core Topology, Scoped Isolation, and Stateful Site Activation](adr/2026-11-20-ctms-core-topology-scoped-isolation-and-stateful-site-activation.md) | Current | - Status: Accepted |
| [ADR-2643: Wire Web CTMS, Study Training, and eTMF Workspaces With Signed Completeness Webhooks](adr/2026-11-20-wire-web-ctms-training-and-etmf-workspaces-and-signed-completeness-webhooks.md) | Current | - Status: Accepted |
| [ADR-2527: Enforce Dependency Alignment with MSBuild Properties and pnpm Catalogs](adr/2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md) | Current | - Status: Accepted |
| [ADR-2552: Kernel Site Setup, Sandbox Site Provisioning, and Execution Tenant-Scoped Subject Uniqueness](adr/2026-11-21-kernel-site-setup-and-execution-tenancy.md) | Current | - Status: Accepted |
| [ADR-2624: Outbox Collection Edit Checks, Stateless Pre-Commit Form Preview, and ODM Rule Compilation](adr/2026-11-21-outbox-collection-edit-checks-and-pre-commit-preview.md) | Current | - Status: Accepted |
| [ADR-2604: Signed Gateway Expiry Guard and Scoped Trial Upgrades](adr/2026-11-21-signed-gateway-expiry-guard-and-scoped-trial-upgrades.md) | Current | - Status: Accepted |
| [ADR-2821: Migrate Web Overlays to Clinical Primitives and Enforce AST Guardrails](adr/2026-11-22-migrate-web-overlays-to-clinical-primitives-and-enforce-ast-guardrails.md) | Current | - Status: Accepted |
| [ADR-2822: Shared Accessibility Primitives (`ClinicalIconButton`, `ClinicalDropzone`) and Label-in-Name AST Gates](adr/2026-11-22-shared-a11y-primitives-and-label-in-name-ast-gates.md) | Current | - Status: Accepted |
| [ADR-2644: CTMS Study and Site Scope Isolation Across DOA and Monitoring Letter Routes](adr/2026-11-22-ctms-study-and-site-scope-isolation.md) | Current | - Status: Accepted |
| [ADR-2539: Enforce Strict Workspace TypeScript and Vue SFC Typechecking](adr/2026-11-22-enforce-workspace-vue-tsc-typecheck.md) | Current | - Status: Accepted |
| [ADR-2605: Signed Gateway Entitlements and Bitemporal Subscriptions](adr/2026-11-22-signed-gateway-entitlements-and-bitemporal-subscriptions.md) | Current | - Status: Accepted |
| [ADR-2625: Bitemporal FormQuery Aggregate, Separation of Duties, and SLA Escalation](adr/2026-11-22-bitemporal-form-query-aggregate-and-provenance.md) | Current | - Status: Accepted |
| [ADR-2650: Consolidate Shared UI CSS Aliases, Focus Resets, and Table Numerals](adr/2026-11-22-consolidate-shared-ui-css-aliases-and-table-numerals.md) | Current | - Status: Accepted |
| [ADR-2651: Shared ClinicalContextBar and Canonical URL Scope Codec](adr/2026-11-22-shared-clinical-context-bar-and-url-scope-codec.md) | Current | - Status: Accepted |
| [ADR-2645: Live Tickets and Knowledge Hubs with Durable Ticket Step-Up Signatures](adr/2026-11-22-live-tickets-and-knowledge-hubs-with-durable-ticket-step-up-signatures.md) | Current | - Status: Accepted |
| [ADR-2646: Standardize 24px Target Tokens and Dual Focus Rings](adr/2026-11-22-standardize-24px-target-tokens-and-dual-focus-rings.md) | Current | - Status: Accepted |
| [ADR-2536: Unify 18-Module Migration Tooling, Fail-Closed Metadata Verification, and Count-Free Schema Docs](adr/2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md) | Current | - Status: Accepted |
| [ADR-2537: Split UI Vitest Accessibility Matcher and Enforce Staged Demo Bundle Budgets](adr/2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md) | Current | - Status: Accepted |
| [ADR-2538: Share Demo Runtime, Unbacked-Action Guards, and MSW Startup Factories](adr/2026-11-22-share-demo-runtime-and-msw-worker-factories.md) | Current | - Status: Accepted |
| [ADR-2593: Extend Focus Primitives with Inline Focus Handoff](adr/2026-11-22-extend-focus-primitives-with-inline-focus-handoff.md) | Current | - Status: Accepted |
| [ADR-2606: Clinical Canvas Casebook Unification and Kernel Protocol Governance Endpoints](adr/2026-11-22-clinical-canvas-casebook-unification-and-protocol-governance-endpoints.md) | Current | - Status: Accepted |
| [ADR-2647: Standalone Optimistic Concurrency, Participant Wallet Row Locks, and Bitemporal or Insert-Only Revisions](adr/2026-11-22-standalone-concurrency-wallet-locks-and-bitemporal-revisions.md) | Current | - Status: Accepted |
| [ADR-2823: Self-Service Trial Site Workflows and Scoped Trial Coordinator Enrollment](adr/2026-11-22-self-service-trial-site-workflows-and-scoped-trial-coordinator-enrollment.md) | Current | - Status: Accepted |
| [ADR-2661: Consolidate Standalone Caller Attribution and Clinical Scope Guards in Cadence.Security](adr/2026-11-22-consolidate-standalone-caller-and-clinical-scope-guards.md) | Current | - Status: Accepted |
| [ADR-2652: Shared `ClinicalTabs`, Stack-Aware Focus Fallbacks, and Hub Accessibility AST Gates](adr/2026-11-23-clinical-tabs-focus-fallbacks-and-hub-a11y-gates.md) | Current | - Status: Accepted |
| [ADR-2653: Adopt ClinicalModal for Electronic Signatures and Reflow Split-Pane Workspaces at 320px](adr/2026-11-23-adopt-clinical-modal-for-signatures-and-320px-workspace-reflow.md) | Current | - Status: Accepted |
| [ADR-2654: Prune Global Web CSS Overrides, Standardize GxP Status Badges, and Gate AI Token Exclusivity](adr/2026-11-23-prune-global-web-css-and-standardize-gxp-status-badges.md) | Current | - Status: Accepted |
| [ADR-2655: Unify Modal Focus Coordination, Safe Confirmation Dialogs, and Persistent Accessible Toasts](adr/2026-11-23-unify-focus-coordination-safe-confirmations-and-persistent-toasts.md) | Current | - Status: Accepted |
| [ADR-2648: Restrict CTMS Delegation of Authority Sign-Off Permissions to Principal Investigator Roles](adr/2026-11-23-restrict-ctms-doa-sign-off-to-pi-roles.md) | Current | - Status: Accepted |
| [ADR-2649: Index GxP Merkle Audit Chains and Prefetch Chain Tip Hashes](adr/2026-11-23-index-gxp-audit-chains-and-prefetch-tip-hashes.md) | Current | - Status: Accepted |
| [ADR-2656: Keyboard Operability and WAI-ARIA Semantics in `ClinicalDataTable`](adr/2026-11-23-clinical-data-table-keyboard-and-aria-compliance.md) | Current | - Status: Accepted |
| [ADR-2528: Extend Cohort, xUnit v3, and PostgreSQL Digest Guardrails](adr/2026-11-23-extend-cohort-xunit-v3-and-postgres-digest-guardrails.md) | Current | - Status: Accepted |
| [ADR-2657: Share UI Primitives and Synchronize Participant Portal Route Queries](adr/2026-11-23-share-ui-primitives-and-query-synced-portal-routes.md) | Current | - Status: Accepted |
| [ADR-2498: Hoist CSP Meta Directives, Delegate Clinical Widgets, and Scope Mode-Aware Vite CSP Profiles](adr/2026-11-22-hoist-csp-delegate-widgets-and-mode-aware-vite-profiles.md) | Current | - Status: Accepted |
| [ADR-2607: Complete Kernel NodeKinds Scope Mapping and Persist Canonical Enrollment NodeRefs](adr/2026-11-23-persist-enrollment-id-and-map-all-kernel-node-kinds.md) | Current | - Status: Accepted |
| [ADR-2658: Resolve Shared UI from Workspace Source and Align Verification Scripts](adr/2026-11-23-resolve-shared-ui-from-source-and-align-verification-scripts.md) | Current | - Status: Accepted |
| [ADR-2660: Enforce Configured Biometric Liveness HMAC Key and Single-Use Replay Protection](adr/2026-11-23-biometric-liveness-hmac-key-and-single-use-replay-protection.md) | Current | - Status: Accepted |
| [ADR-2662: Enforce Verified Trial-Tenant Writes Centrally and Switch Ticket Comments to a Sponsor Allowlist](adr/2026-11-23-enforce-trial-tenant-writes-and-sponsor-comment-allowlist.md) | Current | - Status: Accepted |
| [ADR-2831: Stage Encrypted Randomization Slots Before Acquiring the Study Lock](adr/2026-11-23-stage-encrypted-randomization-slots-before-acquiring-the-study-lock.md) | Current | - Status: Accepted |
| [ADR-2553: Unify Root ESLint Gates, Explicit Runtime Globals, and Demo-Fixtures Typechecking](adr/2026-11-23-unify-root-eslint-gates-globals-and-demo-fixtures-typecheck.md) | Current | - Status: Accepted |
| [ADR-2832: Expand IL Sentinels and Shrink-Only CLI Ratchets](adr/2026-11-23-expand-il-sentinels-and-shrink-only-cli-ratchets.md) | Current | - Status: Accepted |
| [ADR-2659: Centralize `ClinicalTabs` Visual Variants, Overflow Containment, Focus Scroll Alignment, and Scoped CSS AST Gates](adr/2026-11-23-centralize-clinical-tabs-variants-overflow-and-scoped-css-gates.md) | Current | - Status: Accepted |
| [ADR-2670: Enforce Tabular Numerals via Shared CSS Tokens and AST Lint](adr/2026-11-22-enforce-tabular-numerals-via-css-tokens-and-lint.md) | Current | - Status: Accepted |
| [ADR-2626: Observation Assertion Audit Coverage, SDV Precondition Guards, and CRA Modification Alerts](adr/2026-11-23-observation-assertions-sdv-guards-and-cra-alerts.md) | Current | - Status: Accepted |
| [ADR-2671: Composable Dirty Navigation Guards, Inline eCRF Draft Recovery Banners, and Edited-Row Contrast Tokens](adr/2026-11-23-composable-dirty-guards-and-inline-draft-recovery.md) | Current | - Status: Accepted |

## Developing and running

| Document | Status | What it is |
| --- | --- | --- |
| [Local development environment](LOCAL_DEV_ENVIRONMENT.md) | Current | Prerequisites, build, run (compose, Aspire, single service), ports |
| [Environment variables](ENVIRONMENT_VARIABLES.md) | Current | Every setting the code reads, required or optional |
| [Database layout and migrations](OPERATIONAL_DATABASE.md) | Current | One `cadence` database with a schema per service; migrations at startup or as a job |
| [Testing and requirement qualification](TESTING.md) | Current | Running the .NET and Vitest suites, PostgreSQL opt-ins, `Cadence.Testing` helpers, requirement tagging and when a requirement counts as qualified |
| [CI and quality gates](CI.md) | Current | Workflows, CI jobs, the quality gates, the pre-commit hook and a failure runbook |
| [CLI reference](cli.md) | Generated | Every `pnpm cadence` command (`pnpm cadence generate-cli-docs`) |
| [Service and package READMEs](../ARCHITECTURE.md#3-running-services-today) | Current | One README per `apps/*` and `packages/*` directory |
| [REST API reference](api/index.md) | Generated | Per-service endpoint reference from `docs/openapi/*.json` (`pnpm cadence generate-api-docs`) |
| [OpenAPI specifications](openapi/README.md) | Generated | Exported OpenAPI documents (`pnpm cadence sync-openapi-spec`); legacy aggregate snapshot in `openapi/legacy/cdisc_openapi.json` |
| [UI components](components/index.md) | Current | Shared Vue component catalog (`packages/ui`) |
| [Moving the web app onto the .NET API](web/API_MIGRATION.md) | Current | Which web screens have a .NET backend, which are hidden, and how a screen moves over (epic #4833) |
| [Migration workflow playbook](MIGRATION_WORKFLOW.md) | Current | How to port the remaining Python reference sources to C# |
| [Python reference inventory](PYTHON_REFERENCE_INVENTORY.md) | Current | Each remaining Python file, its C# status and what it needs before deletion (#4789) |
| [Operations and deployment guide](SDLC/07_Operations_Deployment_Guide.md) | Baseline | Deployment topology and runbooks; being revised with the production-readiness work |
| [Lab reference ranges](OPERATIONAL_LAB_RANGES.md) | Baseline | Range selection, scoring and tie-breaking rules |
| [eTMF inbound email](OPERATIONAL_INBOUND_EMAIL.md) | Baseline | Inbound-email webhook security and processing |
| [Data lifecycle](DATA_LIFECYCLE.md) | Baseline | eTMF document QC lifecycle |

| [ctms API](api/ctms.md) | Generated | Generated from `docs/openapi/ctms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [designer API](api/designer.md) | Generated | Generated from `docs/openapi/designer_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [econsent API](api/econsent.md) | Generated | Generated from `docs/openapi/econsent_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [eisf API](api/eisf.md) | Generated | Generated from `docs/openapi/eisf_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [etmf API](api/etmf.md) | Generated | Generated from `docs/openapi/etmf_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [execution_forms API](api/execution_forms.md) | Generated | Generated from `docs/openapi/execution_forms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [execution API](api/execution.md) | Generated | Generated from `docs/openapi/execution_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [fileshare API](api/fileshare.md) | Generated | Generated from `docs/openapi/fileshare_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [imaging API](api/imaging.md) | Generated | Generated from `docs/openapi/imaging_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [intelligence API](api/intelligence.md) | Generated | Generated from `docs/openapi/intelligence_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [interop API](api/interop.md) | Generated | Generated from `docs/openapi/interop_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [kernel API](api/kernel.md) | Generated | Generated from `docs/openapi/kernel_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [knowledge API](api/knowledge.md) | Generated | Generated from `docs/openapi/knowledge_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [notifications API](api/notifications.md) | Generated | Generated from `docs/openapi/notifications_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by... |
| [org API](api/org.md) | Generated | Generated from `docs/openapi/org_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [payments API](api/payments.md) | Generated | Generated from `docs/openapi/payments_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [quality API](api/quality.md) | Generated | Generated from `docs/openapi/quality_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [safety API](api/safety.md) | Generated | Generated from `docs/openapi/safety_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [tickets API](api/tickets.md) | Generated | Generated from `docs/openapi/tickets_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand. |
| [AuditLogViewer](components/audit-viewer.md) | Current | The `AuditLogViewer` renders an interactive, chronological 21 CFR Part 11 audit trail feed with before/after value di... |
| [ClinicalDataTable](components/data-table.md) | Current | The `ClinicalDataTable` component provides an accessible, performant tabular viewer for clinical observation records,... |
| [ClinicalModal](components/modal.md) | Current | The `ClinicalModal` component renders accessible dialogs with focus trapping, `Escape` key listeners, backdrop blur, ... |
| [PersonaSwitcher](components/persona-switcher.md) | Current | The `PersonaSwitcher` component enables multi-persona simulation across clinical operations workflows. Users and auto... |
| [ctms database schema](schema/ctms.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [econsent database schema](schema/econsent.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [eisf database schema](schema/eisf.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [etmf database schema](schema/etmf.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [execution database schema](schema/execution.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [fileshare database schema](schema/fileshare.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [imaging database schema](schema/imaging.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [intelligence database schema](schema/intelligence.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [interop database schema](schema/interop.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [kernel database schema](schema/kernel.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [knowledge database schema](schema/knowledge.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [notifications database schema](schema/notifications.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [org database schema](schema/org.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [payments database schema](schema/payments.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [quality database schema](schema/quality.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [safety database schema](schema/safety.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [tickets database schema](schema/tickets.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |
| [executionmodule database schema](schema/executionmodule.md) | Generated | <!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -- |

## Requirements, validation and GxP

| Document | Status | What it is |
| --- | --- | --- |
| [SDLC documentation suite](SDLC/SDLC_Documentation_Suite.md) | Baseline | Overview of the SDLC document set |
| [Product requirements (PRD)](SDLC/01_Product_Requirements_Document_PRD.md) | Baseline | `PRD-…` requirement IDs used by tests and the RTM |
| [Software requirements (SRS)](SRS.md) | Baseline | `SRS-…` and `Trace-…` requirement IDs |
| [Requirements traceability matrix](SDLC/Requirements_Traceability_Matrix.md) | Generated | Requirement ↔ test mapping (snapshot from `pnpm cadence gxp snapshot`) |
| [IQ/OQ/PQ execution report](SDLC/IQ_OQ_PQ_Execution_Report.md) | Generated | Qualification evidence from the last snapshot |
| [Technical design (TDD)](SDLC/02_Technical_Design_Document_TDD.md) | Baseline | Superseded by the kernel spec and ARCHITECTURE.md |
| [API and integration specification](SDLC/03_API_Integration_Specification.md) | Baseline | Planned integration contracts |
| [Data standards and interoperability](SDLC/04_Data_Standards_Interoperability_Blueprint.md) | Baseline | CDISC, FHIR and coding dictionaries |
| [Security, compliance and audit](SDLC/05_Security_Compliance_Audit_Spec.md) | Baseline | Part 11 controls; current controls are in [ARCHITECTURE §4](../ARCHITECTURE.md#4-request-path-and-security) (ADR-2256, ADR-2259, ADR-2493, ADR-2494, ADR-2495) |
| [Signature token contract](SDLC/Signature_Token_Cryptographic_Contract.md) | Current | E-signature step-up token: claims, issuance at the gateway and verification in services (ADR-2259); used today by PI form approval |
| [QA and validation plan](SDLC/06_QA_Validation_Plan.md) | Baseline | GAMP 5 strategy and test tiers |
| [Implementation roadmap](SDLC/08_Implementation_Roadmap.md) | Baseline | Superseded by spec §15 |
| [Issue structure guide](SDLC/ISSUE_STRUCTURE_GUIDE.md) | Baseline | Issue formatting and work streams; epics now organise the backlog |
| [Knowledge hub specification](SDLC/SPEC_Knowledge_Support_Hub.md) | Baseline | Knowledge and support hub feature spec |
| [Neo4j decommissioning specification](SDLC/SPEC_Neo4j_to_Relational_Algebraic_MDR.md) | Baseline | Decided in ADR-2245 |

## Product

| Document | Status | What it is |
| --- | --- | --- |
| [Functional product specification](FUNCTIONAL_PRODUCT_SPECIFICATION.md) | Baseline | Capabilities reverse-engineered from the Python implementation |
| [Feature matrix](FEATURE_MATRIX.md) | Baseline | Features by service in the per-service design |
| [Competitive analysis](COMPETITIVE_ANALYSIS_AND_MARKET_SUPERIORITY.md) | Baseline | Product vision and market positioning |
| [Cadence market hypotheses and evidence plan](strategy/MARKET_HYPOTHESES.md) | Current | Status: hypotheses, not customer-validated market conclusions. Competitors are active in EDC, eTMF, CDISC, study buil... |
| [Cadence: proposed product packaging and licensing](strategy/PRODUCT_CATALOG.md) | Current | Status: commercial design proposal; not products available for purchase. Marketing names, suite name and trademarks a... |

## Design proposals

| Document | Status | What it is |
| --- | --- | --- |
| [GxP lifecycle hooks](specs/01-antigravity-gxp-lifecycle-hooks.md) | Baseline | Agent guardrail hooks |
| [Domain MCP tools and agent sidecars](specs/02-antigravity-domain-mcp-tools-and-agent-sidecars.md) | Baseline | MCP tooling proposal |
| [Developer tooling and MCP guardrails](specs/developer-tooling-mcp-guardrails.md) | Baseline | CLI, MCP server and staged guardrails |
| [eCRF designer IDE](specs/ecrf_designer_ide_spec.md) | Baseline | Visual metadata IDE proposal |

## Python-to-.NET port (completed)

| Document | Status | What it is |
| --- | --- | --- |
| [Modernization hub](migration/index.md) | Historical | Phase 0 baseline and links to the port documents |
| [System topology audit](migration/SYSTEM_TOPOLOGY_AUDIT.md) | Historical | Entry points and persistence of the Python system |
| [Dependency and parity catalog](migration/DEPENDENCY_AND_PARITY_CATALOG.md) | Historical | Python-to-.NET library crosswalk |
| [Bounded context migration matrix](migration/BOUNDED_CONTEXT_MIGRATION_MATRIX.md) | Historical | Port sequencing |
| [Phase 0 decomposition and scaffold](migration/PHASE0_DOMAIN_DECOMPOSITION_AND_SCAFFOLD.md) | Historical | Solution scaffold decisions |
| [Solution scaffold guide](migration/SCAFFOLD_GUIDE.md) | Historical | How the solution was scaffolded |
| [Phase 0 readiness audit](migration/Migration_Phase0_Readiness_Audit.md) | Historical | Gap analysis before the port |
| [Phase 0 working papers](migration/phase0/) | Historical | Validation gate, topology, contexts, plan |

| [00a. Gate 5 Remediation — Clean dotnet build Cadence.sln Restored](migration/phase0/00a_build_remediation.md) | Historical | Document ID: PHASE0-NET10-00A-BUILD-REMEDIATION |
| [00. Phase 0 Validation Gate — Audit Report](migration/phase0/00_validation_gate_report.md) | Historical | Document ID: PHASE0-NET10-00-VALIDATION |
| [01. System Topology & Entry Point Audit](migration/phase0/01_system_topology.md) | Historical | Historical record (Phase 0 discovery, September 2026). It describes the system as it was then. Neo4j, listed here as ... |
| [02. Bounded Context Decomposition & Coupling Analysis](migration/phase0/02_bounded_contexts.md) | Historical | Historical record (Phase 0 discovery, September 2026). It describes the system as it was then. Neo4j, listed here as ... |
| [03. Migration Sequencing Plan & Strangler Fig Design](migration/phase0/03_migration_plan.md) | Historical | Historical record (Phase 0 discovery, September 2026). It describes the system as it was then. Neo4j, listed here as ... |
| [05. Orphan Audit Gate — Zero-Orphan Verification](migration/phase0/05_orphan_audit_gate.md) | Historical | Document ID: PHASE0-NET10-05 |
| [Behavioral & Invariant Specification: Clinical Observations, Form Submissions & Dynamic Projections](migration/slices/execution-observations/01_spec.md) | Historical | Slice: `execution-observations` |
| [Technical Design: EXECUTION-OBSERVATIONS (.NET 10 / C# 14)](migration/slices/execution-observations/02_technical_design.md) | Historical | Slice: `execution-observations` · Bounded Context: `Cadence.Apps.Execution` · Target Assembly: `apps/execution/src/Ca... |
| [Tracer-Bullet Development Tickets: EXECUTION-OBSERVATIONS](migration/slices/execution-observations/03_tickets.md) | Historical | Slice: `execution-observations` · Bounded Context: `Cadence.Apps.Execution` · Standard: 6-Step Service Transcription ... |
| [Implementation Sign-Off: execution-observations](migration/slices/execution-observations/04_implementation_signoff.md) | Historical | Slice: `execution-observations` · Bounded Context: `Cadence.Apps.Execution` · Standard: 6-Step Service Transcription ... |
| [Phase 1 Validation Gate Report: execution-subjects](migration/slices/execution-subjects/00_phase1_validation_report.md) | Historical | Remediation Addendum (2026-09-04, same-day follow-up): The five FAIL verdicts recorded below were addressed in a reme... |
| [Phase 2 Validation Gate Report: EXECUTION-SUBJECTS](migration/slices/execution-subjects/00_phase2_validation_report.md) | Historical | Scope note on file naming. The validation prompt names `01_functional_spec.md` as the reference spec; this slice's ac... |
| [Phase 3 Validation Gate Report: EXECUTION-SUBJECTS](migration/slices/execution-subjects/00_phase3_validation_report.md) | Historical | Superseded Notice (2026-09-05): The verdict below (§5, "PHASE 4 BLOCKED") audited the pre-remediation 8-ticket `03_ti... |
| [Behavioral & Invariant Specification: Subject Lifecycle & Enrollment](migration/slices/execution-subjects/01_spec.md) | Historical | Slice: `execution-subjects` |
| [Target .NET 10 Architecture: Subject Lifecycle & Enrollment](migration/slices/execution-subjects/02_architecture.md) | Historical | Slice: `execution-subjects` |
| [Technical Design: EXECUTION-SUBJECTS (.NET 10 / C# 14)](migration/slices/execution-subjects/02_technical_design.md) | Historical | Supersedes the pre-remediation draft at `02_architecture.md` (and the backlog in `03_tickets.md`), both of which were... |
| [Migration Backlog: EXECUTION-SUBJECTS](migration/slices/execution-subjects/03_tickets.md) | Historical | Slice Code: `SUBJ` · Bounded Context: `Cadence.Apps.Execution` · Target Project: `apps/execution/src/Cadence.Apps.Exe... |
| [Implementation Sign-Off: execution-subjects](migration/slices/execution-subjects/04_implementation_signoff.md) | Historical | - Status: VERIFIED & COMPLETED |
| [Functional Specification: Participant Compensation](migration/slices/participant-compensation/Participant_Compensation_Functional_Spec.md) | Historical | Slice: `participant-compensation` |
## Standards reference material

| Location | What it is |
| --- | --- |
| [docs/CDISC](CDISC/README.md) | CDISC Library API description and USDM compliance data |
| [docs/NCI](NCI/) | NCI EVS REST API description |
| [docs/schema](schema/README.md) | Generated database schema of every module, from the EF Core models (`pnpm cadence generate-schema-doc`) |

## Repository policies

| Document | What it is |
| --- | --- |
| [SECURITY.md](../SECURITY.md) | How to report a vulnerability |
| [SUPPORT.md](../SUPPORT.md) | Where to get help |
| [LICENSE](../LICENSE) | Proprietary licence, all rights reserved |
| [Agent working notes](agents/domain.md) | How agents use domain docs, the [issue tracker](agents/issue-tracker.md) and [triage labels](agents/triage-labels.md) |

## Documentation site

`pnpm docs:dev` serves this folder with VitePress; `pnpm docs:build` builds the static site (validating
`rtm`, `openapi`, `schema-doc` and `markdown` on every run), and
`pnpm docs:stage-demos` stages the interactive staff (`/demo/`) and participant (`/subject-portal/`) demos into
`docs/.vitepress/dist`. `docs/index.md` serves as the site's home page, sidebar navigation is synthesized from
this index (`docs/.vitepress/sidebar.mjs`), and the root guides (`README.md`, `ARCHITECTURE.md`, `AGENTS.md`,
`CONTEXT.md`, `CONTRIBUTING.md`, `SECURITY.md`, `SUPPORT.md` and `LICENSE`) are staged into `docs/` (with
`README.md` at `docs/README.md`) so local search indexes them and links to them stay inside the portal.

Write relative links and heading anchors as you would for GitHub. On the site, links to unindexed files
outside `docs/`, to non-Markdown files or to folders without an `index.md` open on GitHub
(`docs/.vitepress/repoLinks.mjs`), while headings emit both VitePress and GitHub slugs. Any link to a
missing page or missing `#anchor` fragment, including in the nav and sidebar, fails the build.
