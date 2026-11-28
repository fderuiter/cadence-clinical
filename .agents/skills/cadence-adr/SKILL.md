---
name: cadence-adr
description: "Scaffold, author, and record Architecture Decision Records (ADRs) aligned with Cadence domain standards and auto-indexed in docs/adr/<domain>/index.md. Use when introducing dependencies, changing data models, modifying service boundaries, or making architectural trade-offs."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Architecture Decision Records (ADR)

Records significant, hard-to-reverse architectural decisions with clear rationale, context, and consequences using Antigravity scaffolding and editing tools.

## Antigravity Tool Execution Matrix

| Phase           | Antigravity Tool       | Invocation Pattern                                                       | Objective                                                                           |
| :-------------- | :--------------------- | :----------------------------------------------------------------------- | :---------------------------------------------------------------------------------- |
| **Scaffolding** | `run_command`          | `CommandLine="pnpm cadence scaffold adr --domain <domain> '<Title>'"`    | Generate dated ADR template and insert sorted entry in `docs/adr/<domain>/index.md` |
| **Inspection**  | `view_file`            | `AbsolutePath=".../docs/adr/<domain>/YYYY-MM-DD-slug.md"`                | Inspect scaffolded sections and template comments                                   |
| **Authoring**   | `replace_file_content` | Target: `docs/adr/<domain>/YYYY-MM-DD-slug.md`, contiguous section edits | Populate Problem Statement, Options, Outcome, and Trade-offs                        |
| **Validation**  | `run_command`          | `CommandLine="pnpm cadence check adrs"`                                  | Verify format, unique ID, shard sorting, and requirement ID traceability            |

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

Use the automated ADR generator with `--domain` (`kernel`, `platform`, `security`, `standards`, `operations`, `compliance`, `frontend`, `tooling`):

```bash
pnpm cadence scaffold adr --domain security "Descriptive Title"
```

This creates `docs/adr/<domain>/YYYY-MM-DD-slug.md` headed `# ADR-<DOMAIN>-YYYYMMDD-<slug>: <title>` and inserts a sorted entry into `docs/adr/<domain>/index.md` while keeping [`docs/adr/index.md`](../../../docs/adr/index.md) and [`docs/adr/legacy-index.md`](../../../docs/adr/legacy-index.md) untouched.

## ADR Authoring Protocol

1. **Scaffold**: Execute `pnpm cadence scaffold adr --domain <domain> "Title"`.
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
3. **Verify Compliance**: Run `pnpm cadence check adrs` to confirm shard index alignment, alphabetical sorting, top-level section presence, and requirement references.

## Gotchas

- Always choose the owning domain shard (`kernel`, `platform`, `security`, `standards`, `operations`, `compliance`, `frontend`, `tooling`). Sequential `ADR-NNNN` numbers in `docs/adr/legacy-index.md` are frozen and closed to new ADRs ([numbering rules](../../../docs/adr/index.md#numbering-rules)).
- Entries in `docs/adr/<domain>/index.md` must stay sorted alphabetically by filename (`YYYY-MM-DD-<slug>.md`), not appended out of order.
- Superseding an ADR also changes the old ADR's status, adds a `> [!NOTE]` callout, annotates its shard or legacy index entry, and updates `ARCHITECTURE.md` and `CONTEXT.md` (AGENTS.md).
