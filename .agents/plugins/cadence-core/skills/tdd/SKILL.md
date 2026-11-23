---
name: tdd
description: "Execute Test-Driven Development (TDD) for Cadence clinical features and bug fixes. Use when building features test-first, doing red-green-refactor loops, or adding unit and integration tests."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Test-Driven Development (TDD) (.NET 10 / C# 14)

TDD in Cadence follows a strict, rapid red → green → refactor loop anchored to domain seams, in-memory repository fakes, and 21 CFR Part 11 requirements traceability using Antigravity code and execution tools.

## Antigravity Tool Execution Matrix

| Phase                      | Antigravity Tool                         | Invocation Pattern                                                                                                              | Objective                                                                  |
| :------------------------- | :--------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------ | :------------------------------------------------------------------------- |
| **Seam Discovery**         | `find_by_name` / `view_file`             | Read target service and test suite via `AbsolutePath`                                                                           | Inspect existing domain seams and test doubles                             |
| **Red (Failing Test)**     | `replace_file_content` / `write_to_file` | Author new test case with `[GxPRequirement]` and `/// @req:`                                                                    | Add failing test case testing public interface                             |
| **Test Execution**         | `run_command`                            | `CommandLine="dotnet test --project tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter 'FullyQualifiedName~<TestName>'"` | Execute sub-second unit test verification                                  |
| **Green (Implementation)** | `replace_file_content`                   | Target: production code, single contiguous edit                                                                                 | Write minimal production code to pass test                                 |
| **Quality Sentinel**       | `run_command`                            | `CommandLine="pnpm cadence fix && pnpm cadence check"`                                                                          | Format, verify layer boundaries, and run every quality gate                |
| **Compliance Sync**        | `run_command`                            | `CommandLine="pnpm cadence gxp sync"`                                                                                           | Validate requirement tags and results (preview only; never commit the RTM) |

---

## Centralized Test Infrastructure (`Cadence.Testing`)

Always use the shared test infrastructure from `Cadence.Testing` instead of ad-hoc mocks:

1. **Entity Factories**: `SubjectFactory`, `ProtocolDefinitionFactory`, `ObservationFactory` from `Cadence.Testing.Factories`.
2. **In-Memory Fakes**: `InMemoryRepository<T>` from `Cadence.Testing.Fakes` to test use cases without hitting live databases.
3. **Security Test Context**: `TestSecurityContext.CreateHmacHeaders(...)` from `Cadence.Testing.Security`.

---

## Mandatory 21 CFR Part 11 Traceability (`[GxPRequirement]` and `@req:`)

Every single test method **must** declare its traceability requirement tag using both XML doc comments and the `[GxPRequirement]` attribute:

```csharp
[Fact]
[GxPRequirement("PRD-SYS-042")]
public async Task RegisterSubject_WithValidPayload_PersistsAuditTrailAndAdvancesState()
{
    /// <summary>
    /// Validates that subject registration generates an immutable audit record with user identity.
    /// @req:PRD-SYS-042
    /// </summary>

    // Arrange
    var repository = new InMemorySubjectRepository();
    var subject = SubjectFactory.Create(siteId: "SITE-101", status: "ENROLLED");

    // Act
    await repository.SaveAsync(subject);

    // Assert
    var persisted = await repository.GetByIdAsync(subject.Id);
    persisted.Should().NotBeNull();
    persisted!.VersionIndex.Should().Be(1);
}
```

---

## The TDD Execution Loop

### 1. Seam Agreement

Agree on the public interface boundary before authoring tests:

- Ask: "What is the public interface and which seams are we testing?"
- Never test internal/private methods or reach into ORM internals directly.
- If the interface seam is ambiguous, invoke `ask_question` to confirm the boundary.

### 2. Red: Author Failing Test (MANDATORY TOOL: `replace_file_content` / `write_to_file`)

- Use `replace_file_content` or `write_to_file` to author a minimal failing test at the agreed seam.
- Include the `[GxPRequirement("PRD-SYS-xxx")]` attribute and `/// @req:PRD-SYS-XXX` doc comment.
- Execute with sub-second fast test runner via `run_command`:
  ```bash
  dotnet test --project tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~test_name"
  ```
- Confirm the test fails for the expected reason (red).

### 3. Green: Minimal Implementation (MANDATORY TOOL: `replace_file_content`)

- Use `replace_file_content` for surgical edits (never full-file overwrite) to write only enough production code to make the test pass.
- Adhere strictly to Cadence conventions:
  - File-scoped namespaces (`namespace Cadence.<Service>.<Layer>;`).
  - Strict alphabetical using directive ordering.
  - Return `Result<TValue, DomainError>` monads; avoid throwing exceptions for domain logic.
- Re-run the test via `run_command`:
  ```bash
  dotnet test --project tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~test_name"
  ```
- Confirm the test passes (green).

### 4. Refactor & Lint (MANDATORY TOOL: `run_command`)

- Clean up the implementation without altering public behavior.
- Format and auto-fix code styling:
  ```bash
  dotnet format Cadence.sln
  ```

### 5. Compliance Sync (MANDATORY TOOL: `run_command`)

When the slice is complete and all tests pass:

```bash
pnpm cadence gxp sync
```

It writes a preview to `TestResults/gxp/preview/` and nothing under `docs/SDLC`. Never commit the generated RTM or IQ/OQ/PQ report on a feature branch (ADR-2532).
