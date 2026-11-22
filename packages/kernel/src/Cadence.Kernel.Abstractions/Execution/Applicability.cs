using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Execution;

/// <summary>The participant's decision on one consent scope (spec §9.4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConsentDecision>))]
public enum ConsentDecision
{
    Granted,
    Declined,
}

/// <summary>How consent was evidenced in the trial world (spec §9.4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConsentBasisKind>))]
public enum ConsentBasisKind
{
    ElectronicAttestation,
    PaperSourceDocument,
}

/// <summary>Standard consent purposes recognised by computable consent (spec §9.4).</summary>
public static class ConsentPurposes
{
    public const string MainStudy = "MainStudy";
    public const string FutureResearch = "FutureResearch";
    public const string GeneticTesting = "GeneticTesting";
    public const string CommercialResearch = "CommercialResearch";
    public const string SpecimenStorage = "SpecimenStorage";
    public const string InternationalTransfer = "InternationalTransfer";
    public const string Contact = "Contact";
}

/// <summary>One purpose-scoped grant or decline inside a <see cref="ConsentStatusState"/> (spec §9.4).</summary>
public sealed record ConsentScopeEntry(
    string Purpose,
    ConsentDecision Decision,
    string? Conditions = null);

/// <summary>
/// A protocol amendment transition held until the participant signs consent covering <c>TargetVersionId</c>
/// (spec §7.5, PRD-SUB-007).
/// </summary>
public sealed record PendingReconsentTransition(
    ProtocolVersionId? FromVersionId,
    ProtocolVersionId TargetVersionId,
    int TargetVersionNumber,
    EffectivityId EffectivityId,
    DateTimeOffset RequiredSince);

/// <summary>
/// The resolved governing protocol version and re-consent/inconsistency state for an enrollment at a
/// <c>(validAt, knownAt)</c> point (spec §7.5, K-TIME-6, K-TIME-8).
/// </summary>
public sealed record ApplicabilityResolution(
    ApplicabilityId? ApplicabilityId,
    ProtocolVersionId? ProtocolVersionId,
    int? ProtocolVersionNumber,
    DateTimeOffset? ValidFrom,
    bool IsInconsistent,
    string? InconsistencyReason,
    IReadOnlyList<PendingReconsentTransition> PendingTransitions)
{
    public static ApplicabilityResolution Empty { get; } =
        new(null, null, null, null, false, null, []);

    public bool RequiresReconsent => PendingTransitions.Count > 0;
}

/// <summary>Current bitemporal applicability state of an enrollment after a command.</summary>
public sealed record ApplicabilityState(
    ApplicabilityId ApplicabilityId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId? GoverningVersionId,
    int? GoverningVersionNumber,
    DateTimeOffset? ValidFrom,
    bool IsInconsistent,
    string? InconsistencyReason,
    IReadOnlyList<PendingReconsentTransition> PendingTransitions)
{
    public bool RequiresReconsent => PendingTransitions.Count > 0;
}

/// <summary>State returned after recording or updating bitemporal consent for an enrollment (spec §9.4).</summary>
public sealed record ConsentStatusState(
    ConsentStatusId ConsentStatusId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    int ProtocolVersionNumber,
    bool Signed,
    DateTimeOffset SignedAt,
    ConsentBasisKind BasisKind,
    AttestationId? AttestationId,
    ArtifactVersionId? ArtifactVersionId,
    IReadOnlyList<ConsentScopeEntry> Scopes,
    ApplicabilityState? Applicability);

/// <summary>Reads bitemporal <c>Applicability</c> for enrollments in a tenant (spec §7.5, K-TIME-6, K-TIME-8).</summary>
public interface IApplicabilityReader
{
    Task<ApplicabilityResolution> ResolveAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default);

    Task<ApplicabilityState?> CurrentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads bitemporal <c>ConsentStatus</c> records for enrollments in a tenant (spec §9.4).</summary>
public interface IConsentStatusReader
{
    Task<IReadOnlyList<ConsentStatusState>> HistoryAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default);
}

/// <summary>Errors raised when recording consent or resolving applicability (spec §7.5, §9.4, PRD-SUB-007).</summary>
public static class ConsentErrors
{
    public static readonly KernelError EnrollmentUnknown =
        new("K.Consent.EnrollmentUnknown", 404, "The enrollment does not exist.");

    public static readonly KernelError ProtocolVersionUnknown =
        new("K.Consent.ProtocolVersionUnknown", 404, "The protocol version does not exist in the enrollment's study.");

    public static readonly KernelError SignedAtInvalid =
        new("K.Consent.SignedAtInvalid", 422, "Consent cannot be signed after it is recorded.");

    public static readonly KernelError ScopesInvalid =
        new("K.Consent.ScopesInvalid", 422, "Every consent scope needs a non-empty purpose.");
}
