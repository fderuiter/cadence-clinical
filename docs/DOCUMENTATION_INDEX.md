# Documentation Index

The single map of Cadence documentation. Every document is listed once, with its status:

- **Normative:** the source of truth; other documents must agree with it.
- **Current:** maintained with the code; updated in the same pull request as the change.
- **Generated:** produced by a command; never edit by hand.
- **Baseline:** written for the pre-kernel, per-service design. Kept for requirement IDs and
  traceability; a banner at the top says what supersedes it.
- **Historical:** records of earlier decisions and completed or retired work.

Curated guides and section landing pages are listed here; individual entries under `docs/adr/`, `docs/blog/`,
`docs/api/`, and `docs/schema/` are discovered directly from disk. When you add a curated guide, add it here;
when a change makes a document wrong, fix it in the same PR ([CONTRIBUTING.md §5](../CONTRIBUTING.md#5-documentation-is-part-of-the-change)).

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
| [OpenAPI specifications](openapi/README.md) | Generated | Exported OpenAPI documents (`pnpm openapi:export`); Python-era specs kept in `openapi/legacy/` |
| [UI components](components/index.md) | Current | Shared Vue component catalog (`packages/ui`) |
| [Moving the web app onto the .NET API](web/API_MIGRATION.md) | Current | Which web screens have a .NET backend, which are hidden, and how a screen moves over (epic #4833) |
| [Migration workflow playbook](MIGRATION_WORKFLOW.md) | Current | How to port the remaining Python reference sources to C# |
| [Python reference inventory](PYTHON_REFERENCE_INVENTORY.md) | Current | Each remaining Python file, its C# status and what it needs before deletion (#4789) |
| [Operations and deployment guide](SDLC/07_Operations_Deployment_Guide.md) | Baseline | Deployment topology and runbooks; being revised with the production-readiness work |
| [Lab reference ranges](OPERATIONAL_LAB_RANGES.md) | Baseline | Range selection, scoring and tie-breaking rules |
| [eTMF inbound email](OPERATIONAL_INBOUND_EMAIL.md) | Baseline | Inbound-email webhook security and processing |
| [Data lifecycle](DATA_LIFECYCLE.md) | Baseline | eTMF document QC lifecycle |
| [AuditLogViewer](components/audit-viewer.md) | Current | The `AuditLogViewer` renders an interactive, chronological 21 CFR Part 11 audit trail feed with before/after value di... |
| [ClinicalDataTable](components/data-table.md) | Current | The `ClinicalDataTable` component provides an accessible, performant tabular viewer for clinical observation records,... |
| [ClinicalModal](components/modal.md) | Current | The `ClinicalModal` component renders accessible dialogs with focus trapping, `Escape` key listeners, backdrop blur, ... |
| [PersonaSwitcher](components/persona-switcher.md) | Current | The `PersonaSwitcher` component enables multi-persona simulation across clinical operations workflows. Users and auto... |

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
this index together with disk discovery for `docs/adr/`, `docs/blog/`, `docs/api/`, and `docs/schema/` (`docs/.vitepress/sidebar.mjs`), and the root guides (`README.md`, `ARCHITECTURE.md`, `AGENTS.md`,
`CONTEXT.md`, `CONTRIBUTING.md`, `SECURITY.md`, `SUPPORT.md` and `LICENSE`) are staged into `docs/` (with
`README.md` at `docs/README.md`) so local search indexes them and links to them stay inside the portal.

Write relative links and heading anchors as you would for GitHub. On the site, links to unindexed files
outside `docs/`, to non-Markdown files or to folders without an `index.md` open on GitHub
(`docs/.vitepress/repoLinks.mjs`), while headings emit both VitePress and GitHub slugs. Any link to a
missing page or missing `#anchor` fragment, including in the nav and sidebar, fails the build.
