using System;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Events;

/// <summary>A code system was registered in a tenant's Concept Registry (spec §5.2, Knowledge family).</summary>
[KernelEvent("CodeSystemRegistered", 1)]
public sealed record CodeSystemRegistered(TenantId TenantId, CodeSystemId CodeSystemId, string Uri, string Name, LicenseClass License);

/// <summary>
/// A code-system version was imported. The event names the package digest and entry count, not the entries: the
/// registry is the source of the content (spec §5.2, Knowledge family).
/// </summary>
[KernelEvent("CodeSystemVersionImported", 1)]
public sealed record CodeSystemVersionImported(
    TenantId TenantId,
    CodeSystemVersionId CodeSystemVersionId,
    CodeSystemId CodeSystemId,
    string Version,
    DateOnly ReleasedOn,
    string PackageSha256,
    int EntryCount);

/// <summary>A concept was defined at version 1.</summary>
[KernelEvent("ConceptDefined", 1)]
public sealed record ConceptDefined(TenantId TenantId, ConceptRef Concept, ConceptOrigin Origin, string? ExternalCode, ConceptKind Kind, string PreferredLabel);

/// <summary>A concept's label or definition changed; <see cref="Concept"/> is the new version.</summary>
[KernelEvent("ConceptRevised", 1)]
public sealed record ConceptRevised(TenantId TenantId, ConceptRef Concept, string PreferredLabel);

/// <summary>A concept was deprecated at its current version.</summary>
[KernelEvent("ConceptDeprecated", 1)]
public sealed record ConceptDeprecated(TenantId TenantId, ConceptRef Concept);

/// <summary>A concept version was mapped to a code (spec §5.2 <c>ConceptMapped</c>).</summary>
[KernelEvent("ConceptMapped", 1)]
public sealed record ConceptMapped(
    TenantId TenantId,
    ConceptMappingId ConceptMappingId,
    ConceptRef Concept,
    Coding Target,
    MappingRelation Relation,
    MappingStatus Status);

/// <summary>A mapping was confirmed or rejected.</summary>
[KernelEvent("ConceptMappingReviewed", 1)]
public sealed record ConceptMappingReviewed(TenantId TenantId, ConceptMappingId ConceptMappingId, MappingStatus Status);
