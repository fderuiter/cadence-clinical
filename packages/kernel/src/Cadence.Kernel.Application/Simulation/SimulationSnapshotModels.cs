using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>Bitemporal snapshot of a study site read at a pinned simulation instant.</summary>
public sealed record SimulationSiteSnapshot(
    StudySiteId Id,
    SiteId SiteId,
    string Country,
    string Status,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>Bitemporal snapshot of an effectivity scope read at a pinned simulation instant.</summary>
public sealed record SimulationEffectivitySnapshot(
    EffectivityId Id,
    ProtocolVersionId ProtocolVersionId,
    EffectivityScope Scope,
    EffectivityStatus? StatusAsOf);

/// <summary>Bitemporal snapshot of an enrollment read at a pinned simulation instant.</summary>
public sealed record SimulationEnrollmentSnapshot(
    EnrollmentId Id,
    StudySiteId StudySiteId,
    string SubjectCode,
    EnrollmentStatus StatusAsOf,
    DateOnly ReferenceStartDate);

/// <summary>Bitemporal snapshot of a collected observation revision read at a pinned simulation instant.</summary>
public sealed record SimulationObservationSnapshot(
    ObservationId Id,
    EnrollmentId EnrollmentId,
    StudySiteId StudySiteId,
    ConceptRef Concept,
    ObservationValue Value,
    CollectionInstanceId? CollectionInstanceId = null,
    ProtocolVersionId? GoverningProtocolVersionId = null,
    string? CollectionSpecId = null,
    string? PlannedActivityId = null,
    string? ItemId = null);

/// <summary>One collected item value on a <see cref="SimulationCollectionSnapshot"/>.</summary>
public sealed record SimulationCollectedItemSnapshot(
    ObservationId ObservationId,
    ConceptRef Concept,
    ObservationValue Value);

/// <summary>Bitemporal snapshot of a collection form instance read at a pinned simulation instant.</summary>
public sealed record SimulationCollectionSnapshot(
    CollectionInstanceId Id,
    EnrollmentId EnrollmentId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    DateTimeOffset OpenedAt,
    IReadOnlyDictionary<string, SimulationCollectedItemSnapshot> ValuesByItemId);

/// <summary>Read-only bitemporal study snapshot loaded for simulation evaluation (K-SIM-1, K-SIM-2).</summary>
public sealed record SimulationStudySnapshot(
    IReadOnlyList<SimulationSiteSnapshot> Sites,
    IReadOnlyList<SimulationEffectivitySnapshot> Effectivities,
    IReadOnlyList<SimulationEnrollmentSnapshot> Enrollments,
    IReadOnlyList<SimulationObservationSnapshot> Observations,
    IReadOnlyList<SimulationCollectionSnapshot> Collections,
    IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign> ProtocolDesigns,
    IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> Concepts,
    IReadOnlyList<LibraryItemView> PinnedLibraryItems,
    IReadOnlyList<LibraryItemView> PinnedRulePacks,
    IReadOnlyList<StandardVersionId> DeclaredStandardVersions,
    IReadOnlyDictionary<(string Codelist, string Code), string> CodelistDecodes,
    bool Loaded = true,
    IReadOnlyList<(string Standard, string Version)>? DeclaredStandards = null)
{
    /// <summary>An empty study snapshot when no operational persistence snapshot is supplied.</summary>
    public static SimulationStudySnapshot Empty { get; } = new(
        [],
        [],
        [],
        [],
        [],
        new Dictionary<ProtocolVersionId, ProtocolDesign>(),
        new Dictionary<ConceptId, BlindedConceptMetadata>(),
        [],
        [],
        [],
        new Dictionary<(string Codelist, string Code), string>(),
        Loaded: false);

    /// <summary>True when the snapshot contains any operational, clinical, or standards records.</summary>
    public bool HasAnyRecords =>
        Sites.Count > 0
        || Effectivities.Count > 0
        || Enrollments.Count > 0
        || Observations.Count > 0
        || Collections.Count > 0
        || PinnedLibraryItems.Count > 0
        || PinnedRulePacks.Count > 0;
}

/// <summary>
/// Visibility- and blinding-filtered study snapshot produced before simulation evaluation or hashing (K-SIM-4, K-AUTH-4, ADR-2590).
/// </summary>
internal sealed record MaskedSimulationSnapshot(
    IReadOnlyList<SimulationSiteSnapshot> VisibleSites,
    IReadOnlyList<SimulationEffectivitySnapshot> VisibleEffectivities,
    IReadOnlyList<SimulationEnrollmentSnapshot> VisibleEnrollments,
    IReadOnlyList<SimulationObservationSnapshot> VisibleObservations,
    IReadOnlyList<SimulationCollectionSnapshot> VisibleCollections,
    IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign> ProtocolDesigns,
    IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> Concepts,
    IReadOnlyList<LibraryItemView> PinnedLibraryItems,
    IReadOnlyList<LibraryItemView> PinnedRulePacks,
    IReadOnlyList<StandardVersionId> DeclaredStandardVersions,
    IReadOnlyList<(string Standard, string Version)> DeclaredStandards,
    IReadOnlyDictionary<(string Codelist, string Code), string> CodelistDecodes,
    IReadOnlySet<EnrollmentId> UnblindedEnrollments,
    bool CanUnblindStudy,
    HiddenObjects Hidden,
    IReadOnlyList<MaskedPart> MaskedParts,
    string VisibleStateDigest);
