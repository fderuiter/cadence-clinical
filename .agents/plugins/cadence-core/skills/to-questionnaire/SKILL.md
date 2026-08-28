---
name: to-questionnaire
description: "Turn an ambiguous design decision or unresolved clinical requirement into a structured questionnaire for stakeholders. Use when blocked on product or clinical decisions."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Decision Questionnaire

Turns unresolved clinical, regulatory, or technical questions into a clear, structured questionnaire for clinical data managers, study designers, or product owners.

---

## 1. Questionnaire Structure

Group questions by domain area and provide clear context:
1. **Background & Clinical Context**: Why this decision is open.
2. **Options & Regulatory Trade-offs**: Concrete choices with pros/cons and GxP implications.
3. **Recommended Default**: What the engineering team recommends.
4. **Impact of Delay**: How this blocks downstream development.

---

## 2. Output as Antigravity Artifact

Write the questionnaire to `<appDataDir>/brain/<conversation-id>/questionnaire.md` with `ArtifactMetadata` (`UserFacing: true`, `RequestFeedback: true`).
