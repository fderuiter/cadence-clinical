---
name: prototype
description: "Build a rapid prototype to validate a UI design, state machine, or API schema before full implementation. Use when exploring UI layouts in Vue 3 or proving backend data flows."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Rapid Prototyping

Build throwaway or exploratory prototypes to quickly answer clinical design questions, validate user flows across personas, or test complex state machines using Antigravity visual, scratch, and interactive tools.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Visual Wireframing** | `generate_image` | `Prompt="Vue 3 clinical interface mockup for ... with semantic CSS tokens"`, `AspectRatio="16:9"` | Generate high-fidelity visual UI wireframe |
| **Sandbox Subagent** | `invoke_subagent` | `Subagents=[{"Role": "Prototype Sandboxer", "Workspace": "branch"}]` | Launch isolated prototype exploration in branched workspace |
| **Scratch Implementation**| `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/scratch/<proto_file>` | Write isolated, throwaway prototype code |
| **Prototype Execution**| `run_command` | `CommandLine="uv run python <scratch_script>"`, `WaitMsBeforeAsync=5000` | Execute prototype in ephemeral scratch space |
| **User Feedback** | `ask_question` | `questions=[{"question": "Evaluate Prototype Approach", ...}]` | Solicit interactive feedback on layout/logic |
| **Transition Plan** | `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/prototype_findings.md` | Emit structured findings artifact |

---

## Prototyping Guidelines

### 1. Frontend Prototypes (`apps/web`)
- Use existing Vue 3 components and design tokens from `apps/web/src/style.css` (`var(--primary)`, `var(--surface)`, `var(--border)`).
- Never use Tailwind utility classes.
- Ensure persona context switcher compatibility (`apps/web/src/stores/auth.ts`).
- Generate visual mockups with `generate_image` before writing complex UI templates.

### 2. Backend Prototypes & Sandboxing (`apps/`)
- When exploring risky or complex state mutations, dispatch a subagent with `Workspace: "branch"` to prevent polluting the main working branch.
- Prototype domain logic using pure in-memory models (`packages.testing.fakes`) before adding database tables or migrations.
- Save temporary exploratory scripts in `<appDataDir>/brain/<conversation-id>/scratch/`.
- Test state transitions using ephemeral test cases (`uv run cadence test --fast`).

### 3. Evaluate & Transition via `ask_question`
- Record prototype findings in an Antigravity walkthrough artifact via `write_to_file`.
- Solicit interactive feedback using `ask_question`:
```json
{
  "questions": [
    {
      "question": "How should we proceed with the evaluated prototype?",
      "options": [
        "(Recommended) Graduate prototype into production slice using tdd and implement skills",
        "Refactor prototype layout based on current feedback before implementing",
        "Discard prototype as exploratory research and document findings in ADR"
      ],
      "is_multi_select": false
    }
  ]
}
```


