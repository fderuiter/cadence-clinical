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

    private static readonly IReadOnlyDictionary<string, ProtocolConceptResolution> EmptyResolutions =
        new Dictionary<string, ProtocolConceptResolution>(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<BundledProtocolTemplate> ListBundledTemplates() => _catalog.ListTemplates();

    /// <inheritdoc />
    public Task<ProtocolDesignImportResult> ImportAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        CancellationToken cancellationToken = default,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolveConcept);
        return ImportAsync(request, OdmDesignImporter.AdaptSingleResolver(resolveConcept), cancellationToken, resolveTerminology);
    }

    /// <inheritdoc />
    public async Task<ProtocolDesignImportResult> ImportAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptBatchResolver resolveConcepts,
        CancellationToken cancellationToken = default,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolveConcepts);

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
            return await ImportTemplatesAsync(request, resolveConcepts, resolveTerminology, cancellationToken).ConfigureAwait(false);
        }

        var detected = DetectFormat(request.RawBytes!, request.FileName, request.Format);
        return detected switch
        {
            ProtocolImportFormat.OdmXml => await ImportOdmPayloadAsync(request, resolveConcepts, resolveTerminology, cancellationToken).ConfigureAwait(false),
            ProtocolImportFormat.UsdmJson => await ImportUsdmPayloadAsync(request, resolveConcepts, resolveTerminology, cancellationToken).ConfigureAwait(false),
            _ => new ProtocolDesignImportResult(
                null,
                ["Unsupported file format. Synchronous protocol import accepts CDISC ODM v1.3.2 XML (.xml) or CDISC USDM v4 JSON (.json) files."],
                []),
        };
    }

    private async Task<ProtocolDesignImportResult> ImportTemplatesAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptBatchResolver resolveConcepts,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var preNotes = new List<ProtocolImportNote>();
        var stagedTemplates = new List<(string RawId, OdmDesignImporter.StagedOdmDocument Staged)>();
        var placeholderSpecs = new List<CollectionSpec>();
        var placeholderRules = new List<Rule>();
        var uniqueCandidates = new List<ProtocolConceptCandidate>();
        var seenCandidateKeys = new HashSet<string>(StringComparer.Ordinal);
        var uniqueTerminologyCandidates = new List<ProtocolTerminologyCandidate>();
        var seenTerminologyKeys = new HashSet<string>(StringComparer.Ordinal);
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

            var staged = OdmDesignImporter.StageBytes(templateBytes, batchUsedIds);
            firstTemplateTitle ??= staged.StudyTitle;
            firstStudyOid ??= staged.StudyOid;
            preNotes.AddRange(staged.MaterializePreResolutionNotes());
            if (!staged.CanResolve)
            {
                problems.AddRange(staged.Problems.Select(p => $"Template '{rawId}': {p}"));
                continue;
            }

            stagedTemplates.Add((rawId, staged));
            placeholderSpecs.AddRange(staged.BuildPlaceholderSpecs());
            if (staged.Rules is { Count: > 0 })
            {
                placeholderRules.AddRange(staged.Rules);
            }

            foreach (var candidate in staged.UniqueCandidates)
            {
                if (seenCandidateKeys.Add(candidate.StableKey))
                {
                    uniqueCandidates.Add(candidate);
                }
            }

            foreach (var termCandidate in staged.UniqueTerminologyCandidates)
            {
                var termKey = termCandidate.CodelistCode ?? termCandidate.SourceKey;
                if (seenTerminologyKeys.Add(termKey))
                {
                    uniqueTerminologyCandidates.Add(termCandidate);
                }
            }
        }

        if (problems.Count > 0)
        {
            return new ProtocolDesignImportResult(null, problems, DeduplicateNotes(preNotes));
        }

        var effectiveTitle = request.FallbackTitle ?? firstTemplateTitle ?? "Protocol Draft";
        var preCheck = FinalizeMergedFormsDesign(
            request.BaseDesign,
            placeholderSpecs,
            preNotes,
            effectiveTitle,
            placeholderRules,
            firstStudyOid);
        if (!preCheck.IsSuccess)
        {
            return preCheck;
        }

        var resolutions = uniqueCandidates.Count == 0
            ? EmptyResolutions
            : await resolveConcepts(uniqueCandidates, cancellationToken).ConfigureAwait(false);
        var allConceptsResolved = uniqueCandidates.All(c =>
            resolutions.TryGetValue(c.StableKey, out var r) && r.Concept is not null);
        var termResolutions = allConceptsResolved
            ? await OdmDesignImporter.ResolveTerminologyCandidatesAsync(
                uniqueTerminologyCandidates,
                resolveTerminology,
                cancellationToken).ConfigureAwait(false)
            : null;

        var notes = new List<ProtocolImportNote>();
        var importedSpecs = new List<CollectionSpec>();
        var importedRules = new List<Rule>();
        foreach (var (rawId, staged) in stagedTemplates)
        {
            var odm = OdmDesignImporter.MaterializeStagedDocument(staged, resolutions, termResolutions);
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
        }

        if (problems.Count > 0)
        {
            return new ProtocolDesignImportResult(null, problems, DeduplicateNotes(notes));
        }

        return FinalizeMergedFormsDesign(
            request.BaseDesign,
            importedSpecs,
            notes,
            effectiveTitle,
            importedRules,
            firstStudyOid);
    }

    private static async Task<ProtocolDesignImportResult> ImportOdmPayloadAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptBatchResolver resolveConcepts,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        var reservedIds = NonCollectionElementIds(request.BaseDesign);
        var staged = OdmDesignImporter.StageBytes(request.RawBytes!, reservedIds);
        if (staged.PayloadTooLarge)
        {
            return new ProtocolDesignImportResult(null, staged.Problems, staged.MaterializePreResolutionNotes(), PayloadTooLarge: true);
        }

        if (!staged.CanResolve)
        {
            return new ProtocolDesignImportResult(null, staged.Problems, staged.MaterializePreResolutionNotes());
        }

        var fallbackTitle = FirstNonBlank(request.BaseDesign?.Title, staged.StudyTitle, request.FallbackTitle, "Imported ODM Protocol")!;
        var preCheck = FinalizeMergedFormsDesign(
            request.BaseDesign,
            staged.BuildPlaceholderSpecs(),
            staged.MaterializePreResolutionNotes(),
            fallbackTitle,
            staged.Rules,
            staged.StudyOid);
        if (!preCheck.IsSuccess)
        {
            return preCheck;
        }

        var resolutions = staged.UniqueCandidates.Count == 0
            ? EmptyResolutions
            : await resolveConcepts(staged.UniqueCandidates, cancellationToken).ConfigureAwait(false);
        var allConceptsResolved = staged.UniqueCandidates.All(c =>
            resolutions.TryGetValue(c.StableKey, out var r) && r.Concept is not null);
        var termResolutions = allConceptsResolved
            ? await OdmDesignImporter.ResolveStagedTerminologyAsync(staged, resolveTerminology, cancellationToken).ConfigureAwait(false)
            : null;
        var odm = OdmDesignImporter.MaterializeStagedDocument(staged, resolutions, termResolutions);
        if (!odm.IsSuccess)
        {
            return new ProtocolDesignImportResult(null, odm.Problems, odm.Notes);
        }

        return FinalizeMergedFormsDesign(request.BaseDesign, odm.CollectionSpecs, odm.Notes, fallbackTitle, odm.Rules, odm.StudyOid);
    }

    private static async Task<ProtocolDesignImportResult> ImportUsdmPayloadAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptBatchResolver resolveConcepts,
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
        var orderedCodes = new List<string>();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);

        Task<ConceptRef?> CollectBcAsync(string cdiscCode, CancellationToken _)
        {
            if (seenCodes.Add(cdiscCode))
            {
                orderedCodes.Add(cdiscCode);
            }

            return Task.FromResult<ConceptRef?>(OdmDesignImporter.PlaceholderConceptRef);
        }

        var dryRun = await UsdmDesignImporter.StageAsync(json, CollectBcAsync, options: null, cancellationToken).ConfigureAwait(false);
        var dryNotes = dryRun.Notes
            .Select(n => new ProtocolImportNote(n.Source, n.Message))
            .Concat(vendorNotes)
            .Distinct()
            .ToList();
        if (dryRun.Problems.Count > 0 || dryRun.Design is null)
        {
            return new ProtocolDesignImportResult(null, dryRun.Problems, dryNotes);
        }

        var promptsByBcId = ExtractUsdmPromptsByBcId(json);
        var finalTitle = FirstNonBlank(dryRun.Design.Title, request.FallbackTitle, "Imported USDM Protocol")!;
        var dryValidation = ProtocolDesignDocument.Read(
            ProtocolDesignDocument.Write(EnrichUsdmDesign(dryRun.Design, promptsByBcId, finalTitle, dryNotes)));
        if (!dryValidation.IsValid)
        {
            return new ProtocolDesignImportResult(null, dryValidation.Problems, dryNotes);
        }

        var candidates = new List<ProtocolConceptCandidate>(orderedCodes.Count);
        var candidateByCode = new Dictionary<string, ProtocolConceptCandidate>(orderedCodes.Count, StringComparer.Ordinal);
        foreach (var cdiscCode in orderedCodes)
        {
            var meta = bcMetadata.GetValueOrDefault(cdiscCode);
            var label = FirstNonBlank(meta?.Label, meta?.Name, cdiscCode)!;
            var definition = FirstNonBlank(meta?.Description, meta?.Label, meta?.Name, $"Biomedical concept {cdiscCode}")!;
            var candidate = new ProtocolConceptCandidate($"usdm:{cdiscCode}:{label}", cdiscCode, label, definition, ConceptKind.BiomedicalConcept);
            candidates.Add(candidate);
            candidateByCode[cdiscCode] = candidate;
        }

        var resolutions = candidates.Count == 0
            ? EmptyResolutions
            : await resolveConcepts(candidates, cancellationToken).ConfigureAwait(false);

        var conceptProblems = new List<string>();
        var unmappedConceptNotes = new List<ProtocolImportNote>();
        Task<ConceptRef?> ResolveStagedBcAsync(string cdiscCode, CancellationToken _)
        {
            var candidate = candidateByCode[cdiscCode];
            if (!resolutions.TryGetValue(candidate.StableKey, out var resolution)
                || resolution.Concept is not { } resolved)
            {
                conceptProblems.Add(resolution?.ErrorMessage ?? $"Could not resolve or define Biomedical Concept '{cdiscCode}'.");
                return Task.FromResult<ConceptRef?>(null);
            }

            if (resolution.UsedFallbackSponsorConcept)
            {
                unmappedConceptNotes.Add(new ProtocolImportNote(
                    cdiscCode,
                    $"Unmapped terminology code '{cdiscCode}' ({candidate.PreferredLabel}) is not in the tenant Concept Registry; registered as a sponsor-local concept."));
            }

            return Task.FromResult<ConceptRef?>(resolved);
        }

        var allConceptsResolved = candidates.All(c =>
            resolutions.TryGetValue(c.StableKey, out var r) && r.Concept is not null);

        var staged = await UsdmDesignImporter.StageAsync(
            json,
            ResolveStagedBcAsync,
            options: null,
            cancellationToken,
            allConceptsResolved ? resolveTerminology : null).ConfigureAwait(false);
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

        var enrichedDesign = EnrichUsdmDesign(staged.Design, promptsByBcId, finalTitle, combinedNotes);
        var validation = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(enrichedDesign));
        return validation.IsValid
            ? new ProtocolDesignImportResult(validation.Design, [], combinedNotes)
            : new ProtocolDesignImportResult(null, validation.Problems, combinedNotes);
    }

    private static ProtocolDesign EnrichUsdmDesign(
        ProtocolDesign design,
        IReadOnlyDictionary<string, string> promptsByBcId,
        string finalTitle,
        IReadOnlyList<ProtocolImportNote> notes)
    {
        var enrichedSpecs = design.CollectionSpecs.Select(spec =>
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

        return design with
        {
            Title = finalTitle,
            CollectionSpecs = enrichedSpecs,
            ImportNotes = notes.Count > 0 ? notes : null,
        };
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

    private static string BuildLibraryKeySlug(string source)
    {
        var sb = new StringBuilder("odm-");
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
}
