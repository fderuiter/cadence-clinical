using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// Projects canonical kernel truth at a bitemporal coordinate <c>(validAt, knownAt)</c> for streaming study exports and
/// participant casebooks (K-TRUTH-1, K-LIN-3). Never accesses <c>IdentityVault</c>; only pseudonymous kernel identifiers
/// and <see cref="SubjectCode"/> values are read.
/// </summary>
public interface IKernelExportReader
{
    /// <summary>
    /// Reads the bitemporal snapshot of <paramref name="study"/> at <c>(validAt, knownAt)</c>, or null when the study
    /// does not exist in <paramref name="tenant"/>.
    /// </summary>
    Task<StudyExportSnapshot?> ReadStudySnapshotAsync(
        TenantId tenant,
        StudyId study,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the bitemporal participant casebook of <paramref name="enrollment"/> at <c>(validAt, knownAt)</c>, or null
    /// when the enrollment does not exist in <paramref name="tenant"/>.
    /// </summary>
    Task<EnrollmentCasebookSnapshot?> ReadEnrollmentCasebookAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken);
}

/// <summary>Canonical study snapshot projected at a <c>(validAt, knownAt)</c> bitemporal cut.</summary>
public sealed record StudyExportSnapshot(
    StudyId StudyId,
    string StudyCode,
    string StudyTitle,
    string StudyPhase,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<ExportProtocolSnapshot> ProtocolVersions,
    IReadOnlyList<ExportSiteSnapshot> StudySites,
    IReadOnlyList<ExportEnrollmentSnapshot> Enrollments,
    IReadOnlyList<ExportCollectionSnapshot> Collections,
    IReadOnlyList<ExportObservationSnapshot> Observations,
    IReadOnlyList<StudyLockEntry> Locks,
    IReadOnlyList<ExportAttestationSnapshot> Attestations,
    IReadOnlyList<ObservationAuditView> AuditEntries);

/// <summary>Canonical participant casebook projected at a <c>(validAt, knownAt)</c> bitemporal cut.</summary>
public sealed record EnrollmentCasebookSnapshot(
    StudyId StudyId,
    string StudyCode,
    string StudyTitle,
    StudySiteId StudySiteId,
    string SiteCode,
    string SiteName,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    string SubjectCode,
    EnrollmentStatus? StatusAtCut,
    int? EnrollmentNumber,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<ExportProtocolSnapshot> ProtocolVersions,
    IReadOnlyList<ExportCollectionSnapshot> Collections,
    IReadOnlyList<ExportObservationSnapshot> Observations,
    IReadOnlyList<AssertionView> Assertions,
    IReadOnlyList<StudyLockEntry> Locks,
    IReadOnlyList<ExportAttestationSnapshot> Attestations,
    IReadOnlyList<ObservationAuditView> AuditEntries);

/// <summary>Protocol version metadata and compiled ODM XML snapshot when available.</summary>
public sealed record ExportProtocolSnapshot(
    ProtocolVersionId ProtocolVersionId,
    int Number,
    ProtocolVersionStatus Status,
    ProtocolDesign? Design,
    string RawContent,
    string? ContentHash,
    string? CompiledOdmXml);

/// <summary>Study site metadata within a study export.</summary>
public sealed record ExportSiteSnapshot(
    StudySiteId StudySiteId,
    SiteId SiteId,
    string SiteCode,
    string SiteName,
    string Country,
    string Status);

/// <summary>Participant enrollment metadata within a study export (excluding sealed direct identifiers).</summary>
public sealed record ExportEnrollmentSnapshot(
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudySiteId StudySiteId,
    string SiteCode,
    string SubjectCode,
    EnrollmentStatus StatusAtCut,
    int? EnrollmentNumber);

/// <summary>Form / collection instance metadata within a study or casebook export.</summary>
public sealed record ExportCollectionSnapshot(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string CollectionSpecName,
    string? PlannedActivityId,
    string? PlannedActivityName,
    DateTimeOffset CollectedAt,
    DateTimeOffset OpenedAt,
    string OpenedBy,
    string? ContentHashAtCut,
    IReadOnlyDictionary<ObservationId, string> ItemIdsByObservation);

/// <summary>Observation state, revision history, lock coverage, and blinding metadata at the bitemporal cut.</summary>
public sealed record ExportObservationSnapshot(
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudySiteId StudySiteId,
    CollectionInstanceId? CollectionInstanceId,
    string? CollectionSpecId,
    string? PlannedActivityId,
    string? ItemId,
    ConceptRef Concept,
    string ConceptCode,
    string ConceptLabel,
    bool IsBlindedTreatment,
    DateTimeOffset ObservedAt,
    ObservationRevisionView CurrentRevision,
    IReadOnlyList<ObservationRevisionView> Revisions,
    LockKind? EffectiveLock,
    IReadOnlyList<AssertionView> Assertions);

/// <summary>Electronic signature (<c>Attestation</c>) recorded on or before <c>knownAt</c>.</summary>
public sealed record ExportAttestationSnapshot(
    AttestationId AttestationId,
    string ActorId,
    AttestationMeaning Meaning,
    string TargetKind,
    Guid TargetId,
    string TargetContentHash,
    DateTimeOffset AuthenticatedAt,
    string Method,
    DateTimeOffset RecordedAt,
    string Reason);
