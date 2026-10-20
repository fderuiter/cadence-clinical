# ADR-2535: NetAnalyzers 10 Applies the Recommended Rule Set

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails: the checks that guard every commit must be real)
- **Related:** [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), #5541, #5539

---

## 1. Context & Problem Statement

Root `Directory.Build.props` sets `<AnalysisLevel>latest-recommended</AnalysisLevel>` with warnings as errors, and
`Microsoft.CodeAnalysis.NetAnalyzers` was pinned at 9.0.0. Under the .NET 10 SDK the effective analysis level is
`10.0`. The 9.0.0 package turns that level into a rule-set file name with the regular expression `(.0)*$`, whose
unescaped `.` matches any character, so `10.0` became an empty string and no rule-set file was loaded. The repository
therefore never ran the recommended rule set it asked for; only the rules that are on by default applied.

Version 10.0.402 (Dependabot #5539) escapes the dot. Bumping it loads `analysislevel_10_recommended` for the first
time, and the build reported about 2,400 findings (#5541): 1,700 of them CA1707 on test method names, 230 CA1861 in
scaffolded EF Core migrations, and the rest spread over culture-sensitive formatting and parsing, logging, naming,
allocation and disposal rules across the kernel, the packages, the services and the tests.

## 2. Decision Drivers & Constraints

- **The configured rule set should be the one that runs:** `latest-recommended` was chosen on purpose.
- **Real fixes over suppressions:** culture, disposal and logging findings are defects or cheap improvements.
- **No wire or vocabulary changes in a dependency bump:** API query names, serialized enum values, manifest keys and
  kernel specification terms (work queue, activity stream) stay as they are.
- **Every suppression is targeted and says why** (CONTRIBUTING.md §3).

## 3. Options Considered

### Option 1: Take 10.0.402 and fix or justify every finding (Selected)

- **Overview:** bump the package, fix the findings, and suppress only where the rule does not fit, either with a
  `[SuppressMessage(..., Justification = "...")]` on the member or type or with a commented, path-scoped
  `.editorconfig` section.
- **Pros:**
  - ✅ The recommended rule set guards every later change.
  - ✅ Real defects are fixed: culture-dependent `ToString`, `Parse` and `StartsWith` calls, a FluentAssertions reason
    that was an invalid format string, disposable fields that were never disposed.
- **Cons:**
  - ❌ A wide change for a dependency bump.

### Option 2: Pin the rule set to what ran before (`AnalysisLevel` `none`, or the 9.0.0 package)

- **Pros:**
  - ✅ No code changes.
- **Cons:**
  - ❌ Keeps a configuration that says one thing and does another, and blocks future analyzer updates.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- Two scoped `.editorconfig` sections, each with its reason in a comment:
  - `**/Migrations/*.cs`: CA1861, CA1825 and CA1711 are off. Migrations are scaffolded code that runs once per database,
    and their class names are migration names.
  - `tests/**/*.cs`: CA1707 (test names are `Method_Scenario_Result`), CA1861 and CA1859 are off.
- Targeted suppressions, each with its justification: kernel vocabulary (`WorkQueue`, `DefineWorkQueue`,
  `IActivityStream`, `ActivityStream` for CA1711; `IEntityId<TSelf>.New`, `IKernelEventConsumer.Handles`,
  `IModuleCommandStore.Module` and `LinkPart(from, to)` for CA1716); generic factory methods (`CommandResult<T>`,
  `Result<T>`, CA1000); `StandardDataType.Char` (CA1720); and snake_case or UPPER_SNAKE names that are the wire format
  or the published API (endpoint parameters bound by name, export manifest records, serialized engine enums,
  `SubjectId_`, CA1707).
- `packages/security`: `HmacSigner` reuses one cached `JsonSerializerOptions` (the canonical bytes are unchanged), and
  `Principal.UnblindedAccess` drops its redundant `= false`. The public UPPER_SNAKE names ported from the Python
  reference (`RoleNormalization`, `FieldMasker`, `PermissionMatrix.ROLE_PERMISSIONS`,
  `SiteAccessEvaluator.SITE_SCOPED_ROLES`, `AuditLogRecord.GENESIS_BLOCK`) keep their names under a CA1707 suppression;
  renaming them is a public API change of its own.
- `apps/gateway`: the signature re-authentication logging moves to source-generated `[LoggerMessage]` methods (CA1848)
  with the same levels and messages.
- `IEventDispatcherPort.DispatchAsync` names its parameter `domainEvent` and `ISmartSigningKeys.TryGetPrivateKey`
  names its `out` parameter `reason` (CA1716). The kernel handlers that took a `CancellationToken` before a delegate
  take it last (CA1068).

## 5. Consequences & Trade-offs

- **Positive Impact:** the analyzers run the rule set the build asks for, and the culture-sensitive calls in report,
  manifest and identifier code are explicit about using the invariant culture.
- **Negative Impact / Technical Debt:** the security constants keep their Python-era names for now.
- **Mitigation Strategy:** renaming them belongs with the deletion of `packages/security/rbac.py` under #4789.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Directory.Packages.props`, `.editorconfig`, `packages/kernel`,
  `packages/security`, `packages/engine_core`, `packages/core-models`, `packages/telemetry`, `packages/templating`,
  `packages/tooling_core`, `packages/cli`, `packages/compliance`, `packages/ddf`, `packages/deid`, `packages/hexagonal`,
  the services under `apps/`, and the test projects.
- **Verification Plan:** `dotnet build Cadence.sln -warnaserror` is clean with the new package, and the unit and
  architecture tests pass.
