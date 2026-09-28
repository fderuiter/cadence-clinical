# Specification: Antigravity GxP Lifecycle Hooks & AI Coding Guardrails

- **Status:** Ready for Implementation
- **Requirement ID:** PRD-SYS-053
- **ADR Reference:** [ADR-2192](../adr/2026-08-24-antigravity-gxp-lifecycle-hooks-and-agent-guardrails.md)
- **Triage Label:** `ready-for-agent`

---

## Problem Statement

In the Cadence Clinical high-assurance eClinical platform, engineering workflows involve autonomous AI coding agents operating across microservices, CDISC data pipelines, and strict GxP (21 CFR Part 11) compliance boundaries.

While autonomous agents accelerate feature delivery, unsupervised agent operations introduce significant risks in regulated life sciences environments:
1. **Destructive Operations & History Loss:** Agents may issue destructive git commands (such as `git push --force`, `git reset --hard`, `git clean -fdx`, or `git branch -D`) or dangerous shell deletions (`rm -rf /`, dropping database tables), corrupting development history and auditability.
2. **Direct Tampering with Generated GxP Artifacts:** Generated regulatory documents (such as `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`) must only be compiled via deterministic automation (`scripts/sync_gxp.py`). Manual agent edits corrupt audit hashes and invalidate qualification records.
3. **Silent Compliance Violations in Generated Code:** Agents frequently generate anti-patterns such as bare boolean SQL comparisons (`col == True` violating SQLAlchemy `E712` semantics) or mis-ordered imports (`I001`), leading to runtime SQL errors or CI gating failures.
4. **Premature Agent Loop Termination:** Agents may complete execution turns without verifying that newly touched domain entities are accompanied by corresponding tests, `@req:` docstrings, and synchronized RTM matrices.
5. **Context Drift:** Without persistent, in-flight compliance framing, agents lose track of clinical validation rules across extended multi-turn conversations.

---

## Solution

Build a comprehensive, deterministic GxP Lifecycle Hook subsystem leveraging Google Antigravity's native hook orchestration protocol (`.agents/hooks.json`):

1. **`PreToolUse` GxP Command & File Mutation Guardrail (`scripts/hooks/pre_tool_guardrail.py`):**
   - Intercepts all `run_command` executions to block destructive git and system commands, returning immediate JSON `{ "decision": "deny", "reason": "..." }`.
   - Intercepts `write_to_file` and `replace_file_content` to prevent direct manual tampering with locked GxP artifacts (`Requirements_Traceability_Matrix.md`, `IQ_OQ_PQ_Execution_Report.md`).
2. **`PostToolUse` Compliance & Syntax Verifier (`scripts/hooks/post_write_check.py`):**
   - Executes immediate post-write inspections on mutated Python files, validating absence of bare boolean comparisons (`col == True`), import sorting, and potential secret or PHI exposures.
3. **`PreInvocation` In-Flight Compliance Injector (`scripts/hooks/pre_invocation_reminder.py`):**
   - Injects ephemeral system guidance (`injectSteps`) reminding the model of GxP compliance invariants, strict typing requirements, `@req:` docstrings, and `sync_gxp.py` obligations.
4. **`Stop` Termination Sentinel (`scripts/hooks/stop_sentinel.py`):**
   - Intercepts agent loop termination to verify whether modified domain files require running test validation or GxP sync before exiting.
5. **Self-Healing & System Diagnostics Integration:**
   - Embeds hook verification and auto-installation into `cadence doctor --auto-fix` and registers a dedicated `AntigravityHooksSentinel` in the repository's sentinel suite.

---

## User Stories

1. As an autonomous coding agent, I want destructive git commands like `git push --force` or `git reset --hard` to be rejected deterministically by `PreToolUse`, so that I never accidentally destroy repository history or uncommitted work.
2. As an autonomous coding agent, I want destructive filesystem commands like `rm -rf apps/` or `DROP DATABASE` to be denied by `PreToolUse`, so that multi-service architectures and database volumes remain intact.
3. As an autonomous coding agent, I want direct manual edits to `docs/SDLC/Requirements_Traceability_Matrix.md` to be blocked by `PreToolUse`, so that I am guided to use `scripts/sync_gxp.py` for audit-compliant generation.
4. As an autonomous coding agent, I want direct manual edits to `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` to be blocked by `PreToolUse`, ensuring qualification reports are strictly produced from automated test runs.
5. As an autonomous coding agent, I want safe commands (such as `uv run pytest`, `uv run cadence test`, `git status`) to be approved immediately with sub-50ms latency, so that my development inner loop remains fast.
6. As an autonomous coding agent, I want `PostToolUse` to run immediate syntax and style checks on written files, so that bare boolean comparisons (`E712`) or un-sorted imports (`I001`) are flagged immediately after writing.
7. As an autonomous coding agent, I want `PreInvocation` to inject ephemeral GxP reminder messages into my context, so that I always adhere to 21 CFR Part 11 requirements and Google-style docstrings.
8. As an autonomous coding agent, I want `Stop` to warn me if I attempt to finish a turn while modified test files have not been synchronized with the RTM, so that CI compliance never fails.
9. As a human software engineer, I want `.agents/hooks.json` to be committed to version control, so that all developers and agents on the team share identical safety guardrails.
10. As a human software engineer, I want `uv run cadence doctor --auto-fix` to verify and auto-heal `.agents/hooks.json`, so that new developer clones are automatically secured.
11. As a human software engineer, I want hook scripts to execute using fast Python 3.14 standard library tools without external network overhead, so that tool invocation latency is imperceptible.
12. As a human software engineer, I want all hook inputs and outputs to strictly follow Antigravity's camelCase protojson contract, so that tool interception is 100% reliable across CLI and IDE surfaces.
13. As a GxP compliance officer, I want every blocked tool execution to provide a structured, human-readable reason referencing 21 CFR Part 11 and Cadence clinical safety policies.
14. As a GxP compliance officer, I want all hook scripts and tests to be tracked in the Requirements Traceability Matrix under requirement `PRD-SYS-053`.
15. As a QA automation engineer, I want comprehensive unit tests simulating `PreToolUse`, `PostToolUse`, `PreInvocation`, and `Stop` JSON payloads, so that regression tests verify all guardrail logic.
16. As a DevOps engineer, I want the hooks system to be purely configuration-driven via JSON, allowing specific hooks to be enabled or disabled via standard `"enabled": true/false` flags.

---

## Implementation Decisions

### 1. Hook Configuration Architecture (`.agents/hooks.json`)

The hook configuration is stored at the root of the customization directory (`.agents/hooks.json`) and registers the `gxp-guardrails` hook group:

```json
{
  "gxp-guardrails": {
    "enabled": true,
    "PreToolUse": [
      {
        "matcher": "run_command|write_to_file|replace_file_content",
        "hooks": [
          {
            "type": "command",
            "command": "python scripts/hooks/pre_tool_guardrail.py",
            "timeout": 15
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "write_to_file|replace_file_content",
        "hooks": [
          {
            "type": "command",
            "command": "python scripts/hooks/post_write_check.py",
            "timeout": 30
          }
        ]
      }
    ],
    "PreInvocation": [
      {
        "type": "command",
        "command": "python scripts/hooks/pre_invocation_reminder.py",
        "timeout": 10
      }
    ],
    "Stop": [
      {
        "type": "command",
        "command": "python scripts/hooks/stop_sentinel.py",
        "timeout": 30
      }
    ]
  }
}
```

### 2. Protocol Contracts & Payload Handling

All hook scripts communicate via JSON over `sys.stdin` and `sys.stdout` utilizing camelCase field names (protojson encoding):

#### A. `PreToolUse` Contract (`scripts/hooks/pre_tool_guardrail.py`)
- **Input (stdin):**
  ```json
  {
    "toolCall": {
      "name": "run_command",
      "args": {
        "CommandLine": "git push --force"
      }
    },
    "stepIdx": 1,
    "conversationId": "uuid",
    "workspacePaths": ["/path/to/cadence-clinical"]
  }
  ```
- **Output (stdout):**
  - Allowed: `{"decision": "allow"}`
  - Denied: `{"decision": "deny", "reason": "GxP Guardrail: Destructive git force push is strictly prohibited."}`

#### B. `PostToolUse` Contract (`scripts/hooks/post_write_check.py`)
- **Input (stdin):** `{"stepIdx": 5, "conversationId": "uuid"}`
- **Output (stdout):** `{}` (Executes fast background validation and logs any warnings to stderr).

#### C. `PreInvocation` Contract (`scripts/hooks/pre_invocation_reminder.py`)
- **Input (stdin):** `{"invocationNum": 1, "conversationId": "uuid"}`
- **Output (stdout):**
  ```json
  {
    "injectSteps": [
      {
        "ephemeralMessage": "GxP Compliance Sentinel: Ensure all clinical database queries use .is_(True)/.is_(False) (E712), test docstrings include @req: tags, and RTM is synced via scripts/sync_gxp.py."
      }
    ]
  }
  ```

#### D. `Stop` Contract (`scripts/hooks/stop_sentinel.py`)
- **Input (stdin):** `{"executionNum": 1, "terminationReason": "model_stop", "fullyIdle": true}`
- **Output (stdout):** `{"decision": "allow"}` or `{"decision": "continue", "reason": "..."}`

### 3. GxP Safety Rule Catalog

The `pre_tool_guardrail.py` script enforces the following deterministic rule sets:

1. **Prohibited Git Commands:**
   - `git push --force` / `git push -f`
   - `git reset --hard`
   - `git clean -f` / `git clean -fdx`
   - `git checkout .` (without path restriction)
   - `git branch -D`
2. **Prohibited Shell Commands:**
   - `rm -rf /` / `rm -rf ~` / `rm -rf apps` / `rm -rf packages` / `rm -rf docs`
   - Raw database drop strings: `DROP DATABASE`, `TRUNCATE TABLE`
   - Sudo / permission overrides: `sudo `, `chmod 777`
3. **Locked GxP Artifacts (Disallow manual file edits):**
   - `docs/SDLC/Requirements_Traceability_Matrix.md`
   - `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`

---

## Testing Decisions

### What Makes a Good Test
- Tests must assert external JSON stdin/stdout behaviors, exit codes, and decisions across all lifecycle hooks rather than mocking internal string splits.
- Test suites must execute in sub-second time without external process spawning or internet connectivity.

### Tested Surfaces
1. **`PreToolUse` Guardrail Tests (`tests/test_antigravity_gxp_hooks.py`):**
   - Validates blocking of prohibited git commands (`push --force`, `reset --hard`, `clean -fdx`).
   - Validates blocking of dangerous shell commands (`rm -rf`, `DROP DATABASE`).
   - Validates blocking of direct edits to locked GxP documents.
   - Validates allowing benign commands (`uv run pytest`, `uv run cadence check`, `git status`).
   - Validates allowing legitimate file modifications.
2. **`PostToolUse` Guardrail Tests:**
   - Asserts `{}` output structure on file modification completions.
3. **`PreInvocation` Hook Tests:**
   - Asserts `injectSteps` structure containing ephemeral GxP reminder messages.
4. **`Stop` Sentinel Tests:**
   - Asserts valid `decision` JSON structure under clean and pending-action states.
5. **Configuration & Sentinel Tests:**
   - Asserts `.agents/hooks.json` schema validity and registration in `packages/sentinels/`.

---

## Out of Scope

- Modifying core Antigravity C++ / Go host runtime binaries.
- Replacing the `uv run cadence` CLI tooling.
- Altering clinical domain schemas or microservice database engines.

---

## Further Notes

- All hook scripts and test cases must be annotated with `@req:PRD-SYS-053` for 21 CFR Part 11 traceability.
- All code must pass `ruff check` and `ruff format`.
