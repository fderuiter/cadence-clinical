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

1. Read [`CONTEXT.md`](../../../../CONTEXT.md) and relevant ADRs in [`docs/adr/`](../../../../docs/adr/) to anchor domain concepts.
2. Initialize the **Design Tree**: Map the root goal into branching prerequisite and downstream decisions.
3. Compute the **Frontier**: Identify all questions whose prerequisites are settled.
4. **MANDATORY TOOL INVOCATION (`ask_question`)**:
   You **MUST** call the `ask_question` tool to present frontier questions to the user. **Do NOT print questions as plain markdown text.**
   ```json
   {
     "questions": [
       {
         "question": "Which architectural pattern should govern <Feature>?",
         "options": [
           "(Recommended) Option A: Implement deep domain engine in apps/<service> with in-memory double",
           "Option B: Expose direct REST endpoints in apps/gateway with client proxy routing"
         ],
         "is_multi_select": false
       }
     ]
   }
   ```
   - **Rules**:
     - Format options as the user's direct response (e.g. _"Use PostgreSQL JSONB for eCRF observations"_ rather than _"I will configure JSONB"_).
     - Prefix the recommended answer with `(Recommended)` and list it first.
     - Never add a manual "Other" option (provided by UI).
     - Set `is_multi_select: true` if multiple options can be chosen together.
5. Perform fact-finding autonomously using `grep_search` and `view_file` before asking questions; never ask the user for information you can look up yourself.
6. When terms crystallize, update `CONTEXT.md` inline immediately using `replace_file_content`.
7. When significant architectural trade-offs are settled, scaffold an ADR (`pnpm cadence scaffold adr --domain <domain> "Title"`).
8. Conclude only when the frontier is empty and the user confirms shared understanding.
