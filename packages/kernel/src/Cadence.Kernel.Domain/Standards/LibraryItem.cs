using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Domain.Standards;

/// <summary>
/// One version of a governed Metadata Registry library item (spec §6.3): a reusable collection specification, mapping
/// specification, rule pack or codelist subset. Each version is its own aggregate with fixed content, identified by
/// <c>(kind, key, version)</c> and by <see cref="ContentHash"/>. Studies pin versions, so approving or deprecating a
/// later version never changes what a study uses.
/// </summary>
public sealed class LibraryItem : AggregateRoot<LibraryItemId>
{
    /// <summary>Longest key or title the registry stores.</summary>
    public const int MaxKeyLength = 128;

    public const int MaxTitleLength = 500;

    private LibraryItem() { }

    private LibraryItem(
        LibraryItemId id,
        TenantId tenantId,
        LibraryItemKind kind,
        string key,
        int version,
        string title,
        string content,
        string contentHash,
        IReadOnlyList<StandardVersionId> standardVersions)
        : base(id)
    {
        TenantId = tenantId;
        Kind = kind;
        Key = key;
        ItemVersion = version;
        Title = title;
        Content = content;
        ContentHash = contentHash;
        StandardVersionIds = [.. standardVersions.Distinct().Order()];
        Status = GovernanceStatus.Draft;
    }

    public TenantId TenantId { get; private set; }

    public LibraryItemKind Kind { get; private set; }

    /// <summary>The item's name within its kind; stable across versions.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The item version, from 1, one more than the key's previous version.</summary>
    public int ItemVersion { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>The content as canonical JSON (<see cref="CanonicalJson"/>).</summary>
    public string Content { get; private set; } = "{}";

    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>Standard versions the content is written against.</summary>
    public StandardVersionId[] StandardVersionIds { get; private set; } = [];

    public GovernanceStatus Status { get; private set; }

    public AttestationId? ApprovalAttestationId { get; private set; }

    public AttestationId? DeprecationAttestationId { get; private set; }

    /// <summary>Drafts version <paramref name="version"/> of an item. The content must be a JSON object.</summary>
    public static (LibraryItem? Item, KernelError? Error) Draft(
        LibraryItemId id,
        TenantId tenantId,
        LibraryItemKind kind,
        string key,
        int version,
        string title,
        JsonElement content,
        IReadOnlyList<StandardVersionId> standardVersions)
    {
        ArgumentNullException.ThrowIfNull(standardVersions);

        if (!Enum.IsDefined(kind))
        {
            return (null, StandardsErrors.LibraryItemInvalid.WithMessage($"Unknown library item kind {(int)kind}."));
        }

        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength || key.Trim().Length != key.Length)
        {
            return (null, StandardsErrors.LibraryItemInvalid.WithMessage($"A key of 1 to {MaxKeyLength} characters with no surrounding spaces is required."));
        }

        if (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitleLength)
        {
            return (null, StandardsErrors.LibraryItemInvalid.WithMessage($"A title of 1 to {MaxTitleLength} characters is required."));
        }

        if (content.ValueKind != JsonValueKind.Object)
        {
            return (null, StandardsErrors.LibraryItemInvalid.WithMessage("Library item content must be a JSON object."));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Library item versions start at 1.");
        }

        var canonical = CanonicalJson.Serialize(content);
        return (new LibraryItem(id, tenantId, kind, key, version, title, canonical, CanonicalJson.HashText(canonical), standardVersions), null);
    }

    /// <summary>Approves a draft. The caller has verified the Attestation.</summary>
    public KernelError? Approve(AttestationId attestation)
    {
        if (Status != GovernanceStatus.Draft)
        {
            return StandardsErrors.InvalidTransition.WithMessage($"{Kind} {Key} v{ItemVersion} is {Status}; only a draft can be approved.");
        }

        Status = GovernanceStatus.Approved;
        ApprovalAttestationId = attestation;
        return null;
    }

    /// <summary>Deprecates a draft or approved version. The caller has verified the Attestation.</summary>
    public KernelError? Deprecate(AttestationId attestation)
    {
        if (Status == GovernanceStatus.Deprecated)
        {
            return StandardsErrors.InvalidTransition.WithMessage($"{Kind} {Key} v{ItemVersion} is already deprecated.");
        }

        Status = GovernanceStatus.Deprecated;
        DeprecationAttestationId = attestation;
        return null;
    }

    public LibraryItemVersion ToVersion() => new(Id, Kind, Key, ItemVersion, ContentHash);
}
