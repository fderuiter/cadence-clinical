using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;

namespace Cadence.Ddf;

internal static class UsdmBiomedicalConceptItemBuilder
{
    public static async Task<CollectionItem?> BuildItemAsync(
        string activityId,
        string bcId,
        IReadOnlyDictionary<string, JsonElement> biomedicalConcepts,
        BiomedicalConceptResolver resolve,
        ProtocolTerminologyResolver? resolveTerminology,
        Dictionary<string, ConceptRef?> resolved,
        Dictionary<string, ProtocolTerminologyResolution> resolvedTerminology,
        List<string> problems,
        List<UsdmImportNote> notes,
        List<UsdmUnmappedConceptToken> unmappedConcepts,
        CancellationToken cancellationToken)
    {
        if (!biomedicalConcepts.TryGetValue(bcId, out var bc))
        {
            problems.Add($"{activityId}: Biomedical Concept '{bcId}' is not in the study version.");
            return null;
        }

        var code = Usdm.Code(bc.TryGetProperty("code", out var alias) ? alias : default)?.Code;
        if (code is null)
        {
            problems.Add($"{bcId}: the Biomedical Concept has no code.");
            return null;
        }

        if (!resolved.TryGetValue(code, out var concept))
        {
            concept = await resolve(code, cancellationToken).ConfigureAwait(false);
            resolved[code] = concept;
        }

        if (concept is not { } found)
        {
            unmappedConcepts.Add(new UsdmUnmappedConceptToken(code, Usdm.Str(bc, "name") ?? code, $"{activityId}.{bcId}"));
            return null;
        }

        var itemId = $"{activityId}.{bcId}";
        var enabledProps = Usdm.Arr(bc, "properties").Where(p => !IsExplicitlyDisabled(p)).ToList();
        if (HasItemRulesOrConditions(bc) || enabledProps.Any(HasItemRulesOrConditions))
        {
            notes.Add(new UsdmImportNote(itemId, "item rules and conditions on Biomedical Concept properties are not carried as executable rules."));
        }

        JsonElement? primaryProp = SelectPrimaryProperty(enabledProps, code);
        bool required = (primaryProp is { } reqProp ? ReadBool(reqProp, "isRequired") : null)
            ?? ReadBool(bc, "isRequired")
            ?? false;

        string? unit = null;
        string? carriedUnitPropId = null;
        if (primaryProp is { } explicitPrimary && UsdmUcumUnitResolver.TryReadExplicitUnit(explicitPrimary, itemId, notes, out var primaryUnit))
        {
            unit = primaryUnit;
        }
        else if (UsdmUcumUnitResolver.TryReadExplicitUnit(bc, itemId, notes, out var bcUnit))
        {
            unit = bcUnit;
        }
        else if (primaryProp is { } chosenPrimary)
        {
            var unitProps = enabledProps
                .Where(p => !IsSameProperty(p, chosenPrimary) && IsUnitProperty(p))
                .ToList();
            if (unitProps.Count > 0)
            {
                var unitProp = unitProps[0];
                if (UsdmUcumUnitResolver.TryReadExplicitUnit(unitProp, itemId, notes, out var propExplicitUnit))
                {
                    unit = propExplicitUnit;
                    carriedUnitPropId = Usdm.Id(unitProp);
                }
                else
                {
                    var unitCodes = ReadEnabledResponseCodes(unitProp);
                    if (unitCodes.Count == 1)
                    {
                        var single = unitCodes[0];
                        if (UsdmUcumUnitResolver.TryResolveUcumUnit(single.Code, single.Decode, allowArbitraryUcumExpression: false, out var mappedUcum))
                        {
                            unit = mappedUcum;
                            carriedUnitPropId = Usdm.Id(unitProp);
                        }
                        else
                        {
                            notes.Add(new UsdmImportNote(
                                itemId,
                                $"unit '{single.Decode}' ({single.Code}) on '{PropertyLabel(unitProp)}' has no canonical UCUM equivalent; imported item without fixed unit constraint."));
                        }
                    }
                }
            }
        }

        TerminologyBinding? terminology = null;
        var responseSource = primaryProp ?? bc;
        var responseCodes = ReadEnabledResponseCodes(responseSource);
        if (responseCodes.Count > 0)
        {
            var codelistCode = FirstNonBlank(
                PropertyStandardCode(responseSource),
                VariableStem(Usdm.Text(responseSource, "name")),
                Usdm.Id(responseSource),
                code)!;
            var codelistName = FirstNonBlank(
                Usdm.Text(responseSource, "label"),
                Usdm.Text(responseSource, "name"),
                Usdm.Text(bc, "label"),
                Usdm.Text(bc, "name"),
                codelistCode)!;

            if (resolveTerminology is not null)
            {
                var groupCode = OdmDesignImporter.BoundTerminologyToken(codelistCode);
                var entries = new List<CodeSystemEntryData>(responseCodes.Count);
                var seenCodes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var rc in responseCodes)
                {
                    var boundedCode = OdmDesignImporter.BoundTerminologyToken(rc.Code)!;
                    if (!seenCodes.Add(boundedCode))
                    {
                        continue;
                    }

                    entries.Add(new CodeSystemEntryData(
                        Code: boundedCode,
                        Display: FirstNonBlank(rc.Decode, rc.Label, rc.Code)!,
                        GroupCode: groupCode,
                        SubmissionValue: !string.Equals(boundedCode, rc.Code, StringComparison.Ordinal) ? rc.Code : null));
                }

                var primarySystem = responseCodes.Select(r => r.CodeSystem).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                var primaryVersion = responseCodes.Select(r => r.CodeSystemVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                var cacheKey = $"{codelistCode}|{primarySystem}|{primaryVersion}|{string.Join(",", entries.Select(e => e.Code))}";

                if (!resolvedTerminology.TryGetValue(cacheKey, out var termResolution))
                {
                    termResolution = await resolveTerminology(
                        new ProtocolTerminologyCandidate(
                            SourceKey: $"usdm:{bcId}:{Usdm.Id(responseSource)}",
                            CodelistCode: codelistCode,
                            CodelistName: codelistName,
                            CodeSystemUri: primarySystem,
                            CodeSystemName: primarySystem,
                            CodeSystemVersion: primaryVersion,
                            Entries: entries),
                        cancellationToken).ConfigureAwait(false);
                    resolvedTerminology[cacheKey] = termResolution;
                }

                if (!string.IsNullOrWhiteSpace(termResolution.ErrorMessage))
                {
                    problems.Add($"{itemId}: {termResolution.ErrorMessage}");
                    return null;
                }

                terminology = termResolution.Binding;
            }
            else
            {
                notes.Add(new UsdmImportNote(
                    itemId,
                    $"responseCodes ({responseCodes.Count}) on '{codelistName}' were not bound to a code-system version because no terminology resolver was supplied."));
            }
        }

        if (terminology is not null)
        {
            unit = null;
        }

        var rawDatatype = (primaryProp is { } dtProp ? Usdm.Text(dtProp, "datatype") : null) ?? Usdm.Text(bc, "datatype");
        var valueKind = terminology is not null
            ? ObservationValueKind.Coded
            : MapUsdmDatatype(rawDatatype, unit, itemId, notes);

        if (primaryProp is { } selectedPrimary)
        {
            var uncarriedQualifiers = enabledProps
                .Where(p => !IsSameProperty(p, selectedPrimary) && (carriedUnitPropId is null || !string.Equals(Usdm.Id(p), carriedUnitPropId, StringComparison.Ordinal)))
                .Select(PropertyLabel)
                .ToList();
            if (uncarriedQualifiers.Count > 0)
            {
                notes.Add(new UsdmImportNote(
                    itemId,
                    $"qualifier properties ({string.Join(", ", uncarriedQualifiers)}) are not carried; ADR-2473 keeps one item per Biomedical Concept."));
            }
        }

        return new CollectionItem(
            itemId,
            found,
            Unit: unit,
            Terminology: terminology,
            Required: required,
            Purpose: null,
            ValueKind: valueKind);
    }

    private static JsonElement? SelectPrimaryProperty(IReadOnlyList<JsonElement> enabledProps, string bcCode)
    {
        if (enabledProps.Count == 0)
        {
            return null;
        }

        foreach (var prop in enabledProps)
        {
            var stdCode = PropertyStandardCode(prop);
            var stem = VariableStem(Usdm.Text(prop, "name"));
            if (string.Equals(stdCode, "C70856", StringComparison.Ordinal)
                || (stem is not null
                    && (stem.EndsWith("ORRES", StringComparison.OrdinalIgnoreCase)
                        || stem.EndsWith("AVAL", StringComparison.OrdinalIgnoreCase))))
            {
                return prop;
            }
        }

        foreach (var prop in enabledProps)
        {
            if (!IsUnitProperty(prop)
                && !IsTimingOrTopicIdentifierProperty(prop, bcCode)
                && string.Equals(PropertyStandardCode(prop), bcCode, StringComparison.Ordinal))
            {
                return prop;
            }
        }

        foreach (var prop in enabledProps)
        {
            if (!IsUnitProperty(prop)
                && !IsTimingOrTopicIdentifierProperty(prop, bcCode)
                && (!string.IsNullOrWhiteSpace(Usdm.Text(prop, "datatype")) || ReadEnabledResponseCodes(prop).Count > 0))
            {
                return prop;
            }
        }

        foreach (var prop in enabledProps)
        {
            if (!IsUnitProperty(prop) && !IsTimingOrTopicIdentifierProperty(prop, bcCode))
            {
                return prop;
            }
        }

        return enabledProps[0];
    }

    private static ObservationValueKind? MapUsdmDatatype(string? rawDatatype, string? unit, string itemId, List<UsdmImportNote> notes)
    {
        if (string.IsNullOrWhiteSpace(rawDatatype))
        {
            return unit is not null ? ObservationValueKind.Quantity : null;
        }

        switch (rawDatatype.Trim().ToLowerInvariant())
        {
            case "decimal":
            case "float":
            case "double":
            case "number":
            case "quantity":
            case "integer":
            case "int":
                return ObservationValueKind.Quantity;
            case "boolean":
            case "bool":
                return ObservationValueKind.Boolean;
            case "date":
            case "datetime":
            case "time":
            case "partialdate":
            case "partialtime":
            case "partialdatetime":
            case "durationdatetime":
                return ObservationValueKind.DateTime;
            case "string":
            case "text":
            case "uri":
                return unit is not null ? ObservationValueKind.Quantity : ObservationValueKind.Text;
            default:
                notes.Add(new UsdmImportNote(itemId, $"datatype '{rawDatatype}' is not recognized and was approximated as Text."));
                return unit is not null ? ObservationValueKind.Quantity : ObservationValueKind.Text;
        }
    }

    private static List<UsdmResponseCodeItem> ReadEnabledResponseCodes(JsonElement element)
    {
        var list = new List<UsdmResponseCodeItem>();
        foreach (var rc in Usdm.Arr(element, "responseCodes"))
        {
            if (IsExplicitlyDisabled(rc))
            {
                continue;
            }

            var codeContainer = Usdm.Obj(rc, "code") ?? rc;
            if (codeContainer.ValueKind == JsonValueKind.Object && Usdm.Obj(codeContainer, "standardCode") is { } std)
            {
                codeContainer = std;
            }

            var code = Usdm.Text(codeContainer, "code");
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var decode = FirstNonBlank(Usdm.Text(codeContainer, "decode"), Usdm.Text(rc, "label"), Usdm.Text(rc, "name"), code)!;
            var label = FirstNonBlank(Usdm.Text(rc, "label"), Usdm.Text(rc, "name"));
            var system = Usdm.Text(codeContainer, "codeSystem");
            var version = Usdm.Text(codeContainer, "codeSystemVersion");
            list.Add(new UsdmResponseCodeItem(code, decode, label, system, version));
        }

        return list;
    }

    private static bool IsUnitProperty(JsonElement prop)
    {
        var stdCode = PropertyStandardCode(prop);
        if (stdCode is "C48208" or "C49669" or "C50400" or "C42578" or "C25709")
        {
            return true;
        }

        var stem = VariableStem(Usdm.Text(prop, "name"));
        if (stem is not null
            && (stem.EndsWith("ORRESU", StringComparison.OrdinalIgnoreCase)
                || stem.EndsWith("STRESU", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var label = Usdm.Text(prop, "label") ?? Usdm.Text(prop, "name");
        return label is not null
            && (string.Equals(label, "Unit", StringComparison.OrdinalIgnoreCase)
                || label.EndsWith(" Unit", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTimingOrTopicIdentifierProperty(JsonElement prop, string bcCode)
    {
        var stdCode = PropertyStandardCode(prop);
        if (string.Equals(stdCode, "C82515", StringComparison.Ordinal))
        {
            return true;
        }

        var stem = VariableStem(Usdm.Text(prop, "name"));
        if (stem is null)
        {
            return false;
        }

        if (stem.EndsWith("DTC", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith("TESTCD", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(stdCode, bcCode, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(Usdm.Text(prop, "datatype"))
            && ReadEnabledResponseCodes(prop).Count == 0
            && stem.EndsWith("TEST", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasItemRulesOrConditions(JsonElement element) =>
        Usdm.Arr(element, "rules").Count > 0
        || Usdm.Arr(element, "conditions").Count > 0
        || Usdm.Strs(element, "conditionIds").Count > 0
        || HasNonEmptyField(element, "rule")
        || HasNonEmptyField(element, "condition");

    private static bool HasNonEmptyField(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var val)
        && val.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(val.GetString()),
            JsonValueKind.Object => true,
            JsonValueKind.Array => val.GetArrayLength() > 0,
            _ => false,
        };

    private static bool IsExplicitlyDisabled(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("isEnabled", out var flag)
        && flag.ValueKind == JsonValueKind.False;

    private static bool? ReadBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var prop)
            ? prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static bool IsSameProperty(JsonElement left, JsonElement right) =>
        string.Equals(Usdm.Id(left), Usdm.Id(right), StringComparison.Ordinal)
        && string.Equals(Usdm.Str(left, "name"), Usdm.Str(right, "name"), StringComparison.Ordinal);

    private static string? PropertyStandardCode(JsonElement prop) =>
        Usdm.Obj(prop, "code") is { } codeObj ? Usdm.Code(codeObj)?.Code : null;

    private static string PropertyLabel(JsonElement prop) =>
        FirstNonBlank(VariableStem(Usdm.Text(prop, "name")), Usdm.Text(prop, "label"), Usdm.Text(prop, "name"), Usdm.Id(prop)) ?? "property";

    private static string? VariableStem(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var trimmed = rawName.Trim();
        var parenIdx = trimmed.IndexOf(" (", StringComparison.Ordinal);
        return parenIdx > 0 ? trimmed[..parenIdx].Trim() : trimmed;
    }

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    private sealed record UsdmResponseCodeItem(
        string Code,
        string Decode,
        string? Label,
        string? CodeSystem,
        string? CodeSystemVersion);
}
