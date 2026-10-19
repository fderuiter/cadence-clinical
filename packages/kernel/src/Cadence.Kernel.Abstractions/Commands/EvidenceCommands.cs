using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;

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
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record AddArtifactVersion(
    TenantId TenantId,
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    ArtifactContent Content,
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

    public static readonly KernelError ContentUnchanged =
        new("K.Evidence.ContentUnchanged", 409, "The artifact already has a version with this content.");
}
