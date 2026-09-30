using System.Collections.Generic;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Standards;

/// <summary>A standard version was registered as a draft (spec §5.2, Knowledge family).</summary>
[KernelEvent("StandardVersionRegistered", 1)]
public sealed record StandardVersionRegistered(
    TenantId TenantId,
    StandardVersionId StandardVersionId,
    string StandardCode,
    string Version,
    string ContentHash,
    int DatasetCount,
    int VariableCount);

/// <summary>A standard version was approved for use by studies.</summary>
[KernelEvent("StandardVersionApproved", 1)]
public sealed record StandardVersionApproved(TenantId TenantId, StandardVersionId StandardVersionId, AttestationId AttestationId);

/// <summary>A standard version was deprecated; studies that declared it keep it.</summary>
[KernelEvent("StandardVersionDeprecated", 1)]
public sealed record StandardVersionDeprecated(TenantId TenantId, StandardVersionId StandardVersionId, AttestationId AttestationId);

/// <summary>A new version of a Metadata Registry library item was drafted.</summary>
[KernelEvent("LibraryItemDrafted", 1)]
public sealed record LibraryItemDrafted(
    TenantId TenantId,
    LibraryItemId LibraryItemId,
    LibraryItemKind Kind,
    string Key,
    int Version,
    string ContentHash);

/// <summary>A library item version was approved.</summary>
[KernelEvent("LibraryItemApproved", 1)]
public sealed record LibraryItemApproved(TenantId TenantId, LibraryItemId LibraryItemId, LibraryItemKind Kind, string Key, int Version, AttestationId AttestationId);

/// <summary>A library item version was deprecated; studies that pinned it keep it.</summary>
[KernelEvent("LibraryItemDeprecated", 1)]
public sealed record LibraryItemDeprecated(TenantId TenantId, LibraryItemId LibraryItemId, LibraryItemKind Kind, string Key, int Version, AttestationId AttestationId);

/// <summary>A study declared, or revised, the standards, terminology versions and library items it uses.</summary>
[KernelEvent("StudyStandardsDeclared", 1)]
public sealed record StudyStandardsDeclared(
    TenantId TenantId,
    StudyId StudyId,
    StudyStandardsProfileId ProfileId,
    long Revision,
    IReadOnlyList<StandardVersionId> StandardVersions,
    IReadOnlyList<CodeSystemVersionRef> ControlledTerminology,
    IReadOnlyList<LibraryItemId> LibraryPins);
