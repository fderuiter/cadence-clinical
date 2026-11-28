using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;

using static Cadence.Ddf.ProtocolDesignMerger;

namespace Cadence.Ddf;

internal static class ProtocolUsdmPayloadImporter
{
    public static async Task<ProtocolDesignImportResult> ImportUsdmPayloadAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = DecodeUtf8(request.RawBytes!);
        }
        catch (DecoderFallbackException)
        {
            return new ProtocolDesignImportResult(null, ["Uploaded JSON file is not valid UTF-8 text."], []);
        }

        var bcMetadata = ExtractUsdmBiomedicalConceptMetadata(json, out var vendorNotes);
        var conceptProblems = new List<string>();
        var unmappedConceptNotes = new List<ProtocolImportNote>();

        async Task<ConceptRef?> ResolveBcAsync(string cdiscCode, CancellationToken ct)
        {
            var meta = bcMetadata.GetValueOrDefault(cdiscCode);
            var label = FirstNonBlank(meta?.Label, meta?.Name, cdiscCode)!;
            var definition = FirstNonBlank(meta?.Description, meta?.Label, meta?.Name, $"Biomedical concept {cdiscCode}")!;
            var resolution = await resolveConcept(
                new ProtocolConceptCandidate($"usdm:{cdiscCode}:{label}", cdiscCode, label, definition, ConceptKind.BiomedicalConcept),
                ct).ConfigureAwait(false);
            if (resolution.Concept is not { } resolved)
            {
                conceptProblems.Add(resolution.ErrorMessage ?? $"Could not resolve or define Biomedical Concept '{cdiscCode}'.");
                return null;
            }

            if (resolution.UsedFallbackSponsorConcept)
            {
                unmappedConceptNotes.Add(new ProtocolImportNote(
                    cdiscCode,
                    $"Unmapped terminology code '{cdiscCode}' ({label}) is not in the tenant Concept Registry; registered as a sponsor-local concept."));
            }

            return resolved;
        }

        var staged = await UsdmDesignImporter.StageAsync(
            json,
            ResolveBcAsync,
            options: null,
            cancellationToken,
            resolveTerminology).ConfigureAwait(false);
        var combinedNotes = staged.Notes
            .Select(n => new ProtocolImportNote(n.Source, n.Message))
            .Concat(unmappedConceptNotes)
            .Concat(vendorNotes)
            .Distinct()
            .ToList();

        if (conceptProblems.Count > 0 || staged.Problems.Count > 0 || staged.Design is null)
        {
            return new ProtocolDesignImportResult(null, [.. staged.Problems, .. conceptProblems], combinedNotes);
        }

        var promptsByBcId = ExtractUsdmPromptsByBcId(json);
        var enrichedSpecs = staged.Design.CollectionSpecs.Select(spec =>
        {
            var enrichedItems = spec.Items.Select(item =>
            {
                var dot = item.Id.LastIndexOf('.');
                var bcId = dot >= 0 ? item.Id[(dot + 1)..] : item.Id;
                var prompt = promptsByBcId.GetValueOrDefault(bcId) ?? item.Prompt;
                return prompt is not null ? item with { Prompt = prompt } : item;
            }).ToList();
            return spec with { Items = enrichedItems };
        }).ToList();

        var finalTitle = FirstNonBlank(staged.Design.Title, request.FallbackTitle, "Imported USDM Protocol")!;
        if (request.BaseDesign is null)
        {
            var enrichedDesign = staged.Design with
            {
                Title = finalTitle,
                CollectionSpecs = enrichedSpecs,
                ImportNotes = combinedNotes.Count > 0 ? combinedNotes : null,
            };

            var validation = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(enrichedDesign));
            return validation.IsValid
                ? new ProtocolDesignImportResult(validation.Design, [], combinedNotes)
                : new ProtocolDesignImportResult(null, validation.Problems, combinedNotes);
        }

        if (request.ReplaceForms)
        {
            return FinalizeMergedFormsDesign(
                request.BaseDesign,
                enrichedSpecs,
                combinedNotes,
                finalTitle,
                replaceForms: true);
        }

        return MergeUsdmDesignOntoBase(request.BaseDesign, staged.Design, enrichedSpecs, combinedNotes, finalTitle);
    }

    internal static ReadOnlySpan<byte> StripUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes.AsSpan();

    private static string DecodeUtf8(byte[] rawBytes)
    {
        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        return utf8Strict.GetString(StripUtf8Bom(rawBytes));
    }

    private static Dictionary<string, UsdmBcMeta> ExtractUsdmBiomedicalConceptMetadata(
        string json,
        out List<ProtocolImportNote> vendorNotes)
    {
        vendorNotes = [];
        var map = new Dictionary<string, UsdmBcMeta>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            CollectUsdmExtensionNotes(doc.RootElement, "$", vendorNotes);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("study", out var study)
                && study.ValueKind == JsonValueKind.Object
                && study.TryGetProperty("versions", out var versions)
                && versions.ValueKind == JsonValueKind.Array)
            {
                foreach (var version in versions.EnumerateArray())
                {
                    if (version.ValueKind != JsonValueKind.Object
                        || !version.TryGetProperty("biomedicalConcepts", out var bcs)
                        || bcs.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var bc in bcs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                    {
                        var code = ReadNestedCode(bc);
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            continue;
                        }

                        map[code] = new UsdmBcMeta(
                            ReadStr(bc, "name"),
                            ReadStr(bc, "label"),
                            ReadStr(bc, "reference") ?? ReadStr(bc, "description"));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Handled and reported by UsdmDesignImporter.StageAsync.
        }

        return map;
    }

    private static Dictionary<string, string> ExtractUsdmPromptsByBcId(string json)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("study", out var study)
                && study.ValueKind == JsonValueKind.Object
                && study.TryGetProperty("versions", out var versions)
                && versions.ValueKind == JsonValueKind.Array)
            {
                foreach (var version in versions.EnumerateArray())
                {
                    if (version.ValueKind != JsonValueKind.Object
                        || !version.TryGetProperty("biomedicalConcepts", out var bcs)
                        || bcs.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var bc in bcs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                    {
                        var id = ReadStr(bc, "id");
                        var prompt = FirstNonBlank(ReadStr(bc, "label"), ReadStr(bc, "name"));
                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(prompt))
                        {
                            map[id] = prompt;
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignored after StageAsync validation.
        }

        return map;
    }

    private static void CollectUsdmExtensionNotes(JsonElement element, string path, List<ProtocolImportNote> notes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var owner = ReadStr(element, "id") ?? ReadStr(element, "name") ?? path;
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "extensionAttributes", StringComparison.Ordinal)
                        && prop.Value.ValueKind == JsonValueKind.Array
                        && prop.Value.GetArrayLength() > 0)
                    {
                        foreach (var ext in prop.Value.EnumerateArray())
                        {
                            var url = ext.ValueKind == JsonValueKind.Object
                                ? ReadStr(ext, "url") ?? ReadStr(ext, "name") ?? "extensionAttribute"
                                : "extensionAttribute";
                            notes.Add(new ProtocolImportNote(
                                owner,
                                $"Optional USDM vendor extension attribute '{url}' on '{owner}' was noted in the draft summary."));
                        }
                    }
                    else if (prop.Name.StartsWith("x-", StringComparison.OrdinalIgnoreCase)
                             || prop.Name.StartsWith("vendor", StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add(new ProtocolImportNote(
                            owner,
                            $"Optional vendor attribute '{prop.Name}' on '{owner}' was noted in the draft summary."));
                    }
                    else
                    {
                        CollectUsdmExtensionNotes(prop.Value, $"{path}.{prop.Name}", notes);
                    }
                }

                break;

            case JsonValueKind.Array:
                int idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectUsdmExtensionNotes(item, $"{path}[{idx++}]", notes);
                }

                break;
        }
    }

    private static string? ReadNestedCode(JsonElement bc)
    {
        if (!bc.TryGetProperty("code", out var codeEl) || codeEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (codeEl.TryGetProperty("standardCode", out var stdEl) && stdEl.ValueKind == JsonValueKind.Object)
        {
            return ReadStr(stdEl, "code");
        }

        return ReadStr(codeEl, "code");
    }

    private static string? ReadStr(JsonElement obj, string propName) =>
        obj.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()?.Trim()
            : null;

    private sealed record UsdmBcMeta(string? Name, string? Label, string? Description);
}
