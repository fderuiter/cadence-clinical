using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Api;

/// <summary>Streamed JSON response shape for <c>GET /api/v1/kernel/enrollments/{enrollmentRef}/casebook</c>.</summary>
/// <param name="Producer">Component that generated the certified casebook projection.</param>
/// <param name="GeneratedAt">Server timestamp from <see cref="IClock"/> when the casebook was projected.</param>
/// <param name="RequestedBy">Authenticated caller identifier.</param>
/// <param name="ValidAt">Evaluated valid-time coordinate of the bitemporal snapshot.</param>
/// <param name="KnownAt">Evaluated recorded-time coordinate of the bitemporal snapshot.</param>
/// <param name="CasebookDigestSha256">Canonical SHA-256 digest over the projected casebook contents.</param>
/// <param name="StudyRef">Study NodeRef.</param>
/// <param name="StudyCode">Study code.</param>
/// <param name="StudyTitle">Study title.</param>
/// <param name="SiteRef">Study site NodeRef, or <c>masked</c> when hidden.</param>
/// <param name="SiteCode">Site code, or <c>masked</c> when hidden.</param>
/// <param name="EnrollmentRef">Enrollment NodeRef, or <c>masked</c> when hidden.</param>
/// <param name="SubjectCode">Pseudonymous subject code (never a direct identifier), or <c>masked</c> when hidden.</param>
/// <param name="EnrollmentStatus">Enrollment status at the evaluated coordinate, or null when masked.</param>
/// <param name="Masked">True when the enrollment itself is masked for the caller.</param>
/// <param name="Mask">Why the enrollment is masked, or null when visible.</param>
/// <param name="Forms">Form instances collected for the participant.</param>
/// <param name="Observations">Recorded observations with current and superseded revisions.</param>
/// <param name="Queries">Raised queries and answers on the participant's observations.</param>
/// <param name="Verifications">Source data verifications recorded on the participant's observations.</param>
/// <param name="Locks">Locks covering the study, site, enrollment, forms or observations.</param>
/// <param name="Attestations">Electronic signatures recorded on forms and lock assertions.</param>
/// <param name="AuditTrail">Kernel audit-chain entries for the participant's clinical records.</param>
/// <param name="ProtocolVersions">Internal protocol version snapshots used when rendering structured XML casebooks (omitted from JSON wire output).</param>
public sealed record KernelCasebookResponse(
    string Producer,
    DateTimeOffset GeneratedAt,
    string RequestedBy,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    string CasebookDigestSha256,
    string StudyRef,
    string StudyCode,
    string StudyTitle,
    string SiteRef,
    string SiteCode,
    string EnrollmentRef,
    string SubjectCode,
    string? EnrollmentStatus,
    bool Masked,
    string? Mask,
    IReadOnlyList<KernelCasebookFormView> Forms,
    IReadOnlyList<KernelCasebookObservationView> Observations,
    IReadOnlyList<KernelCasebookQueryView> Queries,
    IReadOnlyList<KernelCasebookVerificationView> Verifications,
    IReadOnlyList<KernelLockResponse> Locks,
    IReadOnlyList<KernelCasebookAttestationView> Attestations,
    IReadOnlyList<ObservationAuditView> AuditTrail,
    [property: JsonIgnore] IReadOnlyList<ExportProtocolSnapshot>? ProtocolVersions = null);

/// <summary>One form instance in a participant casebook.</summary>
public sealed record KernelCasebookFormView(
    string Ref,
    string CollectionSpecId,
    string Title,
    string? PlannedActivityId,
    string? PlannedActivityName,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? OpenedAt,
    string? OpenedBy,
    string? ContentHash,
    bool Masked,
    string? Mask,
    [property: JsonIgnore] Guid? GroupingFormId = null);

/// <summary>One observation with its current and superseded revisions in a participant casebook.</summary>
public sealed record KernelCasebookObservationView(
    string Ref,
    string? CollectionInstanceRef,
    string? CollectionSpecId,
    string? ItemId,
    string ConceptRef,
    string ConceptCode,
    string ConceptLabel,
    DateTimeOffset? ObservedAt,
    int? CurrentRevision,
    string ValueKind,
    string DisplayValue,
    decimal? NumericValue,
    string? Unit,
    string? EffectiveLock,
    bool Masked,
    string? Mask,
    IReadOnlyList<KernelCasebookRevisionView> Revisions,
    [property: JsonIgnore] Guid? GroupingFormId = null);

/// <summary>One revision (current or superseded) of an observation in a participant casebook.</summary>
public sealed record KernelCasebookRevisionView(
    int Revision,
    string ValueKind,
    string DisplayValue,
    decimal? NumericValue,
    string? Unit,
    string? ReasonForChange,
    string? RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? SupersededAt,
    bool Superseded);

/// <summary>One query or query answer in a participant casebook.</summary>
public sealed record KernelCasebookQueryView(
    string Ref,
    string Kind,
    string TargetRef,
    string? Text,
    string? AssertedBy,
    DateTimeOffset AssertedAt,
    string? ReasonForChange,
    bool Closed,
    DateTimeOffset? ClosedAt,
    int? Revision = null);

/// <summary>One source data verification entry in a participant casebook.</summary>
public sealed record KernelCasebookVerificationView(
    string Ref,
    string ObservationRef,
    int? Revision,
    string? VerifiedBy,
    DateTimeOffset VerifiedAt,
    string? ReasonForChange,
    bool Retracted,
    DateTimeOffset? RetractedAt);

/// <summary>One 21 CFR Part 11 electronic signature (<c>Attestation</c>) in a participant casebook.</summary>
public sealed record KernelCasebookAttestationView(
    string Ref,
    string TargetRef,
    string Meaning,
    string SignerId,
    DateTimeOffset AuthenticatedAt,
    string Method,
    DateTimeOffset SignedAt,
    string TargetContentHash,
    string Reason);
