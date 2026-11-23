# ADR-12572: Disk Discovery and Deterministic ADR Identifiers

- **Status:** Accepted
- **Date:** 2026-11-23
- **Domain:** 7. DevOps, Tooling & CI/CD
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** `@req:PRD-SYS-001`

---

## 1. Context & Problem Statement

Parallel feature branches in the single-repository workspace previously collided on two central Markdown registries (`docs/DOCUMENTATION_INDEX.md` and `docs/adr/index.md`) and on sequential `ADR-NNNN` allocations (`max(existing) + 1`). Appending new table rows to section tails and requiring every ADR, blog post, generated API page, and database schema page to appear in central index files created deterministic three-way Git merge conflicts under `strict_required_status_checks_policy` (`@req:PRD-SYS-001`). In addition, `AdrValidatorService.IsArchitecturalFile` matched unanchored `schema` and `model` substrings and internal `packages/ui/src/**` component files, requiring ADRs on routine option classes and UI component refinements.

## 2. Decision Drivers & Constraints

- **Eliminate central registry contention:** Independent branches scaffolding ADRs, blog drafts, API references, or schema references must merge cleanly without editing shared tail-append lists (`@req:PRD-SYS-001`).
- **Deterministic, collision-free ADR identifiers:** Parallel branches must allocate reproducible `ADR-NNNNN` identifiers without coordinating a shared counter, while keeping all existing `ADR-001..2899` headings and `Also known as:` cross-references intact.
- **Native AOT and clock discipline:** All C# scaffolding and validation in `Cadence.ToolingCore` and `Cadence.Cli` must compile cleanly under `PublishAot` with explicit `IClock` / `DateOnly` injection (`K-TIME-3`).
- **Preserve strict link and traceability validation:** VitePress link/anchor checks (`assertThemeLinksExist`, `scripts/build-docs.js`) and `@req:PRD-...` traceability rules must remain enforced.

## 3. Options Considered

### Option 1: Disk Discovery, Sorted Curated Index, and Deterministic Hash ADR IDs (Chosen)

- **Overview:** Scope `docs/DOCUMENTATION_INDEX.md` to curated guides and section landing pages (`adr/index.md`, `adr/TEMPLATE.md`, `blog/index.md`, `api/index.md`, `schema/README.md`) sorted by normalized target path; discover `docs/adr/`, `docs/blog/`, `docs/api/`, and `docs/schema/` directly from disk in VitePress (`docs/adr/adrs.data.mjs`, `<AdrIndex />`, `docs/.vitepress/sidebar.mjs`); unify ADR scaffolding in `CreateAdrService` using SHA-256 `(date, slug)` deterministic IDs in `10000–99999` with collision probing; and anchor `IsArchitecturalFile` to persistence boundaries and `packages/ui` public entrypoints.
- **Pros:**
  - Eliminates merge conflicts across parallel ADR and blog branches while keeping `strict_required_status_checks_policy` intact
  - Preserves every historical ADR heading (`ADR-001..2899`), kernel decision table (`K001–K037`), and `@req:PRD-...` tag
  - Removes false-positive ADR prompts on internal UI components and non-persistence `Models`/`Schema` classes
- **Cons:**
  - Introduces a build-time VitePress data loader (`adrs.data.mjs`) and Vue catalog component (`AdrIndex.vue`) alongside domain metadata validation

### Option 2: Git `merge=union` Attributes on Central Registries

- **Overview:** Retain sequential counters and central Markdown registries, adding `merge=union` in `.gitattributes`.
- **Pros:**
  - Minimal tooling code changes
- **Cons:**
  - Silently admits duplicate `ADR-NNNN` allocations and corrupted Markdown tables during concurrent merges

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Satisfies `@req:PRD-SYS-001` by eliminating shared-file tail contention at its source, guaranteeing deterministic `ADR-NNNNN` identifiers across parallel branches, and keeping Git merge semantics and GxP traceability strict.

## 5. Consequences & Trade-offs

- **Positive Impact:** Parallel branches adding ADRs or blog drafts touch only their own dated Markdown files and merge cleanly without rebasing central index registries (`@req:PRD-SYS-001`).
- **Negative Impact / Technical Debt:** Newly scaffolded ADRs (`>= 3000`) carry explicit `- **Domain:**` metadata so disk discovery can group decisions without manual index curation.
- **Mitigation Strategy:** `CreateAdrService` populates `- **Domain:**`, all six required headings, and `@req:PRD-...` tags automatically, and `AdrValidatorService.FindIntegrityErrors` validates unique IDs, domain metadata, and relative links across `docs/adr/*.md`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/tooling_core`, `packages/cli`, `docs/.vitepress`, `docs/adr`, and `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `AdrValidatorServiceTests` (including two-branch Git merge verification), `DocIndexSyncServiceTests`, `ScaffoldPostAndBlogGatesTests`, `docs/.vitepress/navigation.test.mjs`, `pnpm docs:build`, and `pnpm cadence check`.
