---
name: teach
description: "Teach the user a concept, clinical standard, or architectural pattern using worked examples and interactive questions. Use when explaining complex domain logic or engineering concepts."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Interactive Teaching

Teach a technical or clinical concept using concrete mental models, worked examples, and interactive checks for understanding using Antigravity interactive questioning.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Check Understanding**| `ask_question` | `questions=[{"question": "Quick Check Question", ...}]` | Test comprehension with interactive modal |

---

## Process
1. State the core mental model in 1 sentence.
2. Provide a concrete before/after code or clinical scenario.
3. Highlight the common pitfalls and why they occur.

### MANDATORY TOOL INVOCATION (`ask_question`)

You **MUST** call `ask_question` to test comprehension interactively:
```json
{
  "questions": [
    {
      "question": "<Comprehension Check Question>",
      "options": [
        "(Recommended) Option A: Correct conceptual application",
        "Option B: Common anti-pattern or misconception"
      ],
      "is_multi_select": false
    }
  ]
}
```


