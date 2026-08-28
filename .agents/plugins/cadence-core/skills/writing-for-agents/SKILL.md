---
name: writing-for-agents
description: "Write, structure, and refine documentation, skills, and rules designed for AI coding agents in Antigravity. Use when authoring or editing skills, AGENTS.md, plugin manifests, or agent prompt guidelines."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Writing for Antigravity Agents

Guidelines for authoring clear, deterministic, and token-efficient documentation, skills, rules, and prompts for AI coding agents.

---

## 1. Antigravity Skill Frontmatter
- **`name`**: Unique, lowercase, hyphenated identifier (`cadence-router`, `code-review`).
- **`description`**: **The single most important field.** Antigravity loads only `name` and `description` into the agent's prompt during tool discovery. It must clearly state in 3rd person:
  - **What** the skill does.
  - **When** to use it (trigger words, scenarios, error conditions).

---

## 2. Progressive Disclosure Pattern
- Keep `SKILL.md` concise (< 100 lines).
- Offload long documentation, schemas, and extensive manuals to `references/` subdirectories and link using relative markdown links (`[schema guide](./references/schema.md)`).
- Offload deterministic shell sequences to `scripts/` (`[prepare.sh](./scripts/prepare.sh)`).

---

## 3. High-Leverage Prompting for Skills
- State hard constraints and anti-patterns explicitly ("_Avoid_: raw SQL string interpolation, sibling database imports").
- Provide concrete input/output code examples.
- Include explicit verification and testing steps (`uv run cadence test --fast`).
