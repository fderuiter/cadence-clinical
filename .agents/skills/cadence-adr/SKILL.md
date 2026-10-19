---
name: cadence-adr
description: "Scaffold, author, and record Architecture Decision Records (ADRs) aligned with Cadence domain standards and auto-indexed in docs/adr/index.md. Use when introducing dependencies, changing data models, modifying service boundaries, or making architectural trade-offs."
license: MIT
metadata:
  author: Cadence Team
  version: "1.0"
---

# Cadence Architecture Decision Records (ADR)

Records significant, hard-to-reverse architectural decisions with clear rationale, context, and consequences using Antigravity scaffolding and editing tools.

## Antigravity Tool Execution Matrix

| Phase           | Antigravity Tool       | Invocation Pattern                                              | Objective                                                         |
| :-------------- | :--------------------- | :-------------------------------------------------------------- | :---------------------------------------------------------------- |
| **Scaffolding** | `run_command`          | `CommandLine="dotnet cadence scaffold adr '<Title>'"`           | Generate dated ADR template and auto-index in `docs/adr/index.md` |
| **Inspection**  | `view_file`            | `AbsolutePath=".../docs/adr/YYYY-MM-DD-slug.md"`                | Inspect scaffolded sections and template comments                 |
| **Authoring**   | `replace_file_content` | Target: `docs/adr/YYYY-MM-DD-slug.md`, contiguous section edits | Populate Problem Statement, Options, Outcome, and Trade-offs      |
| **Validation**  | `run_command`          | `CommandLine="dotnet cadence check adrs"`                       | Verify format, frontmatter, and requirement ID traceability       |

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
dotnet cadence scaffold adr "Descriptive Title"
```

This creates a dated file under `docs/adr/YYYY-MM-DD-slug.md` and automatically updates the index table in [`docs/adr/index.md`](../../../docs/adr/index.md).

## ADR Authoring Protocol

1. **Scaffold**: Execute `dotnet cadence scaffold adr "Title"`.
2. **Author Sections**: Use `replace_file_content` or `edit_file` to fill in:
   - **Header Metadata**: Hyphenated list items (`- **Status:**`, `- **Date:**`, `- **Authors:**`, `- **Deciders:**`).
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
3. **Verify Compliance**: Run `dotnet cadence check adrs` to confirm index alignment, top-level section presence, and strict `### Option` subsection formatting.

## Gotchas

- The scaffolder appends the index entry to the end of `docs/adr/index.md`; move it into the matching domain section.
- The number is the next free one on your checkout. Parallel PRs claim numbers too, so re-check `origin/main` before pushing and renumber on a clash ([numbering rules](../../../docs/adr/index.md#numbering-rules)).
- In an agent shell the CLI prints only its banner unless run as `TERM=dumb pnpm cadence check adrs` or `script -qc "pnpm cadence check adrs" /dev/null` ([docs/CI.md](../../../docs/CI.md)).
- Superseding an ADR also changes the old ADR's status, adds a `> [!NOTE]` callout, annotates its index entry, and updates `ARCHITECTURE.md` and `CONTEXT.md` (AGENTS.md).
