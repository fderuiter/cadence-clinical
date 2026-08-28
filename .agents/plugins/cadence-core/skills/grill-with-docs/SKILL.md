---
name: grill-with-docs
description: "A relentless design-tree interview to sharpen a plan or architecture, creating and updating CONTEXT.md glossary definitions and docs/adr/ records as decisions crystallize. Use when stress-testing architectural plans or clinical feature designs."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Grill-with-Docs

Executes an interactive design-tree interview (`grilling`) combined with live domain glossary and ADR generation (`domain-modeling`).

## Execution Protocol

1. Read `CONTEXT.md` and relevant ADRs in `docs/adr/` to anchor domain concepts.
2. Initialize the **Design Tree**: Map the root goal into branching prerequisite and downstream decisions.
3. Compute the **Frontier**: Identify all questions whose prerequisites are settled.
4. Format all frontier questions in the round:
   ```
   ❓ **Q1** - **<question title>**: <body with trade-offs and options>

   ➡️ <recommended answer>
   ```
5. Perform fact-finding autonomously (using file tools / subagents) without asking the user for facts you can look up yourself.
6. When terms crystallize, update `CONTEXT.md` inline immediately.
7. When significant architectural trade-offs are settled, scaffold an ADR (`uv run cadence scaffold adr "Title"`).
8. Conclude only when the frontier is empty and the user confirms shared understanding.
