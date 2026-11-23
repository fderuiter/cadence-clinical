using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Evaluation;

/// <summary>
/// Evaluates collection-stage edit checks over <see cref="CollectionInstanceRecord"/>s using <see cref="RulePackLoader"/>
/// and <see cref="IRuleEvaluator"/> (K-RULE-3, K-FIND-1).
/// </summary>
public sealed class CollectionValidationService(
    RulePackLoader loader,
    IStandardsRegistry registry,
    IRuleEvaluator evaluator,
    ICollectionInstanceRecords collections,
    IValidationFindings findings,
    IKernelCommandSender? commands = null,
    IClock? clock = null,
    IProtocolVersionReader? protocolVersions = null) : ICollectionValidationService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceId formId,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draftValues);

        var target = await collections.FindAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return [];
        }

        return await PreviewFormAsync(tenant, target, draftValues, asOf, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceRecord target,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(draftValues);

        var studyVersions = await LoadStudyVersionMapAsync(tenant, target.StudyId, cancellationToken).ConfigureAwait(false);
        var packs = (await loader.LoadForStudyAsync(tenant, target.StudyId, cancellationToken).ConfigureAwait(false))
            .Where(p => AppliesToInstance(p.GoverningProtocolVersions, target, studyVersions))
            .ToList();
        if (packs.Count == 0)
        {
            return [];
        }

        var enrollmentForms = await collections.ListRecordsForEnrollmentAsync(tenant, target.EnrollmentId, cancellationToken).ConfigureAwait(false);
        var siblings = enrollmentForms.Count > 0 ? enrollmentForms : [target];
        var record = BuildRuleRecord(target, siblings, draftValues);
        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var warnings = new List<CollectionPreviewWarning>();
        foreach (var pack in packs)
        {
            var rulesById = pack.Pack.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
            foreach (var violation in evaluator.Evaluate(pack.Pack, context, [record]))
            {
                var itemId = ResolvePrimaryItemId(target, violation) ?? string.Empty;
                rulesById.TryGetValue(violation.RuleId, out var rule);
                warnings.Add(new CollectionPreviewWarning(
                    pack.Key,
                    violation.RuleId,
                    violation.RuleVersion,
                    violation.Severity,
                    violation.Dataset,
                    itemId,
                    violation.Explanation,
                    rule?.RemediationGuidance));
            }
        }

        return warnings;
    }

    /// <inheritdoc />
    public async Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
        TenantId tenant,
        StudyId studyId,
        LibraryItemId rulePackId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var item = await registry.FindLibraryItemAsync(tenant, rulePackId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.Kind != LibraryItemKind.RulePack)
        {
            return new CollectionRulePackPreviewResult(0, 0, []);
        }

        var (pack, problems) = RulePackJson.Read(item.Content);
        if (pack is null || problems.Count > 0)
        {
            return new CollectionRulePackPreviewResult(0, 0, []);
        }

        var allInstances = await collections.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var studyVersions = await LoadStudyVersionMapAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var governedInstances = allInstances
            .Where(i => AppliesToInstance(item.GoverningProtocolVersions, i, studyVersions))
            .ToList();
        if (governedInstances.Count == 0)
        {
            return new CollectionRulePackPreviewResult(0, pack.Rules.Count, []);
        }

        var byEnrollment = allInstances
            .GroupBy(i => i.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstanceRecord>)[.. g]);
        var instancesByKey = governedInstances.ToDictionary(i => i.CollectionInstanceId.Value.ToString("D"), StringComparer.Ordinal);
        var records = governedInstances
            .Select(i => BuildRuleRecord(i, byEnrollment[i.EnrollmentId], draftOverlay: null))
            .ToList();

        var rulesById = pack.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var violations = new List<CollectionRulePackPreviewViolation>();
        foreach (var violation in evaluator.Evaluate(pack, context, records))
        {
            if (!instancesByKey.TryGetValue(violation.RecordKey, out var targetInstance))
            {
                continue;
            }

            var itemId = ResolvePrimaryItemId(targetInstance, violation);
            rulesById.TryGetValue(violation.RuleId, out var rule);
            violations.Add(new CollectionRulePackPreviewViolation(
                targetInstance.CollectionInstanceId,
                item.Key,
                violation.RuleId,
                violation.RuleVersion,
                violation.Severity,
                violation.Dataset,
                itemId,
                violation.Explanation,
                rule?.RemediationGuidance));
        }

        return new CollectionRulePackPreviewResult(governedInstances.Count, pack.Rules.Count, violations);
    }

    /// <inheritdoc />
    public async Task<CommandResult<CollectionValidationResult>> ValidateStudyCollectionsAsync(
        TenantId tenant,
        StudyId studyId,
        DateOnly asOf,
        DateTimeOffset evaluatedAt,
        string idempotencyKey,
        string reasonForChange,
        CommandMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (commands is null)
        {
            throw new InvalidOperationException("IKernelCommandSender is not registered.");
        }

        var packs = await loader.LoadForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        if (packs.Count == 0)
        {
            return CommandResult<CollectionValidationResult>.Success(new CollectionValidationResult(null, []));
        }

        var instances = await collections.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var studyVersions = await LoadStudyVersionMapAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var priorFindings = await ListAllCollectionFindingsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);

        var datasets = instances.Select(i => i.CollectionSpecId)
            .Concat(packs.SelectMany(p => p.Pack.Rules)
                .Where(r => r.Applicability.Stage == ValidationStage.Collection && !string.IsNullOrWhiteSpace(r.Applicability.Dataset))
                .Select(r => r.Applicability.Dataset!))
            .Concat(priorFindings.Select(f => f.Target.Dataset))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (datasets.Count == 0)
        {
            return CommandResult<CollectionValidationResult>.Success(new CollectionValidationResult(null, priorFindings));
        }

        var byEnrollment = instances
            .GroupBy(i => i.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstanceRecord>)[.. g]);
        var instancesByKey = instances.ToDictionary(i => i.CollectionInstanceId.Value.ToString("D"), StringComparer.Ordinal);
        var recordsByInstance = instances
            .Select(i => (Instance: i, Record: BuildRuleRecord(i, byEnrollment[i.EnrollmentId], draftOverlay: null)))
            .ToList();

        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var detections = new List<ValidationDetection>();
        var seenIdentities = new HashSet<ValidationFindingId>();
        foreach (var pack in packs)
        {
            var packRecords = recordsByInstance
                .Where(x => AppliesToInstance(pack.GoverningProtocolVersions, x.Instance, studyVersions))
                .Select(x => x.Record)
                .ToList();
            if (packRecords.Count == 0)
            {
                continue;
            }

            foreach (var violation in evaluator.Evaluate(pack.Pack, context, packRecords))
            {
                instancesByKey.TryGetValue(violation.RecordKey, out var targetInstance);
                var primaryVariable = targetInstance is null ? null : ResolvePrimaryItemId(targetInstance, violation);
                var target = new FindingTarget(FindingTargetKind.DatasetRecord, violation.Dataset, violation.RecordKey, primaryVariable);
                var identity = new FindingIdentity(studyId, ValidationStage.Collection, pack.Key, violation.RuleId, target);
                if (!seenIdentities.Add(ValidationFindingId.For(tenant, identity)))
                {
                    continue;
                }

                detections.Add(new ValidationDetection(
                    pack.Key,
                    violation.RuleId,
                    violation.RuleVersion,
                    violation.Severity,
                    target,
                    violation.Explanation,
                    Expected: null,
                    [.. violation.Observed.Select(o => new ObservedVariable(o.Variable, o.Value.IsNull ? null : Shorten(o.Value.ToString())))]));
            }
        }

        var stampedAt = KernelTimestamps.ToStoredPrecision(evaluatedAt);
        if (clock is not null)
        {
            var now = KernelTimestamps.ToStoredPrecision(clock.UtcNow);
            if (stampedAt > now)
            {
                stampedAt = now;
            }
        }

        var runId = DeriveRunId(tenant, studyId, idempotencyKey);
        if (findings is not null)
        {
            var existingRun = await findings.FindRunAsync(tenant, runId, cancellationToken).ConfigureAwait(false);
            if (existingRun is not null)
            {
                var currentFindings = await ListAllCollectionFindingsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
                return CommandResult<CollectionValidationResult>.Success(
                    new CollectionValidationResult(new ValidationRunOutcome(existingRun.Id, existingRun.Detected, 0, 0), currentFindings));
            }
        }

        var runResult = await commands.SendAsync(
            new RecordValidationRun(
                tenant,
                runId,
                studyId,
                ValidationStage.Collection,
                [.. packs.Select(p => p.Id)],
                asOf,
                Standard: null,
                StandardVersion: null,
                Agency: null,
                Inputs: [],
                datasets,
                stampedAt,
                stampedAt,
                detections,
                idempotencyKey,
                reasonForChange),
            metadata,
            cancellationToken).ConfigureAwait(false);
        if (!runResult.IsSuccess)
        {
            return runResult.Error!;
        }

        var updatedFindings = await ListAllCollectionFindingsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        return CommandResult<CollectionValidationResult>.Success(
            new CollectionValidationResult(runResult.Value, updatedFindings));
    }

    /// <summary>Builds the <see cref="RuleRecord"/> for <paramref name="target"/>, including prior-visit and visit-aligned cross-form variables on the enrollment.</summary>
    public static RuleRecord BuildRuleRecord(
        CollectionInstanceRecord target,
        IReadOnlyList<CollectionInstanceRecord> enrollmentForms,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(enrollmentForms);

        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        var recordedByItem = target.Values.ToDictionary(v => v.ItemId, v => (ObservationValue?)v.Value, StringComparer.Ordinal);
        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                recordedByItem[itemId] = draftValue;
            }
        }

        var allTargetItemIds = target.Items.Select(i => i.Id)
            .Concat(recordedByItem.Keys)
            .Distinct(StringComparer.Ordinal);
        foreach (var itemId in allTargetItemIds)
        {
            recordedByItem.TryGetValue(itemId, out var raw);
            var converted = ToRuleValue(raw);
            values[itemId] = converted;
            var qualified = $"{target.CollectionSpecId}.{itemId}";
            if (qualified.Length <= FindingTarget.MaxNameLength)
            {
                values[qualified] = converted;
            }
        }

        // Bind consecutive prior-visit values (prev.*) for repeat visits of the same CollectionSpecId ordered chronologically.
        var sameSpecForms = enrollmentForms
            .Where(f => f.CollectionInstanceId != target.CollectionInstanceId
                && string.Equals(f.CollectionSpecId, target.CollectionSpecId, StringComparison.Ordinal))
            .Append(target)
            .OrderBy(f => f.CollectedAt)
            .ThenBy(f => f.OpenedAt)
            .ThenBy(f => f.CollectionInstanceId)
            .ToList();
        var targetIndex = sameSpecForms.FindIndex(f => f.CollectionInstanceId == target.CollectionInstanceId);
        if (targetIndex > 0)
        {
            var prior = sameSpecForms[targetIndex - 1];
            var priorRecorded = prior.Values.ToDictionary(v => v.ItemId, v => (ObservationValue?)v.Value, StringComparer.Ordinal);
            var priorItemIds = prior.Items.Select(i => i.Id)
                .Concat(priorRecorded.Keys)
                .Distinct(StringComparer.Ordinal);
            foreach (var priorItemId in priorItemIds)
            {
                priorRecorded.TryGetValue(priorItemId, out var raw);
                var converted = ToRuleValue(raw);
                SetIfFits(values, $"prev.{priorItemId}", converted);
                SetIfFits(values, $"prev.{prior.CollectionSpecId}.{priorItemId}", converted);
                if (priorItemId.StartsWith("item.", StringComparison.Ordinal) && priorItemId.Length > 5)
                {
                    var shortKey = $"prev.{priorItemId[5..]}";
                    if (!values.ContainsKey(shortKey))
                    {
                        SetIfFits(values, shortKey, converted);
                    }
                }
            }
        }

        // Bind visit-aligned sibling form values per CollectionSpecId (same PlannedActivityId, or nearest populated log).
        var siblingGroups = enrollmentForms
            .Where(f => f.CollectionInstanceId != target.CollectionInstanceId
                && !string.Equals(f.CollectionSpecId, target.CollectionSpecId, StringComparison.Ordinal))
            .GroupBy(f => f.CollectionSpecId, StringComparer.Ordinal);

        foreach (var group in siblingGroups)
        {
            if (SelectSiblingForm(target, group) is not { } sibling)
            {
                continue;
            }

            var siblingRecorded = sibling.Values.ToDictionary(v => v.ItemId, v => (ObservationValue?)v.Value, StringComparer.Ordinal);
            var siblingItemIds = sibling.Items.Select(i => i.Id)
                .Concat(siblingRecorded.Keys)
                .Distinct(StringComparer.Ordinal);
            foreach (var siblingItemId in siblingItemIds)
            {
                siblingRecorded.TryGetValue(siblingItemId, out var raw);
                var converted = ToRuleValue(raw);
                SetIfFits(values, $"{sibling.CollectionSpecId}.{siblingItemId}", converted);
                if (!values.ContainsKey(siblingItemId))
                {
                    SetIfFits(values, siblingItemId, converted);
                }
            }
        }

        return new RuleRecord(target.CollectionSpecId, target.CollectionInstanceId.Value.ToString("D"), values);
    }

    /// <summary>Converts a typed <see cref="ObservationValue"/> to the corresponding <see cref="RuleValue"/>.</summary>
    public static RuleValue ToRuleValue(ObservationValue? value) => value switch
    {
        null or MissingValue => RuleValue.Null,
        QuantityValue q => RuleValue.FromNumber(q.Value),
        BooleanValue b => RuleValue.FromBoolean(b.Value),
        CodedValue c => RuleValue.FromText(c.Code),
        TextValue t => RuleValue.FromText(t.Value),
        DateTimeValue d => RuleValue.FromText(d.Value),
        _ => RuleValue.FromText(value.ToDisplayString()),
    };

    /// <summary>Identifies the primary item on <paramref name="target"/> referenced by <paramref name="violation"/>.</summary>
    public static string? ResolvePrimaryItemId(CollectionInstanceRecord target, RuleViolation violation)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(violation);

        var targetItems = target.Items.Select(i => i.Id)
            .Concat(target.Values.Select(v => v.ItemId))
            .ToHashSet(StringComparer.Ordinal);
        var prefix = target.CollectionSpecId + ".";

        foreach (var variable in violation.Observed.Select(observed => observed.Variable))
        {
            if (targetItems.Contains(variable))
            {
                return variable;
            }

            if (variable.StartsWith(prefix, StringComparison.Ordinal))
            {
                var stripped = variable[prefix.Length..];
                if (targetItems.Contains(stripped))
                {
                    return stripped;
                }
            }
        }

        foreach (var variable in violation.Observed.Select(observed => observed.Variable))
        {
            if (!variable.StartsWith("prev.", StringComparison.Ordinal))
            {
                continue;
            }

            var withoutPrev = variable[5..];
            if (targetItems.Contains(withoutPrev))
            {
                return withoutPrev;
            }

            if (withoutPrev.StartsWith(prefix, StringComparison.Ordinal))
            {
                var stripped = withoutPrev[prefix.Length..];
                if (targetItems.Contains(stripped))
                {
                    return stripped;
                }
            }

            var itemPrefixed = "item." + withoutPrev;
            if (targetItems.Contains(itemPrefixed))
            {
                return itemPrefixed;
            }
        }

        if (target.Items.Count > 0)
        {
            return target.Items[0].Id;
        }

        var fallback = violation.Observed.FirstOrDefault()?.Variable;
        return fallback is not null && fallback.Length <= FindingTarget.MaxNameLength ? fallback : null;
    }

    private async Task<IReadOnlyList<ValidationFindingView>> ListAllCollectionFindingsAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken)
    {
        var all = new List<ValidationFindingView>();
        var skip = 0;
        while (true)
        {
            var page = await findings.ListAsync(
                tenant,
                new FindingQuery(studyId)
                {
                    Stage = ValidationStage.Collection,
                    Skip = skip,
                    Take = FindingQuery.MaxTake,
                },
                cancellationToken).ConfigureAwait(false);
            all.AddRange(page.Findings);
            if (page.Findings.Count == 0 || all.Count >= page.Total)
            {
                break;
            }

            skip += page.Findings.Count;
        }

        return all;
    }

    private async Task<IReadOnlyDictionary<ProtocolVersionId, ProtocolVersionSummary>?> LoadStudyVersionMapAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken)
    {
        if (protocolVersions is null)
        {
            return null;
        }

        var versions = await protocolVersions.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        return versions.Count == 0
            ? null
            : versions.ToDictionary(v => v.Id);
    }

    private static bool AppliesToInstance(
        IReadOnlyList<ProtocolVersionId> governingVersions,
        CollectionInstanceRecord instance,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersionSummary>? studyVersions)
    {
        if (governingVersions.Count == 0 || governingVersions.Contains(instance.ProtocolVersionId))
        {
            return true;
        }

        if (studyVersions is null)
        {
            return false;
        }

        var anyApprovedGoverning = governingVersions.Any(id =>
            !studyVersions.TryGetValue(id, out var summary) || summary.Status == ProtocolVersionStatus.Approved);
        if (anyApprovedGoverning)
        {
            return false;
        }

        foreach (var governingId in governingVersions)
        {
            var visited = new HashSet<ProtocolVersionId> { governingId };
            var current = studyVersions.GetValueOrDefault(governingId)?.ParentVersionId;
            while (current is { } parentId && visited.Add(parentId))
            {
                if (!studyVersions.TryGetValue(parentId, out var parentSummary))
                {
                    break;
                }

                if (parentSummary.Status == ProtocolVersionStatus.Approved)
                {
                    if (parentId == instance.ProtocolVersionId)
                    {
                        return true;
                    }

                    break;
                }

                current = parentSummary.ParentVersionId;
            }
        }

        return false;
    }

    private static CollectionInstanceRecord? SelectSiblingForm(
        CollectionInstanceRecord target,
        IEnumerable<CollectionInstanceRecord> candidates)
    {
        if (!string.IsNullOrWhiteSpace(target.PlannedActivityId))
        {
            var sameVisit = candidates
                .Where(f => string.Equals(f.PlannedActivityId, target.PlannedActivityId, StringComparison.Ordinal))
                .ToList();
            if (sameVisit.Count > 0)
            {
                return SelectNearestPreferred(target, sameVisit);
            }

            var unscheduled = candidates
                .Where(f => string.IsNullOrWhiteSpace(f.PlannedActivityId))
                .ToList();
            return unscheduled.Count > 0 ? SelectNearestPreferred(target, unscheduled) : null;
        }

        var list = candidates.ToList();
        return list.Count > 0 ? SelectNearestPreferred(target, list) : null;
    }

    private static CollectionInstanceRecord SelectNearestPreferred(
        CollectionInstanceRecord target,
        IReadOnlyList<CollectionInstanceRecord> candidates) =>
        candidates
            .OrderByDescending(HasPopulatedValues)
            .ThenBy(f => Math.Abs((f.CollectedAt - target.CollectedAt).Ticks))
            .ThenByDescending(f => f.CollectedAt)
            .ThenByDescending(f => f.OpenedAt)
            .ThenByDescending(f => f.CollectionInstanceId)
            .First();

    private static bool HasPopulatedValues(CollectionInstanceRecord form) =>
        form.Values.Any(v => v.Value is not null and not MissingValue);

    private static void SetIfFits(Dictionary<string, RuleValue> values, string key, RuleValue value)
    {
        if (key.Length <= FindingTarget.MaxNameLength)
        {
            values[key] = value;
        }
    }

    private static ValidationRunId DeriveRunId(TenantId tenant, StudyId study, string idempotencyKey)
    {
        var input = $"collection_validation_run/v1|{tenant}|{study}|{idempotencyKey}";
        Span<byte> bytes = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(input), bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return ValidationRunId.FromGuid(new Guid(bytes[..16], bigEndian: true));
    }

    private static string Shorten(string value) =>
        value.Length <= ValidationDetection.MaxTextLength
            ? value
            : string.Concat(value.AsSpan(0, ValidationDetection.MaxTextLength - 1), "…");
}
