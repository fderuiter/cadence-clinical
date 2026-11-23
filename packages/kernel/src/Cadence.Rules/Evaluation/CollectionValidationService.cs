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
    IClock? clock = null) : ICollectionValidationService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceId formId,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        PreviewFormAsync(tenant, formId, draftValues, asOf, maskBlinded: false, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceId formId,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        bool maskBlinded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draftValues);

        var target = await collections.FindAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return [];
        }

        return await PreviewFormAsync(tenant, target, draftValues, asOf, maskBlinded, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceRecord target,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        PreviewFormAsync(tenant, target, draftValues, asOf, maskBlinded: false, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceRecord target,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        bool maskBlinded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(draftValues);

        var packs = await loader.LoadForStudyAsync(tenant, target.StudyId, cancellationToken).ConfigureAwait(false);
        if (packs.Count == 0)
        {
            return [];
        }

        var enrollmentForms = await collections.ListRecordsForEnrollmentAsync(tenant, target.EnrollmentId, cancellationToken).ConfigureAwait(false);
        var siblings = enrollmentForms.Count > 0 ? enrollmentForms : [target];
        var record = BuildRuleRecord(target, siblings, draftValues, maskBlinded);
        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var warnings = new List<CollectionPreviewWarning>();
        foreach (var pack in packs)
        {
            var rulesById = pack.Pack.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
            foreach (var violation in evaluator.Evaluate(pack.Pack, context, [record]))
            {
                var itemId = ResolvePrimaryItemId(target, violation) ?? string.Empty;
                var citesBlinded = maskBlinded && ViolationCitesBlindedVariable(target, siblings, violation);
                rulesById.TryGetValue(violation.RuleId, out var rule);
                warnings.Add(new CollectionPreviewWarning(
                    pack.Key,
                    violation.RuleId,
                    violation.RuleVersion,
                    violation.Severity,
                    violation.Dataset,
                    itemId,
                    citesBlinded ? "masked" : violation.Explanation,
                    citesBlinded ? null : rule?.RemediationGuidance));
            }
        }

        return warnings;
    }

    /// <inheritdoc />
    public Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
        TenantId tenant,
        StudyId studyId,
        LibraryItemId rulePackId,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        PreviewRulePackAsync(tenant, studyId, rulePackId, asOf, visibleEnrollments: null, unblindedEnrollments: null, cancellationToken);

    /// <inheritdoc />
    public async Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
        TenantId tenant,
        StudyId studyId,
        LibraryItemId rulePackId,
        DateOnly asOf,
        IReadOnlySet<EnrollmentId>? visibleEnrollments,
        IReadOnlySet<EnrollmentId>? unblindedEnrollments,
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
        var instances = visibleEnrollments is null
            ? allInstances
            : [.. allInstances.Where(i => visibleEnrollments.Contains(i.EnrollmentId))];
        if (instances.Count == 0)
        {
            return new CollectionRulePackPreviewResult(0, pack.Rules.Count, []);
        }

        var byEnrollment = instances
            .GroupBy(i => i.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstanceRecord>)[.. g]);
        var instancesByKey = instances.ToDictionary(i => i.CollectionInstanceId.Value.ToString("D"), StringComparer.Ordinal);
        var records = instances
            .Select(i => BuildRuleRecord(
                i,
                byEnrollment[i.EnrollmentId],
                draftOverlay: null,
                maskBlinded: unblindedEnrollments is not null && !unblindedEnrollments.Contains(i.EnrollmentId)))
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
            var maskForInstance = unblindedEnrollments is not null && !unblindedEnrollments.Contains(targetInstance.EnrollmentId);
            var citesBlinded = maskForInstance && ViolationCitesBlindedVariable(targetInstance, byEnrollment[targetInstance.EnrollmentId], violation);
            rulesById.TryGetValue(violation.RuleId, out var rule);
            violations.Add(new CollectionRulePackPreviewViolation(
                targetInstance.CollectionInstanceId,
                item.Key,
                violation.RuleId,
                violation.RuleVersion,
                violation.Severity,
                violation.Dataset,
                itemId,
                citesBlinded ? "masked" : violation.Explanation,
                citesBlinded ? null : rule?.RemediationGuidance));
        }

        return new CollectionRulePackPreviewResult(instances.Count, pack.Rules.Count, violations);
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
        var priorFindings = await findings.ListAsync(
            tenant,
            new FindingQuery(studyId) { Stage = ValidationStage.Collection, Take = FindingQuery.MaxTake },
            cancellationToken).ConfigureAwait(false);

        var datasets = instances.Select(i => i.CollectionSpecId)
            .Concat(packs.SelectMany(p => p.Pack.Rules)
                .Where(r => r.Applicability.Stage == ValidationStage.Collection && !string.IsNullOrWhiteSpace(r.Applicability.Dataset))
                .Select(r => r.Applicability.Dataset!))
            .Concat(priorFindings.Findings.Select(f => f.Target.Dataset))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (datasets.Count == 0)
        {
            return CommandResult<CollectionValidationResult>.Success(new CollectionValidationResult(null, priorFindings.Findings));
        }

        var byEnrollment = instances
            .GroupBy(i => i.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstanceRecord>)[.. g]);
        var instancesByKey = instances.ToDictionary(i => i.CollectionInstanceId.Value.ToString("D"), StringComparer.Ordinal);
        var records = instances
            .Select(i => BuildRuleRecord(i, byEnrollment[i.EnrollmentId], draftOverlay: null))
            .ToList();

        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var detections = new List<ValidationDetection>();
        var seenIdentities = new HashSet<ValidationFindingId>();
        foreach (var pack in packs)
        {
            foreach (var violation in evaluator.Evaluate(pack.Pack, context, records))
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

        var updatedFindings = await findings.ListAsync(
            tenant,
            new FindingQuery(studyId) { Stage = ValidationStage.Collection, Take = FindingQuery.MaxTake },
            cancellationToken).ConfigureAwait(false);
        return CommandResult<CollectionValidationResult>.Success(
            new CollectionValidationResult(runResult.Value, updatedFindings.Findings));
    }

    /// <summary>Builds the <see cref="RuleRecord"/> for <paramref name="target"/>, including cross-form variables from sibling instances on the enrollment.</summary>
    public static RuleRecord BuildRuleRecord(
        CollectionInstanceRecord target,
        IReadOnlyList<CollectionInstanceRecord> enrollmentForms,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        bool maskBlinded = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(enrollmentForms);

        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        var recordedByItem = target.Values.ToDictionary(
            v => v.ItemId,
            v => maskBlinded && target.IsValueBlinded(v.ItemId, v.ObservationId) ? null : (ObservationValue?)v.Value,
            StringComparer.Ordinal);
        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                if (maskBlinded && (target.BlindedItemIds?.Contains(itemId) ?? false))
                {
                    recordedByItem[itemId] = null;
                }
                else
                {
                    recordedByItem[itemId] = draftValue;
                }
            }
        }

        var allTargetItemIds = target.Items.Select(i => i.Id)
            .Concat(recordedByItem.Keys)
            .Distinct(StringComparer.Ordinal);
        foreach (var itemId in allTargetItemIds)
        {
            recordedByItem.TryGetValue(itemId, out var raw);
            var converted = maskBlinded && (target.BlindedItemIds?.Contains(itemId) ?? false)
                ? RuleValue.Null
                : ToRuleValue(raw);
            values[itemId] = converted;
            var qualified = $"{target.CollectionSpecId}.{itemId}";
            if (qualified.Length <= FindingTarget.MaxNameLength)
            {
                values[qualified] = converted;
            }
        }

        // Bind latest sibling form values per CollectionSpecId on the same enrollment for cross-form edit checks.
        var latestBySpec = enrollmentForms
            .Where(f => f.CollectionInstanceId != target.CollectionInstanceId
                && !string.Equals(f.CollectionSpecId, target.CollectionSpecId, StringComparison.Ordinal))
            .GroupBy(f => f.CollectionSpecId, StringComparer.Ordinal)
            .Select(g => g.OrderBy(f => f.OpenedAt).ThenBy(f => f.CollectionInstanceId).Last());

        foreach (var sibling in latestBySpec)
        {
            var siblingRecorded = sibling.Values.ToDictionary(
                v => v.ItemId,
                v => maskBlinded && sibling.IsValueBlinded(v.ItemId, v.ObservationId) ? null : (ObservationValue?)v.Value,
                StringComparer.Ordinal);
            var siblingItemIds = sibling.Items.Select(i => i.Id)
                .Concat(siblingRecorded.Keys)
                .Distinct(StringComparer.Ordinal);
            foreach (var siblingItemId in siblingItemIds)
            {
                siblingRecorded.TryGetValue(siblingItemId, out var raw);
                var converted = maskBlinded && (sibling.BlindedItemIds?.Contains(siblingItemId) ?? false)
                    ? RuleValue.Null
                    : ToRuleValue(raw);
                var qualified = $"{sibling.CollectionSpecId}.{siblingItemId}";
                if (qualified.Length <= FindingTarget.MaxNameLength)
                {
                    values[qualified] = converted;
                }

                if (!values.ContainsKey(siblingItemId) && siblingItemId.Length <= FindingTarget.MaxNameLength)
                {
                    values[siblingItemId] = converted;
                }
            }
        }

        return new RuleRecord(target.CollectionSpecId, target.CollectionInstanceId.Value.ToString("D"), values);
    }

    private static bool ViolationCitesBlindedVariable(
        CollectionInstanceRecord target,
        IReadOnlyList<CollectionInstanceRecord> enrollmentForms,
        RuleViolation violation)
    {
        var blindedTokens = new HashSet<string>(StringComparer.Ordinal);
        if (target.BlindedItemIds is { Count: > 0 } targetBlinded)
        {
            foreach (var itemId in targetBlinded)
            {
                blindedTokens.Add(itemId);
                blindedTokens.Add($"{target.CollectionSpecId}.{itemId}");
            }
        }

        foreach (var sibling in enrollmentForms)
        {
            if (sibling.BlindedItemIds is not { Count: > 0 } siblingBlinded)
            {
                continue;
            }

            foreach (var itemId in siblingBlinded)
            {
                blindedTokens.Add($"{sibling.CollectionSpecId}.{itemId}");
                blindedTokens.Add(itemId);
            }
        }

        return blindedTokens.Count > 0
            && violation.Observed.Any(o => blindedTokens.Contains(o.Variable));
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

        if (target.Items.Count > 0)
        {
            return target.Items[0].Id;
        }

        var fallback = violation.Observed.FirstOrDefault()?.Variable;
        return fallback is not null && fallback.Length <= FindingTarget.MaxNameLength ? fallback : null;
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
