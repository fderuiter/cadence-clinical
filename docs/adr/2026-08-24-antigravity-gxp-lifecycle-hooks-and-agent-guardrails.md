# ADR-2192: Antigravity GxP Lifecycle Hooks and AI Coding Guardrails

- **Status:** Accepted
- **Date:** 2026-08-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
* **Requirement ID:** PRD-SYS-053

---

## 1. Context & Problem Statement

Cadence Clinical is a high-assurance eClinical research software platform synthesizing Clinical Metadata Management (MDR) with Electronic Data Capture (EDC) under strict GxP (21 CFR Part 11, ICH GCP E6) compliance rules. Autonomous AI coding agents operating via the Google Antigravity platform execute shell commands, edit source code, and run tests.

However, unregulated agent operations introduce significant risks in regulated life sciences environments:
1. **Destructive Shell & Git Operations:** Agents may inadvertently invoke destructive commands (e.g. `git push --force`, `git reset --hard`, `git clean -fdx`, or `rm -rf /`), destroying repository history or uncommitted clinical audit trails.
2. **Direct Tampering with Generated GxP Artifacts:** Generated regulatory documents (such as `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`) must only be compiled via deterministic automation (`scripts/sync_gxp.py`). Manual edits corrupt audit hashes and invalidate qualification records.
3. **Silent Compliance Violations in Generated Code:** Agents frequently generate anti-patterns such as bare boolean SQL comparisons (`col == True` violating SQLAlchemy `E712` semantics) or mis-ordered imports (`I001`).
4. **Premature Agent Loop Termination:** Agents may complete execution turns without verifying that newly touched domain entities are accompanied by corresponding tests, `@req:` docstrings, and synchronized RTM matrices.

## 2. Decision Drivers & Constraints

* **21 CFR Part 11 & GxP Compliance (PRD-SYS-053):** All tool invocations and file mutations must strictly enforce safety guardrails and auditability.
* **Deterministic Execution & Low Latency:** Lifecycle hooks must execute locally with sub-50ms latency using Python 3.14 standard library without external network calls.
* **Antigravity JSON-RPC Protocol Conformance:** Payloads must strictly follow Antigravity's camelCase protojson specification across `PreToolUse`, `PostToolUse`, `PreInvocation`, and `Stop` events.
* **Progressive Disclosure & Self-Healing:** The hooks system must be verifiable and auto-installable via `uv run cadence doctor --auto-fix`.

## 3. Options Considered

### Option 1: Unenforced Agent Prompt Rules Only
- **Overview:** Rely purely on `AGENTS.md` system prompt text instructions without programmatic interception.
- ***Pros:*** No code or hook configuration required.
- ***Cons:*** High error rate; models frequently violate prompt rules under complex multi-step reasoning.

### Option 2: Pre-Commit Hooks Only
- **Overview:** Catch violations only when the agent or developer commits code to git.
- ***Pros:*** Reuses existing git hook pipeline.
- ***Cons:*** Does not prevent dangerous runtime tool execution (e.g. destructive `rm` or `git reset`), does not inject in-flight compliance reminders, and does not block premature agent stop.

### Option 3: Comprehensive Antigravity GxP Lifecycle Hooks (Selected)
- **Overview:** Configure native Antigravity lifecycle hooks (`.agents/hooks.json`) executing Python guardrail scripts across all 4 execution phases:
  1. `PreToolUse`: Intercepts `run_command`, `write_to_file`, and `replace_file_content` to block destructive git/shell commands and direct edits to locked GxP documents.
  2. `PostToolUse`: Scans mutated files for bare boolean SQL comparisons (`E712`), import order, and sensitive tokens.
  3. `PreInvocation`: Injects ephemeral GxP compliance reminders into agent context before model execution.
  4. `Stop`: Intercepts termination to verify clean repository state and prompt for GxP sync if tests were touched.
- ***Pros:***
  - Hard programmatic enforcement preventing dangerous operations before execution.
  - Zero context loss with in-flight ephemeral compliance reminders.
  - Complete GxP traceability under requirement `PRD-SYS-053`.
- ***Cons:*** Requires maintaining hook runner scripts and keeping test coverage above 80%.

## 4. Decision Outcome

**Chosen Option:** Option 3 (Comprehensive Antigravity GxP Lifecycle Hooks).

### Architectural Implementation:
1. **Hook Configuration (`.agents/hooks.json`):** Registers `gxp-guardrails` with handlers for `PreToolUse`, `PostToolUse`, `PreInvocation`, and `Stop`.
2. **Hook Handlers (`scripts/hooks/`):**
   - `pre_tool_guardrail.py`: Evaluates `toolCall` against prohibited git/shell commands and protected file paths.
   - `post_write_check.py`: Evaluates file syntax, `E712` boolean patterns, and import formatting.
   - `pre_invocation_reminder.py`: Emits `injectSteps` with ephemeral GxP reminders.
   - `stop_sentinel.py`: Validates stop readiness and guides remaining validation steps.
3. **Diagnostics & Sentinels:** Integrated into `cadence doctor --auto-fix` and registered in `packages/sentinels/`.

## 5. Consequences & Trade-offs

### Positive Consequences
- **Absolute GxP Invariant Protection:** Destructive git commands and dangerous filesystem wipes are caught before execution, eliminating risk of data or history loss.
- **Automated SDLC Compliance:** Generated regulatory documents (RTM, IQ/OQ/PQ) cannot be inadvertently edited by agents.
- **Sub-50ms In-Flight Latency:** Pure Python standard library checks ensure zero latency overhead for safe commands.
- **21 CFR Part 11 Audit Alignment:** All guardrails are formally traceable to `PRD-SYS-053`.

### Negative Trade-offs
- **Maintenance Overhead:** Developers modifying CLI tools or SDLC paths must update the hook rule catalog if new protected artifacts are introduced.

## 6. Implementation & Verification

### Implementation Deliverables
- Specification: `docs/specs/01-antigravity-gxp-lifecycle-hooks.md`
- Configuration: `.agents/hooks.json`
- Hook scripts: `scripts/hooks/pre_tool_guardrail.py`, `scripts/hooks/post_write_check.py`, `scripts/hooks/pre_invocation_reminder.py`, `scripts/hooks/stop_sentinel.py`
- Diagnostics auto-heal in `packages/cli/commands/doctor.py`

### Verification Strategy
- Comprehensive unit and contract tests in `scripts/tests/test_antigravity_gxp_hooks.py` with `@req:PRD-SYS-053`.
- Architecture sentinel validation via `uv run python scripts/validate_adrs.py`.
- Full GxP compliance document regeneration via `uv run python scripts/sync_gxp.py`.
