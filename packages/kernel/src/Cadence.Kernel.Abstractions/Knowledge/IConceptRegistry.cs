using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Knowledge;

/// <summary>
/// Reads the tenant's Concept Registry (spec §6.1). Every lookup names a concept version or a code-system version:
/// there is no overload that resolves "the latest", so a reference can never change meaning under the fact that
/// holds it. Changes go through the kernel commands (<c>DefineConcept</c>, <c>MapConcept</c>, …).
/// </summary>
public interface IConceptRegistry
{
    /// <summary>The concept exactly as it was at <paramref name="concept"/>'s version, or null if there is no such version.</summary>
    Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default);

    /// <summary>The concept's current version, for authoring a new reference. Facts store the returned <see cref="ConceptRef"/>.</summary>
    Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default);

    /// <summary>The CDISC Biomedical Concept with this CDISC concept code, at its current version.</summary>
    Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default);

    /// <summary>A named version of a code system. <paramref name="version"/> is required; a blank one throws.</summary>
    Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default);

    /// <summary>Every entry for the code in its pinned version, one per group it appears in.</summary>
    Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default);

    /// <summary>The entries of one group (a CDISC CT codelist) in the pinned version, in code order.</summary>
    Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// The concepts a code means, through confirmed mappings. A LOINC-coded lab result and an eCRF field for the same
    /// Biomedical Concept resolve to the same <see cref="ConceptRef"/>.
    /// </summary>
    Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default);

    /// <summary>Every mapping of the concept, in any status, across its versions.</summary>
    Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default);
}

/// <summary>One version of a concept, with its mapping state derived from its mappings.</summary>
public sealed record ConceptVersionView(
    ConceptRef Concept,
    ConceptOrigin Origin,
    string? ExternalCode,
    ConceptKind Kind,
    string PreferredLabel,
    string Definition,
    ConceptStatus Status,
    int CurrentVersion,
    ConceptMappingState MappingState,
    DateTimeOffset RecordedAt);

/// <summary>A code-system version and where it came from.</summary>
public sealed record CodeSystemVersionView(
    CodeSystemVersionId Id,
    CodeSystemId CodeSystemId,
    string CodeSystemUri,
    string Version,
    DateOnly ReleasedOn,
    LicenseClass License,
    string PackageSha256,
    int EntryCount);

/// <summary>One entry of a code-system version.</summary>
public sealed record CodeSystemEntryView(
    CodeSystemVersionId CodeSystemVersionId,
    string Code,
    string Display,
    string? GroupCode,
    string? SubmissionValue,
    string? Definition,
    IReadOnlyList<string> Synonyms);

/// <summary>A mapping between a concept version and a pinned code.</summary>
public sealed record ConceptMappingView(
    ConceptMappingId Id,
    ConceptRef Concept,
    Coding Target,
    MappingRelation Relation,
    MappingStatus Status,
    string Evidence);
