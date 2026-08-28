# ADR-2219: Antigravity Workspace Plugin and Skill Architecture

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Cadence Clinical Research Software Platform relies on AI pair-programming and autonomous agent workflows to author clinical protocols, execute TDD slices, conduct two-axis code reviews, and maintain 21 CFR Part 11 GxP traceability. Previously, the agent skills library in the `.agents` folder consisted of generic external skill definitions with references to Claude Code, external TypeScript frameworks, and fragmented lifecycle hooks. To optimize agent performance, reduce context window overhead, and enforce GxP compliance boundaries, we require an Antigravity-native workspace plugin architecture tailored specifically to the Cadence clinical stack.

Reference system requirement: `PRD-SYS-053`.

## 2. Decision Drivers & Constraints

* **Antigravity Customization Architecture**: Support progressive disclosure, native subagent dispatch (`invoke_subagent`), session artifacts (`<appDataDir>/brain/<conversation-id>/...`), and lifecycle hooks (`hooks.json`).
* **Cadence Platform Stack**: Deeply integrate Python 3.14 (PEP 695 generics, Pydantic v2 domain models), PostgreSQL / SQLModel (`apps/execution`), Neo4j async graph models (`apps/designer`), and Vue 3 vanilla CSS tokens.
* **GxP 21 CFR Part 11 Compliance**: Enforce requirement docstring tags (`@req:PRD-SYS-053`), automated RTM synchronization (`scripts/sync_gxp.py`), and architectural sentinel checks (`uv run cadence check`).
* **Developer Experience**: Unified CLI commands (`uv run cadence ...`) eliminating tool trial-and-error.

## 3. Options Considered

1. **Option A (Selected): Encapsulated Antigravity Workspace Plugin (`.agents/plugins/cadence-core`)**:
   Bundle curated skills, rules (`.agents/plugins/cadence-core/rules/AGENTS.md`), and lifecycle hooks (`.agents/plugins/cadence-core/hooks.json`) into a single structured plugin. Prune incompatible external skills, introduce Cadence domain skills (`cadence-router`, `cadence-gxp-sync`, `cadence-adr`, `usdm-protocol-designer`), and standardize subagent and artifact patterns across all skills.
2. **Option B: Flat Top-Level Customizations with Claude Compatibility**:
   Maintain loose skills under `.agents` and adapt prompts with runtime environment conditional branches.


## 4. Decision Outcome

Chosen option: **Option A**. Packaging customizations into `.agents/plugins/cadence-core/` provides clean discovery encapsulation, enables native Antigravity lifecycle hooks and subagent roles, eliminates legacy external clutter, and tightly aligns all skills with the Cadence eClinical platform mission.

## 5. Consequences & Trade-offs

* **Positive**:
  * **Progressive Disclosure**: High-precision YAML frontmatter descriptions prevent prompt bloat while ensuring accurate on-demand skill activation.
  * **Artifact-First Deliverables**: Rich reports (two-axis reviews, deep module scans, research dossiers) are written to Antigravity session artifacts with Mermaid diagrams and clickable `file:///` links, keeping conversation history uncluttered.
  * **Reactive Subagent Dispatch**: Multi-agent workflows (`code-review`, `research`, `improve-codebase-architecture`) utilize `invoke_subagent` without polling loops.
  * **GxP Automated Gates**: Standardized development loop incorporates `cadence fix --all`, `cadence test --fast`, and `scripts/sync_gxp.py`.
* **Negative / Trade-offs**:
  * Legacy Claude-specific hook configurations (`.claude/`) are deprecated in favor of Antigravity native `.agents/plugins/cadence-core/hooks.json`.

## 6. Implementation & Verification

* Created `.agents/plugins/cadence-core/plugin.json` and `.agents/plugins/cadence-core/hooks.json`.
* Authored and migrated 31 curated skills under `.agents/plugins/cadence-core/skills/`.
* Updated `CONTEXT.md` with domain definitions.
* Validated via `uv run cadence check --parallel`, `uv run cadence test --fast`, and `uv run python scripts/sync_gxp.py`.

