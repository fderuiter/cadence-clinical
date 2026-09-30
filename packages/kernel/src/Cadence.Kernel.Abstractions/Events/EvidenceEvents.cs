using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;

namespace Cadence.Kernel.Events;

/// <summary>An artifact was registered with its first version (spec §8.3, Evidence family).</summary>
[KernelEvent("ArtifactRegistered", 1)]
public sealed record ArtifactRegistered(
    TenantId TenantId,
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    ArtifactKind Kind,
    StudyId? StudyId,
    string ContentHash,
    string MediaType,
    long SizeBytes);

/// <summary>A new immutable version was added to an artifact.</summary>
[KernelEvent("ArtifactVersioned", 1)]
public sealed record ArtifactVersioned(
    TenantId TenantId,
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    int Number,
    string ContentHash,
    string MediaType,
    long SizeBytes);
