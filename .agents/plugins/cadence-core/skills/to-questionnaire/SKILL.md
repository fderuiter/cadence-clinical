---
name: to-questionnaire
description: "Turn an ambiguous design decision or unresolved clinical requirement into a structured questionnaire for stakeholders. Use when blocked on product or clinical decisions."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Decision Questionnaire

Turns unresolved clinical, regulatory, or technical questions into a clear, structured questionnaire for clinical data managers, study designers, or product owners using Antigravity interactive tools and artifacts.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Interactive Prompt**| `ask_question` | `questions=[{"question": "...", "options": [...]}]` | Prompt stakeholder with interactive modal directly |
| **Questionnaire Artifact**| `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/questionnaire.md` with `ArtifactMetadata` | Emit complete questionnaire artifact for offline review |

---

## 1. Questionnaire Structure

Group questions by domain area and provide clear context:
1. **Background & Clinical Context**: Why this decision is open.
2. **Options & Regulatory Trade-offs**: Concrete choices with pros/cons and GxP implications.
3. **Recommended Default**: What the engineering team recommends.
4. **Impact of Delay**: How this blocks downstream development.

### MANDATORY TOOL INVOCATION (`ask_question`)

When administering the questionnaire interactively, you **MUST** call `ask_question`:
```json
{
  "questions": [
    {
      "question": "<Domain Decision Question>",
      "options": [
        "(Recommended) Option A: Preferred approach",
        "Option B: Alternative option"
      ],
      "is_multi_select": false
    }
  ]
}
```

---

## 2. Output as Antigravity Artifact

Write the full questionnaire document to `<appDataDir>/brain/<conversation-id>/questionnaire.md` with `ArtifactMetadata` (`UserFacing: true`, `RequestFeedback: true`).


