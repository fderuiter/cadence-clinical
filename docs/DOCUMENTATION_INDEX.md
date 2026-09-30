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

## Start here

| Document | Status | What it is |
| --- | --- | --- |
| [README](../README.md) | Current | What Cadence is, where the code is today, quickstart |
| [Kernel specification v0.2](kernel/CLINICAL_KERNEL_SPECIFICATION.md) | Normative | Clinical Research OS: engines, kernel concepts, invariants (`K-…`), roadmap (§15) |
| [ARCHITECTURE.md](../ARCHITECTURE.md) | Current | Target architecture, key decisions, running services, security, persistence |
| [CONTEXT.md](../CONTEXT.md) | Current | Domain glossary: kernel vocabulary and application terms |
| [AGENTS.md](../AGENTS.md) | Current | Coding rules for humans and AI agents |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | Current | Setup, workflow, local checks, documentation rules |

## Kernel and roadmap

| Document | Status | What it is |
| --- | --- | --- |
| [Kernel specification v0.2](kernel/CLINICAL_KERNEL_SPECIFICATION.md) | Normative | The model every capability builds on |
| [Roadmap (spec §15)](kernel/CLINICAL_KERNEL_SPECIFICATION.md#15-roadmap) | Normative | Phases 0–5, tracked in epic [#4719](https://github.com/fderuiter/cadence-clinical/issues/4719) |
| [Kernel decisions K001–K014](adr/index.md) | Current | Kernel decisions and their ADRs; [ARCHITECTURE §2](../ARCHITECTURE.md#2-key-decisions) summarises them with the platform decisions |
| [Kernel package guide](https://github.com/fderuiter/cadence-clinical/blob/main/packages/kernel/README.md) | Current | How the kernel projects are built and used: command pipeline, tenancy, NodeRef, explanations, Concept and Standards Registries |
| [Execution migration inventory](kernel/EXECUTION_MIGRATION_INVENTORY.md) | Current | What `apps/execution` must change to run on the kernel (roadmap phase 1) |
| [Clock migration backlog](kernel/CLOCK_MIGRATION_BACKLOG.md) | Current | Remaining `DateTime.Now/UtcNow` call sites to move to `IClock` (K-TIME-3) |
| [Clinical Canvas](ux/CLINICAL_CANVAS.md) | Current | UI interaction model (study-centric navigation, three panes, six verbs) and the kernel read APIs the UI needs |

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
| [CI and quality gates](CI.md) | Current | Workflows, CI jobs, the 21 quality gates, the pre-commit hook and a failure runbook |
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

## Requirements, validation and GxP

| Document | Status | What it is |
| --- | --- | --- |
| [SDLC documentation suite](SDLC/SDLC_Documentation_Suite.md) | Baseline | Overview of the SDLC document set |
| [Product requirements (PRD)](SDLC/01_Product_Requirements_Document_PRD.md) | Baseline | `PRD-…` requirement IDs used by tests and the RTM |
| [Software requirements (SRS)](SRS.md) | Baseline | `SRS-…` and `Trace-…` requirement IDs |
| [Requirements traceability matrix](SDLC/Requirements_Traceability_Matrix.md) | Generated | Requirement ↔ test mapping (`pnpm cadence gxp sync`) |
| [IQ/OQ/PQ execution report](SDLC/IQ_OQ_PQ_Execution_Report.md) | Generated | Qualification evidence from the last sync |
| [Technical design (TDD)](SDLC/02_Technical_Design_Document_TDD.md) | Baseline | Superseded by the kernel spec and ARCHITECTURE.md |
| [API and integration specification](SDLC/03_API_Integration_Specification.md) | Baseline | Planned integration contracts |
| [Data standards and interoperability](SDLC/04_Data_Standards_Interoperability_Blueprint.md) | Baseline | CDISC, FHIR and coding dictionaries |
| [Security, compliance and audit](SDLC/05_Security_Compliance_Audit_Spec.md) | Baseline | Part 11 controls; authentication now per ADR-2256 |
| [Signature token contract](SDLC/Signature_Token_Cryptographic_Contract.md) | Baseline | E-signature step-up token format (step-up not yet wired, #4759) |
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
| [LICENSE](../LICENSE) | ISC license |
| [Agent working notes](agents/domain.md) | How agents use domain docs, the [issue tracker](agents/issue-tracker.md) and [triage labels](agents/triage-labels.md) |

## Documentation site

`pnpm docs:dev` serves this folder with VitePress; `pnpm docs:build` builds the static site (it also
runs the quality gates and regenerates the API, schema and RTM documents). The site's home page is
generated from the root README.

Write relative links as you would for GitHub. On the site, a link to a file outside `docs/`, to a
non-Markdown file or to a folder without an `index.md` opens that file on GitHub
(`docs/.vitepress/repoLinks.mjs`). Any other link to a missing page, including in the nav and sidebar,
fails the build.
