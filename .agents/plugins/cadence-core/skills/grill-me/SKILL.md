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

1. **Rounds & The Frontier**:
   - The **frontier** contains every decision whose prerequisites are settled.
   - You must evaluate the entire frontier in rounds.
   - For each decision, perform autonomous fact-finding first using `grep_search` and `find_by_name`.

2. **MANDATORY TOOL INVOCATION (`ask_question`)**:
   - You **MUST** call the `ask_question` tool to present frontier questions to the user. **Do NOT print multiple-choice questions as plain markdown text in chat.**
   - Format options from the user's perspective (e.g. _"Use PostgreSQL JSONB"_).
   - Place the recommended option first, prefixed with `(Recommended)`.
   - Never add a manual "Other" option (provided by UI).
   - Set `is_multi_select: true` when multiple orthogonal choices can be selected simultaneously.

   ```json
   {
     "questions": [
       {
         "question": "Which design approach should govern <Topic>?",
         "options": [
           "(Recommended) Option A: Concrete recommended decision with rationale",
           "Option B: Alternative approach with trade-offs"
         ],
         "is_multi_select": false
       }
     ]
   }
   ```

3. **Autonomous Fact-Finding**:
   - Facts are the agent's job. When a question requires repo, file, or schema facts, look them up or dispatch a subagent. Never ask the user for information available in the environment.

4. **Iterate & Conclude**:
   - Each round of user answers settles decisions and advances the frontier outward.
   - Emit a summary artifact to `<appDataDir>/brain/<conversation-id>/grill_summary.md` via `write_to_file`.
   - The session ends when the frontier is empty and no unspoken assumptions remain.
