---
name: wayfinder
description: "Plan and navigate large multi-session initiatives by building a shared map of decision tickets and roadmap milestones in an Antigravity artifact. Use when planning major architectural refactors or cross-service feature suites."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Wayfinder

Plans large-scale initiatives that exceed what a single agent turn or session can hold, mapping dependencies, decision milestones, and tracer bullets into a shared living document.

---

## 1. Map the Architectural Terrain
- Break the initiative into distinct phases and dependency layers (Database Schemas → Domain Core → REST APIs → Frontend UI → GxP Verification).
- Identify critical decision forks that require user alignment or ADRs.

---

## 2. Dispatch Research Subagents
When exploratory facts are needed across services, launch research subagents (`invoke_subagent` with `TypeName: "research"`).

---

## 3. Generate Living Wayfinder Map Artifact

Write to `<appDataDir>/brain/<conversation-id>/wayfinder_map.md`:
- **Phase Breakdown**: Sequential milestones with explicit exit criteria.
- **Dependency Graph (Mermaid)**: Visual DAG of blocking dependencies.
- **Tracer-Bullet Tickets**: Actionable vertical slices.
- **Resolved vs. Open Decisions**: Links to ADRs and open questionnaires.

---

## 4. Work One Slice at a Time
Guide the user through executing one slice at a time using `/tdd` and `/implement`.
