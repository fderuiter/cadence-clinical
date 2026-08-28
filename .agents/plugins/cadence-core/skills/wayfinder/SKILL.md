---
name: wayfinder
description: "Plan and navigate large multi-session initiatives by building a shared map of decision tickets and roadmap milestones in an Antigravity artifact. Use when planning major architectural refactors or cross-service feature suites."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Wayfinder

Plans large-scale initiatives that exceed what a single agent turn or session can hold, mapping dependencies, decision milestones, and tracer bullets into a shared living document using Antigravity subagents, visual diagrams, and session artifacts.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Exploratory Survey**| `invoke_subagent` | `Subagents=[{"TypeName": "research", "Role": "Roadmap Analyst"}]` | Survey multi-service dependencies concurrently |
| **Visual Roadmap** | `generate_image` | `Prompt="Gantt-style milestone roadmap with dependency arrows for ..."`, `AspectRatio="16:9"` | Generate visual project milestone graphic |
| **Interactive Sign-Off**| `ask_question` | `questions=[{"question": "Approve Phase 1 Milestone Scope", ...}]` | Align on sequential execution boundaries |
| **Living Map Artifact**| `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/wayfinder_map.md` | Emit comprehensive roadmap and Mermaid DAG |

---

## 1. Map the Architectural Terrain
- Break the initiative into distinct phases and dependency layers (Database Schemas → Domain Core → REST APIs → Frontend UI → GxP Verification).
- Identify critical decision forks that require user alignment or ADRs.

---

## 2. Dispatch Research Subagents
When exploratory facts are needed across services, launch research subagents (`invoke_subagent` with `TypeName: "research"`).

---

## 3. Generate Living Wayfinder Map Artifact

Write to `<appDataDir>/brain/<conversation-id>/wayfinder_map.md` via `write_to_file`:
- **Phase Breakdown**: Sequential milestones with explicit exit criteria.
- **Dependency Graph (Mermaid)**: Visual DAG of blocking dependencies.
- **Tracer-Bullet Tickets**: Actionable vertical slices.
- **Resolved vs. Open Decisions**: Links to ADRs and open questionnaires.

---

## 4. Work One Slice at a Time
Guide the user through executing one slice at a time using `/tdd` and `/implement`.

