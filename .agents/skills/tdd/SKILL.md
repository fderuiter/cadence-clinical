---
name: tdd
description: "Execute Test-Driven Development (TDD) for Cadence clinical features and bug fixes. Use when building features test-first, doing red-green-refactor loops, or adding unit and integration tests."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Test-Driven Development (TDD)

TDD in Cadence follows a strict, rapid red → green → refactor loop anchored to domain seams, in-memory test doubles, and 21 CFR Part 11 requirements traceability using Antigravity code and execution tools.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Seam Discovery** | `find_by_name` / `view_file` | Read target service and test suite via `AbsolutePath` | Inspect existing domain seams and test doubles |
| **Red (Failing Test)** | `replace_file_content` / `write_to_file` | Author new test case with `@req:PRD-SYS-xxx` | Add failing test case testing public interface |
| **Test Execution** | `run_command` | `CommandLine="uv run cadence test --fast -k <test_name>"`, `WaitMsBeforeAsync=5000` | Execute sub-second unit test verification |
| **Green (Implementation)**| `replace_file_content` | Target: production code, single contiguous edit | Write minimal production code to pass test |
| **Quality Sentinel** | `run_command` | `CommandLine="uv run cadence fix --all && uv run cadence check --parallel"` | Format, sort imports, and run 10 quality gates |
| **Compliance Sync** | `run_command` | `CommandLine="uv run python scripts/sync_gxp.py"` | Regenerate and stage GxP Traceability Matrix |

---

## Centralized Test Infrastructure (`packages/testing`)

Always use the shared test infrastructure instead of ad-hoc mocks or raw dictionaries:

1. **Entity Factories**: `SubjectFactory`, `ProtocolDefinitionFactory`, `ClinicalObservationFactory`, `QueryDiscrepancyFactory`, `ConsentRecordFactory`, `AuditLogFactory` from `packages.testing.factories`.
2. **In-Memory Fakes**: `InMemoryRepository[T]` from `packages.testing.fakes` to test use cases without hitting live databases.
3. **Gateway Security Context**: `create_test_auth_headers`, `create_test_security_context`, `create_test_token` from `packages.testing.security`.

---

## GxP Requirement Docstrings (`@req:`)

Every single test method **must** declare its traceability requirement tag in its Google-style docstring:

```python
import pytest
from packages.testing.factories import SubjectFactory
from packages.testing.fakes import InMemoryRepository


@pytest.mark.asyncio
async def test_subject_enrollment_state_transition():
    """Validate subject state machine advances on enrollment trigger.

    @req:PRD-SYS-042
    """
    repo = InMemoryRepository()
    subject = SubjectFactory.create(site_id="SITE-101", status="SCREENED")
    ...
```

---

## The TDD Execution Loop

### 1. Seam Agreement
Agree on the public interface boundary before authoring tests:
- Ask: "What is the public interface and which seams are we testing?"
- Never test internal/private methods or reach into ORM internals directly.

### 2. Red: Author Failing Test
- Use `replace_file_content` or `write_to_file` to author a minimal failing test at the agreed seam.
- Include the `@req:PRD-SYS-xxx` docstring tag.
- Execute with sub-second fast test runner:
  ```bash
  uv run cadence test --fast -k test_subject_enrollment
  ```
- Confirm the test fails for the expected reason (red).

### 3. Green: Minimal Implementation
- Use `replace_file_content` to write only enough production code to make the test pass.
- Adhere strictly to Cadence conventions:
  - Boolean ORM filters: `.is_(True)` / `.is_(False)` (E712).
  - Alphabetical import sorting (I001).
  - PEP 695 type parameters for generics: `class Service[T]: ...` (UP046).
- Re-run the test:
  ```bash
  uv run cadence test --fast -k test_subject_enrollment
  ```
- Confirm the test passes (green).

### 4. Refactor & Lint
- Clean up the implementation without altering public behavior.
- Format and auto-fix lints:
  ```bash
  uv run cadence fix --all
  ```

### 5. Compliance Sync
When the slice is complete and all tests pass:
```bash
uv run python scripts/sync_gxp.py
```

