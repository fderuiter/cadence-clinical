using System;
using System.Collections.Generic;

using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Registers a code system (LOINC, UCUM, CDISC CT, MedDRA, a sponsor dictionary, …) in the tenant's Concept Registry
/// (spec §6.1). A known URI must carry the licence class <see cref="KnownCodeSystems"/> gives it (K-SEM-2).
/// </summary>
public sealed record RegisterCodeSystem(
    TenantId TenantId,
    CodeSystemId CodeSystemId,
    string Uri,
    string Name,
    string Publisher,
    LicenseClass License,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<CodeSystemId>;

/// <summary>
/// Imports one release of a code system with all of its entries. A version is imported once and never changes; every
/// specification, rule and projection names the version it used, never "the latest" (spec §6.1).
/// </summary>
public sealed record ImportCodeSystemVersion(
    TenantId TenantId,
    CodeSystemVersionId CodeSystemVersionId,
    CodeSystemId CodeSystemId,
    string Version,
    DateOnly ReleasedOn,
    TerminologyPackage Package,
    IReadOnlyList<CodeSystemEntryData> Entries,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<CodeSystemVersionId>;

/// <summary>
/// The file a code-system version was loaded from: its SHA-256, name, source and size. The digest binds the imported
/// entries to the exact bytes the publisher released. It becomes an Artifact reference once Artifacts exist (§8.3).
/// </summary>
public sealed record TerminologyPackage(string Sha256, string FileName, string SourceUri, long ByteLength, string Format);

/// <summary>
/// One code in a code-system version. <see cref="GroupCode"/> is the containing codelist or parent where the code
/// system has one (a CDISC CT codelist C-code, a MedDRA PT for an LLT); the same code can appear in several groups.
/// <see cref="SubmissionValue"/> is the value written into submission datasets where it differs from the code.
/// </summary>
public sealed record CodeSystemEntryData(
    string Code,
    string Display,
    string? GroupCode = null,
    string? SubmissionValue = null,
    string? Definition = null,
    IReadOnlyList<string>? Synonyms = null);

/// <summary>
/// Defines a concept at version 1. A CDISC Biomedical Concept is identified by its CDISC concept code
/// (<paramref name="ExternalCode"/>), which is unique in the tenant; a sponsor concept has none.
/// </summary>
public sealed record DefineConcept(
    TenantId TenantId,
    ConceptId ConceptId,
    ConceptOrigin Origin,
    string? ExternalCode,
    ConceptKind Kind,
    string PreferredLabel,
    string Definition,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ConceptRef>;

/// <summary>
/// Changes a concept's label or definition as a new version. Earlier versions stay resolvable, so every fact keeps the
/// meaning it was recorded against.
/// </summary>
public sealed record ReviseConcept(
    TenantId TenantId,
    ConceptId ConceptId,
    string PreferredLabel,
    string Definition,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ConceptRef>;

/// <summary>Deprecates a concept: no new references should use it; existing ones still resolve.</summary>
public sealed record DeprecateConcept(
    TenantId TenantId,
    ConceptId ConceptId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ConceptRef>;

/// <summary>
/// Maps a concept version to a code in a pinned code-system version (spec §6.1). A new mapping starts
/// <see cref="MappingStatus.Proposed"/> unless <paramref name="Confirmed"/> is set. <paramref name="Evidence"/> says
/// why the mapping holds (a CDISC publication, a reviewer's rationale).
/// </summary>
public sealed record MapConcept(
    TenantId TenantId,
    ConceptMappingId ConceptMappingId,
    ConceptRef Concept,
    Coding Target,
    MappingRelation Relation,
    string Evidence,
    bool Confirmed,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ConceptMappingId>;

/// <summary>Confirms or rejects a proposed mapping, or rejects a confirmed one that turned out wrong.</summary>
public sealed record ReviewConceptMapping(
    TenantId TenantId,
    ConceptMappingId ConceptMappingId,
    MappingStatus Status,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ConceptMappingId>;

/// <summary>Errors raised by the Knowledge engine's Concept Registry (K-ERR-1).</summary>
public static class KnowledgeErrors
{
    public static readonly KernelError CodeSystemUriInvalid =
        new("K.Knowledge.CodeSystemUriInvalid", 422, "A code system is identified by an absolute URI.");

    public static readonly KernelError LicenseMismatch =
        new("K.Knowledge.LicenseMismatch", 422, "The code system's licence class does not match its known licence terms (K-SEM-2).");

    public static readonly KernelError CodeSystemUnknown =
        new("K.Knowledge.CodeSystemUnknown", 404, "The code system is not registered in this tenant.");

    public static readonly KernelError VersionRequired =
        new("K.Knowledge.VersionRequired", 422, "A code-system version must be named; nothing resolves \"latest\".");

    public static readonly KernelError PackageInvalid =
        new("K.Knowledge.PackageInvalid", 422, "The terminology package must name its SHA-256, file, source and format.");

    public static readonly KernelError PackageChecksumMismatch =
        new("K.Knowledge.PackageChecksumMismatch", 422, "The terminology package does not match its published checksum.");

    public static readonly KernelError EntriesInvalid =
        new("K.Knowledge.EntriesInvalid", 422, "A code-system version needs at least one entry, each with a code and a display.");

    public static readonly KernelError DuplicateEntry =
        new("K.Knowledge.DuplicateEntry", 422, "The same code appears twice in one group of the code-system version.");

    public static readonly KernelError ConceptLabelRequired =
        new("K.Knowledge.ConceptLabelRequired", 422, "A concept needs a preferred label and a definition.");

    public static readonly KernelError ExternalCodeMismatch =
        new("K.Knowledge.ExternalCodeMismatch", 422, "A CDISC Biomedical Concept names its CDISC concept code; a sponsor concept has none.");

    public static readonly KernelError ConceptUnknown =
        new("K.Knowledge.ConceptUnknown", 404, "The concept, or the concept version, does not exist in this tenant.");

    public static readonly KernelError ConceptDeprecated =
        new("K.Knowledge.ConceptDeprecated", 422, "The concept is deprecated; new references and mappings cannot use it.");

    public static readonly KernelError ConceptUnchanged =
        new("K.Knowledge.ConceptUnchanged", 422, "The revision changes neither the label nor the definition.");

    public static readonly KernelError CodeUnknown =
        new("K.Knowledge.CodeUnknown", 404, "The code does not exist in the named code-system version.");

    public static readonly KernelError EvidenceRequired =
        new("K.Knowledge.EvidenceRequired", 422, "A mapping states the evidence it rests on.");

    public static readonly KernelError MappingUnknown =
        new("K.Knowledge.MappingUnknown", 404, "The concept mapping does not exist in this tenant.");

    public static readonly KernelError MappingTransitionInvalid =
        new("K.Knowledge.MappingTransitionInvalid", 422, "The mapping cannot move to that status from its current one.");
}
