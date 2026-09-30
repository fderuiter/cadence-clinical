# ADR-2496: Parse the CLI with System.CommandLine and Publish It as Native AOT Without Suppressions

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development environment and agent guardrails: the quality gates must run from a
  trustworthy tool)

---

## 1. Context & Problem Statement

[ADR-2249](2026-09-29-native-aot-cli-compilation-and-makefile-binding.md) made the `cadence` CLI a Native AOT
binary. The CLI parsed its command line with Spectre.Console.Cli, which builds commands and settings by reflection
and is not annotated for trimming. To publish at all, `Cadence.Cli.csproj` suppressed the warnings that say the
binary can break at runtime (IL2104, IL3053, IL3000), rooted every assembly for the trimmer, and nothing checked
AOT compatibility in CI (#4794). Spectre.Console.Cli 0.57 still marks `CommandApp` as requiring dynamic code, so an
upgrade does not fix it.

## 2. Decision Drivers & Constraints

- The CLI runs every quality gate, locally and in CI; it must not fail at runtime in ways the build hides.
- No new suppressions; warnings are errors solution-wide.
- `docs/cli.md` must stay in parity with the registered commands (the `cli-docs` gate).
- Keep Spectre.Console's rendering (tables, colours, banner), which is AOT-safe.

## 3. Options Considered

### Option 1: System.CommandLine for parsing, Spectre.Console for rendering (Selected)

- **Overview:** Build the command tree explicitly in `Program.cs` with System.CommandLine 2.0, which is trim- and
  AOT-compatible, and keep Spectre.Console only for output.
- **Pros:**
  - ✅ No reflection over command types; the publish is warning-free with no `NoWarn` and no trimmer roots.
  - ✅ Built-in help, `--version`, and errors for unknown commands, unknown options and missing arguments.
- **Cons:**
  - ❌ Every command is rewritten once (about 50 commands, mostly one-line wrappers).

### Option 2: Keep Spectre.Console.Cli and annotate or suppress per call site

- **Overview:** Add `DynamicDependency` attributes for every settings type and keep the assembly-level suppressions.
- **Cons:**
  - ❌ The IL2104/IL3053 aggregates cannot be scoped to call sites; the suppressions would stay.
  - ❌ `CommandApp` itself requires dynamic code, so correctness would rest on trimmer roots.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It is the only option that removes the suppressions instead of moving them, and it gives CI a
  check that fails on any future trim or AOT warning.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - `Cadence.Cli.csproj` has no IL2xxx/IL3xxx suppressions and no `TrimmerRootAssembly` or
    `RootAllApplicationAssemblies`.
  - CI runs `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64`, which fails on any trim
    or AOT warning.
  - Commands are registered with `AddCommand`/`AddBranch` in `Program.cs`, which the `cli-docs` gate reads.
- **Negative Impact / Technical Debt:** A free positional argument accepts option-like tokens; `cadence check`
  validates its arguments so a mistyped option is rejected rather than run as a gate name.
- **Mitigation Strategy:** `tests/Cadence.UnitTests/Packages/CliCommandLineTests.cs` parses the documented invocations and the
  rejected ones through `Program.BuildRootCommand()`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/cli`, `packages/tooling_core` (`CliDocsGeneratorService`),
  `.github/workflows/ci.yml`, `Directory.Packages.props`.
- **Verification Plan:** CLI parser tests and the CLI docs parity tests in `tests/Cadence.UnitTests/Packages`; the Native AOT publish
  in CI; a smoke run of the published binary (`--help`, `doctor`, `check markdown`, error paths).
