---
name: loop-me
description: "Grill the user about specifications for workflows and clinical features within this workspace. Use when scoping a workflow or defining execution logic."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Workflow Looper

Executes a focused grilling session specifically tailored to scoping and validating multi-step clinical workflows (e.g. subject re-consent gating, SDV verification, query discrepancy lifecycle, site reimbursement triggers) using Antigravity search, interactive questioning, and artifact generation.

## Antigravity Tool Execution Matrix

| Phase                        | Antigravity Tool            | Invocation Pattern                                               | Objective                                                 |
| :--------------------------- | :-------------------------- | :--------------------------------------------------------------- | :-------------------------------------------------------- |
| **Workflow State Discovery** | `grep_search` / `view_file` | Read related domain models and services                          | Audit existing state machines and transition guards       |
| **State Machine Query**      | `ask_question`              | `questions=[{"question": "Define State Transition Guard", ...}]` | Interactively settle edge cases and unhandled transitions |
| **Workflow Spec Artifact**   | `write_to_file`             | Target: `<appDataDir>/brain/<conversation-id>/workflow_spec.md`  | Emit structured state transition table and audit rules    |

### MANDATORY TOOL INVOCATION (`ask_question`)

You **MUST** call the `ask_question` tool to settle state machine transitions and clinical guardrails. **Do NOT print questions as plain markdown text.**

```json
{
  "questions": [
    {
      "question": "How should the state machine handle <Clinical Edge Case>?",
      "options": [
        "(Recommended) Enforce synchronous pre-condition check and emit audit log failure",
        "Allow transition with flagged discrepancy query for monitor review"
      ],
      "is_multi_select": false
    }
  ]
}
```

Calls [`grill-with-docs`](../grill-with-docs/SKILL.md) to map out the complete workflow state machine, pre-conditions, error transitions, and 21 CFR Part 11 audit trails.
