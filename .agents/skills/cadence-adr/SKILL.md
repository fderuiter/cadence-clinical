---
name: cadence-adr
description: "Scaffold, author, and record Architecture Decision Records (ADRs) aligned with Cadence domain standards and discovered directly from disk in docs/adr/. Use when introducing dependencies, changing data models, modifying service boundaries, or making architectural trade-offs."
license: MIT
metadata:
  author: Cadence Team
  version: "1.0"
---

# Cadence Architecture Decision Records (ADR)

Records significant, hard-to-reverse architectural decisions with clear rationale, context, and consequences using Antigravity scaffolding and editing tools.

## Antigravity Tool Execution Matrix

| Phase           | Antigravity Tool       | Invocation Pattern                                                                      | Objective                                                                   |
| :-------------- | :--------------------- | :-------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------- |
| **Scaffolding** | `run_command`          | `CommandLine="pnpm cadence scaffold adr '<Title>' --domain <domain> --req PRD-SYS-001"` | Generate self-contained dated ADR with deterministic ID and `- **Domain:**` |
| **Inspection**  | `view_file`            | `AbsolutePath=".../docs/adr/YYYY-MM-DD-slug.md"`                                        | Inspect scaffolded sections and template comments                           |
| **Authoring**   | `replace_file_content` | Target: `docs/adr/YYYY-MM-DD-slug.md`, contiguous section edits                         | Populate Problem Statement, Options, Outcome, and Trade-offs                |
| **Validation**  | `run_command`          | `CommandLine="pnpm cadence check adrs"`                                                 | Verify format, `- **Domain:**` metadata, and requirement ID traceability    |

## When an ADR is Required

Create an ADR when:

1. Adding a new third-party dependency or database engine.
2. Modifying inter-service data contracts or introducing new API gateways.
3. Changing data storage models (PostgreSQL schema migrations; Neo4j is retired, ADR-2245).
4. Making architectural decisions that satisfy all three criteria:
   - **Hard to reverse**: High cost to change later.
   - **Surprising without context**: Future maintainers would ask "why was this done this way?".
   - **Real trade-off**: Deliberate choice between viable alternatives.

## Scaffolding an ADR

Use the automated ADR generator:

```bash
pnpm cadence scaffold adr "Descriptive Title" --domain <domain> --req PRD-SYS-001
```

This creates a self-contained dated file under `docs/adr/YYYY-MM-DD-slug.md` with a deterministic SHA-256 date-and-slug hash ID (`10000–99999`) and canonical `- **Domain:**` metadata; individual ADR files are discovered directly from disk by `<AdrIndex />` in [`docs/adr/index.md`](../../../docs/adr/index.md) without editing central index files.

## ADR Authoring Protocol

1. **Scaffold**: Execute `pnpm cadence scaffold adr "Title" --domain <domain> --req PRD-SYS-001`.
2. **Author Sections**: Use `replace_file_content` or `edit_file` to fill in:
   - **Header Metadata**: Hyphenated list items (`- **Status:**`, `- **Date:**`, `- **Domain:**`, `- **Authors:**`, `- **Deciders:**`, `- **Requirements:**`).
   - **Context & Problem Statement**: Background, forces, requirements, and constraints (`PRD-SYS-xxx`).
   - **Decision Drivers & Constraints**: Bulleted list of criteria (`- **Driver 1:**`, `- **Driver 2:**`).
   - **Options Considered**: Each option must be structured under an `### Option N: [Name]` subheading and include:
     - `- **Overview:**` Brief description.
     - `- **Pros:**` Bulleted list (`  - ✅ ...`).
     - `- **Cons:**` Bulleted list (`  - ❌ ...`).
   - **Decision Outcome**:
     - `- **Chosen Option:** Option N`
     - `- **Justification:**` Rationale tied to decision drivers.
   - **Consequences & Trade-offs**:
     - `- **Positive Impact:**`
     - `- **Negative Impact / Technical Debt:**`
     - `- **Mitigation Strategy:**`
   - **Implementation & Verification**:
     - `- **Affected Repositories / Services:**`
     - `- **Verification Plan:**`
3. **Verify Compliance**: Run `pnpm cadence check adrs` to confirm unique ADR numbering, `- **Domain:**` metadata, top-level section presence, and strict `### Option` subsection formatting.

## Gotchas

- Never add per-ADR bullet entries to `docs/adr/index.md` or `docs/DOCUMENTATION_INDEX.md`; both the `<AdrIndex />` catalog and VitePress navigation discover `docs/adr/*.md` directly from disk ([numbering rules](../../../docs/adr/index.md#numbering-rules)).
- Superseding an ADR changes the old ADR's `- **Status:**` to `Superseded by [ADR-NNNN](YYYY-MM-DD-slug.md)`, adds a `> [!NOTE]` callout, and updates `ARCHITECTURE.md` and `CONTEXT.md` (AGENTS.md).
