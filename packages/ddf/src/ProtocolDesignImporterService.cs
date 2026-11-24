using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

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

        if (hasTemplates)
        {
            return await ImportTemplatesAsync(request, resolveConcept, resolveTerminology, cancellationToken).ConfigureAwait(false);
        }

        var detected = DetectFormat(request.RawBytes!, request.FileName, request.Format);
        return detected switch
        {
            ProtocolImportFormat.OdmXml => await ImportOdmPayloadAsync(request, resolveConcept, resolveTerminology, cancellationToken).ConfigureAwait(false),
            ProtocolImportFormat.UsdmJson => await ImportUsdmPayloadAsync(request, resolveConcept, resolveTerminology, cancellationToken).ConfigureAwait(false),
            _ => new ProtocolDesignImportResult(
                null,
                ["Unsupported file format. Synchronous protocol import accepts CDISC ODM v1.3.2 XML (.xml) or CDISC USDM v4 JSON (.json) files."],
                []),
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
        var batchUsedIds = NonCollectionElementIds(request.BaseDesign);
        string? firstTemplateTitle = null;
        string? firstStudyOid = null;

        foreach (var rawId in request.TemplateIds!.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var templateBytes = _catalog.ReadTemplateBytes(rawId);
            if (templateBytes is null)
            {
                problems.Add($"Bundled CDISC Library template '{rawId}' was not found.");
                continue;
            }

            var odm = await OdmDesignImporter.ImportAsync(templateBytes, resolveConcept, batchUsedIds, cancellationToken, resolveTerminology).ConfigureAwait(false);
            firstTemplateTitle ??= odm.Title;
            firstStudyOid ??= odm.StudyOid;
            notes.AddRange(odm.Notes);
            if (!odm.IsSuccess)
            {
                problems.AddRange(odm.Problems.Select(p => $"Template '{rawId}': {p}"));
                continue;
            }

            importedSpecs.AddRange(odm.CollectionSpecs);
            if (odm.Rules is { Count: > 0 })
            {
                importedRules.AddRange(odm.Rules);
            }
            else
            {
                var starterRules = _catalog.SynthesizeStarterRules(rawId, odm.CollectionSpecs);
                if (starterRules.Count > 0)
                {
                    importedRules.AddRange(starterRules);
                }
            }
        }

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
            firstStudyOid);
    }

    private static async Task<ProtocolDesignImportResult> ImportOdmPayloadAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        var reservedIds = NonCollectionElementIds(request.BaseDesign);
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
        return FinalizeMergedFormsDesign(request.BaseDesign, odm.CollectionSpecs, odm.Notes, fallbackTitle, odm.Rules, odm.StudyOid);
    }

    private static async Task<ProtocolDesignImportResult> ImportUsdmPayloadAsync(
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

        // Enrich imported USDM items with human-readable prompts from BiomedicalConcept labels/names.
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
        var enrichedDesign = staged.Design with
        {
            Title = finalTitle,
            CollectionSpecs = enrichedSpecs,
            ImportNotes = combinedNotes.Count > 0 ? combinedNotes : null,
        };

        var validation = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(enrichedDesign));
        if (!validation.IsValid)
        {
            return new ProtocolDesignImportResult(null, validation.Problems, combinedNotes);
        }

        var starterPack = SynthesizeUsdmStarterRulePack(json, validation.Design!, finalTitle, out var packProblems);
        return packProblems.Count > 0
            ? new ProtocolDesignImportResult(null, packProblems, combinedNotes)
            : new ProtocolDesignImportResult(validation.Design, [], combinedNotes, RulePack: starterPack);
    }

    private static ProtocolDesignImportResult FinalizeMergedFormsDesign(
        ProtocolDesign? baseDesign,
        IReadOnlyList<CollectionSpec> incomingSpecs,
        IReadOnlyList<ProtocolImportNote> incomingNotes,
        string fallbackTitle,
        IReadOnlyList<Rule>? incomingRules = null,
        string? studyOid = null)
    {
        var mergedSpecs = new List<CollectionSpec>();
        var usedIds = NonCollectionElementIds(baseDesign);
        var idRenameMap = new Dictionary<string, string>(StringComparer.Ordinal);

        if (baseDesign is not null)
        {
            var incomingKeys = incomingSpecs.Select(s => (s.Id, s.Name)).ToHashSet();
            foreach (var existing in baseDesign.CollectionSpecs)
            {
                if (incomingKeys.Contains((existing.Id, existing.Name)))
                {
                    // Replaced in-place below so idempotent retries produce byte-for-byte identical ProtocolDesign JSON.
                    continue;
                }

                usedIds.Add(existing.Id);
                foreach (var item in existing.Items)
                {
                    usedIds.Add(item.Id);
                }

                mergedSpecs.Add(existing);
            }
        }

        foreach (var spec in incomingSpecs)
        {
            var specId = OdmDesignImporter.AllocateUniqueElementId(spec.Id, "Form", usedIds);
            idRenameMap[spec.Id] = specId;
            var adjustedItems = spec.Items.Select(item =>
            {
                var candidate = usedIds.Contains(item.Id) ? $"{specId}.{item.Id}" : item.Id;
                var itemId = OdmDesignImporter.AllocateUniqueElementId(candidate, "Item", usedIds);
                idRenameMap[item.Id] = itemId;
                idRenameMap[$"{spec.Id}.{item.Id}"] = $"{specId}.{itemId}";
                return itemId == item.Id ? item : item with { Id = itemId };
            }).ToList();
            mergedSpecs.Add(spec with { Id = specId, Items = adjustedItems });
        }

        var mergedNotes = DeduplicateNotes((baseDesign?.ImportNotes ?? []).Concat(incomingNotes));
        var title = FirstNonBlank(baseDesign?.Title, fallbackTitle, "Protocol Draft")!;
        var candidateDesign = (baseDesign ?? new ProtocolDesign()) with
        {
            Title = title,
            CollectionSpecs = mergedSpecs,
            ImportNotes = mergedNotes.Count > 0 ? mergedNotes : null,
        };

        var check = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(candidateDesign));
        if (!check.IsValid)
        {
            return new ProtocolDesignImportResult(null, check.Problems, mergedNotes);
        }

        ImportedRulePack? importedPack = null;
        if (incomingRules is { Count: > 0 })
        {
            var usedRuleIds = new HashSet<string>(StringComparer.Ordinal);
            var remappedRules = incomingRules.Select(rule =>
            {
                var mappedDataset = rule.Applicability.Dataset is not null && idRenameMap.TryGetValue(rule.Applicability.Dataset, out var ds)
                    ? ds
                    : rule.Applicability.Dataset;
                var mappedCondition = OdmFormalExpressionParser.RewriteVariables(
                    rule.Condition,
                    varName => idRenameMap.TryGetValue(varName, out var mappedVar) ? mappedVar : varName);
                var uniqueRuleId = OdmDesignImporter.AllocateRuleId(rule.Id, usedRuleIds);
                return rule with
                {
                    Id = uniqueRuleId,
                    Applicability = rule.Applicability with { Dataset = mappedDataset },
                    Condition = mappedCondition,
                };
            }).ToList();

            var packTitle = $"{title} Edit Checks";
            if (packTitle.Length > RulePackJson.MaxTextLength)
            {
                packTitle = packTitle[..RulePackJson.MaxTextLength];
            }

            var pack = new RulePack(RuleAuthority.Study, packTitle, remappedRules);
            var packJson = RulePackJson.ToJson(pack);
            var (_, packProblems) = RulePackJson.Read(packJson);
            if (packProblems.Count > 0)
            {
                return new ProtocolDesignImportResult(null, packProblems, mergedNotes);
            }

            var keySlug = BuildLibraryKeySlug(FirstNonBlank(studyOid, title, "odm")!);
            importedPack = new ImportedRulePack(keySlug, packTitle, remappedRules.Count, packJson);
        }

        return new ProtocolDesignImportResult(check.Design, [], mergedNotes, RulePack: importedPack);
    }

    private static string BuildLibraryKeySlug(string source, string prefix = "odm-")
    {
        var sb = new StringBuilder(prefix);
        bool lastDash = true;
        foreach (var ch in source)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 52)
        {
            slug = slug[..52].TrimEnd('-');
        }

        return $"{slug}-edit-checks";
    }

    private static HashSet<string> NonCollectionElementIds(ProtocolDesign? design)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (design is null)
        {
            return ids;
        }

        foreach (var entry in design.Elements().Where(e => e.Kind is not (ProtocolElementKind.CollectionSpec or ProtocolElementKind.CollectionItem)))
        {
            ids.Add(entry.Id);
        }

        return ids;
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

        var span = StripUtf8Bom(rawBytes);
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

    private static string DecodeUtf8(byte[] rawBytes)
    {
        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        return utf8Strict.GetString(StripUtf8Bom(rawBytes));
    }

    private static ReadOnlySpan<byte> StripUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes.AsSpan();

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

    private static ImportedRulePack? SynthesizeUsdmStarterRulePack(
        string json,
        ProtocolDesign design,
        string title,
        out IReadOnlyList<string> problems)
    {
        problems = [];
        if (design.CollectionSpecs.Count == 0)
        {
            return null;
        }

        var boundsByBcId = ExtractUsdmBoundsByBcId(json, out var studyIdentifier);
        var rules = new List<Rule>();
        var usedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        var citationDoc = TruncateRuleText($"CDISC USDM v4 Protocol Import ({title})");

        foreach (var spec in design.CollectionSpecs.Where(s => s.Id.Length <= FindingTarget.MaxNameLength))
        {
            string? sbpItemId = null;
            string? dbpItemId = null;

            foreach (var item in spec.Items.Where(i => i.Id.Length <= FindingTarget.MaxNameLength))
            {
                var dot = item.Id.LastIndexOf('.');
                var bcId = dot >= 0 ? item.Id[(dot + 1)..] : item.Id;
                boundsByBcId.TryGetValue(bcId, out var bounds);

                if (string.Equals(bounds?.ConceptCode, "C25298", StringComparison.OrdinalIgnoreCase))
                {
                    sbpItemId ??= item.Id;
                }
                else if (string.Equals(bounds?.ConceptCode, "C25299", StringComparison.OrdinalIgnoreCase))
                {
                    dbpItemId ??= item.Id;
                }

                var label = FirstNonBlank(item.Prompt, bounds?.Label, item.Id)!;
                var section = FirstNonBlank(bounds?.Reference, $"{spec.Id}/{bcId}");
                var variable = new VariableExpression(item.Id);

                if (item.Required || (bounds?.HasRequiredProperty ?? false))
                {
                    rules.Add(new Rule(
                        OdmDesignImporter.AllocateRuleId($"REQ.{item.Id}", usedRuleIds),
                        1,
                        RuleAuthority.Study,
                        new SourceCitation(citationDoc, section, null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                        ValidationSeverity.Warning,
                        new FunctionExpression(RuleFunction.IsEmpty, [variable]),
                        TruncateRuleText($"Required item '{label}' is missing on '{spec.Name}'."),
                        "Enter the required clinical value or record why it was not collected.",
                        EffectivePeriod.Always));
                }

                if (bounds?.ResponseValues is { Count: > 0 and <= 100 } responseValues)
                {
                    var sample = responseValues.Count <= 6
                        ? string.Join(", ", responseValues)
                        : $"{string.Join(", ", responseValues.Take(6))}, +{responseValues.Count - 6} more";
                    var condition = new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [variable]),
                            new FunctionExpression(
                                RuleFunction.NotIn,
                                [variable, .. responseValues.Select(v => (RuleExpression)new LiteralExpression(RuleValue.FromText(v)))]),
                        ]);
                    rules.Add(new Rule(
                        OdmDesignImporter.AllocateRuleId($"CL.{item.Id}", usedRuleIds),
                        1,
                        RuleAuthority.Study,
                        new SourceCitation(citationDoc, section, null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                        ValidationSeverity.Warning,
                        condition,
                        TruncateRuleText($"Item '{label}' must match a permitted response code ({sample})."),
                        "Select one of the protocol's permitted response values.",
                        EffectivePeriod.Always));
                }

                if (bounds?.MinValue is { } minVal)
                {
                    rules.Add(new Rule(
                        OdmDesignImporter.AllocateRuleId($"MIN.{item.Id}", usedRuleIds),
                        1,
                        RuleAuthority.Study,
                        new SourceCitation(citationDoc, section, null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                        ValidationSeverity.Warning,
                        new ComparisonExpression(ComparisonOperator.Less, variable, new LiteralExpression(RuleValue.FromNumber(minVal))),
                        TruncateRuleText($"Item '{label}' must be at least {minVal.ToString(CultureInfo.InvariantCulture)}."),
                        "Verify the recorded measurement against the protocol range.",
                        EffectivePeriod.Always));
                }

                if (bounds?.MaxValue is { } maxVal)
                {
                    rules.Add(new Rule(
                        OdmDesignImporter.AllocateRuleId($"MAX.{item.Id}", usedRuleIds),
                        1,
                        RuleAuthority.Study,
                        new SourceCitation(citationDoc, section, null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                        ValidationSeverity.Warning,
                        new ComparisonExpression(ComparisonOperator.Greater, variable, new LiteralExpression(RuleValue.FromNumber(maxVal))),
                        TruncateRuleText($"Item '{label}' must not exceed {maxVal.ToString(CultureInfo.InvariantCulture)}."),
                        "Verify the recorded measurement against the protocol range.",
                        EffectivePeriod.Always));
                }
            }

            if (sbpItemId is not null && dbpItemId is not null)
            {
                rules.Add(new Rule(
                    OdmDesignImporter.AllocateRuleId($"BP.{spec.Id}", usedRuleIds),
                    1,
                    RuleAuthority.Study,
                    new SourceCitation(citationDoc, $"{spec.Id}/C25298-C25299", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                    ValidationSeverity.Warning,
                    new ComparisonExpression(ComparisonOperator.LessOrEqual, new VariableExpression(sbpItemId), new VariableExpression(dbpItemId)),
                    TruncateRuleText($"Systolic Blood Pressure must exceed Diastolic Blood Pressure on '{spec.Name}'."),
                    "Verify both blood pressure measurements on the source record.",
                    EffectivePeriod.Always));
            }
        }

        if (rules.Count == 0)
        {
            return null;
        }

        var packTitle = TruncateRuleText($"{title} Edit Checks");
        var pack = new RulePack(RuleAuthority.Study, packTitle, rules);
        var packJson = RulePackJson.ToJson(pack);
        var (_, readProblems) = RulePackJson.Read(packJson);
        if (readProblems.Count > 0)
        {
            problems = readProblems;
            return null;
        }

        var keySlug = BuildLibraryKeySlug(FirstNonBlank(studyIdentifier, title, "usdm")!, "usdm-");
        return new ImportedRulePack(keySlug, packTitle, rules.Count, packJson);
    }

    private static Dictionary<string, UsdmBcBounds> ExtractUsdmBoundsByBcId(string json, out string? studyIdentifier)
    {
        studyIdentifier = null;
        var map = new Dictionary<string, UsdmBcBounds>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("study", out var study)
                || study.ValueKind != JsonValueKind.Object)
            {
                return map;
            }

            studyIdentifier = FirstNonBlank(ReadStr(study, "name"), ReadStr(study, "id"));
            if (!study.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            {
                return map;
            }

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
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var conceptCode = ReadNestedCode(bc);
                    var label = FirstNonBlank(ReadStr(bc, "label"), ReadStr(bc, "name"));
                    var reference = ReadStr(bc, "reference");
                    bool hasRequired = false;
                    decimal? minValue = TryReadDecimalBound(bc, "minValue", "minimum");
                    decimal? maxValue = TryReadDecimalBound(bc, "maxValue", "maximum");
                    List<string>? responseValues = null;

                    if (bc.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Array)
                    {
                        var enabledProps = props.EnumerateArray()
                            .Where(p => p.ValueKind == JsonValueKind.Object
                                && (!p.TryGetProperty("isEnabled", out var en) || en.ValueKind != JsonValueKind.False))
                            .ToList();

                        hasRequired = enabledProps.Any(p => p.TryGetProperty("isRequired", out var req) && req.ValueKind == JsonValueKind.True);
                        if (enabledProps.Count > 0)
                        {
                            var primaryProp = enabledProps[0];
                            minValue ??= TryReadDecimalBound(primaryProp, "minValue", "minimum");
                            maxValue ??= TryReadDecimalBound(primaryProp, "maxValue", "maximum");
                            responseValues = ExtractResponseValues(primaryProp);
                        }
                    }

                    if (hasRequired || responseValues is { Count: > 0 } || minValue is not null || maxValue is not null || !string.IsNullOrWhiteSpace(conceptCode))
                    {
                        map[id] = new UsdmBcBounds(conceptCode, label, reference, hasRequired, responseValues, minValue, maxValue);
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

    private static List<string>? ExtractResponseValues(JsonElement prop)
    {
        if (!prop.TryGetProperty("responseCodes", out var rcArr) || rcArr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var rc in rcArr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
        {
            if (rc.TryGetProperty("isEnabled", out var en) && en.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            if (rc.TryGetProperty("code", out var codeObj) && codeObj.ValueKind == JsonValueKind.Object)
            {
                if (ReadStr(codeObj, "code") is { Length: > 0 } codeVal)
                {
                    values.Add(codeVal);
                }

                if (ReadStr(codeObj, "decode") is { Length: > 0 } decodeVal)
                {
                    values.Add(decodeVal);
                }
            }
        }

        var distinct = values.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count > 0 ? distinct : null;
    }

    private static decimal? TryReadDecimalBound(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var val))
            {
                continue;
            }

            if (val.ValueKind == JsonValueKind.Number && val.TryGetDecimal(out var d))
            {
                return d;
            }

            if (val.ValueKind == JsonValueKind.String
                && decimal.TryParse(val.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string TruncateRuleText(string text) =>
        text.Length <= RulePackJson.MaxTextLength ? text : text[..RulePackJson.MaxTextLength];

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

    private static List<ProtocolImportNote> DeduplicateNotes(IEnumerable<ProtocolImportNote> notes) =>
        [.. notes.Distinct()];

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    private sealed record UsdmBcMeta(string? Name, string? Label, string? Description);

    private sealed record UsdmBcBounds(
        string? ConceptCode,
        string? Label,
        string? Reference,
        bool HasRequiredProperty,
        IReadOnlyList<string>? ResponseValues,
        decimal? MinValue,
        decimal? MaxValue);
}
