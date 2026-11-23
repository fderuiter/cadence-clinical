using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;

namespace Cadence.Kernel.Protocol.Import;

/// <summary>Supported interchange formats for synchronous protocol draft import.</summary>
public enum ProtocolImportFormat
{
    /// <summary>Infer format from file name or leading content (<c>&lt;</c> for ODM XML, <c>{</c> for USDM JSON).</summary>
    Auto,

    /// <summary>CDISC Operational Data Model (ODM) v1.3.2 XML.</summary>
    OdmXml,

    /// <summary>CDISC Unified Study Definitions Model (USDM) v4 JSON.</summary>
    UsdmJson,
}

/// <summary>Metadata describing a bundled standard eCRF template available for protocol draft assembly.</summary>
/// <param name="Id">Stable template identifier used when selecting templates for import.</param>
/// <param name="Category">Clinical domain or category folder name.</param>
/// <param name="FormOid">Form OID declared in the template.</param>
/// <param name="FormName">Human-readable form title preserved on import.</param>
/// <param name="FileName">Source file name in the bundled template library.</param>
/// <param name="ItemCount">Number of data collection items on the template form.</param>
/// <param name="Repeating">True when the template form declares <c>Repeating="Yes"</c> (such as an event or log form).</param>
public sealed record BundledProtocolTemplate(
    string Id,
    string Category,
    string FormOid,
    string FormName,
    string FileName,
    int ItemCount,
    bool Repeating = false);

/// <summary>Explicit planned-activity assignment chosen for a bundled protocol template during draft assembly.</summary>
/// <param name="TemplateId">Identifier of the bundled template being scheduled.</param>
/// <param name="ActivityIds">Ordered planned activity identifiers that should collect the template; empty keeps the form unscheduled.</param>
public sealed record TemplateActivityAssignment(
    string TemplateId,
    IReadOnlyList<string> ActivityIds);

/// <summary>
/// Candidate concept requested by an external protocol document during synchronous import so the host can resolve an
/// existing concept in the tenant's Concept Registry or define a sponsor-local concept through the command pipeline.
/// </summary>
/// <param name="StableKey">Deterministic key identifying the item concept across repeated imports.</param>
/// <param name="BiomedicalConceptCode">CDISC Biomedical Concept code (for example <c>C66742</c>), when present.</param>
/// <param name="PreferredLabel">Human-facing item prompt or label.</param>
/// <param name="Definition">Question text or item description.</param>
/// <param name="Kind">Concept kind to register when defining a sponsor-local concept.</param>
public sealed record ProtocolConceptCandidate(
    string StableKey,
    string? BiomedicalConceptCode,
    string PreferredLabel,
    string Definition,
    ConceptKind Kind = ConceptKind.BiomedicalConcept);

/// <summary>Result of resolving a <see cref="ProtocolConceptCandidate"/> against the tenant's Concept Registry.</summary>
/// <param name="Concept">Resolved or newly defined concept version reference, or null when resolution failed.</param>
/// <param name="UsedFallbackSponsorConcept">True when a CDISC concept code was not mapped in the tenant and fell back to a sponsor-local concept.</param>
/// <param name="ErrorMessage">Error message when concept definition failed.</param>
public sealed record ProtocolConceptResolution(
    ConceptRef? Concept,
    bool UsedFallbackSponsorConcept = false,
    string? ErrorMessage = null);

/// <summary>Delegate invoked by <see cref="IProtocolDesignImporter"/> to resolve or define each item's concept.</summary>
public delegate ValueTask<ProtocolConceptResolution> ProtocolConceptResolver(
    ProtocolConceptCandidate candidate,
    CancellationToken cancellationToken);

/// <summary>Input specification for synchronous protocol design import.</summary>
/// <param name="RawBytes">Uploaded file payload bytes, when importing an uploaded ODM v1.3.2 XML or USDM v4 JSON file.</param>
/// <param name="FileName">Original file name of the uploaded document, when present.</param>
/// <param name="Format">Requested import format, or <see cref="ProtocolImportFormat.Auto"/>.</param>
/// <param name="TemplateIds">Selected bundled template identifiers to import, when assembling from library templates.</param>
/// <param name="BaseDesign">Existing draft design to merge forms into when revising or appending forms, or null for a fresh draft.</param>
/// <param name="FallbackTitle">Study title used when the imported forms do not carry a protocol title.</param>
/// <param name="TemplateAssignments">Optional designer-specified planned activity assignments per selected template.</param>
public sealed record ProtocolDesignImportRequest(
    byte[]? RawBytes = null,
    string? FileName = null,
    ProtocolImportFormat Format = ProtocolImportFormat.Auto,
    IReadOnlyList<string>? TemplateIds = null,
    ProtocolDesign? BaseDesign = null,
    string? FallbackTitle = null,
    IReadOnlyList<TemplateActivityAssignment>? TemplateAssignments = null);

/// <summary>Compiled <c>RulePack</c> library item payload produced during ODM v1.3.2 protocol import.</summary>
/// <param name="Key">Stable library item key derived from the protocol metadata.</param>
/// <param name="Title">Human-facing rule pack title.</param>
/// <param name="RuleCount">Number of edit-check rules compiled into the pack.</param>
/// <param name="Content">Serialized <c>RulePack</c> JSON content ready for <c>DraftLibraryItem</c>.</param>
public sealed record ImportedRulePack(
    string Key,
    string Title,
    int RuleCount,
    JsonElement Content);

/// <summary>Outcome of compiling an uploaded file or bundled template selection into a <see cref="ProtocolDesign"/>.</summary>
/// <param name="Design">Validated <see cref="ProtocolDesign"/> ready to persist as a draft, or null when <paramref name="Problems"/> is non-empty.</param>
/// <param name="Problems">Actionable structural or validation error messages explaining why the import was rejected.</param>
/// <param name="Notes">Readable import notes describing unmapped terminology codes or optional vendor attributes.</param>
/// <param name="PayloadTooLarge">True when the input exceeded the 10 MB synchronous import limit.</param>
/// <param name="RulePack">Optional compiled edit-check rule pack extracted from ODM <c>RangeCheck</c> and <c>ConditionDef</c> elements.</param>
public sealed record ProtocolDesignImportResult(
    ProtocolDesign? Design,
    IReadOnlyList<string> Problems,
    IReadOnlyList<ProtocolImportNote> Notes,
    bool PayloadTooLarge = false,
    ImportedRulePack? RulePack = null)
{
    /// <summary>True when a valid <see cref="ProtocolDesign"/> was produced with zero problems.</summary>
    public bool IsSuccess => Design is not null && Problems.Count == 0 && !PayloadTooLarge;
}

/// <summary>
/// Synchronously imports CDISC ODM v1.3.2 XML files, CDISC USDM v4 JSON files, and bundled CDISC Library eCRF templates
/// into a kernel <see cref="ProtocolDesign"/>.
/// </summary>
public interface IProtocolDesignImporter
{
    /// <summary>Maximum allowed byte length for synchronous file uploads (10 MB).</summary>
    const int MaxPayloadBytes = 10 * 1024 * 1024;

    /// <summary>Lists all bundled standard eCRF templates available for selection.</summary>
    IReadOnlyList<BundledProtocolTemplate> ListBundledTemplates();

    /// <summary>Imports a file payload or selected bundled templates into a <see cref="ProtocolDesign"/>.</summary>
    Task<ProtocolDesignImportResult> ImportAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        CancellationToken cancellationToken = default);
}
