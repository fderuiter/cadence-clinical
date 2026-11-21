---
name: writing-beats
description: "Assemble raw concepts and fragments into an ordered progression of beats, grounding prerequisite terms before downstream concepts lean on them. Use when structuring technical articles or specs."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Writing: Beats

Structure raw ideas into a logical journey of beats where each concept is introduced and grounded before subsequent beats depend on it using Antigravity artifact tools.

## Antigravity Tool Execution Matrix

| Phase                | Antigravity Tool | Invocation Pattern                                              | Objective                        |
| :------------------- | :--------------- | :-------------------------------------------------------------- | :------------------------------- |
| **Beat Structuring** | `write_to_file`  | Target: `<appDataDir>/brain/<conversation-id>/beats_outline.md` | Emit topological concept outline |

---

## Execution Rules

1. List the essential concepts to convey.
2. Order them topologically: prerequisites first, dependent conclusions last.
3. Verify that every beat transitions naturally to the next.
