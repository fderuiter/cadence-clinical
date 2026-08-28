---
name: cadence-router
description: "Navigate and trigger the Cadence skill suite across Clinical Protocol Design, Development & TDD, Code Review & GxP Compliance, and Planning & Alignment. Use when deciding which skill to run or asking for guidance on Cadence workflows."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Skill Suite Router

This skill routes tasks, clinical workflows, and engineering questions to the appropriate specialized Cadence skill.

## Skill Catalog by Domain

### 1. Clinical Protocol & Architecture
- **`usdm-protocol-designer`**: CDISC USDM v3/v4 study design authoring, SoA matrix editing, and biomedical concepts.
- **`domain-modeling`**: Build and sharpen clinical domain models, validate against `CONTEXT.md`, and refine terminology.
- **`codebase-design`**: Deep module design principles for Python 3.14 (PEP 695 generics, Pydantic v2 domain schemas) and Vue 3.
- **`cadence-adr`**: Scaffold, author, and auto-index Architecture Decision Records under `docs/adr/`.

### 2. Development & Testing
- **`tdd`**: Test-Driven Development using `uv run cadence test --fast`, `packages.testing` factories/fakes, and `@req:` docstring tags.
- **`implement`**: End-to-end task implementation with automated Cadence quality gate validation (`cadence fix --all`, `cadence test`, `scripts/sync_gxp.py`).
- **`prototype`**: Rapid exploratory prototyping for Vue 3 interfaces and backend API routes.
- **`diagnosing-bugs`**: Systematic root-cause debugging using `uv run cadence doctor` and isolated service test runs.

### 3. Review & Compliance
- **`code-review`**: Parallel two-axis review (Standards & GxP vs. Spec Compliance) using Antigravity subagents and artifacts.
- **`cadence-gxp-sync`**: 21 CFR Part 11 Traceability Matrix and verification report synchronization (`uv run python scripts/sync_gxp.py`).
- **`improve-codebase-architecture`**: Deep module architectural scanning, seam identification, and visual inspection artifacts.

### 4. Planning & Alignment
- **`grill-with-docs`**: Relentless design-tree interview to sharpen plans while updating `CONTEXT.md` and drafting ADRs.
- **`grill-me` / `grilling`**: Deep frontier questioning to stress-test architectural choices.
- **`to-spec`**: Synthesize discussions into structured specifications aligned with PRD/SRS requirements.
- **`to-tickets`**: Break features into tracer-bullet tickets mapped to Cadence domain queues (`PROTOCOL_DESIGNER`, `DATA_CAPTURE_ECRF`, `REGULATORY_COMPLIANCE`).
- **`wayfinder`**: Multi-session roadmap planning with subagent delegation.
- **`handoff`**: Session state compaction into an Antigravity artifact with clickable conversation links.

## Routing Workflow

1. Identify the user's intent from their prompt or task context.
2. Recommend the best matching skill with a 1-2 sentence rationale.
3. Suggest the direct invocation command (e.g. `/code-review`, `/tdd`, `/grill-with-docs`).
