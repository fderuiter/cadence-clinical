---
name: improve-codebase-architecture
description: "Scan the Cadence codebase for shallow interfaces, boundary leaks, or architectural deepening opportunities, and generate a visual inspection report as an Antigravity artifact. Use when refactoring, auditing module depth, or planning architecture enhancements."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Architecture Improvement Scanner

Scans modules across `apps/` and `packages/` to identify shallow abstractions, leaky boundaries, and high-leverage deepening opportunities using Antigravity subagents, visual diagrams, and session artifacts.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Boundary Scan** | `find_by_name` / `grep_search` | Search module directories under `apps/` and `packages/` | Identify multi-file clusters and shallow wrappers |
| **Deep Inspection** | `invoke_subagent` | `Subagents=[{"TypeName": "research", "Role": "Deep Module Inspector", "Model": "flash"}]` | Launch fast research subagent for structural audit |
| **Structural Analysis**| `invoke_subagent` | `Subagents=[{"Role": "Architecture Sentinel", "Model": "pro"}]` | Deep reasoning on complex decoupling refactors |
| **Architecture Diagram**| `generate_image` | `Prompt="Hexagonal architecture boundary diagram showing deep domain core for ..."`, `AspectRatio="16:9"` | Generate visual architecture diagram |
| **Opportunity Select**| `ask_question` | `questions=[{"question": "Select Deepening Opportunity to Refactor", ...}]` | Prompt user with interactive selection modal |
| **Audit Artifact** | `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/architecture_audit.md` | Emit structured architecture report with Mermaid diagrams |

---

## 1. Dispatch Architecture Scanner Subagent

Launch a fast `flash` tier research subagent to audit the target domain:

```json
{
  "Subagents": [
    {
      "TypeName": "research",
      "Role": "Deep Module Inspector",
      "Model": "flash",
      "Workspace": "inherit",
      "Prompt": "Analyze the codebase under <target-dir> (e.g. apps/execution, apps/designer, apps/tickets) for architectural depth:\n\n1. Identify shallow modules: files/classes with large surface areas but minimal encapsulated logic.\n2. Identify leaky abstractions: direct sibling database imports, unencapsulated ORM queries in routers, or raw graph walks.\n3. Identify high-leverage deepening opportunities: where multiple related helpers can be unified behind a deep domain interface.\n\nCite exact files with line ranges. Under 500 words."
    }
  ]
}
```

---

## 2. Compile Architecture Report Artifact

Write findings to `<appDataDir>/brain/<conversation-id>/architecture_audit.md` via `write_to_file`:

- **Executive Summary**: Overview of architectural health and identified seams.
- **Deepening Opportunities Table**: Module path, current vs. proposed interface surface area, and leverage rationale.
- **Visual & Mermaid Diagrams**: Visual architecture layout and Mermaid boundary diagrams.
- **Actionable Next Steps**: Candidate ADRs or refactoring tickets.

---

## 3. Interactive Drill-Down
Present the opportunities to the user using `ask_question`:
```json
{
  "questions": [
    {
      "question": "Which architectural deepening opportunity would you like to drill into?",
      "options": [
        "(Recommended) Unify <Service> behind a deep domain port and collapse pass-through helpers",
        "Refactor <Router> to eliminate direct SQLModel queries via repository seam",
        "Scaffold an ADR under docs/adr/ and defer code refactoring to next cycle"
      ],
      "is_multi_select": false
    }
  ]
}
```
Follow up on the selected choice using [`grill-with-docs`](../grill-with-docs/SKILL.md) or [`tdd`](../tdd/SKILL.md).


