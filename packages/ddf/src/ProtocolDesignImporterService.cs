using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Rules.Packs;

using static Cadence.Ddf.ProtocolDesignMerger;

namespace Cadence.Ddf;

/// <summary>
/// Implements <see cref="IProtocolDesignImporter"/> over <see cref="OdmDesignImporter"/>,
/// <see cref="UsdmDesignImporter"/>, and <see cref="CdiscLibraryTemplateCatalog"/>.
/// </summary>
public sealed class ProtocolDesignImporterService : IProtocolDesignImporter
{
    private readonly CdiscLibraryTemplateCatalog _catalog;

    /// <summary>Initializes a new instance of <see cref="ProtocolDesignImporterService"/>.</summary>
    public ProtocolDesignImporterService(CdiscLibraryTemplateCatalog? catalog = null)
    {
        _catalog = catalog ?? new CdiscLibraryTemplateCatalog();
    }

    /// <inheritdoc />
    public IReadOnlyList<BundledProtocolTemplate> ListBundledTemplates() => _catalog.ListTemplates();

    /// <inheritdoc />
    public async Task<ProtocolDesignImportResult> ImportAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        CancellationToken cancellationToken = default,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolveConcept);

        var hasTemplates = request.TemplateIds is { Count: > 0 };
        var hasBytes = request.RawBytes is { Length: > 0 };

        if (request.RawBytes is { Length: > IProtocolDesignImporter.MaxPayloadBytes } oversized)
        {
            return new ProtocolDesignImportResult(
                null,
                [$"Uploaded payload ({oversized.Length:N0} bytes) exceeds the 10 MB ({IProtocolDesignImporter.MaxPayloadBytes:N0} bytes) synchronous import limit."],
                [],
                PayloadTooLarge: true);
        }

        if (!hasTemplates && !hasBytes)
        {
            return new ProtocolDesignImportResult(
                null,
                ["Provide an ODM v1.3.2 XML file, a USDM v4 JSON file, or at least one bundled CDISC Library eCRF template."],
                []);
        }

        if (hasBytes)
        {
            var payloadRequest = hasTemplates ? request with { TemplateIds = null } : request;
            var payloadOutcome = await ImportPayloadBytesAsync(payloadRequest, resolveConcept, resolveTerminology, cancellationToken).ConfigureAwait(false);
            if (!hasTemplates || !payloadOutcome.IsSuccess || payloadOutcome.Design is null)
            {
                return payloadOutcome;
            }

            var templateOutcome = await ImportTemplatesAsync(
                request with
                {
                    RawBytes = null,
                    BaseDesign = payloadOutcome.Design,
                    ReplaceForms = false,
                },
                resolveConcept,
                resolveTerminology,
                cancellationToken).ConfigureAwait(false);
            if (!templateOutcome.IsSuccess || templateOutcome.Design is null)
            {
                return templateOutcome;
            }

            var mergedPack = ProtocolRulePackMerger.MergeRulePacks(
                payloadOutcome.RulePack,
                templateOutcome.RulePack,
                templateOutcome.Design.Title,
                out var packProblems);
            if (packProblems.Count > 0)
            {
                return new ProtocolDesignImportResult(null, packProblems, templateOutcome.Notes);
            }

            return templateOutcome with { RulePack = mergedPack };
        }

        return await ImportTemplatesAsync(request, resolveConcept, resolveTerminology, cancellationToken).ConfigureAwait(false);
    }

    private static Task<ProtocolDesignImportResult> ImportPayloadBytesAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        var detected = DetectFormat(request.RawBytes!, request.FileName, request.Format);
        return detected switch
        {
            ProtocolImportFormat.OdmXml => ImportOdmPayloadAsync(request, resolveConcept, resolveTerminology, cancellationToken),
            ProtocolImportFormat.UsdmJson => ProtocolUsdmPayloadImporter.ImportUsdmPayloadAsync(request, resolveConcept, resolveTerminology, cancellationToken),
            _ => Task.FromResult(new ProtocolDesignImportResult(
                null,
                ["Unsupported file format. Synchronous protocol import accepts CDISC ODM v1.3.2 XML (.xml) or CDISC USDM v4 JSON (.json) files."],
                [])),
        };
    }

    private async Task<ProtocolDesignImportResult> ImportTemplatesAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var notes = new List<ProtocolImportNote>();
        var importedSpecs = new List<CollectionSpec>();
        var importedRules = new List<Rule>();
        var specsByTemplate = new List<(string TemplateId, BundledProtocolTemplate? Metadata, IReadOnlyList<CollectionSpec> Specs)>();
        var batchUsedIds = request.ReplaceForms
            ? NonCollectionElementIds(request.BaseDesign)
            : NonScheduleAndNonCollectionElementIds(request.BaseDesign);
        string? firstTemplateTitle = null;
        string? firstStudyOid = null;

        var catalogById = _catalog.ListTemplates()
            .ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var assignmentsByTemplate = ProtocolTemplateScheduleBuilder.BuildTemplateAssignmentLookup(request.TemplateAssignments);

        foreach (var rawId in request.TemplateIds!.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trimmedId = rawId.Trim();
            var templateBytes = _catalog.ReadTemplateBytes(trimmedId);
            if (templateBytes is null)
            {
                problems.Add($"Bundled CDISC Library template '{trimmedId}' was not found.");
                continue;
            }

            var odm = await OdmDesignImporter.ImportAsync(templateBytes, resolveConcept, batchUsedIds, cancellationToken, resolveTerminology).ConfigureAwait(false);
            firstTemplateTitle ??= odm.Title;
            firstStudyOid ??= odm.StudyOid;
            notes.AddRange(odm.Notes);
            if (!odm.IsSuccess)
            {
                problems.AddRange(odm.Problems.Select(p => $"Template '{trimmedId}': {p}"));
                continue;
            }

            catalogById.TryGetValue(trimmedId, out var metadata);
            specsByTemplate.Add((trimmedId, metadata, odm.CollectionSpecs));
            importedSpecs.AddRange(odm.CollectionSpecs);
            if (odm.Rules is { Count: > 0 })
            {
                importedRules.AddRange(odm.Rules);
            }
        }

        var (templateEpochs, templateActivities) = ProtocolTemplateScheduleBuilder.BuildTemplateSchedule(
            request.BaseDesign,
            specsByTemplate,
            assignmentsByTemplate,
            problems);

        if (problems.Count > 0)
        {
            return new ProtocolDesignImportResult(null, problems, DeduplicateNotes(notes));
        }

        return FinalizeMergedFormsDesign(
            request.BaseDesign,
            importedSpecs,
            notes,
            request.FallbackTitle ?? firstTemplateTitle ?? "Protocol Draft",
            importedRules,
            firstStudyOid,
            request.ReplaceForms,
            templateEpochs,
            templateActivities);
    }

    private static async Task<ProtocolDesignImportResult> ImportOdmPayloadAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        var reservedIds = request.ReplaceForms
            ? NonCollectionElementIds(request.BaseDesign)
            : NonScheduleAndNonCollectionElementIds(request.BaseDesign);
        var odm = await OdmDesignImporter.ImportAsync(request.RawBytes!, resolveConcept, reservedIds, cancellationToken, resolveTerminology).ConfigureAwait(false);
        if (odm.PayloadTooLarge)
        {
            return new ProtocolDesignImportResult(null, odm.Problems, odm.Notes, PayloadTooLarge: true);
        }

        if (!odm.IsSuccess)
        {
            return new ProtocolDesignImportResult(null, odm.Problems, odm.Notes);
        }

        var fallbackTitle = FirstNonBlank(request.BaseDesign?.Title, odm.Title, request.FallbackTitle, "Imported ODM Protocol")!;
        return FinalizeMergedFormsDesign(
            request.BaseDesign,
            odm.CollectionSpecs,
            odm.Notes,
            fallbackTitle,
            odm.Rules,
            odm.StudyOid,
            request.ReplaceForms,
            odm.Epochs,
            odm.PlannedActivities);
    }

    private static ProtocolImportFormat DetectFormat(byte[] rawBytes, string? fileName, ProtocolImportFormat requested)
    {
        if (requested != ProtocolImportFormat.Auto)
        {
            return requested;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return ProtocolImportFormat.OdmXml;
            }

            if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return ProtocolImportFormat.UsdmJson;
            }
        }

        var span = ProtocolUsdmPayloadImporter.StripUtf8Bom(rawBytes);
        foreach (byte b in span)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                continue;
            }

            return b switch
            {
                (byte)'<' => ProtocolImportFormat.OdmXml,
                (byte)'{' or (byte)'[' => ProtocolImportFormat.UsdmJson,
                _ => ProtocolImportFormat.Auto,
            };
        }

        return ProtocolImportFormat.Auto;
    }
}
