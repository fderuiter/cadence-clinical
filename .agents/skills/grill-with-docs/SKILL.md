---
name: grill-with-docs
description: "A relentless design-tree interview to sharpen a plan or architecture, creating and updating CONTEXT.md glossary definitions and docs/adr/ records as decisions crystallize. Use when stress-testing architectural plans or clinical feature designs."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Grill-with-Docs

Executes an interactive design-tree interview (`grilling`) combined with live domain glossary and ADR generation (`domain-modeling`) using Antigravity search, interactive questioning, and editing tools.

## Antigravity Tool Execution Matrix

| Phase               | Antigravity Tool               | Invocation Pattern                                                    | Objective                                                 |
| :------------------ | :----------------------------- | :-------------------------------------------------------------------- | :-------------------------------------------------------- |
| **Fact Finding**    | `grep_search` / `find_by_name` | Autonomous queries across `apps/` & `packages/`                       | Look up facts, models, and constraints before asking user |
| **Domain Anchor**   | `view_file`                    | Read `CONTEXT.md` and `docs/adr/` via relative paths                  | Anchor discussions to established ubiquitous language     |
| **Frontier Query**  | `ask_question`                 | `questions=[{"question": "...", "options": [...]}]`                   | Prompt user with interactive multi-choice when useful     |
| **Glossary Update** | `replace_file_content`         | Target: `CONTEXT.md`, surgical section update                         | Add or refine terms under `# Clinical Domain Glossary`    |
| **ADR Creation**    | `run_command`                  | `CommandLine="pnpm cadence scaffold adr --domain <domain> '<Title>'"` | Scaffold dated ADR for high-stakes decisions              |

---

## Execution Protocol

1. Read [`CONTEXT.md`](../../../CONTEXT.md) and relevant ADRs in [`docs/adr/`](../../../docs/adr/) to anchor domain concepts.
2. Initialize the **Design Tree**: Map the root goal into branching prerequisite and downstream decisions.
3. Compute the **Frontier**: Identify all questions whose prerequisites are settled.
4. Ask each round of frontier questions through `ask_question`, following the rules in [`grilling`](../grilling/SKILL.md#mandatory-tool-invocation-ask_question) (recommended option first, options phrased as the user's answer, no manual "Other", multi-select only for orthogonal choices). For example, phrase an option as _"Use PostgreSQL JSONB for eCRF observations"_ rather than _"I will configure JSONB"_.
5. Perform fact-finding autonomously using `grep_search` and `view_file` before asking questions; never ask the user for information you can look up yourself.
6. When terms crystallize, update `CONTEXT.md` inline immediately using `replace_file_content`.
7. When significant architectural trade-offs are settled, scaffold an ADR (`pnpm cadence scaffold adr --domain <domain> "Title"`).
8. Conclude only when the frontier is empty and the user confirms shared understanding.
