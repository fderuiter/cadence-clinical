using System.Collections.Generic;
using System.Text.Json;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Standards;

/// <summary>
/// The governance lifecycle shared by standard versions and Metadata Registry library items (spec §6.3):
/// <c>Draft → Approved → Deprecated</c>. Approval and deprecation each need an Attestation by the acting person.
/// Approved content never changes; a change is a new version.
/// </summary>
public enum GovernanceStatus
{
    /// <summary>Loaded or authored; not yet usable by a study.</summary>
    Draft,

    /// <summary>Approved for use; studies may pin it.</summary>
    Approved,

    /// <summary>No longer offered for new pins. Studies that pinned it keep it.</summary>
    Deprecated,
}

/// <summary>What a Metadata Registry library item holds. Each kind's content has its own schema.</summary>
public enum LibraryItemKind
{
    /// <summary>A reusable collection specification (items bound to concepts, units and terminology).</summary>
    CollectionSpecification,

    /// <summary>A mapping specification from concepts to standard variables (#4810).</summary>
    MappingSpecification,

    /// <summary>A pack of versioned rules (#4811).</summary>
    RulePack,

    /// <summary>A sponsor subset of a controlled-terminology codelist.</summary>
    CodelistSubset,
}

/// <summary>
/// Registers one version of a data standard in the tenant's Standards Registry as a <see cref="GovernanceStatus.Draft"/>
/// (spec §6.3). The package is stored as given; its content hash is computed by the kernel and, when
/// <see cref="ExpectedContentHash"/> is set, must match it.
/// </summary>
public sealed record RegisterStandardVersion(
    TenantId TenantId,
    StandardVersionId StandardVersionId,
    StandardPackage Package,
    string? ExpectedContentHash,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<StandardVersionId>;

/// <summary>Approves a draft standard version for use by studies. Needs an <c>Approved</c> Attestation bound to its content hash.</summary>
public sealed record ApproveStandardVersion(
    TenantId TenantId,
    StandardVersionId StandardVersionId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<StandardVersionId>;

/// <summary>
/// Deprecates a standard version: no new study may declare it; studies that declared it keep it. Needs a
/// <c>Responsibility</c> Attestation bound to its content hash.
/// </summary>
public sealed record DeprecateStandardVersion(
    TenantId TenantId,
    StandardVersionId StandardVersionId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<StandardVersionId>;

/// <summary>
/// Drafts the next version of a Metadata Registry library item. <see cref="Key"/> names the item within its
/// <see cref="Kind"/>; the kernel assigns the version number (one more than the key's latest version).
/// <see cref="Content"/> must be a JSON object valid for the kind.
/// </summary>
public sealed record DraftLibraryItem(
    TenantId TenantId,
    LibraryItemId LibraryItemId,
    LibraryItemKind Kind,
    string Key,
    string Title,
    JsonElement Content,
    IReadOnlyList<StandardVersionId> StandardVersions,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<LibraryItemVersion>;

/// <summary>Approves a draft library item version. Needs an <c>Approved</c> Attestation bound to its content hash.</summary>
public sealed record ApproveLibraryItem(
    TenantId TenantId,
    LibraryItemId LibraryItemId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<LibraryItemVersion>;

/// <summary>Deprecates a library item version. Needs a <c>Responsibility</c> Attestation. Pinned studies keep it.</summary>
public sealed record DeprecateLibraryItem(
    TenantId TenantId,
    LibraryItemId LibraryItemId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<LibraryItemVersion>;

/// <summary>
/// Declares, or revises, the standards a study uses (spec §6.3, K-STD-1): at most one version per standard, the
/// controlled-terminology versions, and pinned library item versions (at most one per kind and key). Everything
/// declared must be <see cref="GovernanceStatus.Approved"/> when it is declared; later deprecation never changes the
/// declaration. The declaration replaces the previous one as a whole.
/// </summary>
public sealed record DeclareStudyStandards(
    TenantId TenantId,
    StudyId StudyId,
    StudyStandardsProfileId ProfileId,
    IReadOnlyList<StandardVersionId> StandardVersions,
    IReadOnlyList<CodeSystemVersionRef> ControlledTerminology,
    IReadOnlyList<LibraryItemId> LibraryPins,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<StudyStandardsProfileId>;

/// <summary>A library item version, as returned by the library commands.</summary>
public sealed record LibraryItemVersion(LibraryItemId Id, LibraryItemKind Kind, string Key, int Version, string ContentHash);

/// <summary>Errors raised by the Standards Registry and Metadata Registry commands (K-ERR-1).</summary>
public static class StandardsErrors
{
    public static readonly KernelError PackageInvalid =
        new("K.Standards.PackageInvalid", 422, "The standard package is not valid.");

    public static readonly KernelError ContentHashMismatch =
        new("K.Standards.ContentHashMismatch", 422, "The content does not match the expected content hash.");

    public static readonly KernelError VersionAlreadyRegistered =
        new("K.Standards.VersionAlreadyRegistered", 409, "This standard version is already registered in the tenant.");

    public static readonly KernelError StandardVersionUnknown =
        new("K.Standards.StandardVersionUnknown", 404, "The standard version does not exist.");

    public static readonly KernelError LibraryItemUnknown =
        new("K.Standards.LibraryItemUnknown", 404, "The library item does not exist.");

    public static readonly KernelError LibraryItemInvalid =
        new("K.Standards.LibraryItemInvalid", 422, "The library item is not valid.");

    public static readonly KernelError InvalidTransition =
        new("K.Standards.InvalidTransition", 409, "The governance transition is not allowed from the current status.");

    public static readonly KernelError AttestationRejected =
        new("K.Standards.AttestationRejected", 403, "The attestation does not authorize this governance action.");

    public static readonly KernelError NotApproved =
        new("K.Standards.NotApproved", 422, "Only approved standard versions and library items can be declared by a study.");

    public static readonly KernelError DeclarationInvalid =
        new("K.Standards.DeclarationInvalid", 422, "The study standards declaration is not valid.");

    public static readonly KernelError StudyUnknown =
        new("K.Standards.StudyUnknown", 404, "The study does not exist.");
}
