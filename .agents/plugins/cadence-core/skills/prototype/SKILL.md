---
name: prototype
description: "Build a rapid prototype to validate a UI design, state machine, or API schema before full implementation. Use when exploring UI layouts in Vue 3 or proving backend data flows."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Rapid Prototyping

Build throwaway or exploratory prototypes to quickly answer clinical design questions, validate user flows across personas, or test complex state machines.

---

## Prototyping Guidelines

### 1. Frontend Prototypes (`apps/web`)
- Use existing Vue 3 components and design tokens from `apps/web/src/style.css` (`var(--primary)`, `var(--surface)`, `var(--border)`).
- Never use Tailwind utility classes.
- Ensure persona context switcher compatibility (`apps/web/src/stores/auth.ts`).

### 2. Backend Prototypes (`apps/`)
- Prototype domain logic using pure in-memory models (`packages.testing.fakes`) before adding database tables or migrations.
- Test state transitions using ephemeral test cases (`uv run cadence test --fast`).

### 3. Evaluate & Transition
- Record prototype findings in an Antigravity walkthrough artifact.
- If the design is approved, convert the prototype into a production vertical slice using `tdd` and `implement`.
