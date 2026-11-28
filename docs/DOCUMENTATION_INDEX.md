# Documentation Index

The top-level map of Cadence documentation. Individual document tables live in per-directory shard indexes (`ROOT_INDEX.md`, `<category>/INDEX.md`, `<category>/DOC_INDEX.md`, and `adr/<domain>/index.md`) sorted alphabetically by file path so parallel pull requests never contend on a single table tail. Every document carries a lifecycle status:

- **Normative:** the source of truth; other documents must agree with it.
- **Current:** maintained with the code; updated in the same pull request as the change.
- **Generated:** produced by a command; never edit by hand.
- **Baseline:** written for the pre-kernel, per-service design. Kept for requirement IDs and
  traceability; a banner at the top says what supersedes it.
- **Historical:** records of earlier decisions and completed or retired work.

When you add or rename a non-ADR document under `docs/`, run `pnpm cadence generate-doc-index` to update only its owning directory shard (`CONTRIBUTING.md` [§5](../CONTRIBUTING.md#5-documentation-is-part-of-the-change)). When you add an ADR, run `pnpm cadence scaffold adr --domain <domain> "<title>"` to update `docs/adr/<domain>/index.md`.

---

## Category documentation shards

| Document | Status | What it is |
| --- | --- | --- |
| [Agent working notes index](agents/INDEX.md) | Current | Agent domain conventions, issue tracker, and triage labels (`docs/agents/`) |
| [AI agents & governance index](ai/INDEX.md) | Current | Governed AI runtime, MCP grounding tools, and evaluation specs (`docs/ai/`) |
| [REST API reference index](api/DOC_INDEX.md) | Generated | Per-service REST API Markdown pages generated from OpenAPI specs (`docs/api/`) |
| [Former repository migration index](architecture/INDEX.md) | Historical | Archived records from the retired ten-repository extraction plan (`docs/architecture/`) |
| [Engineering blog index](blog/DOC_INDEX.md) | Current | Published Cadence Clinical engineering blog posts (`docs/blog/`) |
| [CDISC standards index](CDISC/INDEX.md) | Current | CDISC Library API description and USDM reference material (`docs/CDISC/`) |
| [UI components index](components/DOC_INDEX.md) | Current | Shared Vue component catalog pages (`docs/components/`) |
| [Clinical Kernel & roadmap index](kernel/INDEX.md) | Normative | Clinical Kernel v0.2 specification, rule packs, indicators, mapping specs, and clock migration (`docs/kernel/`) |
| [Python-to-.NET port archive index](migration/DOC_INDEX.md) | Historical | Completed Phase 0 audits and bounded-context migration slice records (`docs/migration/`) |
| [OpenAPI specifications index](openapi/INDEX.md) | Generated | Exported `.NET` OpenAPI specification guide (`docs/openapi/`) |
| [Root & cross-cutting guides index](ROOT_INDEX.md) | Current | Onboarding, architecture, local dev, testing, CI, CLI, and root policies (`docs/*.md` and repo root) |
| [Database schema reference index](schema/INDEX.md) | Generated | Per-module EF Core database schema pages (`docs/schema/`) |
| [SDLC, validation & GxP index](SDLC/INDEX.md) | Current | GxP qualification snapshots, signature token contract, and pre-kernel SDLC baselines (`docs/SDLC/`) |
| [Pre-kernel design proposals index](specs/INDEX.md) | Baseline | Early design proposals retained for historical context (`docs/specs/`) |
| [Product & strategy index](strategy/INDEX.md) | Current | Market hypotheses and commercial packaging proposals (`docs/strategy/`) |
| [Clinical Canvas & UX index](ux/INDEX.md) | Current | Study-centric Clinical Canvas UI model and read APIs (`docs/ux/`) |
| [Web application guides index](web/INDEX.md) | Current | Frontend API migration and web client architecture (`docs/web/`) |

## Architecture decision records

| Document | Status | What it is |
| --- | --- | --- |
| [Compliance, audit & governance ADRs](adr/compliance/index.md) | Current | Domain shard `compliance` (`ADR-COMPLIANCE-YYYYMMDD-<slug>`) |
| [Frontend & design system ADRs](adr/frontend/index.md) | Current | Domain shard `frontend` (`ADR-FRONTEND-YYYYMMDD-<slug>`) |
| [ADR hub & kernel decisions K001–K037](adr/index.md) | Current | Static ADR overview, identifier rules, and K001–K037 decision table |
| [Clinical Kernel ADRs](adr/kernel/index.md) | Current | Domain shard `kernel` (`ADR-KERNEL-YYYYMMDD-<slug>`) |
| [Frozen legacy ADRs (ADR-049–ADR-2899)](adr/legacy-index.md) | Historical | Frozen index of sequential `ADR-NNNN` records in `docs/adr/*.md` |
| [Clinical operations & business modules ADRs](adr/operations/index.md) | Current | Domain shard `operations` (`ADR-OPERATIONS-YYYYMMDD-<slug>`) |
| [Core platform & execution engine ADRs](adr/platform/index.md) | Current | Domain shard `platform` (`ADR-PLATFORM-YYYYMMDD-<slug>`) |
| [API gateway, security & identity ADRs](adr/security/index.md) | Current | Domain shard `security` (`ADR-SECURITY-YYYYMMDD-<slug>`) |
| [Clinical data interoperability & standards ADRs](adr/standards/index.md) | Current | Domain shard `standards` (`ADR-STANDARDS-YYYYMMDD-<slug>`) |
| [ADR template](adr/TEMPLATE.md) | Current | Standard 6-section template for new Architectural Decision Records |
| [DevOps, tooling & CI/CD ADRs](adr/tooling/index.md) | Current | Domain shard `tooling` (`ADR-TOOLING-YYYYMMDD-<slug>`) |

## Documentation site

`pnpm docs:dev` serves this folder with VitePress; `pnpm docs:build` builds the static site (validating
`rtm`, `openapi`, `schema-doc` and `markdown` on every run), and
`pnpm docs:stage-demos` stages the interactive staff (`/demo/`) and participant (`/subject-portal/`) demos into
`docs/.vitepress/dist`. `docs/index.md` serves as the site's home page, sidebar navigation is synthesized across
this index and its category/ADR shards (`docs/.vitepress/sidebar.mjs`), and the root guides (`README.md`, `ARCHITECTURE.md`, `AGENTS.md`,
`CONTEXT.md`, `CONTRIBUTING.md`, `SECURITY.md`, `SUPPORT.md` and `LICENSE`) are staged into `docs/` (with
`README.md` at `docs/README.md`) so local search indexes them and links to them stay inside the portal.

Write relative links and heading anchors as you would for GitHub. On the site, links to unindexed files
outside `docs/`, to non-Markdown files or to folders without an `index.md` open on GitHub
(`docs/.vitepress/repoLinks.mjs`), while headings emit both VitePress and GitHub slugs. Any link to a
missing page or missing `#anchor` fragment, including in the nav and sidebar, fails the build.
