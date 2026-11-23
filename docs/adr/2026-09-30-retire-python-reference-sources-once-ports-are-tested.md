# ADR-2492: Retire Python Reference Sources Once Their C# Ports Are Tested

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development environment and agent guardrails: agents must not treat reference code as the implementation)

---

## 1. Context & Problem Statement

After the Python-to-.NET port (ADR-2233, ADR-2444), 148 Python files and 14 `pyproject.toml` files stayed in
`packages/` as behavioural references. Nothing recorded when each could go, so search, the duplication gate,
agents and reviewers kept treating them as live code, and some of them (the Neo4j graph manager, role-string
delegation checks, module-local audit mixins) describe behaviour the kernel specification has replaced.

## 2. Decision Drivers & Constraints

- **Single source of truth:** only the C# code is the implementation; a reference must not look like one.
- **No lost behaviour:** a Python file is the only record of behaviour whose C# port is missing or untested.
- **Kernel alignment:** behaviour the kernel specification supersedes must not be ported (spec §5.3, §9.3, §9.5, §10.5, §11).

## 3. Options Considered

### Option 1: Delete all Python now

- **Overview:** Remove every `.py` file; rely on git history.
- **Pros:**
  - ✅ Simplest; no Python left in the tree.
- **Cons:**
  - ❌ Behaviour with no C# port (FSM, SLA, timeline, derived variables, financial ledger, the DDF compiler stages) would survive only in history, with nothing pointing at it.

### Option 2: Delete per file against a recorded rule, and inventory the rest (Selected)

- **Overview:** A Python file is deleted when a C# port exists and tests cover its main behaviour, when the kernel
  specification or an ADR supersedes it, or when it has no behaviour (re-export `__init__.py`, Python tests and test
  helpers, `pyproject.toml`). Every remaining file is listed in `docs/PYTHON_REFERENCE_INVENTORY.md` with its C#
  status and what it needs before deletion.
- **Pros:**
  - ✅ Removes most of the reference code at once without losing unported behaviour.
  - ✅ Each remaining file has a visible, checkable exit condition.
- **Cons:**
  - ❌ Some Python stays until its port and tests land.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** It keeps the only record of unported behaviour while removing everything that is ported,
  tested or superseded. The first pass deleted 76 files (and 6 `pyproject.toml`) and inventoried 55 files in 7 packages;
  subsequent ports and retirement passes removed all remaining ported, manifest and superseded files—including
  `packages/ddf/dataset_json_etl.py` ([ADR-2504](2026-09-30-the-tripartite-form-definition-is-not-carried-forward.md)),
  `packages/engine_core/operations/{engine,evaluator,models}.py`
  ([ADR-2456](2026-09-30-effectivity-and-study-site-activation.md),
  [ADR-2459](2026-09-30-qualifications-gate-capabilities.md),
  [ADR-2805](2026-10-02-ctms-site-startup-greenlight-matrix.md)), and
  `packages/security/{auditor_token,permissions}.py`
  ([ADR-2256](2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md),
  [ADR-2516](2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md))—leaving 7 `Port missing` `.py`
  files across `packages/engine_core` and `packages/security` (and 0 `pyproject.toml` or `__init__.py` files).

## 5. Consequences & Trade-offs

- **Positive Impact:** Search and review stop surfacing dead Python; the duplication gate no longer scans `.py`
  files; Dependabot tracks NuGet instead of a Python workspace that no longer exists.
- **Negative Impact / Technical Debt:** 7 Python files remain until their ports are written or dispositioned.
- **Mitigation Strategy:** A PR that ports or tests one of them deletes the Python file and its inventory row in
  the same change (`docs/MIGRATION_WORKFLOW.md` §4).

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/*` Python sources, `DuplicationDetectorService`, package READMEs,
  `.github/dependabot.yml`.
- **Verification Plan:** `dotnet build`, the unit, architecture and E2E suites and `pnpm cadence check` pass after the
  deletion; no C# code or test reads a deleted file.
