# Technical Design: EXECUTION-SUBJECTS (.NET 10 / C# 14)

> Supersedes the pre-remediation draft at `02_architecture.md` (and the backlog in `03_tickets.md`), both of which were written against `01_spec.md` _before_ the Phase 1 remediation pass recorded in `00_phase1_validation_report.md` §0. This document is built strictly against the remediated spec (**PHASE 2 UNBLOCKED**, all 5 gates PASS) and resolves, as explicit Phase 2 decisions, the two open items that report calls out: the `SubjectResponse` under-population quirk (§2.6) and the GxP audit-context DB-trigger vs. `GxPAuditInterceptor` divergence (§4.4).

**Slice:** `execution-subjects` · **Bounded Context:** `Cadence.Apps.Execution` · **Project:** `apps/execution/src/Cadence.Apps.Execution.csproj`

---

## 1. Architectural Overview & Component Boundaries

### 1.1 Single-assembly layering (ADR-2233 / ADR-2428)

Per ADR-2428, this slice ships inside the existing single-assembly `Cadence.Apps.Execution` project, organized by namespace, not by project boundary:

```
apps/execution/src/
  Domain/
    Subjects/
      SubjectId.cs, SubjectCode.cs, StudyCode.cs, SiteCode.cs   (strongly-typed IDs)
      SubjectState.cs                                            (enum + transition graph)
      ClinicalSubject.cs                                         (aggregate root)
      SubjectConsent.cs                                          (aggregate root)
      ProtocolVersionRef.cs                                      (EF Core complex type)
      DomainEvents/ SubjectEnrolledDomainEvent.cs, ...
      Errors/ SubjectErrors.cs                                   (DomainError factory)
    Compliance/
      SiteComplianceCache.cs                                     (aggregate root)
    Ports/
      ISubjectRepository.cs, IConsentRepository.cs, ISiteComplianceRepository.cs
  Application/
    Subjects/
      CreateSubjectCommand.cs + Handler
      RecordConsentCommand.cs + Handler
      AdvanceSubjectStateCommand.cs + Handler
      UpdateDemographicsCommand.cs + Handler
      DeleteDemographicsCommand.cs + Handler
      EvaluateAndTransitionScreeningCommand.cs + Handler
      GetSubjectDetailQuery.cs + Handler
      GetComplianceStatusQuery.cs + Handler
    Validators/ (FluentValidation, one per request DTO)
    Abstractions/ IDemographicsEncryptionService.cs, IDemographicsNormalizationService.cs, IClock.cs
  Infrastructure/
    Persistence/
      ExecutionDbContext.cs (partial — this slice's `OnModelCreating` contributions)
      Configurations/ ClinicalSubjectConfiguration.cs, SubjectConsentConfiguration.cs, SiteComplianceCacheConfiguration.cs
      Repositories/ SubjectRepository.cs, ConsentRepository.cs, SiteComplianceRepository.cs
    Security/ AesGcmDemographicsEncryptionService.cs
    Consumers/ EConsentCompletedConsumer.cs (MassTransit — replaces the Redis subscriber, Rule 8 / MIG-06)
  Endpoints/
    SubjectEndpoints.cs (MapSubjectEndpoints)
    SiteComplianceEndpoints.cs (MapSiteComplianceEndpoints)
    Filters/ ChangeReasonHeaderFilter.cs, AuditContextFilter.cs
```

Dependency direction: `Endpoints -> Application -> Domain`, `Infrastructure -> Domain` (implements `Domain.Ports`), `Infrastructure -> Application` (implements `Application.Abstractions`). No layer references `Endpoints`. Enforced by the existing `Cadence.ArchitectureTests` ArchUnitNET suite (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) — this design adds one rule: `Domain` must not reference `Microsoft.AspNetCore.*` or `MassTransit`.

### 1.2 DI lifetimes

| Service                                                                    | Lifetime                                                                                       | Rationale                                    |
| -------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- | -------------------------------------------- |
| `ExecutionDbContext`                                                       | Scoped                                                                                         | one unit of work per request                 |
| `ISubjectRepository`, `IConsentRepository`, `ISiteComplianceRepository`    | Scoped                                                                                         | wrap `ExecutionDbContext`                    |
| `IDemographicsEncryptionService` (`AesGcmDemographicsEncryptionService`)   | Singleton                                                                                      | stateless, holds the configured key material |
| `IDemographicsNormalizationService`                                        | Singleton                                                                                      | pure functions (Rule 7)                      |
| `IClock`                                                                   | Singleton                                                                                      | testability seam for `DateTimeOffset.UtcNow` |
| `CreateSubjectHandler`, `RecordConsentHandler`, ... (application services) | Scoped                                                                                         | inject scoped repositories                   |
| `GxPAuditInterceptor`                                                      | Scoped (registered via `AddInterceptors`, resolves `ICurrentAuditContextAccessor` per-request) | see §4.4                                     |
| `EConsentCompletedConsumer`                                                | Scoped (MassTransit default)                                                                   | one DB transaction per message               |

### 1.3 NuGet Dependency Mapping Table

| Python library / mechanism                                                         | .NET 10 / NuGet package                                                                                                                              | Notes                                                    |
| ---------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| `pydantic` request/response models                                                 | C# `record` DTOs + `FluentValidation` (`FluentValidation`, `FluentValidation.DependencyInjectionExtensions` — already in `Directory.Packages.props`) |                                                          |
| FastAPI routes (`subjects.py`, part of the eTMF/site-activation routes)            | ASP.NET Core 10 Minimal API route groups (`Microsoft.AspNetCore.OpenApi`, in-box `Microsoft.AspNetCore.App`)                                         |                                                          |
| SQLAlchemy models + Alembic                                                        | EF Core 10 (`Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` — already declared)                                             |                                                          |
| `cryptography.fernet.Fernet` (`demographics.py`)                                   | `System.Security.Cryptography.AesGcm` (in-box, .NET 10 BCL)                                                                                          | Rule 7 — key sourced from configuration, never a literal |
| Redis pub/sub consent-completion subscriber (`consent_subscriber.py`)              | `MassTransit` + `MassTransit.EntityFrameworkCore` transactional outbox (already declared)                                                            | Rule 8 / MIG-06 — see §5.2                               |
| `httpx`/`requests` (eConsent client, out-of-slice but referenced)                  | `IHttpClientFactory` + `Microsoft.Extensions.Http.Polly` (already declared)                                                                          | not re-specified here (out of slice)                     |
| Python `logging`                                                                   | `[LoggerMessage]` source-generated logging with `ILogger<T>` (in-box `Microsoft.Extensions.Logging.Abstractions`)                                    |                                                          |
| `contextvars.ContextVar` (`current_user_id`, `current_change_reason`)              | `AsyncLocal<T>` wrapped in a scoped `ICurrentAuditContextAccessor`                                                                                   | see §4.4                                                 |
| Postgres `set_config` DB-trigger audit bridge (`apps/execution/database/audit.py`) | **Superseded**, not ported — see §4.4 decision                                                                                                       |

All packages above are already present in `Directory.Packages.props`; no additions required for this slice.

---

## 2. Type System & Domain Contracts

### 2.1 Strongly-Typed IDs & Value Objects

```csharp
namespace Cadence.Apps.Execution.Domain.Subjects;

public readonly record struct SubjectId(Guid Value)
{
    public static SubjectId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct SubjectConsentId(Guid Value)
{
    public static SubjectConsentId New() => new(Guid.NewGuid());
}

/// <summary>Domain subject code (legacy `subject_id` field) — distinct from the surrogate <see cref="SubjectId"/> PK.</summary>
public readonly record struct SubjectCode
{
    public string Value { get; }

    public SubjectCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Subject code cannot be empty or whitespace.", nameof(value));
        if (value.Length > 255)
            throw new ArgumentException("Subject code cannot exceed 255 characters.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}

public readonly record struct StudyCode
{
    public string Value { get; }

    public StudyCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Study code cannot be empty or whitespace.", nameof(value));
        if (value.Length > 255)
            throw new ArgumentException("Study code cannot exceed 255 characters.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Nullable-by-design — unlike <see cref="SubjectCode"/>/<see cref="StudyCode"/>, absence is a legal domain state
/// (a subject may have no site yet), so this is validated only when a non-null instance is constructed
/// (Rule/trap: an explicitly-empty string is rejected rather than silently treated as "absent" — §3 traps table,
/// `study_id`/`target_state`/`site_id` truthiness sites).</summary>
public readonly record struct SiteCode
{
    public string Value { get; }

    public SiteCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Site code cannot be empty or whitespace when provided.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}
```

### 2.2 `SubjectState` & the Transition Graph (Rule 9's `normalize_state` + `guard_subject_transition`)

```csharp
namespace Cadence.Apps.Execution.Domain.Subjects;

public enum SubjectState
{
    Screening,
    ScreenFailed,
    Enrolled,
    Randomized,
    Active,
    Unblinded,
    Completed,
    Withdrawn,
    ReconsentRequired
}

/// <summary>
/// Funnel for every transition check (Rule 9). Reproduces `normalize_state`'s exact string shape
/// (`UPPER_SNAKE_CASE`) purely for parity with §5's fixed-string response bodies (e.g. "INVALID_STATE_TRANSITION");
/// internally the domain always works with the <see cref="SubjectState"/> enum, never bare strings.
/// </summary>
public static class SubjectStateCodec
{
    private static readonly IReadOnlyDictionary<string, SubjectState> ByCode = new Dictionary<string, SubjectState>
    {
        ["SCREENING"] = SubjectState.Screening,
        ["SCREEN_FAILED"] = SubjectState.ScreenFailed,
        ["ENROLLED"] = SubjectState.Enrolled,
        ["RANDOMIZED"] = SubjectState.Randomized,
        ["ACTIVE"] = SubjectState.Active,
        ["UNBLINDED"] = SubjectState.Unblinded,
        ["COMPLETED"] = SubjectState.Completed,
        ["WITHDRAWN"] = SubjectState.Withdrawn,
        ["RECONSENT_REQUIRED"] = SubjectState.ReconsentRequired,
    };

    public static string ToCode(SubjectState state) =>
        ByCode.First(kv => kv.Value == state).Key;

    /// <summary>Mirrors `normalize_state`: trims, upper-cases, and replaces spaces with underscores before lookup.
    /// Returns false (rather than throwing) for unmapped input — callers decide how to surface that, matching
    /// raise site #6's "no distinct error code from a legal-but-forbidden transition."</summary>
    public static bool TryParse(string? raw, out SubjectState state)
    {
        state = default;
        if (raw is null) return false;
        string normalized = raw.Trim().ToUpperInvariant().Replace(' ', '_');
        return ByCode.TryGetValue(normalized, out state);
    }
}

public static class SubjectTransitionGraph
{
    private static readonly IReadOnlyDictionary<SubjectState, IReadOnlySet<SubjectState>> Allowed =
        new Dictionary<SubjectState, IReadOnlySet<SubjectState>>
        {
            [SubjectState.Screening] = new HashSet<SubjectState> { SubjectState.ScreenFailed, SubjectState.Enrolled, SubjectState.Withdrawn, SubjectState.ReconsentRequired },
            [SubjectState.Enrolled] = new HashSet<SubjectState> { SubjectState.Randomized, SubjectState.Withdrawn, SubjectState.ReconsentRequired },
            [SubjectState.Randomized] = new HashSet<SubjectState> { SubjectState.Active, SubjectState.Withdrawn, SubjectState.Unblinded, SubjectState.ReconsentRequired },
            [SubjectState.Active] = new HashSet<SubjectState> { SubjectState.Completed, SubjectState.Withdrawn, SubjectState.Unblinded, SubjectState.ReconsentRequired },
            [SubjectState.Unblinded] = new HashSet<SubjectState> { SubjectState.Withdrawn, SubjectState.Completed, SubjectState.ReconsentRequired },
            [SubjectState.ReconsentRequired] = new HashSet<SubjectState> { SubjectState.Screening, SubjectState.Enrolled, SubjectState.Randomized, SubjectState.Active, SubjectState.Unblinded, SubjectState.Completed, SubjectState.Withdrawn },
            [SubjectState.ScreenFailed] = new HashSet<SubjectState>(),
            [SubjectState.Completed] = new HashSet<SubjectState>(),
            [SubjectState.Withdrawn] = new HashSet<SubjectState>(),
        };

    /// <summary>
    /// Rule 9 order of checks, exactly: (1) same-state is always a silent no-op — including terminal states;
    /// (2) current == null legally targets only Screening; (3) unknown/malformed state strings, checked identically
    /// to (4) legal-but-forbidden known-state transitions. Returns a <see cref="DomainError"/> rather than throwing
    /// (raise sites #4/#5/#6 collapse onto one error family in this port).
    /// </summary>
    public static Result<SubjectState> Guard(SubjectState? current, SubjectState target)
    {
        if (current == target)
            return target; // same-state no-op, including e.g. WITHDRAWN -> WITHDRAWN

        if (current is null)
            return target == SubjectState.Screening
                ? target
                : SubjectErrors.InvalidStateTransition("None", SubjectStateCodec.ToCode(target));

        return Allowed[current.Value].Contains(target)
            ? target
            : SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(current.Value), SubjectStateCodec.ToCode(target));
    }
}
```

> **Unknown/malformed input.** Because raw request strings are parsed via `SubjectStateCodec.TryParse` _before_ `Guard` runs, an unparseable string never reaches `Guard` as a `SubjectState?` — it is rejected at the parse boundary with the same `INVALID_STATE_TRANSITION` error code (raise site #6), preserving the legacy behavior that a typo'd state name is indistinguishable from a legal-but-forbidden transition.

### 2.3 `ClinicalSubject` Aggregate

```csharp
namespace Cadence.Apps.Execution.Domain.Subjects;

public sealed class ClinicalSubject : AggregateRoot<SubjectId>, IGxPAuditable
{
    public SubjectCode SubjectCode { get; private set; } = default!;
    public StudyCode StudyCode { get; private set; } = default!;
    public SiteCode? SiteCode { get; private set; }
    public string? TreatmentGroupCiphertext { get; private set; }
    public SubjectState Status { get; private set; }
    public string ActiveProtocolVersion { get; private set; } = "1.0.0";
    public string? EncryptedDemographics { get; private set; }
    public IReadOnlyDictionary<string, object?> StratFactors => _stratFactors;
    private readonly Dictionary<string, object?> _stratFactors = new();
    public int EnrollmentIndex { get; private set; }
    public bool IsUnblinded { get; private set; }
    public string? UnblindedBy { get; private set; }
    public string? UnblindedReason { get; private set; }
    public DateTimeOffset? UnblindedAt { get; private set; }
    public string? WithdrawalReason { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public Guid? RandomizationId { get; private set; }
    public string? KitReference { get; private set; }

    private ClinicalSubject() { } // EF Core

    public static ClinicalSubject Create(SubjectCode subjectCode, StudyCode studyCode, SiteCode? siteCode,
        int enrollmentIndex, string? encryptedDemographics, IReadOnlyDictionary<string, object?>? stratFactors)
    {
        var subject = new ClinicalSubject
        {
            Id = SubjectId.New(),
            SubjectCode = subjectCode,
            StudyCode = studyCode,
            SiteCode = siteCode,
            Status = SubjectState.Screening,
            EnrollmentIndex = enrollmentIndex,
            EncryptedDemographics = encryptedDemographics,
        };
        if (stratFactors is not null)
            foreach (var (k, v) in stratFactors) subject._stratFactors[k] = v;
        return subject;
    }

    /// <summary>Rule 1: pre-randomization only. Enforced here (domain invariant) — see §4.1 for the
    /// EF Core-level `IEntityTypeConfiguration` mirror of this same rule (defense in depth, matching
    /// the legacy system's dual ORM-`@validates` + router-level enforcement at raise sites #3/#21/#23).</summary>
    public Result<bool> UpdateStratFactors(IReadOnlyDictionary<string, object?> newFactors)
    {
        if (IsPostRandomization())
            return SubjectErrors.LockedFactorMutation();
        _stratFactors.Clear();
        foreach (var (k, v) in newFactors) _stratFactors[k] = v;
        return true;
    }

    public Result<bool> UpdateDemographics(string newEncryptedDemographics)
    {
        if (IsPostRandomization())
            return SubjectErrors.LockedFactorMutation();
        EncryptedDemographics = newEncryptedDemographics;
        return true;
    }

    /// <summary>Raise site #25 (`SOFT_DELETE_BLOCKED`, 403) — distinct error family from
    /// `LOCKED_FACTOR_MUTATION` even though both gate on the same `IsPostRandomization()` predicate.</summary>
    public Result<bool> ClearDemographics()
    {
        if (IsPostRandomization())
            return SubjectErrors.SoftDeleteBlocked();
        EncryptedDemographics = null;
        return true;
    }

    public bool IsPostRandomization() => Status is SubjectState.Randomized or SubjectState.Active
        or SubjectState.Completed or SubjectState.Unblinded or SubjectState.Withdrawn;

    public Result<SubjectState> TransitionTo(SubjectState target)
    {
        var result = SubjectTransitionGraph.Guard(Status, target);
        if (result.IsFailure) return result;
        Status = result.Value;
        return result;
    }

    /// <summary>Rule 9 SCN-008: bypasses the transition-graph HTTP-layer error translation by design —
    /// this mirrors the legacy `unblind_subject_model`, which sets `.status` directly and lets an illegal
    /// call sequence surface as a raw domain exception to whatever caller invoked it, not a formatted
    /// HTTP response. .NET keeps that by returning <see cref="Result{TValue}"/> here too (callers of this
    /// method, not just HTTP endpoints, must still handle failure explicitly).</summary>
    public Result<bool> Unblind(string unblindedBy, string reason, IClock clock)
    {
        var transition = SubjectTransitionGraph.Guard(Status, SubjectState.Unblinded);
        if (transition.IsFailure) return SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(Status), "UNBLINDED");
        Status = SubjectState.Unblinded;
        IsUnblinded = true;
        UnblindedBy = unblindedBy;
        UnblindedReason = reason;
        UnblindedAt = clock.UtcNow; // DateTimeOffset.UtcNow — fixes the naive datetime.now() trap
        return true;
    }

    public Result<bool> Withdraw(string reason, IClock clock)
    {
        var transition = SubjectTransitionGraph.Guard(Status, SubjectState.Withdrawn);
        if (transition.IsFailure) return SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(Status), "WITHDRAWN");
        Status = SubjectState.Withdrawn;
        WithdrawalReason = reason;
        WithdrawnAt = clock.UtcNow;
        return true;
    }
}
```

> **Note on `IGxPAuditable`.** `ClinicalSubject` inherits `AggregateRoot<SubjectId>`, which already implements `IGxPAuditable` (`CreatedAt`/`CreatedBy`/`ReasonForChange`/`VersionIndex`, all with public setters) — no separate interface implementation is needed on `ClinicalSubject` itself. This is the crux of §4.4's required interceptor change, flagged here for reviewers: today's `GxPAuditInterceptor` only recognizes the closed generic `AggregateRoot<string>`, and `ClinicalSubject`'s `TId` is `SubjectId`, not `string`, so the interceptor must be generalized (§4.4, item 1) before it will populate these fields for this slice's entities.

### 2.4 `SubjectConsent` Aggregate

> [!NOTE]
> **Superseded in part by #4824 (2026-09-30).** `Create` and `MergeResubmission` no longer take an `IClock` and never default `IcfSignedDate` to the current time. A signed consent requires a date: `Create` throws `ArgumentException` when `icfSigned` is true and `icfSignedDate` is null, and `MergeResubmission` throws `InvalidOperationException`, before changing anything, when neither the request nor the stored row has a date. `SubjectApplicationService.RecordConsentAsync` checks first and returns `SubjectErrors.SignedConsentDateRequired()` (400, `ICF_SIGNED_DATE_REQUIRED`). The sketch below predates this change.

```csharp
namespace Cadence.Apps.Execution.Domain.Subjects;

public sealed record ProtocolVersionRef(StudyCode StudyCode, string VersionTag, int VersionIndex); // EF Core Complex Type, no own PK

public sealed class SubjectConsent : AggregateRoot<SubjectConsentId>
{
    public SubjectCode SubjectCode { get; private set; } = default!;
    public ProtocolVersionRef ProtocolVersion { get; private set; } = default!;
    public bool IcfSigned { get; private set; }
    public DateTimeOffset? IcfSignedDate { get; private set; }
    public bool RequiresReconsent { get; private set; }
    public bool IsPaperOverride { get; private set; }

    private SubjectConsent() { }

    public static SubjectConsent Create(SubjectCode subjectCode, ProtocolVersionRef version, bool icfSigned,
        DateTimeOffset? icfSignedDate, bool requiresReconsent, bool isPaperOverride, IClock clock) => new()
    {
        Id = SubjectConsentId.New(),
        SubjectCode = subjectCode,
        ProtocolVersion = version,
        IcfSigned = icfSigned,
        IcfSignedDate = icfSignedDate ?? clock.UtcNow,
        RequiresReconsent = requiresReconsent,
        IsPaperOverride = isPaperOverride,
    };

    /// <summary>Rule 9 field-merge-on-resubmission semantics — NOT a full overwrite. `versionTag`/`icfSigned`/
    /// `requiresReconsent`/`isPaperOverride` always replace; `icfSignedDate` only replaces when supplied
    /// (defaults to now only if there was no prior date at all); `protocolVersion` only replaces when the
    /// incoming value is non-null/non-default (an omitted resubmission value leaves the stored value untouched).</summary>
    public void MergeResubmission(string versionTag, bool icfSigned, DateTimeOffset? icfSignedDate,
        bool requiresReconsent, bool isPaperOverride, ProtocolVersionRef? incomingProtocolVersion, IClock clock)
    {
        ProtocolVersion = ProtocolVersion with { VersionTag = versionTag };
        IcfSigned = icfSigned;
        RequiresReconsent = requiresReconsent;
        IsPaperOverride = isPaperOverride;
        IcfSignedDate = icfSignedDate ?? IcfSignedDate ?? clock.UtcNow;
        if (incomingProtocolVersion is not null)
            ProtocolVersion = incomingProtocolVersion;
    }

    /// <summary>Rule 3 cascade target — called on every *other* consent row for the same subject when a new
    /// consent is recorded with `requiresReconsent == false`.</summary>
    public void ClearReconsentRequirement() => RequiresReconsent = false;
}
```

### 2.5 `SiteComplianceCache` Aggregate

```csharp
namespace Cadence.Apps.Execution.Domain.Compliance;

public sealed class SiteComplianceCache : AggregateRoot<Guid>
{
    public StudyCode StudyCode { get; private set; } = default!;
    public SiteCode SiteCode { get; private set; }
    public string Milestone { get; private set; } = default!;
    public bool IsComplete { get; private set; }
    public IReadOnlyList<string> MissingDocuments => _missingDocuments;
    private readonly List<string> _missingDocuments = new();
    public int Version { get; private set; } = 1; // legacy's own optimistic counter — see §4.2, distinct from EF's rowversion

    private SiteComplianceCache() { }

    public static SiteComplianceCache Create(StudyCode studyCode, SiteCode siteCode, string milestone,
        bool isComplete, IEnumerable<string> missingDocuments)
    {
        var cache = new SiteComplianceCache
        {
            Id = Guid.NewGuid(),
            StudyCode = studyCode,
            SiteCode = siteCode,
            Milestone = milestone,
            IsComplete = isComplete,
        };
        cache._missingDocuments.AddRange(missingDocuments);
        return cache;
    }

    public void ApplyWebhookUpdate(bool isComplete, IEnumerable<string> missingDocuments)
    {
        IsComplete = isComplete;
        _missingDocuments.Clear();
        _missingDocuments.AddRange(missingDocuments);
        Version++; // upsert semantics from §1.1: increment on every update, no optimistic-conflict check
    }
}
```

### 2.6 Response DTOs — the `SubjectResponse` under-population decision

> [!NOTE]
> **Superseded by #4771 (D8, 2026-09-29).** The parity-preservation decision below was reversed in the hardening pass it anticipated: every write path now returns `SubjectResponse.FromAggregate(subject)`, `Underpopulated` was removed, and the golden-master fixtures (`SCN-003-A`, `SCN-005-D`, `EDGE-004`, `EDGE-010`) now assert the subject's real status, protocol version and site. The text below is kept as the historical record.

```csharp
namespace Cadence.Apps.Execution.Application.Subjects;

public sealed record SubjectResponse(
    SubjectId Id,
    SubjectCode SubjectId_,           // JSON: "subject_id" — see naming note below
    StudyCode StudyId,
    SiteCode? SiteId,
    string? TreatmentGroup,
    string Status,
    string? ActiveProtocolVersion,
    string? EncryptedDemographics)
{
    /// <summary>Full population — used only by <c>CreateAsync</c>, matching the legacy <c>create_subject</c>
    /// handler being the sole caller that populates every field.</summary>
    public static SubjectResponse FromAggregate(ClinicalSubject subject) => new(
        subject.Id, subject.SubjectCode, subject.StudyCode, subject.SiteCode, subject.TreatmentGroupCiphertext,
        SubjectStateCodec.ToCode(subject.Status), subject.ActiveProtocolVersion, subject.EncryptedDemographics);

    /// <summary>The §2.6 decision: `AdvanceStateAsync`/`UpdateDemographicsAsync`/`DeleteDemographicsAsync` call
    /// this instead of <see cref="FromAggregate"/>, hardcoding `Status`/`ActiveProtocolVersion` to the legacy
    /// Pydantic model's literal defaults regardless of the aggregate's real current values — a deliberate,
    /// tracked parity-preservation choice, not a bug. TODO(CADENCE-EXEC-SUBJ-001): revisit once golden-master
    /// parity ships (see §2.6/§7).</summary>
    public static SubjectResponse Underpopulated(SubjectId id, SubjectCode subjectCode, StudyCode studyCode,
        string? encryptedDemographics) => new(
        id, subjectCode, studyCode, SiteId: null, TreatmentGroup: null,
        Status: "SCREENING", ActiveProtocolVersion: "1.0.0", encryptedDemographics);
}
```

> **Decision (resolves `01_spec.md` §2.2 Rule 9's open item, per `00_phase1_validation_report.md` §0.2 item 1):** **preserve the under-population quirk byte-for-byte for this port.** The golden-master fixtures (`fixtures/output_expected_nominal.json`, scenarios `SCN-003-A` et al.) assert the legacy hardcoded-default behavior explicitly and call out that "fixing" the fixture would misrepresent legacy behavior — Step 5's parity harness is graded against those fixtures as the source of truth, so this design must reproduce them. Concretely: `AdvanceSubjectStateHandler`, `UpdateDemographicsHandler`, and `DeleteDemographicsHandler` construct `SubjectResponse` with `Status = "SCREENING"` (the enum's literal default) and `ActiveProtocolVersion = "1.0.0"` **hardcoded**, not read from the mutated aggregate, while `SiteId`/`TreatmentGroup` are left `null` — mirroring `create_subject`'s handler being the only one that fully populates the DTO. This is implemented as one shared private factory, `SubjectResponse.Underpopulated(id, subjectCode, studyCode, encryptedDemographics)`, so the quirk lives in exactly one place rather than being copy-pasted three times, and is annotated with a `TODO(CADENCE-EXEC-SUBJ-001)`-style comment pointing at this design section — a **tracked, deliberate parity-preservation choice**, not an accidental bug reintroduced silently. A follow-up ticket to fully populate the DTO (a documented behavioral improvement) is out of scope for this slice's initial port and belongs to a subsequent hardening pass once golden-master parity is established and signed off.

(JSON property naming — `subject_id`, `study_id`, `site_id`, `treatment_group`, `active_protocol_version`, `encrypted_demographics` — is applied via `JsonPropertyName` attributes / a snake_case naming policy registered once in `Program.cs`, not shown per-record here for brevity; every DTO in this design follows the same convention.)

Other response DTOs, following the same pattern as `SubjectResponse` (full field lists per `01_spec.md` §1.2):

```csharp
public sealed record SubjectDetailResponse(SubjectCode SubjectId, StudyCode StudyId, string Status,
    SiteCode? SiteId, string? TreatmentGroup, string? RandomizationSeed, string? InvestigationalProductId);

public sealed record SubjectConsentResponse(SubjectConsentId Id, SubjectCode SubjectId, StudyCode StudyId,
    string VersionTag, int VersionIndex, bool IcfSigned, DateTimeOffset? IcfSignedDate,
    bool RequiresReconsent, bool IsPaperOverride, long Version);

public sealed record ComplianceStatusResponse(StudyCode StudyId, SiteCode? SiteId, string Milestone,
    bool IsComplete, IReadOnlyList<string> MissingDocuments);

public sealed record OperationEnvelopeResponse(string Status, string Message);
```

### 2.7 Request DTOs & FluentValidation

```csharp
namespace Cadence.Apps.Execution.Application.Subjects;

public sealed record DemographicsPayload(string? Name, string? Birthdate, string? Gender, string? Race);

public sealed record SubjectCreateRequest(string SubjectId, string StudyId, string? SiteId, string? ArmId,
    DemographicsPayload? Demographics);

public sealed class SubjectCreateRequestValidator : AbstractValidator<SubjectCreateRequest>
{
    public SubjectCreateRequestValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty().MaximumLength(255);
        RuleFor(x => x.StudyId).NotEmpty().MaximumLength(255);
        RuleFor(x => x.SiteId).MaximumLength(255).When(x => x.SiteId is not null);
    }
}

public sealed record ProtocolVersionPayload(string StudyId, string VersionTag, int VersionIndex);

public sealed record SubjectConsentRequest(ProtocolVersionPayload ProtocolVersion, bool IcfSigned,
    DateTimeOffset? IcfSignedDate, bool RequiresReconsent = false, bool IsPaperOverride = false);

public sealed class SubjectConsentRequestValidator : AbstractValidator<SubjectConsentRequest>
{
    public SubjectConsentRequestValidator()
    {
        RuleFor(x => x.ProtocolVersion).NotNull();
        RuleFor(x => x.ProtocolVersion.StudyId).NotEmpty().When(x => x.ProtocolVersion is not null);
        RuleFor(x => x.ProtocolVersion.VersionTag).NotEmpty().When(x => x.ProtocolVersion is not null);
    }
}

/// <summary>`status`/`state` are alternative property names for the same target — exactly one must be non-null.
/// Distinguishing "omitted" from "explicitly null" is why both are declared nullable here rather than defaulted,
/// per the general instruction to keep that distinction explicit (§3 traps table, generalized truthiness guidance).</summary>
public sealed record SubjectStateUpdateRequest(string? Status, string? State)
{
    public string? Target => Status ?? State;
}

public sealed class SubjectStateUpdateRequestValidator : AbstractValidator<SubjectStateUpdateRequest>
{
    public SubjectStateUpdateRequestValidator()
    {
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Status) || !string.IsNullOrWhiteSpace(x.State))
            .WithMessage("Either status or state must be provided.")
            .WithErrorCode("MISSING_TARGET_STATE");
    }
}

public sealed record SubjectDemographicsUpdateRequest(DemographicsPayload? Demographics,
    IReadOnlyDictionary<string, object?>? StratFactors);

public sealed record EtmfCompletenessWebhookPayload(string StudyId, string? SiteId, string Milestone,
    bool IsComplete, IReadOnlyList<string>? MissingArtifacts);

public sealed class EtmfCompletenessWebhookPayloadValidator : AbstractValidator<EtmfCompletenessWebhookPayload>
{
    public EtmfCompletenessWebhookPayloadValidator()
    {
        RuleFor(x => x.StudyId).NotEmpty();
        RuleFor(x => x.Milestone).NotEmpty();
    }
}

public sealed record SiteActivationRequest(string StudyId);
```

`X-Change-Reason` (required on `PATCH .../state` and `PUT .../demographics`, per §1.1) is bound via an `IEndpointFilter`, not a DTO property — see §3.2.

---

## 3. Request Handlers & Minimal API Surface

### 3.1 Application service contracts (direct typed services — ADR-2428 §3.2, no MediatR)

```csharp
namespace Cadence.Apps.Execution.Application.Subjects;

public interface ISubjectApplicationService
{
    Task<Result<SubjectResponse>> CreateAsync(SubjectCreateRequest request, CancellationToken ct);
    Task<Result<SubjectConsentResponse>> RecordConsentAsync(string subjectIdOrCode, SubjectConsentRequest request, CancellationToken ct);
    Task<Result<SubjectResponse>> AdvanceStateAsync(string subjectIdOrCode, SubjectStateUpdateRequest request, string changeReason, CancellationToken ct);
    Task<Result<SubjectResponse>> UpdateDemographicsAsync(string subjectIdOrCode, SubjectDemographicsUpdateRequest request, string changeReason, CancellationToken ct);
    Task<Result<SubjectResponse>> DeleteDemographicsAsync(string subjectIdOrCode, string changeReason, CancellationToken ct);
    Task<Result<SubjectDetailResponse>> GetDetailAsync(string subjectId, bool isBlindedCaller, CancellationToken ct);
    Task<Result<SubjectResponse>> EvaluateAndTransitionScreeningAsync(string subjectId, CancellationToken ct);
}
```

### 3.2 Representative handler — `AdvanceStateAsync` (folds raise sites #1, #4-#6, #14-#19, SCN-003/SCN-005)

```csharp
public sealed class SubjectApplicationService(
    ISubjectRepository subjects,
    ISiteComplianceRepository compliance,
    IAuditLogWriter auditLog,
    IClock clock,
    ICurrentAuditContextAccessor auditContext) : ISubjectApplicationService
{
    public async Task<Result<SubjectResponse>> AdvanceStateAsync(
        string subjectIdOrCode, SubjectStateUpdateRequest request, string changeReason, CancellationToken ct)
    {
        // Rule 9 dual-lookup: try surrogate PK first, then domain code — SubjectNotFoundError only after both fail.
        var subject = await subjects.GetByIdAsync(subjectIdOrCode, ct) ?? await subjects.GetByCodeAsync(subjectIdOrCode, ct);
        if (subject is null)
            return SubjectErrors.NotFound(subjectIdOrCode); // raise sites #1/#15/#17 collapse to one path

        if (!SubjectStateCodec.TryParse(request.Target, out var target))
            return SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(subject.Status), request.Target ?? "");

        // Rule 2: sovereign site activation gate, only enforced when the target is Enrolled (SCN-005 / raise site #16).
        if (target == SubjectState.Enrolled)
        {
            var gate = await compliance.GetAsync(subject.StudyCode, subject.SiteCode, "SITE_ACTIVATION", ct);
            if (gate is not { IsComplete: true })
            {
                await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "BLOCKED_ENROLLMENT",
                    auditContext.UserId, changeReason, oldValues: null, newValues: null, ct);
                return SubjectErrors.BlockedEnrollment(subject.SiteCode);
            }
        }

        var transition = subject.TransitionTo(target);
        if (transition.IsFailure) return Result<SubjectResponse>.Failure(transition.Error!);

        subject.ReasonForChange = changeReason;
        await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), $"TRANSITION_{SubjectStateCodec.ToCode(target)}",
            auditContext.UserId, changeReason, oldValues: null, newValues: null, ct);
        await subjects.SaveChangesAsync(ct);

        // §2.6 decision: under-populated by design for this handler.
        return SubjectResponse.Underpopulated(subject.Id, subject.SubjectCode, subject.StudyCode, subject.EncryptedDemographics);
    }

    // CreateAsync, RecordConsentAsync, UpdateDemographicsAsync, DeleteDemographicsAsync,
    // GetDetailAsync, EvaluateAndTransitionScreeningAsync follow the same shape — each is a private
    // method-per-use-case; omitted here for brevity but hold to the same contract: dual-lookup where
    // applicable, `Result<T>` returns (no throwing on expected domain failures), and a single
    // `SaveChangesAsync` per request so the `GxPAuditInterceptor` (§4.4) commits inside the same transaction
    // as the audit-log write.
}
```

Every `CancellationToken ct` parameter flows unbroken from the Minimal API delegate through the application service to `DbContext.SaveChangesAsync(ct)` and any downstream `IHttpClientFactory` call — no method in this slice swallows or defaults it.

### 3.3 Minimal API endpoint registration

```csharp
namespace Cadence.Apps.Execution.Endpoints;

public static class SubjectEndpoints
{
    public static IEndpointRouteBuilder MapSubjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/subjects")
            .WithTags("Subjects")
            .WithOpenApi();

        group.MapPost("/", async (SubjectCreateRequest request, ISubjectApplicationService svc, CancellationToken ct) =>
            {
                var result = await svc.CreateAsync(request, ct);
                return result.Match(
                    onSuccess: r => Results.Created($"/api/v1/execution/subjects/{r.SubjectId_}", r),
                    onFailure: e => e.ToProblemResult());
            })
            .WithName("CreateSubject")
            .Produces<SubjectResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .AddEndpointFilter<ValidationFilter<SubjectCreateRequest>>();

        group.MapPost("/{subjectId}/consent", async (string subjectId, SubjectConsentRequest request,
                ISubjectApplicationService svc, CancellationToken ct) =>
            (await svc.RecordConsentAsync(subjectId, request, ct)).Match(Results.Ok, e => e.ToProblemResult()))
            .WithName("RecordSubjectConsent")
            .Produces<SubjectConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddEndpointFilter<ValidationFilter<SubjectConsentRequest>>();
        group.MapPost("/{subjectId}/consents", (string subjectId, SubjectConsentRequest request,
                ISubjectApplicationService svc, CancellationToken ct)
            => svc.RecordConsentAsync(subjectId, request, ct)) // alias — see trap table, canonical route is `/consent`
            .WithName("RecordSubjectConsentAlias").ExcludeFromDescription();

        group.MapPatch("/{subjectId}/state", async (string subjectId, SubjectStateUpdateRequest request,
                HttpContext http, ISubjectApplicationService svc, CancellationToken ct) =>
            {
                string changeReason = http.Request.Headers["X-Change-Reason"].FirstOrDefault()
                    ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");
                return (await svc.AdvanceStateAsync(subjectId, request, changeReason, ct)).Match(Results.Ok, e => e.ToProblemResult());
            })
            .WithName("UpdateSubjectState")
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AddEndpointFilter<ValidationFilter<SubjectStateUpdateRequest>>()
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapGet("/{subjectId}", async (string subjectId, ICurrentUserAccessor user,
                ISubjectApplicationService svc, CancellationToken ct) =>
            (await svc.GetDetailAsync(subjectId, user.HasBlindedRole, ct)).Match(Results.Ok, e => e.ToProblemResult()))
            .WithName("GetSubjectDetail")
            .Produces<SubjectDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{subjectId}/demographics", async (string subjectId, SubjectDemographicsUpdateRequest request,
                HttpContext http, ISubjectApplicationService svc, CancellationToken ct) =>
            {
                string changeReason = http.Request.Headers["X-Change-Reason"].FirstOrDefault()!;
                return (await svc.UpdateDemographicsAsync(subjectId, request, changeReason, ct)).Match(Results.Ok, e => e.ToProblemResult());
            })
            .WithName("UpdateSubjectDemographics")
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapDelete("/{subjectId}/demographics", async (string subjectId, HttpContext http,
                ISubjectApplicationService svc, CancellationToken ct) =>
            {
                string changeReason = http.Request.Headers["X-Change-Reason"].FirstOrDefault()!;
                return (await svc.DeleteDemographicsAsync(subjectId, changeReason, ct)).Match(Results.Ok, e => e.ToProblemResult());
            })
            .WithName("DeleteSubjectDemographics")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        return app;
    }

    public static IEndpointRouteBuilder MapSiteComplianceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/execution/webhooks/etmf", async (EtmfCompletenessWebhookPayload payload,
                ISiteComplianceApplicationService svc, CancellationToken ct) =>
            (await svc.ApplyEtmfWebhookAsync(payload, ct)).Match(
                _ => Results.Ok(new OperationEnvelopeResponse("success", "Compliance cache updated successfully.")),
                e => e.ToProblemResult()))
            .WithTags("SiteCompliance").WithName("EtmfCompletenessWebhook")
            .AddEndpointFilter<ValidationFilter<EtmfCompletenessWebhookPayload>>();

        app.MapPost("/api/v1/execution/sites/{siteId}/activate", async (string siteId, SiteActivationRequest request,
                ISiteComplianceApplicationService svc, CancellationToken ct) =>
            (await svc.ActivateSiteAsync(siteId, request, ct)).Match(
                _ => Results.Ok(new OperationEnvelopeResponse("success", $"Site {siteId} activated successfully.")),
                e => e.ToProblemResult()))
            .WithTags("SiteCompliance").WithName("ActivateSite")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/v1/execution/sites/{siteId}/compliance-status", async (string siteId, string studyId,
                string? milestone, ISiteComplianceApplicationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetComplianceStatusAsync(siteId, studyId, milestone ?? "SITE_ACTIVATION", ct)))
            .WithTags("SiteCompliance").WithName("GetSiteComplianceStatus")
            .Produces<ComplianceStatusResponse>(StatusCodes.Status200OK); // always 200 per §1.2 — never 404

        return app;
    }
}
```

### 3.4 Error handling — `DomainError` → RFC 9457 `ProblemDetails`

`SubjectErrors` is the single factory for every domain error this slice raises, each carrying an `ErrorType` (from the existing `Cadence.Core.Domain.DomainError`) and a stable machine-readable `Code` matching §5's fixed-string response bodies where the legacy system used one, or a new stable code where it only used free text:

```csharp
namespace Cadence.Apps.Execution.Domain.Subjects;

public static class SubjectErrors
{
    public static DomainError NotFound(string subjectIdOrCode) =>
        DomainError.NotFound("SUBJECT_NOT_FOUND", "Subject not found"); // unifies raise sites #1,#10,#13,#15,#17,#20,#24 on one message

    public static DomainError InvalidStateTransition(string from, string to) =>
        DomainError.Validation("INVALID_STATE_TRANSITION", $"Transition from {from} to {to} is forbidden.");
        // unifies raise sites #4,#5,#6,#12,#18 — free-text detail always available; .NET picks ONE response
        // shape (structured ProblemDetails with `code`) rather than reproducing the legacy fixed-string-vs-
        // free-text split between raise sites #12 and #18.

    public static DomainError LockedFactorMutation() =>
        DomainError.Validation("LOCKED_FACTOR_MUTATION", "Stratification factors and demographics are locked post-randomization.")
            with { Type = ErrorType.Conflict }; // HTTP 422 — see mapping table below (Conflict maps to 422 for this one code, not 409)

    public static DomainError SoftDeleteBlocked() =>
        DomainError.Forbidden("SOFT_DELETE_BLOCKED", "Demographics cannot be deleted post-randomization.");

    public static DomainError BlockedEnrollment(SiteCode? siteId) =>
        DomainError.Validation("BLOCKED_ENROLLMENT", $"Subject enrollment blocked: Site {siteId} is not compliant.");

    public static DomainError BlockedActivation(string siteId) =>
        DomainError.Validation("BLOCKED_ACTIVATION", $"Site activation blocked: Site {siteId} is not compliant in eTMF. Missing expected documents.");

    public static DomainError ReconsentRequired(string versionTag, string subjectId, string visitId) =>
        DomainError.Conflict("RECONSENT_REQUIRED",
            $"Protocol Amendment {versionTag} is active. Subject {subjectId} must execute re-consent before data entry on visit {visitId}.")
            with { Type = ErrorType.Forbidden }; // maps to 423 explicitly, see table
}
```

`DomainError` → HTTP mapping (registered once via `IProblemDetailsService.CustomizeProblemDetails`, plus one explicit override table for the codes that don't follow the `ErrorType` default):

| `ErrorType` (default) | HTTP Status | Override by `Code`         | HTTP Status |
| --------------------- | ----------- | -------------------------- | ----------- |
| `Validation`          | 400         | `LOCKED_FACTOR_MUTATION`   | **422**     |
| `NotFound`            | 404         | `RECONSENT_REQUIRED`       | **423**     |
| `Conflict`            | 409         | `INVALID_STATE_TRANSITION` | **422**     |
| `Forbidden`           | 403         |                            |             |
| `Unauthorized`        | 401         |                            |             |

`INVALID_STATE_TRANSITION` answers 422 rather than the legacy 400 because PRD-SUB-001 requires a forbidden subject transition to return `422 Unprocessable Entity`; golden masters INV-008 and INV-020 record the deliberate break from parity. This is implemented as a small `static class DomainErrorHttpMapping` consulted by a `ToProblemResult()` extension on `DomainError`, rather than scattering status-code literals across handlers.

**Raise site #19 (`subjects.py:643-644`, the genuine bare `except Exception as e:` catch-all) is explicitly NOT ported.** No handler in this design catches `Exception` and maps it to a 400. An unexpected exception (e.g. a database failure) falls through to ASP.NET Core's global `IExceptionHandler`, which maps to a generic 500 `ProblemDetails` — consistent with `01_spec.md` §5's explicit instruction ("`.NET` must not reproduce a blanket `catch (Exception) -> 400`").

**Global not-found handling.** Per the spec's `.NET decision` callout in §5, `SubjectErrors.NotFound` is a first-class `DomainError` returned through the normal `Result<T>` path on every call site (not just the one the legacy system happened to wrap in a local `try/except`) — there is no analogous "missing global handler" gap in this design, because every application-service method already returns `Result<T>` uniformly.

### 3.5 Full raise-site → design mapping (25 of 25)

| #              | Legacy raise site                                                          | .NET mapping                                                                                                                                                                                                                                                                                                                                                            |
| -------------- | -------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1, 15, 17      | `SubjectNotFoundError` (services.py:188, subjects.py:610, subjects.py:639) | `SubjectErrors.NotFound` — dual-lookup exhausted, §3.2                                                                                                                                                                                                                                                                                                                  |
| 2              | `SubjectNotFoundError` (services.py:221, unreachable from HTTP today)      | Same `SubjectErrors.NotFound`, now reachable — `UpdateDemographicsAsync` uses the service layer's dual-lookup uniformly rather than the endpoint's own inline query (closes the legacy inconsistency deliberately)                                                                                                                                                      |
| 3, 21, 23      | `LockedFactorMutationError` / router 422s                                  | `ClinicalSubject.UpdateStratFactors`/`UpdateDemographics` → `SubjectErrors.LockedFactorMutation()`, enforced once in the aggregate (§2.3) + mirrored in `IEntityTypeConfiguration` (§4.1)                                                                                                                                                                               |
| 4, 12, 18      | `InvalidStateTransitionError`, known-but-forbidden                         | `SubjectTransitionGraph.Guard` → `SubjectErrors.InvalidStateTransition`                                                                                                                                                                                                                                                                                                 |
| 5              | Initial transition ≠ Screening                                             | Same — `Guard(current: null, target)` branch                                                                                                                                                                                                                                                                                                                            |
| 6              | Unknown/malformed state string                                             | Same — `SubjectStateCodec.TryParse` failure path, §3.2                                                                                                                                                                                                                                                                                                                  |
| 7              | `ReConsentRequiredException`, 423                                          | `SubjectErrors.ReconsentRequired` — belongs to the amendments-validation slice's router, not this slice's own endpoints; the exception/error type is defined here per the legacy file layout and exported via `Domain.Subjects.SubjectErrors` for that slice to consume                                                                                                 |
| 8              | Bare `ValueError`, subject-not-found in version gating                     | `SubjectErrors.NotFound` (typed, closing the legacy inconsistency)                                                                                                                                                                                                                                                                                                      |
| 9              | Site-activation-blocked, 400                                               | `SubjectErrors.BlockedActivation`                                                                                                                                                                                                                                                                                                                                       |
| 10, 13, 20, 24 | Subject-not-found (4 more sites)                                           | `SubjectErrors.NotFound`, one message, standardized (closes "Clinical subject not found." vs "Subject not found" inconsistency deliberately)                                                                                                                                                                                                                            |
| 11             | Missing/unresolvable `study_id`                                            | `SubjectDemographicsUpdateRequest`/screening-transition validator — `MISSING_STUDY_ID` `DomainError.Validation`                                                                                                                                                                                                                                                         |
| 14             | Missing both `status`/`state`                                              | `SubjectStateUpdateRequestValidator`, §2.7                                                                                                                                                                                                                                                                                                                              |
| 16             | `BLOCKED_ENROLLMENT`                                                       | `SubjectErrors.BlockedEnrollment`, §3.2                                                                                                                                                                                                                                                                                                                                 |
| 19             | Bare `except Exception`                                                    | **Not ported** — falls through to global `IExceptionHandler` → 500, §3.4                                                                                                                                                                                                                                                                                                |
| 22             | Suppressed decrypt failure on lock-check path                              | **Not ported as a silent suppress** — `AesGcmDemographicsEncryptionService.Decrypt` throws a typed `DemographicsDecryptionFailedException`; `UpdateDemographicsAsync` lets it propagate (mapped to a dedicated `DECRYPTION_FAILED` 4xx via the global handler) rather than defaulting to `current_demo = {}`, per the spec's explicit "fail loudly instead" instruction |
| 25             | `SOFT_DELETE_BLOCKED`, 403                                                 | `SubjectErrors.SoftDeleteBlocked`                                                                                                                                                                                                                                                                                                                                       |

---

## 4. Persistence & Data Access (EF Core 10)

### 4.1 `IEntityTypeConfiguration<T>` — `ClinicalSubject`

```csharp
namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

public sealed class ClinicalSubjectConfiguration : IEntityTypeConfiguration<ClinicalSubject>
{
    public void Configure(EntityTypeBuilder<ClinicalSubject> builder)
    {
        builder.ToTable("clinical_subjects");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, v => new SubjectId(v));

        builder.Property(x => x.SubjectCode).HasConversion(c => c.Value, v => new SubjectCode(v))
            .HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectCode).IsUnique();

        builder.Property(x => x.StudyCode).HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255).IsRequired();
        builder.HasIndex(x => new { x.StudyCode, x.EnrollmentIndex }).IsUnique(); // Rule 5 — see §4.2

        builder.Property(x => x.SiteCode).HasConversion(
            c => c.HasValue ? c.Value.Value : null,
            v => v == null ? (SiteCode?)null : new SiteCode(v));

        builder.Property(x => x.Status).HasConversion(s => SubjectStateCodec.ToCode(s),
            v => SubjectStateCodec.TryParse(v, out var s) ? s : throw new InvalidOperationException($"Corrupt status '{v}' in storage."))
            .HasMaxLength(32);

        // Rule 2: strat_factors, arbitrary untyped JSON dict -> native jsonb. `object?` values rule out
        // EF Core's typed `.ToJson()` owned-entity mapping (it requires a concrete CLR shape), so this maps
        // the private backing field directly through a value converter instead.
        builder.Property<Dictionary<string, object?>>("_stratFactors")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v, JsonSerializerOptions.Default) ?? new())
            .HasColumnName("strat_factors")
            .HasColumnType("jsonb");

        builder.Property<long>("_version").IsRowVersion(); // EF concurrency token — see §4.2 for the enrollment-index lock, which is separate

        builder.Property(x => x.EncryptedDemographics).HasMaxLength(int.MaxValue);
        // Rule 1 (LOCKED_FACTOR_MUTATION) is enforced in the aggregate itself (§2.3's UpdateStratFactors/
        // UpdateDemographics) — this configuration only maps storage shape, it does not re-enforce the guard;
        // there is no ORM-level `@validates`-equivalent trigger in EF Core, so the aggregate is the single
        // enforcement point, unlike the legacy system's dual ORM+router enforcement (raise sites #3 vs #21/#23).
    }
}
```

### 4.2 Concurrency: sequential enrollment index (Rule 5) and `SiteComplianceCache` version (§1.1 upsert)

- **Rule 5 (`enrollment_index`).** Implemented as a `SELECT ... FOR UPDATE`-equivalent: the `EnrollNextIndexAsync(StudyCode)` repository method runs `SELECT MAX(enrollment_index) FROM clinical_subjects WHERE study_code = @study FOR UPDATE` inside the same transaction as the subsequent `INSERT`, using `Database.BeginTransactionAsync(IsolationLevel.RepeatableRead)` plus an explicit PostgreSQL advisory lock keyed on `hashtext(studyCode)` (`pg_advisory_xact_lock`) to serialize concurrent enrollments for the _same_ study without blocking enrollments across different studies. This single code path is shared by both `SubjectApplicationService.CreateAsync` and `EConsentCompletedConsumer` (§5.2), closing the spec's explicit note that the same race exists independently in both places today.
- **`SiteComplianceCache.Version`** is the legacy system's own plain integer counter (upsert-increment, no compare-and-swap) — reproduced as a plain `int` column, NOT as an EF `[Timestamp]`/`IsRowVersion()` concurrency token, because the legacy contract is "always overwrite and bump," not "reject a stale write." The entity's own EF row (`_version` shadow property, §4.1) is the _actual_ optimistic-concurrency guard for `SaveChangesAsync`; `SiteComplianceCache.Version` is domain data, not infrastructure.
- ~~**`DbUpdateConcurrencyException` retry policy.** Registered via `Microsoft.Extensions.Resilience`'s `AddResiliencePipeline`, 3 attempts with exponential backoff (`Polly`, already declared), scoped to `SaveChangesAsync` calls in the repository base class — applies uniformly across this slice's three aggregates.~~

> [!NOTE]
> **Superseded by #4766 / ADR-2450 (2026-09-29): reject on conflict.** Retrying a `DbUpdateConcurrencyException` with a "client wins" reload silently overwrote the other writer's change. A concurrency conflict is now never retried: `ExecutionRepositoryBase.SaveChangesAsync` rethrows it as `ConcurrencyConflictException`, and `ConcurrencyConflictExceptionHandler` returns 409 Conflict ProblemDetails with `code: CONCURRENCY_CONFLICT`. The Polly pipeline retries only transient database failures (`NpgsqlException.IsTransient`, such as serialization failures, deadlocks and dropped connections), and not inside an explicit transaction such as enrollment, where a failed statement aborts the transaction. `GxPAuditInterceptor` undoes a failed attempt's audit entries so a retried save is audited once.

### 4.3 JSON columns & Complex Types

| Legacy shape                                                           | .NET 10 mapping                                                                                                                                                                                                                                                                       |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `strat_factors` (arbitrary JSON dict)                                  | `jsonb` column via `OwnsOne(...).ToJson()` (§4.1)                                                                                                                                                                                                                                     |
| `protocol_version` (embedded `{study_id, version_tag, version_index}`) | EF Core 10 **Complex Type** (`ComplexProperty`), no independent PK, on `SubjectConsent`                                                                                                                                                                                               |
| `missing_documents` (comma-joined string in legacy)                    | **Not reproduced as a delimited string.** Stored as a native `text[]` (Npgsql array support) — the comma-join was a legacy storage artifact, not an observable API contract (the API surface, `ComplianceStatusResponse.MissingDocuments`, is already a `list<string>` on both sides) |

### 4.4 GxP Audit Trail — resolving the DB-trigger vs. `GxPAuditInterceptor` divergence (Rule 6)

**Decision:** the DB-trigger mechanism (the `before_flush` listener in `apps/execution/database/audit.py`, plus `set_config` and a separately-deployed Postgres trigger) is **not ported**. `GxPAuditInterceptor` becomes the single, application-layer, authoritative audit-capture path for every entity this slice mutates, consistent with ADR-2428's platform-wide commitment to "tamper-evident audit records at the persistence boundary... without relying on fragile database triggers." This is a deliberate architecture simplification, not an oversight: the legacy system's two independent audit paths (explicit `AuditLog` writes in `SubjectLifecycleService` + the DB-trigger's separate ledger) never had a documented reconciliation story even in Python; carrying that duplication into .NET would import complexity with no compensating benefit once a single interceptor already guarantees the same commit-time guarantee without needing a second, database-native mechanism.

**Two concrete gaps this decision requires closing, both prerequisites for this slice, not speculative scope:**

1. **`GxPAuditInterceptor` (`packages/database/src/GxPAuditInterceptor.cs`) only recognizes `AggregateRoot<string>`.** `ClinicalSubject : AggregateRoot<SubjectId>`, `SubjectConsent : AggregateRoot<SubjectConsentId>`, and `SiteComplianceCache : AggregateRoot<Guid>` all fail that type test today, silently skipping `CreatedAt`/`CreatedBy`/`VersionIndex` population for every entity in this slice. **Required fix:** introduce a non-generic `AggregateRootBase : IGxPAuditable` with the four settable audit properties, have `AggregateRoot<TId> : AggregateRootBase` inherit them instead of redeclaring them, and change the interceptor's pattern match from `entry.Entity is AggregateRoot<string> agg` to `entry.Entity is AggregateRootBase agg`. This is a shared-kernel change (not scoped to this slice alone) but is a direct, unavoidable precondition for Rule 6 to hold for any strongly-typed-ID aggregate — flagged here as a blocking dependency for implementation, to be raised as its own small PR before or alongside this slice's implementation ticket.
2. **`GxPAuditInterceptor` does not yet write `AuditLogEntry` rows or compute the SHA-256 Merkle chain** ADR-2428 §3.5 commits to — it currently only stamps fields on the entity itself. **Required fix:** after the existing field-stamping loop, for each `IGxPAuditable` entry with `EntityState` `Added`/`Modified`/`Deleted`, append one `AuditLogEntry` (via `eventData.Context.Add(...)`, added to the _same_ `ChangeTracker` so it commits in the identical `SaveChangesAsync` transaction) using `AuditLogEntry.ComputeBlockHash(...)` chained to the previous entry's `BlockHash` for that `EntityName`. `ReasonForChange`/`UserId` are sourced from the scoped `ICurrentAuditContextAccessor` (below), and `Action` is the caller-supplied semantic action string (`ENROLL_SUBJECT`, `RECORD_CONSENT`, `TRANSITION_<STATE>`, `UPDATE_DEMOGRAPHICS`) rather than the raw EF `EntityState` — application services set this via a per-request `IAuditActionScope` (see `IAuditLogWriter.WriteAsync` in §3.2) rather than the interceptor inferring it generically, since `EntityState.Modified` alone cannot distinguish "consent recorded" from "state transitioned."

```csharp
namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>Replaces contextvars.ContextVar (`current_user_id`, `current_change_reason`) with one
/// consistently-defaulting accessor — closing Rule 9's noted two-idiom split (`.get()` + LookupError catch
/// vs. `.get() or "system"`) by giving every reader exactly one resolution path.</summary>
public interface ICurrentAuditContextAccessor
{
    string UserId { get; }         // defaults to "system" when unset
    string ChangeReason { get; }   // defaults to the X-Change-Reason header if present, else "system_operation"
}

public sealed class HttpCurrentAuditContextAccessor(IHttpContextAccessor http) : ICurrentAuditContextAccessor
{
    public string UserId => http.HttpContext?.User.FindFirst("sub")?.Value is { Length: > 0 } uid ? uid : "system";
    public string ChangeReason => http.HttpContext?.Request.Headers["X-Change-Reason"].FirstOrDefault() is { Length: > 0 } reason
        ? reason : "system_operation";
}
```

For the `EConsentCompletedConsumer` (no `HttpContext`), a second implementation (`SystemAuditContextAccessor`, fixed `"system-pubsub"`/`"Automated consent-completion enrollment"`) is registered scoped-per-consume via MassTransit's own DI scope — matching the legacy subscriber's fixed `user_id="system-pubsub"`.

> [!NOTE]
> **Superseded by #4767 / ADR-2257 (2026-09-29).** The `"system"` and `"system-pubsub"` defaults above are gone. `AmbientAuditContextAccessor` implements both `ICurrentAuditContextAccessor` and `IAuditActorScope`. `UserId` is the caller's `sub`, or the `svc:` service account declared with `RunAs`. With neither, it throws `AuditActorUnresolvedException`. The consumer calls `RunAs(ServiceAccounts.EConsentConsumer)` and is recorded as `svc:econsent-consumer`. The eTMF webhook (`ETMF_COMPLIANCE_UPDATE`) and demographics deletion (`DELETE_DEMOGRAPHICS`) now write semantic audit entries.

> [!NOTE]
> **Item 2 implemented by #4768 / ADR-2446 (2026-09-29).** `GxPAuditInterceptor` is the only audit writer. `IAuditLogWriter.WriteAsync` records a semantic action through `Cadence.Database.AuditActions` instead of writing its own row, and the interceptor puts that action on the changed entity's entry. Chains are keyed by CLR type name (`ClinicalSubject`, `SubjectConsent`, `SiteComplianceCache`), not table name, so the `table_name` values in the golden-master fixtures are CLR names. An action with no changed row (`BLOCKED_ENROLLMENT`, `BLOCKED_ACTIVATION`, `SITE_ACTIVATION`) is written as an action-only entry with version index 0. `AuditLogChainVerifier` verifies each chain. Earlier table-name-keyed entries stay as their own read-only chains.

---

## 5. Resilience & External Integrations

### 5.1 Demographics encryption (Rule 7)

```csharp
namespace Cadence.Apps.Execution.Infrastructure.Security;

public interface IDemographicsEncryptionService
{
    string Encrypt(IReadOnlyDictionary<string, object?> demographics);
    IReadOnlyDictionary<string, object?> Decrypt(string ciphertext); // throws DemographicsDecryptionFailedException
}

public sealed class DemographicsDecryptionFailedException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class AesGcmDemographicsEncryptionService(IOptions<DemographicsEncryptionOptions> options)
    : IDemographicsEncryptionService
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.KeyBase64); // configured, never a literal — closes the Fernet hardcoded-key trap

    public string Encrypt(IReadOnlyDictionary<string, object?> demographics)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(demographics);
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return Convert.ToBase64String([.. nonce, .. ciphertext, .. tag]); // collection expression, C# 14
    }

    public IReadOnlyDictionary<string, object?> Decrypt(string ciphertextB64)
    {
        try
        {
            byte[] blob = Convert.FromBase64String(ciphertextB64);
            var nonce = blob.AsSpan(0, AesGcm.NonceByteSizes.MaxSize);
            var tag = blob.AsSpan(blob.Length - AesGcm.TagByteSizes.MaxSize, AesGcm.TagByteSizes.MaxSize);
            var ciphertext = blob.AsSpan(AesGcm.NonceByteSizes.MaxSize, blob.Length - AesGcm.NonceByteSizes.MaxSize - AesGcm.TagByteSizes.MaxSize);
            byte[] plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(plaintext)
                ?? throw new DemographicsDecryptionFailedException("Decrypted payload was not a JSON object.");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            throw new DemographicsDecryptionFailedException("Demographics ciphertext could not be decrypted.", ex);
        }
    }
}
```

`IDemographicsNormalizationService` implements Rule 7's `normalize_gender`/`calculate_age`/`get_safe_demographics` contract as pure, non-throwing functions returning `("U", null)`-shaped defaults on any failure (per the spec's explicit "preserve this silent-fallback contract" decision, §2.2 Rule 7 — the _only_ place in this slice that intentionally does not use `Result<T>`/exceptions, because it is read-only reference-range/eligibility support, never the demographics write path). `CalculateAge` reproduces `(observationDate.DayNumber - birthDate.DayNumber) / 365.25` exactly, per the spec's explicit instruction not to substitute calendar-accurate `Period` math.

### 5.2 PubSub Auto-Enrollment → MassTransit transactional outbox (Rule 8 / MIG-06)

> [!NOTE]
> **Superseded in part by #4769 (2026-09-29).** `EConsentCompletedEvent` now carries `DateTimeOffset? SignedAt`, the time the participant signed. The consumer records it as `IcfSignedDate` instead of `clock.UtcNow`, so a delayed message keeps the true signed time. An event without `SignedAt` is logged and rejected with `ConsentEventRejectedException` before anything is written, and MassTransit moves it to the error queue. The sketch below predates this change.

```csharp
namespace Cadence.Apps.Execution.Infrastructure.Consumers;

public sealed record EConsentCompletedEvent(string SubjectId, string StudyId, string? SiteId,
    string VersionTag, int VersionIndex);

public sealed class EConsentCompletedConsumer(
    ISubjectRepository subjects, IConsentRepository consents, IAuditLogWriter auditLog, IClock clock)
    : IConsumer<EConsentCompletedEvent>
{
    public async Task Consume(ConsumeContext<EConsentCompletedEvent> context)
    {
        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.SubjectId) || string.IsNullOrWhiteSpace(msg.StudyId))
            return; // malformed event — dropped silently, matching the legacy warning-log-only behavior

        var subjectCode = new SubjectCode(msg.SubjectId);
        var studyCode = new StudyCode(msg.StudyId);

        // Idempotency: skip entirely if the subject already exists (by code OR surrogate id).
        var existing = await subjects.GetByCodeAsync(subjectCode.Value, context.CancellationToken);
        if (existing is null)
        {
            int nextIndex = await subjects.EnrollNextIndexAsync(studyCode, context.CancellationToken); // §4.2 shared locked path
            var siteCode = msg.SiteId is { Length: > 0 } s ? new SiteCode(s) : (SiteCode?)null;
            var created = ClinicalSubject.Create(subjectCode, studyCode, siteCode, nextIndex,
                encryptedDemographics: null, stratFactors: null);
            created.ReasonForChange = "Automated consent-completion enrollment";
            await subjects.AddAsync(created, context.CancellationToken);
            await auditLog.WriteAsync(nameof(ClinicalSubject), created.Id.ToString(), "INSERT",
                "system-pubsub", created.ReasonForChange, oldValues: null, newValues: null, context.CancellationToken);
        }

        // Independently: backfill the consent-cache row if missing (idempotent-but-independent of the above).
        var version = new ProtocolVersionRef(studyCode, msg.VersionTag, msg.VersionIndex);
        var consentExists = await consents.ExistsAsync(subjectCode, studyCode, msg.VersionIndex, context.CancellationToken);
        if (!consentExists)
        {
            var consent = SubjectConsent.Create(subjectCode, version, icfSigned: true, icfSignedDate: clock.UtcNow,
                requiresReconsent: false, isPaperOverride: false, clock);
            await consents.AddAsync(consent, context.CancellationToken);
            await auditLog.WriteAsync("subject_consents", consent.Id.ToString(), "INSERT",
                "system-pubsub", "Automated consent-completion enrollment", oldValues: null, newValues: null, context.CancellationToken);
        }

        await subjects.SaveChangesAsync(context.CancellationToken); // one transaction, both writes, outbox-committed
    }
}
```

`MassTransit` is configured with `AddEntityFrameworkOutbox<ExecutionDbContext>()` so publishing `EConsentCompletedEvent` (from wherever eConsent-completion is actually detected upstream — out of this slice's scope, same boundary the spec itself draws around `econsent_client.py`) and consuming it here are both covered by the same transactional-outbox guarantee ADR-2428 mandates platform-wide, closing MIG-06's "no delivery durability" finding. At-least-once delivery is safe because every write above is already idempotent (existing-subject skip; consent-cache existence check) — re-delivery of the same event is a no-op, matching the legacy contract's own idempotency guarantee, not a new behavior.

**Process-lifecycle equivalence.** The Python subscriber's three silent-no-start conditions (running under pytest, `TESTING=true`, unset `REDIS_HOST`) have no MassTransit analogue to preserve: a consumer is either registered in the bus configuration or it isn't, and test isolation is achieved via `ITestHarness`/in-memory transport (§6.2) rather than an environment-variable gate. This is a deliberate simplification, not a gap — the legacy conditions existed only to prevent a real Redis connection during unit tests, a problem the .NET test harness solves structurally instead.

---

## 6. Golden Master Parity Verification Plan

### 6.1 Fixture-driven xUnit harness

```csharp
namespace Cadence.UnitTests.Execution.Subjects;

public sealed class SubjectGoldenMasterTests : IClassFixture<ExecutionApiFactory>
{
    private readonly ExecutionApiFactory _factory;
    public SubjectGoldenMasterTests(ExecutionApiFactory factory) => _factory = factory;

    public static IEnumerable<object[]> NominalScenarios() =>
        GoldenMasterFixtureLoader.LoadScenarios(
            "docs/migration/slices/execution-subjects/fixtures/input_valid_nominal.json",
            "docs/migration/slices/execution-subjects/fixtures/output_expected_nominal.json")
            .Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(NominalScenarios))]
    public async Task NominalScenario_MatchesGoldenMaster(GoldenMasterScenario scenario)
    {
        using var client = _factory.CreateClient();
        var response = await scenario.SendAsync(client);

        response.StatusCode.Should().Be((HttpStatusCode)scenario.ExpectedStatus);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        GoldenMasterAssert.BodyMatches(body, scenario.ExpectedBody); // handles "<opaque>"/"<unchanged>"/"<opaque-non-null>" sentinels
        if (scenario.DbAssertions is not null)
            await GoldenMasterAssert.DbStateMatches(_factory.Services, scenario);
        if (scenario.AuditAssertions is not null)
            await GoldenMasterAssert.AuditLogContains(_factory.Services, scenario);
    }

    public static IEnumerable<object[]> EdgeCaseScenarios() => GoldenMasterFixtureLoader.LoadScenarios(
        "docs/migration/slices/execution-subjects/fixtures/input_valid_edge_cases.json",
        "docs/migration/slices/execution-subjects/fixtures/output_expected_nominal.json").Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(EdgeCaseScenarios))]
    public Task EdgeCaseScenario_MatchesGoldenMaster(GoldenMasterScenario scenario) => NominalScenario_MatchesGoldenMaster(scenario);

    public static IEnumerable<object[]> ErrorScenarios() => GoldenMasterFixtureLoader.LoadScenarios(
        "docs/migration/slices/execution-subjects/fixtures/input_invalid_validation_errors.json",
        "docs/migration/slices/execution-subjects/fixtures/output_expected_errors.json").Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(ErrorScenarios))]
    public async Task ErrorScenario_MatchesExpectedStatusAndDetail(GoldenMasterScenario scenario)
    {
        using var client = _factory.CreateClient();
        var response = await scenario.SendAsync(client);
        response.StatusCode.Should().Be((HttpStatusCode)scenario.ExpectedStatus);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Be(scenario.ExpectedDetail);
    }
}
```

Every `scenario_id` in the fixtures (`SCN-001-A` … `SCN-010`) becomes one `[Theory]` case; `GoldenMasterFixtureLoader` joins `input_*`/`output_*` files by `scenario_id` and fails fast at test-collection time if a scenario is present on one side but not the other, so an incomplete fixture pair cannot silently pass by being skipped.

### 6.2 Unit-level coverage beyond the HTTP fixtures

| Concern                                                                                | Test approach                                                                                                                                                              |
| -------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SubjectTransitionGraph.Guard` full transition matrix (SCN-003)                        | `[Theory]`/`[InlineData]` over every `(current, target)` pair in `01_spec.md` §2.1's mermaid graph, both allowed and forbidden                                             |
| `CalculateAge` / `365.25` divisor parity (Rule 7)                                      | `[Theory]` fixed input/expected-output pairs lifted directly from Python `calculate_age` doctest-equivalent behavior, including the two date-string-parsing fallback paths |
| `normalize_gender` full mapping table (Rule 7)                                         | `[Theory]` over every row in the Rule 7 table, both `preserve_custom` values                                                                                               |
| `EConsentCompletedConsumer` idempotency + independent consent-cache backfill (SCN-009) | `MassTransit.Testing.ITestHarness`, asserting zero duplicate subjects on redelivery and independent backfill when the subject already exists but the consent row doesn't   |
| `SubjectConsent.MergeResubmission` field-by-field precedence (Rule 9)                  | `[Theory]` covering each of the four field categories (unconditional replace / date-conditional / protocol-version-conditional); since #4824, also the signed-without-date rejection |
| `GxPAuditInterceptor` Merkle chain integrity (§4.4)                                    | Two sequential `SaveChangesAsync` calls on the same entity type assert `BlockHash[n].PredecessorHash == BlockHash[n-1].BlockHash`                                          |

### 6.3 Integration test strategy

`WebApplicationFactory<Program>` (`ExecutionApiFactory`, referenced above) backed by `Testcontainers.PostgreSql` (already declared) — one container per test collection, migrations applied once, each test class runs inside a transaction rolled back on disposal for isolation. The Rule 5 concurrency race (sequential enrollment index under concurrent load) gets one dedicated integration test firing N concurrent `CreateAsync` calls for the same study and asserting the resulting `enrollment_index` set is exactly `{0, 1, ..., N-1}` with no duplicates or gaps — this is the one scenario a fixture-replay test cannot exercise (fixtures are sequential-request, not concurrent-load, by construction) and is the direct regression test for the trap the spec calls out twice (once for `SubjectLifecycleService.enroll_subject`, once for `consent_subscriber.py`).

---

## 7. Open Items Carried Forward (not blocking this design, tracked explicitly)

1. **`SubjectResponse` full population** (§2.6) — deliberately deferred; tracked as a follow-up hardening ticket once golden-master parity ships.
2. **`GxPAuditInterceptor` generalization to `AggregateRootBase`** (§4.4, item 1) and **Merkle-chained `AuditLogEntry` writes** (§4.4, item 2) — shared-kernel prerequisites, not scoped to this slice alone; must land in `packages/database` and `packages/engine_core` before or alongside this slice's implementation PR.
3. **Rule 4's `ReConsentRequiredException`/423 path** is defined in this slice's `Domain.Subjects.SubjectErrors` but consumed by the amendments-validation router, which belongs to a different slice's technical design — this document specifies the error contract only, not the consuming endpoint.
