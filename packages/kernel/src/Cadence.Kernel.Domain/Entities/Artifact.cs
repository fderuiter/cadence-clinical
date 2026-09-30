using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Versioned, hash-addressed evidence content (spec §8.3): a signed consent form, an ethics approval, a lab report, a
/// generated dataset. The artifact fixes its <see cref="Kind"/>, title and study; each <see cref="ArtifactVersion"/> is
/// immutable and names its bytes by content hash. Evidence references and attestations name a version.
/// Expected failures return a <see cref="KernelError"/>.
/// </summary>
public class Artifact : AggregateRoot<ArtifactId>
{
    /// <summary>The longest title the kernel stores.</summary>
    public const int MaxTitleLength = 500;

    private readonly List<ArtifactVersion> _versions = [];

    protected Artifact() { }

    private Artifact(ArtifactId id, TenantId tenantId, ArtifactKind kind, StudyId? studyId, string title)
        : base(id)
    {
        TenantId = tenantId;
        Kind = kind;
        StudyId = studyId;
        Title = title;
    }

    public TenantId TenantId { get; private set; }

    public ArtifactKind Kind { get; private set; }

    /// <summary>The study the artifact belongs to, or null for tenant-wide evidence.</summary>
    public StudyId? StudyId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Every version, oldest first.</summary>
    public IReadOnlyList<ArtifactVersion> Versions => _versions;

    /// <summary>The newest version.</summary>
    public ArtifactVersion LatestVersion =>
        _versions.MaxBy(v => v.Number) ?? throw new InvalidOperationException($"Artifact {Id} has no versions; load it with its versions.");

    /// <summary>Registers an artifact with its first version, recorded at <paramref name="recordedAt"/>.</summary>
    public static CommandResult<Artifact> Register(
        ArtifactId id,
        TenantId tenantId,
        ArtifactKind kind,
        StudyId? studyId,
        string? title,
        ArtifactVersionId versionId,
        ArtifactContent? content,
        DateTimeOffset recordedAt)
    {
        if (!Enum.IsDefined(kind))
        {
            return EvidenceErrors.KindInvalid;
        }

        if (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitleLength)
        {
            return EvidenceErrors.TitleInvalid;
        }

        if (Invalid(content) is { } invalid)
        {
            return invalid;
        }

        var artifact = new Artifact(id, tenantId, kind, studyId, title.Trim());
        artifact._versions.Add(new ArtifactVersion(artifact, versionId, 1, content!, KernelTimestamps.ToStoredPrecision(recordedAt)));
        return CommandResult<Artifact>.Success(artifact);
    }

    /// <summary>Adds a version numbered after the latest; the same content twice is refused.</summary>
    public KernelError? AddVersion(ArtifactVersionId versionId, ArtifactContent? content, DateTimeOffset recordedAt)
    {
        if (Invalid(content) is { } invalid)
        {
            return invalid;
        }

        if (_versions.Any(v => string.Equals(v.ContentHash, content!.ContentHash, StringComparison.Ordinal)))
        {
            return EvidenceErrors.ContentUnchanged;
        }

        _versions.Add(new ArtifactVersion(this, versionId, LatestVersion.Number + 1, content!, KernelTimestamps.ToStoredPrecision(recordedAt)));
        return null;
    }

    /// <summary>The version with <paramref name="versionId"/>, or null.</summary>
    public ArtifactVersion? Version(ArtifactVersionId versionId) => _versions.SingleOrDefault(v => v.Id == versionId);

    private static KernelError? Invalid(ArtifactContent? content)
    {
        if (content is null)
        {
            return EvidenceErrors.ContentInvalid;
        }

        var problem = content.Problem();
        return problem is null ? null : EvidenceErrors.ContentInvalid.WithMessage(problem);
    }
}

/// <summary>
/// One immutable version of an <see cref="Artifact"/>: the hash, size, media type and storage location of its bytes.
/// Insert-only in the database. Its content hash is what an attestation over the version signs.
/// </summary>
public class ArtifactVersion
{
    protected ArtifactVersion() { }

    internal ArtifactVersion(Artifact artifact, ArtifactVersionId id, int number, ArtifactContent content, DateTimeOffset recordedAt)
    {
        Id = id;
        TenantId = artifact.TenantId;
        ArtifactId = artifact.Id;
        Number = number;
        ContentHash = content.ContentHash;
        SizeBytes = content.SizeBytes;
        MediaType = content.MediaType;
        StorageRef = content.StorageRef;
        RecordedAt = recordedAt;
    }

    public ArtifactVersionId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public ArtifactId ArtifactId { get; private set; }

    /// <summary>1 for the first version, then one more than the previous.</summary>
    public int Number { get; private set; }

    /// <summary><c>sha256:</c> and the hex SHA-256 of the bytes.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string MediaType { get; private set; } = string.Empty;

    /// <summary>Where the bytes are stored.</summary>
    public string StorageRef { get; private set; } = string.Empty;

    /// <summary>When the version was recorded, from the kernel clock.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>What an attestation over this version signs.</summary>
    public AttestationTarget SignableTarget => new(NodeKinds.ArtifactVersion, Id.Value, ContentHash);
}
