---
name: grill-me
description: "Grill the user relentlessly about a plan, decision, or architecture. Use when the user wants to stress-test their thinking or uses any 'grill' trigger phrase."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Grilling Protocol

Interview the user relentlessly until reaching a shared, unambiguous understanding using Antigravity search, interactive questioning (`ask_question`), and session artifacts. Map this as a **design tree**: every decision branches into the decisions that depend on it.

## Antigravity Tool Execution Matrix

| Phase                 | Antigravity Tool               | Invocation Pattern                                              | Objective                                                  |
| :-------------------- | :----------------------------- | :-------------------------------------------------------------- | :--------------------------------------------------------- |
| **Fact Finding**      | `grep_search` / `find_by_name` | Query files across `apps/` & `packages/`                        | Look up facts and constraints autonomously                 |
| **Frontier Decision** | `ask_question`                 | `questions=[{"question": "...", "options": [...]}]`             | Prompt user with interactive multi-choice when appropriate |
| **Design Synthesis**  | `write_to_file`                | Target: `<appDataDir>/brain/<conversation-id>/grill_summary.md` | Emit structured design tree and settled decisions          |

---

## The Grilling Loop

Run the loop exactly as [`grilling`](../grilling/SKILL.md) defines it: frontier rounds over the design tree, every question through `ask_question` with the recommended option first, facts looked up rather than asked, and no action until the user confirms a shared understanding. That skill is the one source of the procedure; this one adds only the entry point and the summary.

When the frontier is empty, emit a summary artifact to `<appDataDir>/brain/<conversation-id>/grill_summary.md` via `write_to_file`: the design tree, each settled decision and the answer that settled it.

To record the outcome in `CONTEXT.md` and `docs/adr/` as decisions land, use [`grill-with-docs`](../grill-with-docs/SKILL.md) instead.
