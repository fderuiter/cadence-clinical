# Root & Cross-Cutting Guides Index

Root-level `docs/*.md` guides and staged repository-root references. Part of the sharded [Documentation Index](DOCUMENTATION_INDEX.md); rows in each table are sorted alphabetically by file path.

## Start here

| Document | Status | What it is |
| --- | --- | --- |
| [AGENTS.md](../AGENTS.md) | Current | Coding rules for humans and AI agents |
| [ARCHITECTURE.md](../ARCHITECTURE.md) | Current | Target architecture, key decisions, running services, security, persistence |
| [CONTEXT.md](../CONTEXT.md) | Current | Domain glossary: kernel vocabulary and application terms |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | Current | Setup, workflow, local checks, documentation rules |
| [README](../README.md) | Current | What Cadence is, where the code is today, quickstart |
| [Backlog insights archive](BACKLOG_INSIGHTS_ARCHIVE.md) | Historical | Source-to-owner provenance and constraints preserved during the November 2026 reset |
| [Goals and priorities](GOALS_AND_PRIORITIES.md) | Current | Code-level objectives and measurable outcomes, in priority order |
| [Issue management](ISSUE_MANAGEMENT.md) | Current | Canonical issue taxonomy, readiness, horizon caps, and maintenance cadence |
| [Kernel specification v0.2](kernel/CLINICAL_KERNEL_SPECIFICATION.md) | Normative | Clinical Research OS: engines, kernel concepts, invariants (`K-…`), roadmap (§15) |
| [Project plan](PROJECT_PLAN.md) | Current | Single-repository delivery scope, module ownership, milestones, and engineering practices |

## Developing and running

| Document | Status | What it is |
| --- | --- | --- |
| [Service and package READMEs](../ARCHITECTURE.md#3-running-services-today) | Current | One README per `apps/*` and `packages/*` directory |
| [CI and quality gates](CI.md) | Current | Workflows, CI jobs, the quality gates, the pre-commit hook and a failure runbook |
| [CLI reference](cli.md) | Generated | Every `pnpm cadence` command (`pnpm cadence generate-cli-docs`) |
| [Data lifecycle](DATA_LIFECYCLE.md) | Baseline | eTMF document QC lifecycle |
| [Environment variables](ENVIRONMENT_VARIABLES.md) | Current | Every setting the code reads, required or optional |
| [Local development environment](LOCAL_DEV_ENVIRONMENT.md) | Current | Prerequisites, build, run (compose, Aspire, single service), ports |
| [Migration workflow playbook](MIGRATION_WORKFLOW.md) | Current | How to port the remaining Python reference sources to C# |
| [Database layout and migrations](OPERATIONAL_DATABASE.md) | Current | One `cadence` database with a schema per service; migrations at startup or as a job |
| [eTMF inbound email](OPERATIONAL_INBOUND_EMAIL.md) | Baseline | Inbound-email webhook security and processing |
| [Lab reference ranges](OPERATIONAL_LAB_RANGES.md) | Baseline | Range selection, scoring and tie-breaking rules |
| [Python reference inventory](PYTHON_REFERENCE_INVENTORY.md) | Historical | Archived record of retired Python reference files and their C# dispositions (#4789, ADR-2509) |
| [Testing and requirement qualification](TESTING.md) | Current | Running the .NET and Vitest suites, PostgreSQL opt-ins, `Cadence.Testing` helpers, requirement tagging and when a requirement counts as qualified |

## Requirements, validation and GxP

| Document | Status | What it is |
| --- | --- | --- |
| [Software requirements (SRS)](SRS.md) | Baseline | `SRS-…` and `Trace-…` requirement IDs |

## Product

| Document | Status | What it is |
| --- | --- | --- |
| [Competitive analysis](COMPETITIVE_ANALYSIS_AND_MARKET_SUPERIORITY.md) | Baseline | Product vision and market positioning |
| [Feature matrix](FEATURE_MATRIX.md) | Baseline | Features by service in the per-service design |
| [Functional product specification](FUNCTIONAL_PRODUCT_SPECIFICATION.md) | Baseline | Capabilities reverse-engineered from the Python implementation |

## Standards reference material

| Location | What it is |
| --- | --- |
| [docs/NCI](NCI/) | NCI EVS REST API description |

## Repository policies

| Document | What it is |
| --- | --- |
| [LICENSE](../LICENSE) | Proprietary licence, all rights reserved |
| [SECURITY.md](../SECURITY.md) | How to report a vulnerability |
| [SUPPORT.md](../SUPPORT.md) | Where to get help |
