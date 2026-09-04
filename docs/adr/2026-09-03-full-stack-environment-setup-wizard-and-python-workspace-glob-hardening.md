# ADR-2240: Full-Stack Environment Setup Wizard and Python Workspace Glob Hardening

- **Status:** Accepted
- **Date:** 2026-09-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The repository previously had three disconnected onboarding paths: `make setup`
(Python + Node only, no .NET), `scripts/setup_dotnet_wizard.sh` (.NET only,
interactive-only, no CI/agent mode), and the devcontainer's `postCreateCommand`
(a bare `dotnet restore`). None of them provisioned the whole stack in one
step, none adapted their behavior to non-interactive contexts (CI, Codespaces,
Claude Code remote sessions), and `dotnet cadence wizard onboarding` — the
onboarding entry point `CONTRIBUTING.md` already documented — only ever
covered .NET. Per PRD-SYS-053 (developer environment reproducibility), a new
contributor or automated agent session should be able to reach a working
build/test loop with a single command regardless of where it's run.

While exercising the new wizard, `uv sync` was found to fail intermittently:
`pyproject.toml`'s `[tool.uv.workspace]` uses a bare `packages/*` glob for
members, which also matches the transient `__pycache__` bytecode-cache
directory that Python creates the moment any module under `packages/` is
imported. uv then treats that directory as a workspace member lacking a
`pyproject.toml` and aborts. This made the very setup tooling this ADR
introduces non-idempotent.

The wizard's `.NET` stage also revealed that `dotnet restore`/`dotnet format`
were completely broken solution-wide: `Directory.Packages.props` pinned six
packages to exact .NET 10 _preview_ versions no longer published on
nuget.org now that .NET 10 is GA, `ArchUnitNET.xUnit` was pinned under the
wrong package ID (the real package is `TngTech.ArchUnitNET.xUnit`) so it
could never resolve at all, and — once restore was fixed and the solution
could finally be analyzed end-to-end for what may be the first time —
several pre-existing SonarAnalyzer/compiler violations surfaced (a nested
`if`, three `app.Run()` calls flagged in favor of `RunAsync`, and doc
comments placed inside method bodies instead of above the method). Together
these meant `dotnet format Cadence.sln --verify-no-changes` — the
pre-commit gate for any `.cs`/`.csproj`/`.sln` change — could not pass for
_any_ C# change in the repository, including the `Cadence.Cli` fix below.

## 2. Decision Drivers & Constraints

- Reproducible, single-command onboarding across local machines, GitHub
  Codespaces, VS Code devcontainers, Claude Code remote sessions, and CI
  (PRD-SYS-053).
- Must not hang on a TTY prompt when run non-interactively.
- One stack's setup failure (e.g. no network for a Playwright download) must
  not block provisioning the other two stacks.
- Reuse the repository's existing wizard conventions (`scripts/*_wizard.sh`
  - `.ps1` twin, dispatched via `dotnet cadence wizard <name>`) rather than
    inventing a new pattern.

## 3. Options Considered

1. **Add a single full-stack `scripts/setup_wizard.sh` (+ `.ps1` twin) that
   detects its environment and orchestrates the existing per-stack scripts,
   wired into `make wizard`, the devcontainer, the Claude Code SessionStart
   hook, and `dotnet cadence wizard onboarding` (Selected)**
2. Keep the three onboarding paths separate and just document "run these
   three commands in order" — cheaper to write, but leaves the
   non-interactive/CI gap unsolved and keeps three copies of environment
   logic (e.g. `.env` bootstrap, datastore probing) to maintain in sync.
3. Rewrite onboarding entirely in the new `Cadence.Cli` (C#) — the intended
   long-term home per ADR-2233, but that CLI does not yet build cleanly
   (see below) and can't run before the .NET SDK it's meant to install
   exists, so a shell entry point remains necessary regardless.

## 4. Decision Outcome

Chosen option: **1**. `scripts/setup_wizard.sh` (and its `scripts/setup_wizard.ps1`
twin) detects the runtime context (local workstation, GitHub Codespaces, a
devcontainer, a Claude Code remote session, or CI) and, per stage,
installs/validates the .NET 10 SDK (delegating to `scripts/setup_dotnet_env.sh`
/ `scripts/setup_dotnet_env.ps1`), Node/pnpm, Python/uv
(+ Playwright, pre-commit), probes local datastores, bootstraps `.env`, and
finishes with `cadence doctor`. Each stage is non-fatal on failure and the
wizard prints a summary of anything that needs attention rather than
aborting outright — reflecting that the three stacks are independent.

It is wired into every place the repo already bootstraps an environment:
`make wizard`, the devcontainer `postCreateCommand`, the Claude Code
`SessionStart` hook, and `dotnet cadence wizard onboarding` (the existing
Spectre.Console CLI dispatcher). The prior .NET-only onboarding script is
kept as `dotnet cadence wizard dotnet` for cases where only the SDK is
needed. The CLI dispatcher's arg-forwarding was widened from a single
positional value to all trailing args so `--dev`/`--minimal` reach the
underlying script.

While validating this, `packages/cli/src/Program.cs` was found to fail its
own `dotnet format`/analyzer gate (a pre-existing nested ternary and three
`string`-overload `StartsWith` calls tripping `TreatWarningsAsErrors`) —
`Cadence.Cli` had never actually compiled. Since it is the exact entry point
this change extends, it was fixed as part of this ADR rather than filed
separately.

`pyproject.toml`'s workspace `exclude` list now explicitly excludes the
`__pycache__` entries under `apps/` and `packages/` so a stray bytecode
cache directory can no longer break `uv sync`.

`Directory.Packages.props` was bumped from the stale preview pins to the
current GA/patch versions on nuget.org (`Microsoft.AspNetCore.OpenApi`,
`Microsoft.EntityFrameworkCore`(`.Design`/`.InMemory`),
`Microsoft.Extensions.Http.Polly` → `10.0.11`;
`Npgsql.EntityFrameworkCore.PostgreSQL` → `10.0.3`), which also resolved the
`Microsoft.OpenApi` CVE flagged by NU1903 (the GA `AspNetCore.OpenApi`
pulls a non-vulnerable transitive version). `ArchUnitNET.xUnit` was
corrected to the real package ID, `TngTech.ArchUnitNET.xUnit` at `0.13.4`,
in both `Directory.Packages.props` and `tests/Cadence.ArchitectureTests.csproj`.
The now-surfaced SonarAnalyzer/compiler violations were fixed in place
(`packages/database/src/GxPAuditInterceptor.cs`; `apps/gateway`,
`apps/execution`, `apps/designer` `Program.cs`; `tests/ArchitectureSentinelTests.cs`,
`tests/SecurityAndDomainTests.cs`) with no behavior change — nested
conditions merged, `app.Run()` → `await app.RunAsync()`, and doc comments
relocated above the method they document.

## 5. Consequences & Trade-offs

- **Positive:** One documented, idempotent command
  (`make wizard` / `scripts/setup_wizard.sh`) reaches a working build/test
  loop from a clean checkout in any of the supported environments; CI/agent
  sessions no longer hang on a prompt or abort on the first missing tool;
  `dotnet restore`/`dotnet format`/`dotnet build`/`dotnet test` all pass
  solution-wide again, so the pre-commit gate works for C# changes.
- **Negative / Technical debt:** `cadence doctor` still crashes under
  Python 3.14.0rc2 due to a `pydantic`/`typing` incompatibility unrelated to
  this change; the wizard surfaces it in its summary output but does not
  fix it (needs a `pydantic` version investigation, out of scope here).
  Resolved in ADR-2241: the root cause was `uv` silently preferring a
  cached pre-release `3.14.0rc2` interpreter over a correct final-release
  one, not a `pydantic` version gap.
- **Mitigation:** `setup_dotnet_env.sh`/`.ps1` no longer force
  `--quality preview` for the SDK install now that .NET 10 is GA, and prefer
  the OS package manager (`apt`/`winget`) before falling back to
  `dotnet-install.sh`, so newly onboarded machines get a GA SDK by default.

## 6. Implementation & Verification

- **Affected files:** `scripts/setup_wizard.sh`, `scripts/setup_wizard.ps1`
  (new); `scripts/setup_dotnet_env.sh`, `scripts/setup_dotnet_env.ps1`,
  `packages/cli/src/Program.cs`, `Directory.Packages.props`,
  `tests/Cadence.ArchitectureTests.csproj`,
  `packages/database/src/GxPAuditInterceptor.cs`,
  `apps/{gateway,execution,designer}/src/Program.cs`,
  `tests/ArchitectureSentinelTests.cs`, `tests/SecurityAndDomainTests.cs`,
  `scripts/validate_path_patterns.py`, `Makefile`,
  `.devcontainer/devcontainer.json`, `.claude/hooks/session-start.sh`,
  `pyproject.toml`, `CONTRIBUTING.md`, `README.md`, `.vscode/tasks.json`
  (modified).
- **Verification:** Ran `scripts/setup_wizard.sh --dev --minimal` and
  `dotnet cadence wizard onboarding --dev --minimal` end-to-end against a
  clean checkout (confirmed all six stages run and non-fatal failures are
  summarized); `dotnet restore Cadence.sln` — 10/10 projects restore;
  `dotnet format Cadence.sln --verify-no-changes` — passes; `dotnet build
Cadence.sln` — 0 errors; `dotnet test Cadence.sln --filter
"Category!=Integration"` — 14/14 passed; re-ran `uv sync --all-extras`
  after reproducing and fixing the stray-`__pycache__` workspace-glob
  failure.
