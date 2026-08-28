---
name: wait-what
description: "Stop and re-pitch the last response or explanation when it missed the mark, was over-complicated, or misunderstood the user's intent. Use when the user indicates confusion or disorientation."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Wait-What: Reset & Re-Pitch

When the previous message missed the mark, was needlessly complex, or misunderstood the problem, reset and realign using Antigravity interactive questioning.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Realignment Prompt** | `ask_question` | `questions=[{"question": "How would you prefer to proceed?", "options": [...]}]` | Prompt user with simplified choice modal |

---

## 1. Acknowledge and Stop
Do not defend the previous response or double down on complexity.

## 2. Strip the Noise
Reframe the core problem in plain, direct language in 1-2 paragraphs.

## 3. Mandatory Realignment Invocation (`ask_question`)

You **MUST** call `ask_question` to reset and confirm user intent interactively:
```json
{
  "questions": [
    {
      "question": "How would you prefer to proceed?",
      "options": [
        "(Recommended) Simplify and take the direct implementation approach",
        "Take a step back to align on architecture using /grill-with-docs",
        "Explain the underlying domain concept from scratch"
      ],
      "is_multi_select": false
    }
  ]
}
```


