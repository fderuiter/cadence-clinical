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
    private static readonly Epoch[] DefaultTemplateEpochs =
    [
        new("EP.SCREENING", "Screening", 1),
        new("EP.TREATMENT", "Treatment", 2),
        new("EP.FOLLOWUP", "Follow-Up", 3),
    ];

    private static readonly PlannedActivity[] DefaultTemplateActivities =
    [
        new("V.SCREENING", "Screening", "EP.SCREENING", [ActivityLocation.Site], [], new ActivityTiming(-7, 7, 6)),
        new("V.BASELINE", "Baseline (Day 1)", "EP.TREATMENT", [ActivityLocation.Site], [], new ActivityTiming(1, 0, 2)),
        new("V.WEEK4", "Week 4", "EP.TREATMENT", [ActivityLocation.Site], [], new ActivityTiming(28, 3, 3)),
        new("V.EOS", "End of Study", "EP.FOLLOWUP", [ActivityLocation.Site], [], new ActivityTiming(84, 7, 7)),
    ];

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
        CancellationToken cancellationToken = default)
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
            return await ImportTemplatesAsync(request, resolveConcept, cancellationToken).ConfigureAwait(false);
        }

        var detected = DetectFormat(request.RawBytes!, request.FileName, request.Format);
        return detected switch
        {
            ProtocolImportFormat.OdmXml => await ImportOdmPayloadAsync(request, resolveConcept, cancellationToken).ConfigureAwait(false),
            ProtocolImportFormat.UsdmJson => await ImportUsdmPayloadAsync(request, resolveConcept, cancellationToken).ConfigureAwait(false),
            _ => new ProtocolDesignImportResult(
                null,
                ["Unsupported file format. Synchronous protocol import accepts CDISC ODM v1.3.2 XML (.xml) or CDISC USDM v4 JSON (.json) files."],
                []),
        };
    }

    private async Task<ProtocolDesignImportResult> ImportTemplatesAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var notes = new List<ProtocolImportNote>();
        var importedSpecs = new List<CollectionSpec>();
        var importedRules = new List<Rule>();
        var specsByTemplate = new List<(string TemplateId, BundledProtocolTemplate? Metadata, IReadOnlyList<CollectionSpec> Specs)>();
        var batchUsedIds = ReservedNonFormScheduleElementIds(request.BaseDesign);
        string? firstTemplateTitle = null;
        string? firstStudyOid = null;

        var catalogById = _catalog.ListTemplates()
            .ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var assignmentsByTemplate = BuildTemplateAssignmentLookup(request.TemplateAssignments);

        foreach (var rawId in request.TemplateIds!.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trimmedId = rawId.Trim();
            var templateBytes = _catalog.ReadTemplateBytes(trimmedId);
            if (templateBytes is null)
            {
                problems.Add($"Bundled CDISC Library template '{trimmedId}' was not found.");
                continue;
            }

            var odm = await OdmDesignImporter.ImportAsync(templateBytes, resolveConcept, batchUsedIds, cancellationToken).ConfigureAwait(false);
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

        var (templateEpochs, templateActivities) = BuildTemplateSchedule(
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
            templateEpochs,
            templateActivities);
    }

    private static async Task<ProtocolDesignImportResult> ImportOdmPayloadAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
        CancellationToken cancellationToken)
    {
        var reservedIds = ReservedNonFormScheduleElementIds(request.BaseDesign);
        var odm = await OdmDesignImporter.ImportAsync(request.RawBytes!, resolveConcept, reservedIds, cancellationToken).ConfigureAwait(false);
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
            odm.Epochs,
            odm.PlannedActivities);
    }

    private static async Task<ProtocolDesignImportResult> ImportUsdmPayloadAsync(
        ProtocolDesignImportRequest request,
        ProtocolConceptResolver resolveConcept,
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

        var staged = await UsdmDesignImporter.StageAsync(json, ResolveBcAsync, options: null, cancellationToken).ConfigureAwait(false);
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
        return validation.IsValid
            ? new ProtocolDesignImportResult(validation.Design, [], combinedNotes)
            : new ProtocolDesignImportResult(null, validation.Problems, combinedNotes);
    }

    private static ProtocolDesignImportResult FinalizeMergedFormsDesign(
        ProtocolDesign? baseDesign,
        IReadOnlyList<CollectionSpec> incomingSpecs,
        IReadOnlyList<ProtocolImportNote> incomingNotes,
        string fallbackTitle,
        IReadOnlyList<Rule>? incomingRules = null,
        string? studyOid = null,
        IReadOnlyList<Epoch>? incomingEpochs = null,
        IReadOnlyList<PlannedActivity>? incomingActivities = null)
    {
        var mergedSpecs = new List<CollectionSpec>();
        var usedIds = ReservedNonFormScheduleElementIds(baseDesign);
        var idRenameMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var replacedSpecIds = new Dictionary<string, (string Id, string Name)>(StringComparer.Ordinal);

        if (baseDesign is not null)
        {
            foreach (var epoch in baseDesign.Epochs)
            {
                usedIds.Add(epoch.Id);
            }

            foreach (var activity in baseDesign.PlannedActivities)
            {
                usedIds.Add(activity.Id);
            }

            var incomingKeys = incomingSpecs.Select(s => (s.Id, s.Name)).ToHashSet();
            foreach (var existing in baseDesign.CollectionSpecs)
            {
                if (incomingKeys.Contains((existing.Id, existing.Name)))
                {
                    // Replaced in-place below so idempotent retries produce byte-for-byte identical ProtocolDesign JSON.
                    replacedSpecIds[existing.Id] = (existing.Id, existing.Name);
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

        foreach (var (oldId, key) in replacedSpecIds)
        {
            if (mergedSpecs.FirstOrDefault(s => s.Name == key.Name && s.Id == key.Id) is { } exact)
            {
                idRenameMap.TryAdd(oldId, exact.Id);
            }
            else if (mergedSpecs.FirstOrDefault(s => s.Name == key.Name) is { } byName)
            {
                idRenameMap.TryAdd(oldId, byName.Id);
            }
        }

        var validSpecIds = mergedSpecs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var (mergedEpochs, mergedActivities) = MergeSchedule(
            baseDesign,
            incomingEpochs ?? [],
            incomingActivities ?? [],
            idRenameMap,
            validSpecIds,
            usedIds);

        var mergedNotes = DeduplicateNotes((baseDesign?.ImportNotes ?? []).Concat(incomingNotes));
        var title = FirstNonBlank(baseDesign?.Title, fallbackTitle, "Protocol Draft")!;
        var candidateDesign = (baseDesign ?? new ProtocolDesign()) with
        {
            Title = title,
            Epochs = mergedEpochs,
            PlannedActivities = mergedActivities,
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

    private static Dictionary<string, IReadOnlyList<string>> BuildTemplateAssignmentLookup(
        IReadOnlyList<TemplateActivityAssignment>? assignments)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (assignments is null)
        {
            return map;
        }

        foreach (var assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.TemplateId))
            {
                continue;
            }

            var cleanedIds = (assignment.ActivityIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            map[assignment.TemplateId.Trim()] = cleanedIds;
        }

        return map;
    }

    private static (List<Epoch> Epochs, List<PlannedActivity> Activities) BuildTemplateSchedule(
        ProtocolDesign? baseDesign,
        IReadOnlyList<(string TemplateId, BundledProtocolTemplate? Metadata, IReadOnlyList<CollectionSpec> Specs)> specsByTemplate,
        IReadOnlyDictionary<string, IReadOnlyList<string>> assignmentsByTemplate,
        List<string> problems)
    {
        if (specsByTemplate.Count == 0)
        {
            return ([], []);
        }

        var baseActivitiesById = (baseDesign?.PlannedActivities ?? [])
            .ToDictionary(a => a.Id, StringComparer.Ordinal);
        var starterActivitiesById = DefaultTemplateActivities
            .ToDictionary(a => a.Id, StringComparer.Ordinal);
        var starterEpochsById = DefaultTemplateEpochs
            .ToDictionary(e => e.Id, StringComparer.Ordinal);

        var assignedSpecsByActivity = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void AddSpecToActivity(string activityId, string specId)
        {
            if (!assignedSpecsByActivity.TryGetValue(activityId, out var list))
            {
                list = [];
                assignedSpecsByActivity[activityId] = list;
            }

            if (!list.Contains(specId, StringComparer.Ordinal))
            {
                list.Add(specId);
            }
        }

        foreach (var (templateId, metadata, specs) in specsByTemplate)
        {
            if (assignmentsByTemplate.TryGetValue(templateId, out var explicitActivityIds))
            {
                foreach (var activityId in explicitActivityIds)
                {
                    if (!baseActivitiesById.ContainsKey(activityId) && !starterActivitiesById.ContainsKey(activityId))
                    {
                        problems.Add($"Template '{templateId}' assignment references unknown planned activity '{activityId}'.");
                        continue;
                    }

                    foreach (var spec in specs)
                    {
                        AddSpecToActivity(activityId, spec.Id);
                    }
                }

                continue;
            }

            foreach (var spec in specs)
            {
                if (spec.Repeating || metadata is { Repeating: true })
                {
                    // Repeating / log templates remain unscheduled unless explicitly assigned to visits.
                    continue;
                }

                var defaultTargetIds = SelectDefaultActivitiesForTemplate(baseDesign, metadata, spec);
                foreach (var activityId in defaultTargetIds)
                {
                    AddSpecToActivity(activityId, spec.Id);
                }
            }
        }

        if (assignedSpecsByActivity.Count == 0)
        {
            return ([], []);
        }

        var epochs = new List<Epoch>();
        var seenEpochIds = new HashSet<string>(StringComparer.Ordinal);
        var activities = new List<PlannedActivity>();

        // Preserve base activity order first, then starter activity order.
        foreach (var baseAct in baseDesign?.PlannedActivities ?? [])
        {
            if (assignedSpecsByActivity.TryGetValue(baseAct.Id, out var specIds) && specIds.Count > 0)
            {
                activities.Add(baseAct with { CollectionSpecIds = specIds });
            }
        }

        foreach (var starterAct in DefaultTemplateActivities)
        {
            if (baseActivitiesById.ContainsKey(starterAct.Id))
            {
                continue;
            }

            if (assignedSpecsByActivity.TryGetValue(starterAct.Id, out var specIds) && specIds.Count > 0)
            {
                if (starterEpochsById.TryGetValue(starterAct.EpochId, out var starterEpoch) && seenEpochIds.Add(starterEpoch.Id))
                {
                    epochs.Add(starterEpoch);
                }

                activities.Add(starterAct with { CollectionSpecIds = specIds });
            }
        }

        return (epochs, activities);
    }

    private static IReadOnlyList<string> SelectDefaultActivitiesForTemplate(
        ProtocolDesign? baseDesign,
        BundledProtocolTemplate? metadata,
        CollectionSpec spec)
    {
        var label = $"{metadata?.Category} {metadata?.FormName} {spec.Name}".ToLowerInvariant();
        bool isScreeningOnly = label.Contains("inclusion", StringComparison.Ordinal)
            || label.Contains("exclusion", StringComparison.Ordinal)
            || label.Contains("eligibility", StringComparison.Ordinal)
            || label.Contains("medical history", StringComparison.Ordinal)
            || label.Contains("informed consent", StringComparison.Ordinal);
        bool isBaselineOnly = label.Contains("demographics", StringComparison.Ordinal)
            || label.Contains("subject characteristics", StringComparison.Ordinal)
            || label.Contains("substance use", StringComparison.Ordinal);
        bool isEndOfStudyOnly = label.Contains("disposition", StringComparison.Ordinal)
            || label.Contains("subject status", StringComparison.Ordinal)
            || label.Contains("death", StringComparison.Ordinal)
            || label.Contains("end of study", StringComparison.Ordinal);

        if (baseDesign is { PlannedActivities.Count: > 0 })
        {
            var scheduled = baseDesign.PlannedActivities
                .Where(a => !a.Repeating && a.Timing is not null)
                .ToList();
            if (scheduled.Count == 0)
            {
                scheduled = [.. baseDesign.PlannedActivities];
            }

            if (isScreeningOnly)
            {
                var scr = scheduled.FirstOrDefault(a => a.Name.Contains("screen", StringComparison.OrdinalIgnoreCase) || (a.Timing?.Day ?? 1) < 0)
                    ?? scheduled[0];
                return [scr.Id];
            }

            if (isBaselineOnly)
            {
                var bl = scheduled.FirstOrDefault(a => a.Name.Contains("baseline", StringComparison.OrdinalIgnoreCase) || a.Timing?.Day == 1)
                    ?? scheduled[0];
                return [bl.Id];
            }

            if (isEndOfStudyOnly)
            {
                return [scheduled[^1].Id];
            }

            return [.. scheduled.Select(a => a.Id)];
        }

        if (isScreeningOnly)
        {
            return ["V.SCREENING"];
        }

        if (isBaselineOnly)
        {
            return ["V.BASELINE"];
        }

        if (isEndOfStudyOnly)
        {
            return ["V.EOS"];
        }

        return ["V.SCREENING", "V.BASELINE", "V.WEEK4", "V.EOS"];
    }

    private static (List<Epoch> Epochs, List<PlannedActivity> Activities) MergeSchedule(
        ProtocolDesign? baseDesign,
        IReadOnlyList<Epoch> incomingEpochs,
        IReadOnlyList<PlannedActivity> incomingActivities,
        IReadOnlyDictionary<string, string> idRenameMap,
        IReadOnlySet<string> validSpecIds,
        HashSet<string> usedIds)
    {
        var mergedEpochs = new List<Epoch>();
        var epochIdMap = new Dictionary<string, string>(StringComparer.Ordinal);

        if (baseDesign is not null)
        {
            foreach (var existingEpoch in baseDesign.Epochs)
            {
                mergedEpochs.Add(existingEpoch);
                epochIdMap[existingEpoch.Id] = existingEpoch.Id;
            }
        }

        int nextOrder = mergedEpochs.Count == 0 ? 1 : mergedEpochs.Max(e => e.Order) + 1;
        foreach (var incomingEpoch in incomingEpochs)
        {
            var matchingExisting = mergedEpochs.FirstOrDefault(e =>
                string.Equals(e.Id, incomingEpoch.Id, StringComparison.Ordinal)
                || string.Equals(e.Name, incomingEpoch.Name, StringComparison.OrdinalIgnoreCase));
            if (matchingExisting is not null)
            {
                epochIdMap[incomingEpoch.Id] = matchingExisting.Id;
                continue;
            }

            var epochId = OdmDesignImporter.AllocateUniqueElementId(incomingEpoch.Id, "Epoch", usedIds);
            epochIdMap[incomingEpoch.Id] = epochId;
            mergedEpochs.Add(incomingEpoch with { Id = epochId, Order = nextOrder++ });
        }

        List<string> RemapSpecIds(IEnumerable<string> specIds) =>
            [.. specIds
                .Select(id => idRenameMap.TryGetValue(id, out var mapped) ? mapped : id)
                .Where(validSpecIds.Contains)
                .Distinct(StringComparer.Ordinal)];

        var mergedActivities = new List<PlannedActivity>();
        var activityIndexById = new Dictionary<string, int>(StringComparer.Ordinal);

        if (baseDesign is not null)
        {
            foreach (var existingActivity in baseDesign.PlannedActivities)
            {
                var remappedExisting = existingActivity with
                {
                    CollectionSpecIds = RemapSpecIds(existingActivity.CollectionSpecIds),
                };
                activityIndexById[remappedExisting.Id] = mergedActivities.Count;
                mergedActivities.Add(remappedExisting);
            }
        }

        foreach (var incomingActivity in incomingActivities)
        {
            var mappedEpochId = epochIdMap.GetValueOrDefault(incomingActivity.EpochId)
                ?? EnsureFallbackEpoch(incomingActivity.EpochId, mergedEpochs, epochIdMap, usedIds, ref nextOrder);
            var remappedSpecIds = RemapSpecIds(incomingActivity.CollectionSpecIds);

            if (activityIndexById.TryGetValue(incomingActivity.Id, out var existingIdx))
            {
                var existing = mergedActivities[existingIdx];
                var combinedSpecs = existing.CollectionSpecIds
                    .Concat(remappedSpecIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                mergedActivities[existingIdx] = existing with
                {
                    CollectionSpecIds = combinedSpecs,
                    Timing = incomingActivity.Timing ?? existing.Timing,
                    Repeating = incomingActivity.Repeating || existing.Repeating,
                };
                continue;
            }

            var activityId = OdmDesignImporter.AllocateUniqueElementId(incomingActivity.Id, "Activity", usedIds);
            var added = incomingActivity with
            {
                Id = activityId,
                EpochId = mappedEpochId,
                CollectionSpecIds = remappedSpecIds,
            };
            activityIndexById[activityId] = mergedActivities.Count;
            mergedActivities.Add(added);
        }

        return (mergedEpochs, mergedActivities);
    }

    private static string EnsureFallbackEpoch(
        string requestedEpochId,
        List<Epoch> mergedEpochs,
        Dictionary<string, string> epochIdMap,
        HashSet<string> usedIds,
        ref int nextOrder)
    {
        var epochId = OdmDesignImporter.AllocateUniqueElementId(requestedEpochId, "Epoch", usedIds);
        epochIdMap[requestedEpochId] = epochId;
        mergedEpochs.Add(new Epoch(epochId, requestedEpochId, nextOrder++));
        return epochId;
    }

    private static HashSet<string> ReservedNonFormScheduleElementIds(ProtocolDesign? design)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (design is null)
        {
            return ids;
        }

        foreach (var entry in design.Elements().Where(e => e.Kind is not (
            ProtocolElementKind.CollectionSpec
            or ProtocolElementKind.CollectionItem
            or ProtocolElementKind.Epoch
            or ProtocolElementKind.PlannedActivity)))
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
