using System;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Registers an artifact with its first version (spec §8.3): evidence content such as an ethics approval, a signed
/// consent form or a lab report. The bytes are already in object storage; the command records what they are and their
/// hash. Versions are immutable, and later content is a new version (<see cref="AddArtifactVersion"/>).
/// </summary>
/// <remarks>
/// <para><c>StudyId</c>: The study the artifact belongs to, or null for tenant-wide evidence such as a lab's range set.</para>
/// <para><c>Title</c>: A human-readable name, at most 500 characters.</para>
/// </remarks>
[KernelCapability("artifact.register", VerbClass.Execute)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(EvidenceErrors),
    nameof(EvidenceErrors.StudyUnknown),
    nameof(EvidenceErrors.TitleInvalid),
    nameof(EvidenceErrors.KindInvalid),
    nameof(EvidenceErrors.ContentInvalid))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RegisterArtifact(
    TenantId TenantId,
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    ArtifactKind Kind,
    StudyId? StudyId,
    string Title,
    ArtifactContent Content,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ArtifactState>;

/// <summary>Adds a new immutable version to an artifact, numbered after its latest one.</summary>
[KernelCapability("artifact.add_version", VerbClass.Execute, Resource = nameof(AddArtifactVersion.ArtifactId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.NotFound), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(typeof(EvidenceErrors), nameof(EvidenceErrors.ContentInvalid), nameof(EvidenceErrors.ContentUnchanged))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record AddArtifactVersion(
    TenantId TenantId,
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    ArtifactContent Content,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ArtifactState>;

/// <summary>
/// Audits and registers a streamed bitemporal study snapshot export as an immutable <see cref="ArtifactKind.Dataset"/>
/// <c>Artifact</c> before bytes are returned to the caller (PRD-CDR-006, K-AUTH-2, K-AUTH-4).
/// </summary>
[KernelCapability("study.export", VerbClass.Execute, Resource = nameof(StudyId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(EvidenceErrors),
    nameof(EvidenceErrors.StudyUnknown),
    nameof(EvidenceErrors.TitleInvalid),
    nameof(EvidenceErrors.KindInvalid),
    nameof(EvidenceErrors.ContentInvalid))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ExportStudySnapshot(
    TenantId TenantId,
    StudyId StudyId,
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    string Title,
    ArtifactContent Content,
    string Format,
    string LockFilter,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    bool IncludesUnblindedData,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ArtifactState>;

/// <summary>
/// Audits and registers a streamed bitemporal participant casebook export as an immutable <see cref="ArtifactKind.Dataset"/>
/// <c>Artifact</c> before bytes are returned to the caller (PRD-CDR-006, K-AUTH-2, K-AUTH-4).
/// </summary>
[KernelCapability("enrollment.export", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(EvidenceErrors),
    nameof(EvidenceErrors.EnrollmentUnknown),
    nameof(EvidenceErrors.TitleInvalid),
    nameof(EvidenceErrors.KindInvalid),
    nameof(EvidenceErrors.ContentInvalid))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ExportEnrollmentCasebook(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    string Title,
    ArtifactContent Content,
    string Format,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    bool IncludesUnblindedData,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ArtifactState>;

/// <summary>
/// Persists a verified <see cref="SimulationResult"/> as an immutable <see cref="ArtifactKind.SimulationReport"/>
/// artifact whose content hash and canonical <see cref="SimulationArtifactBinding"/> can be attested with
/// <c>AttestationMeaning.Reviewed</c> before approving a forked amendment (K-SIM-5, K-PROT-2).
/// </summary>
[KernelCapability("simulation.save_report", VerbClass.Execute, Resource = nameof(StudyId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(EvidenceErrors),
    nameof(EvidenceErrors.StudyUnknown),
    nameof(EvidenceErrors.TitleInvalid),
    nameof(EvidenceErrors.KindInvalid),
    nameof(EvidenceErrors.ContentInvalid),
    nameof(EvidenceErrors.SimulationReportInvalid))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record SaveSimulationReport(
    TenantId TenantId,
    StudyId StudyId,
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    string Title,
    SimulationResult Result,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ArtifactState>;

/// <summary>An artifact and its latest version after a command.</summary>
public sealed record ArtifactState(
    ArtifactId ArtifactId,
    ArtifactKind Kind,
    StudyId? StudyId,
    string Title,
    ArtifactVersionId LatestVersionId,
    int LatestVersionNumber,
    string LatestContentHash);

/// <summary>Errors raised by the evidence commands (spec §8.3).</summary>
public static class EvidenceErrors
{
    public static readonly KernelError ContentInvalid =
        new("K.Evidence.ContentInvalid", 422, "The artifact content description is not valid.");

    public static readonly KernelError TitleInvalid =
        new("K.Evidence.TitleInvalid", 422, "An artifact needs a title of at most 500 characters.");

    public static readonly KernelError KindInvalid =
        new("K.Evidence.KindInvalid", 422, "The artifact kind is not known.");

    public static readonly KernelError StudyUnknown =
        new("K.Evidence.StudyUnknown", 404, "The study does not exist.");

    public static readonly KernelError EnrollmentUnknown =
        new("K.Evidence.EnrollmentUnknown", 404, "The enrollment does not exist.");

    public static readonly KernelError ContentUnchanged =
        new("K.Evidence.ContentUnchanged", 409, "The artifact already has a version with this content.");

    public static readonly KernelError SimulationReportInvalid =
        new("K.Evidence.SimulationReportInvalid", 422, "The simulation report is masked, belongs to another study, or its result hash does not match its canonical content.");
}
