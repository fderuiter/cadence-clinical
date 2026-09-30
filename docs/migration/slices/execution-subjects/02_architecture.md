# Target .NET 10 Architecture: Subject Lifecycle & Enrollment

**Slice:** `execution-subjects`
**Target Runtime:** .NET 10 (`net10.0`) / C# 14
**Project Assembly:** `Cadence.Apps.Execution` (`apps/execution/src/`)
**Shared Assembly Dependencies:**

- `Cadence.Core.Domain` (`packages/engine_core/src/`)
- `Cadence.Contracts` (`packages/contracts/src/`)
- `Cadence.Security` (`packages/security/src/`)

---

## 1. Architectural Topology & Clean Architecture Seams

The .NET 10 target implementation adheres to **Single-Assembly Clean Architecture** within `Cadence.Apps.Execution`, structured into explicit directory namespaces:

```
apps/execution/src/
├── Domain/
│   ├── Enums/
│   │   └── SubjectState.cs
│   ├── Exceptions/
│   │   ├── InvalidStateTransitionException.cs
│   │   ├── LockedFactorMutationException.cs
│   │   ├── ReConsentRequiredException.cs
│   │   ├── SiteNotCompliantException.cs
│   │   └── SubjectNotFoundException.cs
│   ├── Models/
│   │   ├── ClinicalSubject.cs
│   │   ├── SubjectConsent.cs
│   │   ├── SubjectIdentifiers.cs
│   │   └── StratificationFactors.cs
│   ├── Rules/
│   │   └── SubjectStateMachine.cs
│   └── Ports/
│       ├── ISubjectRepository.cs
│       ├── IConsentRepository.cs
│       ├── ISiteComplianceService.cs
│       └── IDemographicsEncryptionService.cs
├── Application/
│   ├── Commands/
│   │   ├── EnrollSubjectCommand.cs
│   │   ├── RecordConsentCommand.cs
│   │   ├── AdvanceSubjectStateCommand.cs
│   │   └── UpdateDemographicsCommand.cs
│   ├── DTOs/
│   │   ├── SubjectDtos.cs
│   │   └── ConsentDtos.cs
│   └── Services/
│       └── SubjectLifecycleService.cs
├── Infrastructure/
│   ├── Cryptography/
│   │   └── AesGcmDemographicsEncryptionService.cs
│   ├── Persistence/
│   │   ├── ExecutionDbContext.cs
│   │   ├── Configurations/
│   │   │   ├── ClinicalSubjectConfiguration.cs
│   │   │   └── SubjectConsentConfiguration.cs
│   │   └── Repositories/
│   │       ├── EfSubjectRepository.cs
│   │       └── EfConsentRepository.cs
│   └── External/
│       └── HttpSiteComplianceService.cs
└── Endpoints/
    ├── Filters/
    │   └── ChangeJustificationFilter.cs
    └── SubjectEndpoints.cs
```

---

## 2. Domain Primitives & Strongly-Typed Identifiers

```csharp
// 1. System usings
using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Models;

/// <summary>
/// Strongly-typed identifier for a clinical subject domain code (e.g., "SUBJ-001").
/// @req:PRD-SYS-053, PRD-SUB-001
/// </summary>
public readonly record struct SubjectCode(string Value)
{
    public override string ToString() => Value;
    public static implicit operator string(SubjectCode code) => code.Value;
    public static explicit operator SubjectCode(string value) => new(value);
}

/// <summary>
/// Strongly-typed identifier for clinical study identification.
/// @req:PRD-SYS-053
/// </summary>
public readonly record struct StudyId(string Value)
{
    public override string ToString() => Value;
    public static implicit operator string(StudyId id) => id.Value;
    public static explicit operator StudyId(string value) => new(value);
}

/// <summary>
/// Strongly-typed identifier for clinical trial sites.
/// @req:PRD-SYS-053
/// </summary>
public readonly record struct SiteId(string Value)
{
    public override string ToString() => Value;
    public static implicit operator string(SiteId id) => id.Value;
    public static explicit operator SiteId(string value) => new(value);
}
```

---

## 3. Regulated State Machine & Domain Rules

```csharp
// 1. System usings
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

// 2. First-party Cadence usings
using Cadence.Apps.Execution.Domain.Exceptions;

namespace Cadence.Apps.Execution.Domain.Enums;

/// <summary>
/// Regulated clinical subject lifecycle states under 21 CFR Part 11 and ICH GCP.
/// @req:PRD-SYS-042, PRD-SUB-001
/// </summary>
public enum SubjectState
{
    Screening,
    ScreenFailed,
    Enrolled,
    Randomized,
    Active,
    Completed,
    Unblinded,
    Withdrawn,
    ReconsentRequired
}

namespace Cadence.Apps.Execution.Domain.Rules;

/// <summary>
/// Pure domain state machine defining and guarding legal subject progression pathways.
/// @req:PRD-SYS-042, PRD-SUB-001
/// </summary>
public static class SubjectStateMachine
{
    private static readonly FrozenDictionary<SubjectState, FrozenSet<SubjectState>> AllowedTransitions =
        new Dictionary<SubjectState, FrozenSet<SubjectState>>
        {
            [SubjectState.Screening] = new[]
            {
                SubjectState.ScreenFailed,
                SubjectState.Enrolled,
                SubjectState.Withdrawn,
                SubjectState.ReconsentRequired
            }.ToFrozenSet(),

            [SubjectState.Enrolled] = new[]
            {
                SubjectState.Randomized,
                SubjectState.Withdrawn,
                SubjectState.ReconsentRequired
            }.ToFrozenSet(),

            [SubjectState.Randomized] = new[]
            {
                SubjectState.Active,
                SubjectState.Withdrawn,
                SubjectState.Unblinded,
                SubjectState.ReconsentRequired
            }.ToFrozenSet(),

            [SubjectState.Active] = new[]
            {
                SubjectState.Completed,
                SubjectState.Withdrawn,
                SubjectState.Unblinded,
                SubjectState.ReconsentRequired
            }.ToFrozenSet(),

            [SubjectState.Unblinded] = new[]
            {
                SubjectState.Withdrawn,
                SubjectState.Completed,
                SubjectState.ReconsentRequired
            }.ToFrozenSet(),

            [SubjectState.ReconsentRequired] = new[]
            {
                SubjectState.Screening,
                SubjectState.Enrolled,
                SubjectState.Randomized,
                SubjectState.Active,
                SubjectState.Completed,
                SubjectState.Withdrawn,
                SubjectState.Unblinded
            }.ToFrozenSet(),

            [SubjectState.ScreenFailed] = FrozenSet<SubjectState>.Empty,
            [SubjectState.Completed] = FrozenSet<SubjectState>.Empty,
            [SubjectState.Withdrawn] = FrozenSet<SubjectState>.Empty
        }.ToFrozenDictionary();

    public static bool CanTransition(SubjectState current, SubjectState target)
    {
        if (current == target) return true;
        return AllowedTransitions.TryGetValue(current, out var nextStates) && nextStates.Contains(target);
    }

    public static void EnsureValidTransition(SubjectState current, SubjectState target)
    {
        if (!CanTransition(current, target))
        {
            throw new InvalidStateTransitionException(current, target);
        }
    }

    public static bool IsLockedPostRandomization(SubjectState state) =>
        state is SubjectState.Randomized
              or SubjectState.Active
              or SubjectState.Completed
              or SubjectState.Unblinded
              or SubjectState.Withdrawn;
}
```

---

## 4. Sealed Domain Aggregate Roots & Entities

```csharp
// 1. System usings
using System;
using System.Collections.Generic;

// 2. First-party Cadence usings
using Cadence.Apps.Execution.Domain.Enums;
using Cadence.Apps.Execution.Domain.Exceptions;
using Cadence.Apps.Execution.Domain.Rules;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Models;

/// <summary>
/// Pseudonymized clinical subject aggregate root enforcing 21 CFR Part 11 audit trails and state machine guards.
/// @req:PRD-SYS-001, PRD-SYS-042, PRD-SYS-053, PRD-SUB-001
/// </summary>
public sealed class ClinicalSubject : AggregateRoot<string>
{
    public string SubjectCode { get; private set; } = string.Empty;
    public string StudyId { get; private set; } = string.Empty;
    public string? SiteId { get; private set; }
    public string? TreatmentGroup { get; private set; }
    public SubjectState Status { get; private set; } = SubjectState.Screening;
    public int EnrollmentIndex { get; private set; }
    public string? EncryptedDemographics { get; private set; }
    public Dictionary<string, object>? StratificationFactors { get; private set; }
    public string ActiveProtocolVersion { get; private set; } = "1.0.0";

    // Unblinding & Safety Details
    public bool IsUnblinded { get; private set; }
    public DateTimeOffset? UnblindedAt { get; private set; }
    public string? UnblindedBy { get; private set; }
    public string? UnblindedReason { get; private set; }

    // Discontinuation Details
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public string? WithdrawalReason { get; private set; }

    // RTSM Allocation Details
    public string? RandomizationId { get; private set; }
    public string? KitReference { get; private set; }

    // EF Core parameterless constructor
    private ClinicalSubject() : base() { }

    public static ClinicalSubject Create(
        string subjectCode,
        string studyId,
        string? siteId,
        string? treatmentGroup,
        int enrollmentIndex,
        string? encryptedDemographics,
        string createdBy,
        string reasonForChange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);

        var subject = new ClinicalSubject
        {
            Id = Guid.NewGuid().ToString(),
            SubjectCode = subjectCode,
            StudyId = studyId,
            SiteId = siteId,
            TreatmentGroup = treatmentGroup,
            EnrollmentIndex = enrollmentIndex,
            EncryptedDemographics = encryptedDemographics,
            Status = SubjectState.Screening,
            CreatedBy = createdBy,
            ReasonForChange = reasonForChange,
            CreatedAt = DateTime.UtcNow,
            VersionIndex = 1
        };

        return subject;
    }

    public void AdvanceState(SubjectState targetState, string modifiedBy, string justification)
    {
        SubjectStateMachine.EnsureValidTransition(Status, targetState);
        Status = targetState;
        ReasonForChange = justification;
        VersionIndex++;
    }

    public void UpdateDemographics(string encryptedDemographics, string modifiedBy, string justification)
    {
        if (SubjectStateMachine.IsLockedPostRandomization(Status))
        {
            throw new LockedFactorMutationException("Demographics cannot be modified post-randomization.");
        }

        EncryptedDemographics = encryptedDemographics;
        ReasonForChange = justification;
        VersionIndex++;
    }

    public void UpdateStratificationFactors(Dictionary<string, object> factors, string modifiedBy, string justification)
    {
        if (SubjectStateMachine.IsLockedPostRandomization(Status))
        {
            throw new LockedFactorMutationException("Stratification factors are immutable for randomized subjects.");
        }

        StratificationFactors = factors;
        ReasonForChange = justification;
        VersionIndex++;
    }

    public void ClearDemographicsPreRandomization(string modifiedBy, string justification)
    {
        if (SubjectStateMachine.IsLockedPostRandomization(Status))
        {
            throw new InvalidOperationException("SOFT_DELETE_BLOCKED: Cannot delete demographics post-randomization.");
        }

        EncryptedDemographics = null;
        StratificationFactors = null;
        ReasonForChange = justification;
        VersionIndex++;
    }

    public void Unblind(string unblindedBy, string reason)
    {
        AdvanceState(SubjectState.Unblinded, unblindedBy, reason);
        IsUnblinded = true;
        UnblindedAt = DateTimeOffset.UtcNow;
        UnblindedBy = unblindedBy;
        UnblindedReason = reason;
    }

    public void Withdraw(string withdrawnBy, string reason)
    {
        AdvanceState(SubjectState.Withdrawn, withdrawnBy, reason);
        WithdrawnAt = DateTimeOffset.UtcNow;
        WithdrawalReason = reason;
    }

    public void AssignRandomization(string randomizationId, string kitReference, Dictionary<string, object> factors, string modifiedBy, string reason)
    {
        AdvanceState(SubjectState.Randomized, modifiedBy, reason);
        RandomizationId = randomizationId;
        KitReference = kitReference;
        StratificationFactors = factors;
    }

    public void AdvanceProtocolVersion(string newVersion)
    {
        ActiveProtocolVersion = newVersion;
        VersionIndex++;
    }
}
```

> [!NOTE]
> **Superseded by #4824 (2026-09-30).** The `IcfSignedDate = icfSignedDate ?? DateTimeOffset.UtcNow` default below is not implemented. A signed consent requires its signed date; see `02_technical_design.md` §2.4 for the current aggregate.

```csharp
// 1. System usings
using System;

// 2. First-party Cadence usings
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Models;

/// <summary>
/// Represents a subject informed consent record bound to a protocol version.
/// @req:PRD-SYS-001, PRD-SUB-007
/// </summary>
public sealed class SubjectConsent : AggregateRoot<string>
{
    public string SubjectCode { get; private set; } = string.Empty;
    public string StudyId { get; private set; } = string.Empty;
    public string? SiteId { get; private set; }
    public string VersionTag { get; private set; } = string.Empty;
    public int ProtocolVersionIndex { get; private set; } = 1;
    public string? ProtocolVersion { get; private set; }
    public string Status { get; private set; } = "SIGNED";
    public bool IcfSigned { get; private set; }
    public DateTimeOffset? IcfSignedDate { get; private set; }
    public bool RequiresReconsent { get; private set; }
    public bool IsPaperOverride { get; private set; }

    private SubjectConsent() : base() { }

    public static SubjectConsent Create(
        string subjectCode,
        string studyId,
        string? siteId,
        string versionTag,
        int protocolVersionIndex,
        string? protocolVersion,
        bool icfSigned,
        DateTimeOffset? icfSignedDate,
        bool requiresReconsent,
        bool isPaperOverride,
        string createdBy,
        string reason)
    {
        return new SubjectConsent
        {
            Id = Guid.NewGuid().ToString(),
            SubjectCode = subjectCode,
            StudyId = studyId,
            SiteId = siteId,
            VersionTag = versionTag,
            ProtocolVersionIndex = protocolVersionIndex,
            ProtocolVersion = protocolVersion,
            IcfSigned = icfSigned,
            IcfSignedDate = icfSignedDate ?? DateTimeOffset.UtcNow,
            RequiresReconsent = requiresReconsent,
            IsPaperOverride = isPaperOverride,
            CreatedBy = createdBy,
            ReasonForChange = reason,
            CreatedAt = DateTime.UtcNow,
            VersionIndex = 1
        };
    }

    public void UpdateConsent(
        string versionTag,
        bool icfSigned,
        DateTimeOffset? icfSignedDate,
        bool requiresReconsent,
        bool isPaperOverride,
        string? protocolVersion,
        string reason)
    {
        VersionTag = versionTag;
        IcfSigned = icfSigned;
        if (icfSignedDate.HasValue) IcfSignedDate = icfSignedDate.Value;
        RequiresReconsent = requiresReconsent;
        IsPaperOverride = isPaperOverride;
        if (!string.IsNullOrWhiteSpace(protocolVersion)) ProtocolVersion = protocolVersion;
        ReasonForChange = reason;
        VersionIndex++;
    }

    public void ClearReconsentRequirement()
    {
        if (RequiresReconsent)
        {
            RequiresReconsent = false;
            VersionIndex++;
        }
    }
}
```

---

## 5. Domain Ports (Dependency Inversion)

```csharp
// 1. System usings
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 2. First-party Cadence usings
using Cadence.Apps.Execution.Domain.Models;

namespace Cadence.Apps.Execution.Domain.Ports;

public interface ISubjectRepository
{
    Task<ClinicalSubject?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<ClinicalSubject?> GetBySubjectCodeAsync(string subjectCode, string studyId, CancellationToken ct = default);
    Task<int> GetMaxEnrollmentIndexAsync(string studyId, CancellationToken ct = default);
    Task<IReadOnlyList<ClinicalSubject>> ListByStudyAsync(string studyId, string? siteId = null, CancellationToken ct = default);
    Task<ClinicalSubject> SaveAsync(ClinicalSubject subject, CancellationToken ct = default);
}

public interface IConsentRepository
{
    Task<SubjectConsent?> GetSubjectConsentAsync(string subjectCode, string studyId, int versionIndex, CancellationToken ct = default);
    Task<IReadOnlyList<SubjectConsent>> ListSubjectConsentsAsync(string subjectCode, string studyId, CancellationToken ct = default);
    Task<SubjectConsent> SaveAsync(SubjectConsent consent, CancellationToken ct = default);
    Task ClearReconsentOnOtherConsentsAsync(string subjectCode, string studyId, string currentConsentId, CancellationToken ct = default);
}

public interface ISiteComplianceService
{
    Task<bool> IsSiteActiveAndCompliantAsync(string studyId, string? siteId, CancellationToken ct = default);
}

public interface IDemographicsEncryptionService
{
    string Encrypt<T>(T payload);
    T? Decrypt<T>(string ciphertext);
}
```

---

## 6. Application Service & DTOs

```csharp
// 1. System usings
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.DTOs;

public record SubjectCreateRequest(
    string SubjectId,
    string StudyId,
    string? SiteId = null,
    string? ArmId = null,
    DemographicsDto? Demographics = null);

public record DemographicsDto(
    string? Name = null,
    string? Birthdate = null,
    string? Gender = null,
    string? Race = null);

public record SubjectResponse(
    string Id,
    string SubjectId,
    string StudyId,
    string? SiteId,
    string? TreatmentGroup,
    string Status,
    string ActiveProtocolVersion,
    string? EncryptedDemographics,
    long VersionIndex);

public record SubjectStateUpdateRequest(
    string? Status,
    string? State);

public record SubjectDemographicsUpdateRequest(
    DemographicsDto? Demographics,
    Dictionary<string, object>? StratFactors);

public record ProtocolVersionRef(
    string StudyId,
    string VersionTag,
    int VersionIndex);

public record SubjectConsentRequest(
    ProtocolVersionRef ProtocolVersion,
    bool IcfSigned,
    DateTimeOffset? IcfSignedDate,
    bool RequiresReconsent = false,
    bool IsPaperOverride = false);

public record SubjectConsentResponse(
    string Id,
    string SubjectId,
    string StudyId,
    string VersionTag,
    int VersionIndex,
    bool IcfSigned,
    DateTimeOffset? IcfSignedDate,
    bool RequiresReconsent,
    bool IsPaperOverride,
    long Version);
```

```csharp
// 1. System usings
using System;
using System.Threading;
using System.Threading.Tasks;

// 2. First-party Cadence usings
using Cadence.Apps.Execution.Application.DTOs;
using Cadence.Apps.Execution.Domain.Enums;
using Cadence.Apps.Execution.Domain.Exceptions;
using Cadence.Apps.Execution.Domain.Models;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Services;

/// <summary>
/// Application service coordinating Subject Lifecycle, Consent, and Demographics workflows.
/// @req:PRD-SYS-001, PRD-SYS-042, PRD-SUB-001
/// </summary>
public sealed class SubjectLifecycleService(
    ISubjectRepository subjectRepository,
    IConsentRepository consentRepository,
    ISiteComplianceService siteComplianceService,
    IDemographicsEncryptionService encryptionService)
{
    public async Task<Result<SubjectResponse>> EnrollSubjectAsync(
        SubjectCreateRequest request,
        string userId,
        string justification,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectId) || string.IsNullOrWhiteSpace(request.StudyId))
        {
            return DomainError.Validation("INVALID_SUBJECT_REQUEST", "SubjectId and StudyId are required.");
        }

        string? encryptedDemo = null;
        if (request.Demographics is not null)
        {
            encryptedDemo = encryptionService.Encrypt(request.Demographics);
        }

        int maxIdx = await subjectRepository.GetMaxEnrollmentIndexAsync(request.StudyId, ct);
        int nextIdx = maxIdx < 0 ? 0 : maxIdx + 1;

        var subject = ClinicalSubject.Create(
            subjectCode: request.SubjectId,
            studyId: request.StudyId,
            siteId: request.SiteId,
            treatmentGroup: request.ArmId,
            enrollmentIndex: nextIdx,
            encryptedDemographics: encryptedDemo,
            createdBy: userId,
            reasonForChange: justification);

        var saved = await subjectRepository.SaveAsync(subject, ct);

        return new SubjectResponse(
            Id: saved.Id,
            SubjectId: saved.SubjectCode,
            StudyId: saved.StudyId,
            SiteId: saved.SiteId,
            TreatmentGroup: saved.TreatmentGroup,
            Status: saved.Status.ToString().ToUpperInvariant(),
            ActiveProtocolVersion: saved.ActiveProtocolVersion,
            EncryptedDemographics: saved.EncryptedDemographics,
            VersionIndex: saved.VersionIndex);
    }

    public async Task<Result<SubjectResponse>> AdvanceSubjectStateAsync(
        string idOrCode,
        string targetStateStr,
        string userId,
        string justification,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<SubjectState>(targetStateStr.Replace("_", ""), ignoreCase: true, out var targetState))
        {
            return DomainError.Validation("INVALID_STATE", $"Unrecognized subject state '{targetStateStr}'.");
        }

        var subject = await subjectRepository.GetByIdAsync(idOrCode, ct);
        if (subject is null)
        {
            return DomainError.NotFound("SUBJECT_NOT_FOUND", $"Subject '{idOrCode}' was not found.");
        }

        if (targetState == SubjectState.Enrolled)
        {
            bool isCompliant = await siteComplianceService.IsSiteActiveAndCompliantAsync(subject.StudyId, subject.SiteId, ct);
            if (!isCompliant)
            {
                return DomainError.Failure("SITE_NOT_COMPLIANT", $"Subject enrollment blocked: Site '{subject.SiteId}' is not compliant.");
            }
        }

        try
        {
            subject.AdvanceState(targetState, userId, justification);
            var updated = await subjectRepository.SaveAsync(subject, ct);

            return new SubjectResponse(
                Id: updated.Id,
                SubjectId: updated.SubjectCode,
                StudyId: updated.StudyId,
                SiteId: updated.SiteId,
                TreatmentGroup: updated.TreatmentGroup,
                Status: updated.Status.ToString().ToUpperInvariant(),
                ActiveProtocolVersion: updated.ActiveProtocolVersion,
                EncryptedDemographics: updated.EncryptedDemographics,
                VersionIndex: updated.VersionIndex);
        }
        catch (InvalidStateTransitionException ex)
        {
            return DomainError.Validation("INVALID_STATE_TRANSITION", ex.Message);
        }
    }

    public async Task<Result<SubjectConsentResponse>> RecordConsentAsync(
        string subjectCode,
        SubjectConsentRequest request,
        string userId,
        string justification,
        CancellationToken ct = default)
    {
        var existing = await consentRepository.GetSubjectConsentAsync(
            subjectCode,
            request.ProtocolVersion.StudyId,
            request.ProtocolVersion.VersionIndex,
            ct);

        SubjectConsent consentToSave;
        if (existing is not null)
        {
            existing.UpdateConsent(
                versionTag: request.ProtocolVersion.VersionTag,
                icfSigned: request.IcfSigned,
                icfSignedDate: request.IcfSignedDate,
                requiresReconsent: request.RequiresReconsent,
                isPaperOverride: request.IsPaperOverride,
                protocolVersion: request.ProtocolVersion.VersionTag,
                reason: justification);
            consentToSave = existing;
        }
        else
        {
            consentToSave = SubjectConsent.Create(
                subjectCode: subjectCode,
                studyId: request.ProtocolVersion.StudyId,
                siteId: null,
                versionTag: request.ProtocolVersion.VersionTag,
                protocolVersionIndex: request.ProtocolVersion.VersionIndex,
                protocolVersion: request.ProtocolVersion.VersionTag,
                icfSigned: request.IcfSigned,
                icfSignedDate: request.IcfSignedDate,
                requiresReconsent: request.RequiresReconsent,
                isPaperOverride: request.IsPaperOverride,
                createdBy: userId,
                reason: justification);
        }

        if ((request.IcfSigned || request.IsPaperOverride) && !request.RequiresReconsent)
        {
            await consentRepository.ClearReconsentOnOtherConsentsAsync(
                subjectCode,
                request.ProtocolVersion.StudyId,
                consentToSave.Id,
                ct);
        }

        var saved = await consentRepository.SaveAsync(consentToSave, ct);

        return new SubjectConsentResponse(
            Id: saved.Id,
            SubjectId: saved.SubjectCode,
            StudyId: saved.StudyId,
            VersionTag: saved.VersionTag,
            VersionIndex: saved.ProtocolVersionIndex,
            IcfSigned: saved.IcfSigned,
            IcfSignedDate: saved.IcfSignedDate,
            RequiresReconsent: saved.RequiresReconsent,
            IsPaperOverride: saved.IsPaperOverride,
            Version: saved.VersionIndex);
    }
}
```

---

## 7. EF Core 10 Entity Configurations

```csharp
// 1. System usings
using System;
using System.Collections.Generic;

// 2. Third-party usings
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// 3. First-party Cadence usings
using Cadence.Apps.Execution.Domain.Enums;
using Cadence.Apps.Execution.Domain.Models;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

public sealed class ClinicalSubjectConfiguration : IEntityTypeConfiguration<ClinicalSubject>
{
    public void Configure(EntityTypeBuilder<ClinicalSubject> builder)
    {
        builder.ToTable("clinical_subjects");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasMaxLength(36);

        builder.Property(s => s.SubjectCode).HasColumnName("subject_id").HasMaxLength(255).IsRequired();
        builder.Property(s => s.StudyId).HasColumnName("study_id").HasMaxLength(255).IsRequired();
        builder.Property(s => s.SiteId).HasColumnName("site_id").HasMaxLength(255).IsRequired(false);

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(s => s.TreatmentGroup).HasColumnName("treatment_group").HasMaxLength(255).IsRequired(false);
        builder.Property(s => s.EnrollmentIndex).HasColumnName("enrollment_index").IsRequired();
        builder.Property(s => s.EncryptedDemographics).HasColumnName("encrypted_demographics").IsRequired(false);

        builder.Property(s => s.ActiveProtocolVersion).HasColumnName("active_protocol_version").HasMaxLength(50).HasDefaultValue("1.0.0");

        // EF Core 10 JSON column mapping for stratification factors
        builder.OwnsOne(s => s.StratificationFactors, nav =>
        {
            nav.ToJson("strat_factors");
        });

        // 21 CFR Part 11 GxP Auditable fields
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasMaxLength(255).IsRequired();
        builder.Property(s => s.ReasonForChange).HasColumnName("reason_for_change").HasMaxLength(1000).IsRequired();
        builder.Property(s => s.VersionIndex).HasColumnName("version_index").IsConcurrencyToken().IsRequired();

        // Indices
        builder.HasIndex(s => new { s.StudyId, s.SubjectCode }).IsUnique();
        builder.HasIndex(s => new { s.StudyId, s.EnrollmentIndex });
        builder.HasIndex(s => s.SiteId);
    }
}
```

---

## 8. ASP.NET Core 10 Minimal API Endpoints

```csharp
// 1. System usings
using System;
using System.Threading;
using System.Threading.Tasks;

// 2. Third-party usings
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

// 3. First-party Cadence usings
using Cadence.Apps.Execution.Application.DTOs;
using Cadence.Apps.Execution.Application.Services;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Core.Domain;
using Cadence.Security.Signing;

namespace Cadence.Apps.Execution.Endpoints;

public static class SubjectEndpoints
{
    public static RouteGroupBuilder MapSubjectEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateSubject)
            .WithName("CreateSubject")
            .WithSummary("Enrolls a new clinical subject in SCREENING state.")
            .Produces<SubjectResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPatch("/{id}/state", UpdateSubjectState)
            .WithName("UpdateSubjectState")
            .WithSummary("Advances the subject lifecycle state adhering to 21 CFR Part 11.")
            .AddEndpointFilter<ChangeJustificationFilter>()
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{subjectId}/consent", RecordSubjectConsent)
            .WithName("RecordSubjectConsent")
            .WithSummary("Records an Informed Consent Form submission for a subject.")
            .Produces<SubjectConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    public static async Task<Results<Created<SubjectResponse>, BadRequest<ProblemDetails>>> CreateSubject(
        [FromBody] SubjectCreateRequest request,
        [FromHeader(Name = "X-User-Id")] string? userId,
        [FromHeader(Name = "X-Change-Reason")] string? changeReason,
        [FromServices] SubjectLifecycleService service,
        CancellationToken ct)
    {
        var uId = userId ?? "system";
        var reason = changeReason ?? "Initial Subject Enrollment";

        var result = await service.EnrollSubjectAsync(request, uId, reason, ct);

        return result.Match<Results<Created<SubjectResponse>, BadRequest<ProblemDetails>>>(
            onSuccess: subj => TypedResults.Created($"/api/v1/execution/subjects/{subj.Id}", subj),
            onFailure: err => TypedResults.BadRequest(new ProblemDetails
            {
                Title = err.Code,
                Detail = err.Description,
                Status = StatusCodes.Status400BadRequest
            }));
    }

    public static async Task<Results<Ok<SubjectResponse>, NotFound<ProblemDetails>, BadRequest<ProblemDetails>>> UpdateSubjectState(
        [FromRoute] string id,
        [FromBody] SubjectStateUpdateRequest request,
        [FromHeader(Name = "X-User-Id")] string? userId,
        [FromHeader(Name = "X-Change-Reason")] string? changeReason,
        [FromServices] SubjectLifecycleService service,
        CancellationToken ct)
    {
        var targetState = request.Status ?? request.State;
        if (string.IsNullOrWhiteSpace(targetState))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "MISSING_TARGET_STATE",
                Detail = "Either status or state must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var uId = userId ?? "system";
        var reason = changeReason ?? $"Transition to {targetState}";

        var result = await service.AdvanceSubjectStateAsync(id, targetState, uId, reason, ct);

        return result.Match<Results<Ok<SubjectResponse>, NotFound<ProblemDetails>, BadRequest<ProblemDetails>>>(
            onSuccess: updated => TypedResults.Ok(updated),
            onFailure: err => err.Type == ErrorType.NotFound
                ? TypedResults.NotFound(new ProblemDetails { Title = err.Code, Detail = err.Description, Status = StatusCodes.Status404NotFound })
                : TypedResults.BadRequest(new ProblemDetails { Title = err.Code, Detail = err.Description, Status = StatusCodes.Status400BadRequest }));
    }

    public static async Task<Results<Ok<SubjectConsentResponse>, BadRequest<ProblemDetails>>> RecordSubjectConsent(
        [FromRoute] string subjectId,
        [FromBody] SubjectConsentRequest request,
        [FromHeader(Name = "X-User-Id")] string? userId,
        [FromHeader(Name = "X-Change-Reason")] string? changeReason,
        [FromServices] SubjectLifecycleService service,
        CancellationToken ct)
    {
        var uId = userId ?? "system";
        var reason = changeReason ?? "Record Subject Consent";

        var result = await service.RecordConsentAsync(subjectId, request, uId, reason, ct);

        return result.Match<Results<Ok<SubjectConsentResponse>, BadRequest<ProblemDetails>>>(
            onSuccess: consent => TypedResults.Ok(consent),
            onFailure: err => TypedResults.BadRequest(new ProblemDetails
            {
                Title = err.Code,
                Detail = err.Description,
                Status = StatusCodes.Status400BadRequest
            }));
    }
}
```
