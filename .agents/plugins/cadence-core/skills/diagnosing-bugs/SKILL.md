---
name: diagnosing-bugs
description: "Diagnose, isolate, and debug failing tests, backend exceptions, database drift, or frontend errors in the Cadence platform. Use when investigating unexpected errors, regressions, or system anomalies."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Systematic Bug Diagnosis

A structured diagnosis loop for isolating root causes in multi-service clinical backends, PostgreSQL databases, and Vue 3 frontend components using Antigravity debugging, task, and inspection tools.

## Antigravity Tool Execution Matrix

| Phase                  | Antigravity Tool       | Invocation Pattern                                                                                                    | Objective                                               |
| :--------------------- | :--------------------- | :-------------------------------------------------------------------------------------------------------------------- | :------------------------------------------------------ |
| **Error Localization** | `grep_search`          | `Query="<exception_or_symbol>"`, `SearchPath="apps/"`                                                                 | Locate error sites, callers, and exception stack traces |
| **System Diagnostics** | `run_command`          | `CommandLine="pnpm cadence doctor --auto-fix"`, `WaitMsBeforeAsync=5000`                                              | Verify environment, schemas, and dependencies           |
| **Isolated Repro**     | `run_command`          | `CommandLine="dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter 'FullyQualifiedName~<TestName>'"` | Run targeted minimal reproduction test                  |
| **Task Monitoring**    | `manage_task`          | `Action="status"`, `TaskId="..."`                                                                                     | Inspect logs and stderr from background test tasks      |
| **Watchdog Timer**     | `schedule`             | `DurationSeconds=60`, `TimerCondition=<task-id>`                                                                      | Guard against hanging database locks or workers         |
| **Root-Cause Fix**     | `replace_file_content` | Target: source file, single contiguous fix                                                                            | Apply surgical minimal fix to production code           |
| **Diagnostic Report**  | `write_to_file`        | Target: `<appDataDir>/brain/<conversation-id>/bug_diagnosis.md`                                                       | Emit root-cause analysis and verification artifact      |

---

## 1. Environment & Infrastructure Check

Run Cadence diagnostics to ensure local environments and PostgreSQL schemas are intact:

```bash
pnpm cadence doctor --auto-fix
```

---

## 2. Reproduce in Isolation

Always construct or run a minimal reproducing test case rather than diagnosing via exploratory mutations:

- Use `grep_search` and `find_by_name` to pinpoint relevant test files.
- Run the failing test in isolation:
  ```bash
  dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~test_name"
  ```
- If no test exists, author a failing regression test slice using `Cadence.Testing.Factories` and `Cadence.Testing.Fakes`.

---

## 3. Layer-Specific Diagnostic Runbooks

### A. Execution Backend & PostgreSQL (`apps/execution`)

- Check EF Core 10 query expressions and ensure `.AsNoTracking()` is used on read queries.
- Check EF Core migrations under `Infrastructure/Persistence/Migrations/`.
- Verify GxP audit trail triggers (`CreatedAt`, `CreatedBy`, `VersionIndex`).

### B. Designer Backend (`apps/designer`)

- Validate USDM domain models against CDISC specifications.
- Verify protocol version snapshot integrity (content hash, predecessor links, immutable approved versions; ADR-2245, ADR-2452).

### C. Gateway & Security (`apps/gateway`)

- Inspect HMAC signature generation and token header propagation (`Cadence.Security.Signing`).
- Check tenant isolation and persona context headers (`TestSecurityContext.CreateHmacHeaders`).

### D. Web Frontend (`apps/web`)

- Verify vanilla CSS tokens in `apps/web/src/style.css` (ensure no unsupported Tailwind utility classes).
- Check Pinia store state transitions and persona switcher context.

---

## 4. Fix & Verify

### MANDATORY TOOL INVOCATIONS

1. **Root-Cause Fix (`replace_file_content`)**: Apply the minimal surgical fix to production code (never full overwrite).
2. **Regression Verification (`run_command`)**:
   ```bash
   pnpm cadence test --fast
   ```
3. **Quality Sentinels (`run_command`)**:
   ```bash
   pnpm cadence check
   ```
4. **Compliance Sync (`run_command`)**: validates requirement tags and results and writes a preview only; never commit the generated RTM or IQ/OQ/PQ report on the branch.
   ```bash
   pnpm cadence gxp sync
   ```
5. **Diagnostic Report (`write_to_file`)**: Emit diagnosis report artifact to `<appDataDir>/brain/<conversation-id>/bug_diagnosis.md`.
