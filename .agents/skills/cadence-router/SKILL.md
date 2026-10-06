---
name: cadence-router
description: "Navigate and trigger the Cadence skill suite across Clinical Protocol Design, Development & TDD, Code Review & GxP Compliance, and Planning & Alignment. Use when deciding which skill to run or asking for guidance on Cadence workflows."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Skill Suite Router

This skill routes clinical workflows, architectural designs, and engineering tasks to the optimal Cadence skill, orchestrating the full Antigravity tool surface.

## Antigravity Tool Execution Matrix

| Phase                    | Antigravity Tool  | Parameter & Invocation Pattern                                    | Objective                                                     |
| :----------------------- | :---------------- | :---------------------------------------------------------------- | :------------------------------------------------------------ |
| **Intent Analysis**      | `view_file`       | Read target skill `SKILL.md` via `AbsolutePath`                   | Inspect skill instructions and prerequisite tools             |
| **Ambiguity Resolution** | `ask_question`    | `questions=[{"question": "...", "options": [...]}]`               | Prompt user with interactive multi-choice when ambiguous      |
| **Delegation**           | `invoke_subagent` | `Subagents=[{"TypeName": "...", "Role": "...", "Prompt": "..."}]` | Delegate specialized multi-step tasks to background subagents |
| **Dispatch Output**      | `write_to_file`   | Target: `<appDataDir>/brain/<conversation-id>/...`                | Emit execution plan or routing decision artifact              |

## Skill Catalog by Domain

### 1. Clinical Protocol & Architecture

- **[`usdm-protocol-designer`](../usdm-protocol-designer/SKILL.md)**: CDISC USDM v3/v4 study design authoring, SoA matrix editing, and biomedical concepts with `generate_image` wireframes.
- **[`domain-modeling`](../domain-modeling/SKILL.md)**: Build and sharpen clinical domain models, validate against `CONTEXT.md`, and refine terminology using `grep_search` and `replace_file_content`.
- **[`codebase-design`](../codebase-design/SKILL.md)**: Deep module design principles for C# 14 / .NET 10 (ports, records, kernel IDs) and Vue 3 vanilla CSS tokens.
- **[`cadence-adr`](../cadence-adr/SKILL.md)**: Scaffold, author, and auto-index Architecture Decision Records under `docs/adr/`.

### 2. Development & Testing

- **[`tdd`](../tdd/SKILL.md)**: Test-Driven Development using `dotnet cadence test --fast`, `Cadence.Testing` fakes (`FakeClock`, `InMemoryRepository<T>`), and `[GxPRequirement]` tags.
- **[`implement`](../implement/SKILL.md)**: End-to-end task implementation with automated Cadence quality gate validation (`cadence fix --all`, `cadence test`, `cadence gxp sync`).
- **[`prototype`](../prototype/SKILL.md)**: Rapid exploratory prototyping using `generate_image` and ephemeral scratch directories.
- **[`diagnosing-bugs`](../diagnosing-bugs/SKILL.md)**: Systematic root-cause debugging using `manage_task` and `schedule` watchdog timers.

### 3. Review & Compliance

- **[`code-review`](../code-review/SKILL.md)**: Parallel two-axis review (Standards & GxP vs. Spec Compliance) using `invoke_subagent` and session artifacts.
- **[`cadence-gxp-sync`](../cadence-gxp-sync/SKILL.md)**: 21 CFR Part 11 Traceability Matrix and verification report synchronization (`dotnet cadence gxp sync`).
- **[`improve-codebase-architecture`](../improve-codebase-architecture/SKILL.md)**: Deep module architectural scanning and visual inspection artifacts.

### 4. Planning & Alignment

- **[`grill-with-docs`](../grill-with-docs/SKILL.md)**: Relentless design-tree interview to sharpen plans while updating `CONTEXT.md` and drafting ADRs.
- **[`grill-me`](../grill-me/SKILL.md)** / **[`grilling`](../grilling/SKILL.md)**: Deep frontier questioning with `ask_question` to stress-test architectural choices.
- **[`to-spec`](../to-spec/SKILL.md)**: Synthesize discussions into structured specifications aligned with PRD/SRS requirements.
- **[`to-tickets`](../to-tickets/SKILL.md)**: Break features into tracer-bullet tickets mapped to Cadence domain queues.
- **[`wayfinder`](../wayfinder/SKILL.md)**: Multi-session roadmap planning with subagent delegation.
- **[`handoff`](../handoff/SKILL.md)**: Session state compaction into an Antigravity artifact with clickable conversation links.

## Routing Protocol

1. **Intent Matching**: Analyze user prompt and context. If multiple skill paths are viable, use `ask_question` to let the user select the branch:
   ```json
   {
     "questions": [
       {
         "question": "Which workflow would you like to launch?",
         "options": [
           "(Recommended) /grill-with-docs: Stress-test plan and update CONTEXT.md & ADRs",
           "/tdd: Execute Red-Green-Refactor test-driven implementation slice",
           "/code-review: Parallel Standards & Spec compliance audit"
         ],
         "is_multi_select": false
       }
     ]
   }
   ```
2. **Skill Direct Loading**: Read the matching skill's `SKILL.md` using `view_file` to execute its specific instructions.
3. **Subagent Delegation**: If the task requires deep research or parallel reviews, launch subagents via `invoke_subagent` using appropriate `Model` (`flash` vs `pro`) and `Workspace` modes (`inherit`, `share`, `branch`).
