using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;

namespace Cadence.Ddf;

/// <summary>Resolves a CDISC Biomedical Concept code (for example <c>C25298</c>) to the tenant's concept version.</summary>
/// <returns>The concept reference, or null when the tenant's Concept Registry has no such concept.</returns>
public delegate Task<ConceptRef?> BiomedicalConceptResolver(string cdiscConceptCode, CancellationToken cancellationToken);

/// <summary>Which study version and study design of a USDM document to import, when it has more than one.</summary>
public sealed record UsdmImportOptions
{
    /// <summary>The <c>StudyVersion</c> id; needed only when the study has several versions.</summary>
    public string? StudyVersionId { get; init; }

    /// <summary>The study design id; needed only when the version has several designs.</summary>
    public string? StudyDesignId { get; init; }
}

/// <summary>Something the USDM document says that the protocol design does not carry, or carries approximately.</summary>
/// <param name="Source">The USDM id (or property) the note is about.</param>
/// <param name="Message">What was left out or approximated, in words.</param>
public sealed record UsdmImportNote(string Source, string Message);

/// <summary>
/// The result of an import: the design, or the problems that stopped it, and in both cases every note about what the
/// design does not carry.
/// </summary>
public sealed record UsdmImportResult(ProtocolDesign? Design, IReadOnlyList<string> Problems, IReadOnlyList<UsdmImportNote> Notes)
{
    /// <summary>True when a design was produced.</summary>
    public bool IsSuccess => Design is not null;
}

/// <summary>
/// Imports a CDISC USDM 4 document (the API <c>Wrapper</c> JSON) into the kernel's protocol design (ADR-2472,
/// ADR-2473). USDM is the interchange format, not the stored shape: the importer keeps USDM ids as element ids, maps
/// what the design has a place for, resolves every Biomedical Concept to a concept version in the tenant, and lists
/// in <see cref="UsdmImportResult.Notes"/> everything it leaves out or approximates. It fails, with every problem, when
/// a Biomedical Concept is not in the Concept Registry or the result is not a valid design.
/// </summary>
public static class UsdmDesignImporter
{
    /// <summary>The USDM major version the importer reads.</summary>
    public const string SupportedMajorVersion = "4";

    /// <summary>Imports <paramref name="usdmJson"/>, resolving Biomedical Concepts in <paramref name="tenant"/>'s registry.</summary>
    public static Task<UsdmImportResult> ImportAsync(
        string usdmJson,
        IConceptRegistry registry,
        TenantId tenant,
        UsdmImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return ImportAsync(
            usdmJson,
            async (code, ct) => (await registry.FindBiomedicalConceptAsync(tenant, code, ct).ConfigureAwait(false))?.Concept,
            options,
            cancellationToken);
    }

    /// <summary>Imports <paramref name="usdmJson"/>, resolving Biomedical Concepts with <paramref name="resolve"/>.</summary>
    public static async Task<UsdmImportResult> ImportAsync(
        string usdmJson,
        BiomedicalConceptResolver resolve,
        UsdmImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usdmJson);
        ArgumentNullException.ThrowIfNull(resolve);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(usdmJson);
        }
        catch (JsonException ex)
        {
            return new UsdmImportResult(null, [$"The document is not JSON: {ex.Message}"], []);
        }

        using (document)
        {
            var reader = new UsdmDesignReader(options ?? new UsdmImportOptions());
            return await reader.ReadAsync(document.RootElement, resolve, cancellationToken).ConfigureAwait(false);
        }
    }
}
