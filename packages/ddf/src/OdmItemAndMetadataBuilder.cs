using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;

using static Cadence.Ddf.OdmXmlNavigationAndVendorNotes;

namespace Cadence.Ddf;

internal sealed record OdmImportedItemMeta(
    string FormId,
    string RawFormOid,
    string RawGroupOid,
    string ItemOid,
    string ItemName,
    CollectionItem Item,
    XElement ItemDef);

internal sealed record OdmParsedCodeList(
    string Oid,
    string Name,
    IReadOnlyList<string> Codes,
    IReadOnlyList<CodeSystemEntryData> Entries,
    string? ExternalDictionary,
    string? ExternalVersion);

internal static class OdmItemAndMetadataBuilder
{
    public static async Task<CollectionItem?> BuildItemAsync(
        string formId,
        string rawFormOid,
        string itemOid,
        XElement itemDef,
        bool required,
        IReadOnlyDictionary<string, string?> units,
        IReadOnlyDictionary<string, OdmParsedCodeList> codeLists,
        HashSet<string> usedIds,
        HashSet<string> notedCodeLists,
        Dictionary<string, ProtocolTerminologyResolution> resolvedCodeLists,
        List<ProtocolImportNote> notes,
        List<string> problems,
        ProtocolConceptResolver resolveConcept,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        var itemName = FirstNonBlank(Attr(itemDef, "Name"), itemOid)!;
        var prompt = ExtractItemPrompt(itemDef, itemName);
        var question = TranslatedText(Child(itemDef, "Question"));
        var description = TranslatedText(Child(itemDef, "Description"));
        var definition = FirstNonBlank(question, description, prompt)!;
        var extCode = ExtractExtCode(itemDef);

        var unitRefOid = Attr(Child(itemDef, "MeasurementUnitRef"), "MeasurementUnitOID");
        string? ucumUnit = null;
        if (!string.IsNullOrWhiteSpace(unitRefOid))
        {
            if (units.TryGetValue(unitRefOid, out var resolvedUnit))
            {
                ucumUnit = resolvedUnit;
            }
            else
            {
                notes.Add(new ProtocolImportNote(
                    $"{rawFormOid}/{itemOid}",
                    $"Item '{itemName}' references MeasurementUnitOID '{unitRefOid}', which is not declared in <BasicDefinitions>; imported without unit constraint."));
            }
        }

        TerminologyBinding? terminology = null;
        var codeListOid = Attr(Child(itemDef, "CodeListRef"), "CodeListOID");
        if (!string.IsNullOrWhiteSpace(codeListOid))
        {
            if (codeLists.TryGetValue(codeListOid, out var clForResolve) && resolveTerminology is not null)
            {
                if (!resolvedCodeLists.TryGetValue(codeListOid, out var termResolution))
                {
                    termResolution = await resolveTerminology(
                        new ProtocolTerminologyCandidate(
                            SourceKey: $"odm:{codeListOid}",
                            CodelistCode: codeListOid,
                            CodelistName: clForResolve.Name,
                            CodeSystemUri: clForResolve.ExternalDictionary,
                            CodeSystemName: clForResolve.ExternalDictionary,
                            CodeSystemVersion: clForResolve.ExternalVersion,
                            Entries: clForResolve.Entries),
                        cancellationToken).ConfigureAwait(false);
                    resolvedCodeLists[codeListOid] = termResolution;
                }

                if (!string.IsNullOrWhiteSpace(termResolution.ErrorMessage))
                {
                    problems.Add(termResolution.ErrorMessage);
                    return null;
                }

                terminology = termResolution.Binding;
            }

            if (terminology is null && notedCodeLists.Add($"{itemOid}:{codeListOid}"))
            {
                if (codeLists.TryGetValue(codeListOid, out var cl))
                {
                    string sample;
                    if (cl.Codes.Count == 0)
                    {
                        sample = "no coded values";
                    }
                    else
                    {
                        var moreSuffix = cl.Codes.Count > 5 ? $", +{cl.Codes.Count - 5} more" : string.Empty;
                        sample = string.Join(", ", cl.Codes.Take(5)) + moreSuffix;
                    }

                    notes.Add(new ProtocolImportNote(
                        $"{rawFormOid}/{itemOid}",
                        $"Unmapped terminology CodeList '{codeListOid}' ({cl.Name}: {sample}) on item '{itemName}' has no pinned code-system version in the tenant; imported item without terminology binding."));
                }
                else
                {
                    notes.Add(new ProtocolImportNote(
                        $"{rawFormOid}/{itemOid}",
                        $"Unmapped terminology CodeListRef '{codeListOid}' on item '{itemName}' is not defined in <MetaDataVersion>; imported item without terminology binding."));
                }
            }
        }

        if (terminology is not null)
        {
            ucumUnit = null;
        }

        var valueKind = terminology is not null
            ? ObservationValueKind.Coded
            : MapDataType(Attr(itemDef, "DataType"), ucumUnit, itemOid, itemName, notes);
        var stableKey = $"odm:{itemOid}:{prompt}";
        var resolution = await resolveConcept(
            new ProtocolConceptCandidate(stableKey, extCode, prompt, definition, ConceptKind.DataElement),
            cancellationToken).ConfigureAwait(false);
        if (resolution.Concept is not { } conceptRef)
        {
            problems.Add(resolution.ErrorMessage ?? $"Could not resolve or define a concept for ODM item '{itemOid}'.");
            return null;
        }

        if (resolution.UsedFallbackSponsorConcept && !string.IsNullOrWhiteSpace(extCode))
        {
            notes.Add(new ProtocolImportNote(
                $"{rawFormOid}/{itemOid}",
                $"Unmapped terminology code '{extCode}' on item '{itemName}' is not registered in the tenant Concept Registry; bound to a sponsor-local concept."));
        }

        var candidateId = OdmDesignImporter.SanitizeElementId(itemOid, "Item");
        if (usedIds.Contains(candidateId))
        {
            candidateId = OdmDesignImporter.SanitizeElementId($"{formId}.{itemOid}", "Item");
        }

        var itemId = OdmDesignImporter.AllocateUniqueElementId(candidateId, "Item", usedIds);
        return new CollectionItem(
            itemId,
            conceptRef,
            Unit: ucumUnit,
            Terminology: terminology,
            Required: required,
            Purpose: null,
            ValueKind: valueKind,
            Prompt: prompt);
    }

    public static Dictionary<string, string?> ParseMeasurementUnits(XElement study, List<ProtocolImportNote> notes)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var basic = Child(study, "BasicDefinitions");
        if (basic is null)
        {
            return result;
        }

        foreach (var unit in Children(basic, "MeasurementUnit"))
        {
            var oid = Attr(unit, "OID");
            if (string.IsNullOrWhiteSpace(oid))
            {
                continue;
            }

            var symbol = FirstNonBlank(TranslatedText(Child(unit, "Symbol")), Attr(unit, "Name"));
            if (string.IsNullOrWhiteSpace(symbol))
            {
                result[oid] = null;
                continue;
            }

            if (Ucum.Problem(symbol) is null)
            {
                result[oid] = symbol;
            }
            else
            {
                notes.Add(new ProtocolImportNote(
                    oid,
                    $"MeasurementUnit '{oid}' symbol '{symbol}' is not a canonical UCUM expression; imported numeric items without fixed unit constraint."));
                result[oid] = null;
            }
        }

        return result;
    }

    public static Dictionary<string, OdmParsedCodeList> ParseCodeLists(XElement mdv)
    {
        var result = new Dictionary<string, OdmParsedCodeList>(StringComparer.Ordinal);
        foreach (var cl in Children(mdv, "CodeList"))
        {
            var oid = Attr(cl, "OID");
            if (string.IsNullOrWhiteSpace(oid))
            {
                continue;
            }

            var name = FirstNonBlank(Attr(cl, "Name"), oid)!;
            var groupCode = OdmDesignImporter.BoundTerminologyToken(oid);
            var codes = new List<string>();
            var entries = new List<CodeSystemEntryData>();
            var seenBoundedCodes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in Children(cl, "CodeListItem").Concat(Children(cl, "EnumeratedItem")))
            {
                var rawCode = Attr(item, "CodedValue");
                if (string.IsNullOrWhiteSpace(rawCode))
                {
                    continue;
                }

                codes.Add(rawCode);
                var boundedCode = OdmDesignImporter.BoundTerminologyToken(rawCode)!;
                if (!seenBoundedCodes.Add(boundedCode))
                {
                    continue;
                }

                var decode = TranslatedText(Child(item, "Decode"));
                var display = FirstNonBlank(decode, rawCode)!;
                var itemExtCode = ExtractExtCode(item);
                var submissionValue = !string.Equals(boundedCode, rawCode, StringComparison.Ordinal)
                    ? rawCode
                    : itemExtCode;
                entries.Add(new CodeSystemEntryData(
                    Code: boundedCode,
                    Display: display,
                    GroupCode: groupCode,
                    SubmissionValue: submissionValue));
            }

            var externalEl = Child(cl, "ExternalCodeList");
            var externalDict = FirstNonBlank(
                Attr(externalEl, "href"),
                Attr(externalEl, "ref"),
                Attr(externalEl, "Dictionary"));
            var externalVersion = Attr(externalEl, "Version");

            result[oid] = new OdmParsedCodeList(oid, name, codes, entries, externalDict, externalVersion);
        }

        return result;
    }

    private static ObservationValueKind? MapDataType(
        string? dataType,
        string? ucumUnit,
        string itemOid,
        string itemName,
        List<ProtocolImportNote> notes)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return ucumUnit is not null ? ObservationValueKind.Quantity : ObservationValueKind.Text;
        }

        switch (dataType.Trim().ToLowerInvariant())
        {
            case "text":
            case "string":
            case "uri":
                return ObservationValueKind.Text;
            case "integer":
            case "float":
            case "double":
            case "decimal":
            case "hexfloat":
            case "base64float":
                return ObservationValueKind.Quantity;
            case "boolean":
                return ObservationValueKind.Boolean;
            case "date":
            case "time":
            case "datetime":
            case "partialdate":
            case "partialtime":
            case "partialdatetime":
            case "incompletedatetime":
            case "durationdatetime":
            case "intervaldatetime":
                return ObservationValueKind.DateTime;
            default:
                notes.Add(new ProtocolImportNote(
                    itemOid,
                    $"ODM DataType '{dataType}' on item '{itemName}' was approximated as text."));
                return ObservationValueKind.Text;
        }
    }

    private static string ExtractItemPrompt(XElement itemDef, string fallbackName)
    {
        foreach (var alias in Children(itemDef, "Alias"))
        {
            var ctx = NormalizeAliasContext(Attr(alias, "Context"));
            var name = Attr(alias, "Name");
            if (string.Equals(ctx, "prompt", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        if (TranslatedText(Child(itemDef, "Question")) is { } question)
        {
            return question;
        }

        foreach (var alias in Children(itemDef, "Alias"))
        {
            var ctx = NormalizeAliasContext(Attr(alias, "Context"));
            var name = Attr(alias, "Name");
            if (string.Equals(ctx, "questiontext", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return FirstNonBlank(TranslatedText(Child(itemDef, "Description")), fallbackName) ?? fallbackName;
    }

    private static string? ExtractExtCode(XElement itemDef)
    {
        foreach (var alias in Children(itemDef, "Alias"))
        {
            var ctx = Attr(alias, "Context");
            var name = Attr(alias, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var trimmedName = name.Trim();
            if (!string.IsNullOrWhiteSpace(ctx) && ctx.Contains("ExtCodeID", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedName;
            }

            const string prefix = "extCodeID:";
            if (trimmedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var code = trimmedName[prefix.Length..].Trim();
                if (code.Length > 0)
                {
                    return code;
                }
            }

            if (!string.IsNullOrWhiteSpace(ctx)
                && (ctx.Contains("NCI", StringComparison.OrdinalIgnoreCase)
                    || ctx.Contains("CDISC", StringComparison.OrdinalIgnoreCase)
                    || ctx.Contains("CDASH", StringComparison.OrdinalIgnoreCase)
                    || ctx.Contains("SDTM", StringComparison.OrdinalIgnoreCase))
                && trimmedName.Length >= 2
                && (trimmedName[0] == 'C' || trimmedName[0] == 'c')
                && trimmedName[1..].All(char.IsAsciiDigit))
            {
                return trimmedName.ToUpperInvariant();
            }
        }

        return null;
    }
}
