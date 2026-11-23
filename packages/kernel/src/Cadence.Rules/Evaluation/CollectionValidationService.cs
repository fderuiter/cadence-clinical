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
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
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
    INodeVisibility? visibility = null,
    IProtocolVersionReader? protocols = null,
    IConceptRegistry? concepts = null,
    IBlindedTreatmentClassifier? blindingClassifier = null) : ICollectionValidationService
{
    private readonly IBlindedTreatmentClassifier _blinding = blindingClassifier ?? BlindedTreatmentClassifier.Instance;

    /// <inheritdoc />
    public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceId formId,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        PreviewFormAsync(tenant, formId, draftValues, asOf, unblinded: true, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceId formId,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        bool unblinded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draftValues);

        var target = await collections.FindAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return [];
        }

        return await PreviewFormAsync(tenant, target, draftValues, asOf, unblinded, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceRecord target,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        PreviewFormAsync(tenant, target, draftValues, asOf, unblinded: true, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceRecord target,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        bool unblinded,
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

        IReadOnlyList<ProtocolDesign> designs = [];
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap = new Dictionary<ConceptId, BlindedConceptMetadata>();
        if (!unblinded)
        {
            designs = await LoadDesignsAsync(tenant, target.StudyId, siblings, cancellationToken).ConfigureAwait(false);
            conceptMap = await LoadConceptsAsync(tenant, siblings, designs, cancellationToken).ConfigureAwait(false);
        }

        var targetOwnValues = BuildServiceOwnItemValues(
            target,
            draftValues,
            unblinded: unblinded,
            designs,
            conceptMap,
            masks: null);
        var latestSiblings = SelectLatestFormsBySpec(siblings, target.CollectionInstanceId, target.CollectionSpecId);
        var siblingIndex = BuildSiblingIndex(
            latestSiblings,
            sibling => BuildServiceOwnItemValues(sibling, draftOverlay: null, unblinded: unblinded, designs, conceptMap, masks: null));
        IndexedCollectionForm[] singleForm =
        [
            new IndexedCollectionForm(target, targetOwnValues, siblingIndex, unblinded),
        ];

        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var warnings = new List<CollectionPreviewWarning>();
        foreach (var pack in packs)
        {
            var rulesById = BuildRulesById(pack.Pack);
            var sink = new FormPreviewWarningSink(this, pack.Key, target, rulesById, unblinded, designs, conceptMap, warnings);
            evaluator.Evaluate(pack.Pack, context, new CollectionFormCursor(singleForm), sink);
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
        PreviewRulePackAsync(tenant, studyId, rulePackId, asOf, caller: null, unblindedEnrollments: null, cancellationToken);

    /// <inheritdoc />
    public async Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
        TenantId tenant,
        StudyId studyId,
        LibraryItemId rulePackId,
        DateOnly asOf,
        Actor? caller,
        IReadOnlySet<EnrollmentId>? unblindedEnrollments = null,
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
        if (allInstances.Count == 0)
        {
            return new CollectionRulePackPreviewResult(0, pack.Rules.Count, []);
        }

        IReadOnlyDictionary<NodeRef, MaskReason> masks = new Dictionary<NodeRef, MaskReason>();
        IReadOnlyList<CollectionInstanceRecord> instances = allInstances;
        if (caller is not null && visibility is not null)
        {
            var nodesToCheck = allInstances
                .Select(i => NodeRef.For(i.EnrollmentId))
                .Concat(allInstances.SelectMany(i => i.Values).Select(v => NodeRef.For(v.ObservationId)))
                .Distinct()
                .ToList();
            masks = await visibility.MaskAsync(tenant, caller, nodesToCheck, cancellationToken).ConfigureAwait(false);
            instances = [.. allInstances.Where(i => !masks.ContainsKey(NodeRef.For(i.EnrollmentId)))];
            if (instances.Count == 0)
            {
                return new CollectionRulePackPreviewResult(0, pack.Rules.Count, []);
            }
        }

        IReadOnlyList<ProtocolDesign> designs = [];
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap = new Dictionary<ConceptId, BlindedConceptMetadata>();
        if (caller is not null)
        {
            designs = await LoadDesignsAsync(tenant, studyId, instances, cancellationToken).ConfigureAwait(false);
            conceptMap = await LoadConceptsAsync(tenant, instances, designs, cancellationToken).ConfigureAwait(false);
        }

        var indexedForms = IndexStudyForms(
            instances,
            (enrollmentId, enrollmentForms) => IsEnrollmentUnblinded(
                enrollmentId,
                enrollmentForms,
                caller,
                unblindedEnrollments,
                masks,
                designs,
                conceptMap),
            designs,
            conceptMap,
            masks);

        var rulesById = BuildRulesById(pack);
        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var violations = new List<CollectionRulePackPreviewViolation>();
        var cursor = new CollectionFormCursor(indexedForms);
        var sink = new RulePackPreviewSink(this, item.Key, cursor, indexedForms, rulesById, designs, conceptMap, violations);
        evaluator.Evaluate(pack, context, cursor, sink);

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

        var indexedForms = IndexStudyForms(
            instances,
            isEnrollmentUnblinded: null,
            designs: [],
            conceptMap: new Dictionary<ConceptId, BlindedConceptMetadata>(),
            masks: null);

        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var detections = new List<ValidationDetection>();
        var seenIdentities = new HashSet<ValidationFindingId>();
        foreach (var pack in packs)
        {
            var cursor = new CollectionFormCursor(indexedForms);
            var sink = new StudyCollectionDetectionSink(tenant, studyId, pack.Key, cursor, indexedForms, seenIdentities, detections);
            evaluator.Evaluate(pack.Pack, context, cursor, sink);
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
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay) =>
        BuildRuleRecord(target, enrollmentForms, draftOverlay, isBlindedVariable: null);

    /// <summary>
    /// Builds the <see cref="RuleRecord"/> for <paramref name="target"/>, replacing any item matched by
    /// <paramref name="isBlindedVariable"/> with <see cref="RuleValue.Null"/> (K-AUTH-4).
    /// </summary>
    public static RuleRecord BuildRuleRecord(
        CollectionInstanceRecord target,
        IReadOnlyList<CollectionInstanceRecord> enrollmentForms,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        Func<CollectionInstanceRecord, string, ObservationValue?, bool>? isBlindedVariable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(enrollmentForms);

        var targetOwnValues = BuildStaticOwnItemValues(target, draftOverlay, isBlindedVariable);
        var latestBySpec = SelectLatestFormsBySpec(enrollmentForms, target.CollectionInstanceId, target.CollectionSpecId);
        var values = new Dictionary<string, RuleValue>(targetOwnValues.Count * 2 + latestBySpec.Count * 8, StringComparer.Ordinal);

        foreach (var (itemId, converted) in targetOwnValues)
        {
            values[itemId] = converted;
            var qualified = $"{target.CollectionSpecId}.{itemId}";
            if (qualified.Length <= FindingTarget.MaxNameLength)
            {
                values[qualified] = converted;
            }
        }

        for (var i = 0; i < latestBySpec.Count; i++)
        {
            var sibling = latestBySpec[i];
            var siblingOwnValues = BuildStaticOwnItemValues(sibling, draftOverlay: null, isBlindedVariable);
            foreach (var (siblingItemId, converted) in siblingOwnValues)
            {
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

        var targetItems = new HashSet<string>(target.Items.Count + target.Values.Count, StringComparer.Ordinal);
        for (var i = 0; i < target.Items.Count; i++)
        {
            targetItems.Add(target.Items[i].Id);
        }

        for (var i = 0; i < target.Values.Count; i++)
        {
            targetItems.Add(target.Values[i].ItemId);
        }

        var specId = target.CollectionSpecId;
        for (var i = 0; i < violation.Observed.Count; i++)
        {
            var variable = violation.Observed[i].Variable;
            if (targetItems.Contains(variable))
            {
                return variable;
            }

            if (TryStripSpecPrefix(variable, specId, out var stripped) && targetItems.Contains(stripped))
            {
                return stripped;
            }
        }

        if (target.Items.Count > 0)
        {
            return target.Items[0].Id;
        }

        var fallback = violation.Observed.Count > 0 ? violation.Observed[0].Variable : null;
        return fallback is not null && fallback.Length <= FindingTarget.MaxNameLength ? fallback : null;
    }

    private static string? ResolvePrimaryItemId(
        CollectionInstanceRecord target,
        Dictionary<string, RuleValue> targetOwnValues,
        ReadOnlySpan<RuleObservedValue> observed)
    {
        var specId = target.CollectionSpecId;
        for (var i = 0; i < observed.Length; i++)
        {
            var variable = observed[i].Variable;
            if (targetOwnValues.ContainsKey(variable))
            {
                return variable;
            }

            if (TryStripSpecPrefix(variable, specId, out var stripped) && targetOwnValues.ContainsKey(stripped))
            {
                return stripped;
            }
        }

        if (target.Items.Count > 0)
        {
            return target.Items[0].Id;
        }

        var fallback = observed.Length > 0 ? observed[0].Variable : null;
        return fallback is not null && fallback.Length <= FindingTarget.MaxNameLength ? fallback : null;
    }

    private static bool TryStripSpecPrefix(string variable, string specId, out string stripped)
    {
        if (variable.Length > specId.Length + 1
            && variable.StartsWith(specId, StringComparison.Ordinal)
            && variable[specId.Length] == '.')
        {
            stripped = variable[(specId.Length + 1)..];
            return true;
        }

        stripped = string.Empty;
        return false;
    }

    private IndexedCollectionForm[] IndexStudyForms(
        IReadOnlyList<CollectionInstanceRecord> instances,
        Func<EnrollmentId, IReadOnlyList<CollectionInstanceRecord>, bool>? isEnrollmentUnblinded,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        IReadOnlyDictionary<NodeRef, MaskReason>? masks)
    {
        var byEnrollment = new Dictionary<EnrollmentId, List<CollectionInstanceRecord>>();
        for (var i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            if (!byEnrollment.TryGetValue(instance.EnrollmentId, out var group))
            {
                group = [];
                byEnrollment.Add(instance.EnrollmentId, group);
            }

            group.Add(instance);
        }

        var ownValuesByFormId = new Dictionary<CollectionInstanceId, Dictionary<string, RuleValue>>(instances.Count);
        var siblingIndexByEnrollment = new Dictionary<EnrollmentId, EnrollmentSiblingIndex>(byEnrollment.Count);
        var unblindedByEnrollment = new Dictionary<EnrollmentId, bool>(byEnrollment.Count);

        foreach (var (enrollmentId, enrollmentForms) in byEnrollment)
        {
            var enrollmentUnblinded = isEnrollmentUnblinded is null || isEnrollmentUnblinded(enrollmentId, enrollmentForms);
            unblindedByEnrollment.Add(enrollmentId, enrollmentUnblinded);

            for (var i = 0; i < enrollmentForms.Count; i++)
            {
                var form = enrollmentForms[i];
                if (!ownValuesByFormId.ContainsKey(form.CollectionInstanceId))
                {
                    ownValuesByFormId.Add(
                        form.CollectionInstanceId,
                        BuildServiceOwnItemValues(form, draftOverlay: null, enrollmentUnblinded, designs, conceptMap, masks));
                }
            }

            var latestBySpec = SelectLatestFormsBySpec(enrollmentForms);
            siblingIndexByEnrollment.Add(
                enrollmentId,
                BuildSiblingIndex(latestBySpec, sibling => ownValuesByFormId[sibling.CollectionInstanceId]));
        }

        var indexedForms = new IndexedCollectionForm[instances.Count];
        for (var i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            indexedForms[i] = new IndexedCollectionForm(
                instance,
                ownValuesByFormId[instance.CollectionInstanceId],
                siblingIndexByEnrollment[instance.EnrollmentId],
                unblindedByEnrollment[instance.EnrollmentId]);
        }

        return indexedForms;
    }

    private static Dictionary<string, RuleValue> BuildStaticOwnItemValues(
        CollectionInstanceRecord form,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        Func<CollectionInstanceRecord, string, ObservationValue?, bool>? isBlindedVariable)
    {
        var recordedByItem = new Dictionary<string, ObservationValue?>(
            form.Values.Count + (draftOverlay?.Count ?? 0),
            StringComparer.Ordinal);
        for (var i = 0; i < form.Values.Count; i++)
        {
            var recorded = form.Values[i];
            recordedByItem[recorded.ItemId] = recorded.Value;
        }

        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                recordedByItem[itemId] = draftValue;
            }
        }

        var ownValues = new Dictionary<string, RuleValue>(form.Items.Count + recordedByItem.Count, StringComparer.Ordinal);
        for (var i = 0; i < form.Items.Count; i++)
        {
            var itemId = form.Items[i].Id;
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            recordedByItem.TryGetValue(itemId, out var raw);
            ownValues.Add(
                itemId,
                isBlindedVariable is not null && isBlindedVariable(form, itemId, raw) ? RuleValue.Null : ToRuleValue(raw));
        }

        foreach (var (itemId, raw) in recordedByItem)
        {
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            ownValues.Add(
                itemId,
                isBlindedVariable is not null && isBlindedVariable(form, itemId, raw) ? RuleValue.Null : ToRuleValue(raw));
        }

        return ownValues;
    }

    private Dictionary<string, RuleValue> BuildServiceOwnItemValues(
        CollectionInstanceRecord form,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        bool unblinded,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        IReadOnlyDictionary<NodeRef, MaskReason>? masks)
    {
        var recordedByItem = new Dictionary<string, (ObservationValue? Value, ObservationId? ObservationId)>(
            form.Values.Count + (draftOverlay?.Count ?? 0),
            StringComparer.Ordinal);
        for (var i = 0; i < form.Values.Count; i++)
        {
            var recorded = form.Values[i];
            recordedByItem[recorded.ItemId] = (recorded.Value, recorded.ObservationId);
        }

        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                recordedByItem.TryGetValue(itemId, out var existing);
                recordedByItem[itemId] = (draftValue, existing.ObservationId);
            }
        }

        var ownValues = new Dictionary<string, RuleValue>(form.Items.Count + recordedByItem.Count, StringComparer.Ordinal);
        for (var i = 0; i < form.Items.Count; i++)
        {
            var specItem = form.Items[i];
            if (ownValues.ContainsKey(specItem.Id))
            {
                continue;
            }

            recordedByItem.TryGetValue(specItem.Id, out var entry);
            var converted = !unblinded && IsBlindedItemFast(form, specItem.Id, specItem, entry.Value, entry.ObservationId, designs, conceptMap, masks)
                ? RuleValue.Null
                : ToRuleValue(entry.Value);
            ownValues.Add(specItem.Id, converted);
        }

        foreach (var (itemId, entry) in recordedByItem)
        {
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            var converted = !unblinded && IsBlindedItemFast(form, itemId, specItem: null, entry.Value, entry.ObservationId, designs, conceptMap, masks)
                ? RuleValue.Null
                : ToRuleValue(entry.Value);
            ownValues.Add(itemId, converted);
        }

        return ownValues;
    }

    private static List<CollectionInstanceRecord> SelectLatestFormsBySpec(
        IReadOnlyList<CollectionInstanceRecord> enrollmentForms,
        CollectionInstanceId? excludeFormId = null,
        string? excludeSpecId = null)
    {
        var latest = new List<CollectionInstanceRecord>();
        if (enrollmentForms.Count == 0)
        {
            return latest;
        }

        var indexBySpec = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < enrollmentForms.Count; i++)
        {
            var candidate = enrollmentForms[i];
            if (excludeFormId is { } excludedId && candidate.CollectionInstanceId == excludedId)
            {
                continue;
            }

            if (excludeSpecId is not null && string.Equals(candidate.CollectionSpecId, excludeSpecId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!indexBySpec.TryGetValue(candidate.CollectionSpecId, out var existingIndex))
            {
                indexBySpec.Add(candidate.CollectionSpecId, latest.Count);
                latest.Add(candidate);
            }
            else if (IsNewerOrSameSibling(candidate, latest[existingIndex]))
            {
                latest[existingIndex] = candidate;
            }
        }

        return latest;
    }

    private static bool IsNewerOrSameSibling(CollectionInstanceRecord candidate, CollectionInstanceRecord current)
    {
        var openedComparison = candidate.OpenedAt.CompareTo(current.OpenedAt);
        if (openedComparison != 0)
        {
            return openedComparison > 0;
        }

        return Comparer<CollectionInstanceId>.Default.Compare(candidate.CollectionInstanceId, current.CollectionInstanceId) >= 0;
    }

    private static EnrollmentSiblingIndex BuildSiblingIndex(
        List<CollectionInstanceRecord> latestBySpec,
        Func<CollectionInstanceRecord, Dictionary<string, RuleValue>> resolveOwnValues)
    {
        if (latestBySpec.Count == 0)
        {
            return EnrollmentSiblingIndex.Empty;
        }

        var qualifiedValues = new Dictionary<string, QualifiedSiblingEntry>(latestBySpec.Count * 8, StringComparer.Ordinal);
        var unqualifiedValues = new Dictionary<string, UnqualifiedSiblingEntry>(latestBySpec.Count * 8, StringComparer.Ordinal);

        for (var i = 0; i < latestBySpec.Count; i++)
        {
            var sibling = latestBySpec[i];
            var siblingSpec = sibling.CollectionSpecId;
            var ownValues = resolveOwnValues(sibling);
            foreach (var (itemId, converted) in ownValues)
            {
                var qualified = $"{siblingSpec}.{itemId}";
                if (qualified.Length <= FindingTarget.MaxNameLength)
                {
                    if (qualifiedValues.TryGetValue(qualified, out var existingQualified))
                    {
                        qualifiedValues[qualified] = new QualifiedSiblingEntry(siblingSpec, converted, HasPrior: true, existingQualified.Value);
                    }
                    else
                    {
                        qualifiedValues.Add(qualified, new QualifiedSiblingEntry(siblingSpec, converted, HasPrior: false, default));
                    }
                }

                if (itemId.Length <= FindingTarget.MaxNameLength)
                {
                    if (!unqualifiedValues.TryGetValue(itemId, out var existingUnqualified))
                    {
                        unqualifiedValues.Add(itemId, new UnqualifiedSiblingEntry(siblingSpec, converted, HasSecond: false, default));
                    }
                    else if (!existingUnqualified.HasSecond)
                    {
                        unqualifiedValues[itemId] = existingUnqualified with { HasSecond = true, SecondValue = converted };
                    }
                }
            }
        }

        return new EnrollmentSiblingIndex(qualifiedValues, unqualifiedValues);
    }

    private static Dictionary<string, Rule> BuildRulesById(RulePack pack)
    {
        var map = new Dictionary<string, Rule>(pack.Rules.Count, StringComparer.Ordinal);
        for (var i = 0; i < pack.Rules.Count; i++)
        {
            var rule = pack.Rules[i];
            map[rule.Id] = rule;
        }

        return map;
    }

    private bool IsEnrollmentUnblinded(
        EnrollmentId enrollmentId,
        IReadOnlyList<CollectionInstanceRecord> enrollmentForms,
        Actor? caller,
        IReadOnlySet<EnrollmentId>? unblindedEnrollments,
        IReadOnlyDictionary<NodeRef, MaskReason> masks,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap)
    {
        if (caller is null)
        {
            return true;
        }

        var hasMaskedBlindedObservation = enrollmentForms
            .SelectMany(f => f.Values)
            .Any(v => masks.ContainsKey(NodeRef.For(v.ObservationId)));
        if (hasMaskedBlindedObservation)
        {
            return false;
        }

        if (unblindedEnrollments is not null)
        {
            return unblindedEnrollments.Contains(enrollmentId);
        }

        if (visibility is null)
        {
            return false;
        }

        var hasRecordedBlindedObservation = enrollmentForms.Any(form =>
            form.Values.Any(v => IsBlindedFormVariable(form, v.ItemId, v.Value, designs, conceptMap)));
        return hasRecordedBlindedObservation;
    }

    private bool IsBlindedFormVariable(
        CollectionInstanceRecord form,
        string itemId,
        ObservationValue? rawValue,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        IReadOnlyDictionary<NodeRef, MaskReason>? masks = null)
    {
        ObservationId? observationId = null;
        if (masks is not null)
        {
            var recorded = form.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal));
            observationId = recorded?.ObservationId;
        }

        var specItem = form.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
        return IsBlindedItemFast(form, itemId, specItem, rawValue, observationId, designs, conceptMap, masks);
    }

    private bool IsBlindedItemFast(
        CollectionInstanceRecord form,
        string itemId,
        CollectionItem? specItem,
        ObservationValue? rawValue,
        ObservationId? observationId,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        IReadOnlyDictionary<NodeRef, MaskReason>? masks)
    {
        if (masks is not null && observationId is { } obsId && masks.ContainsKey(NodeRef.For(obsId)))
        {
            return true;
        }

        BlindedConceptMetadata? conceptMeta = specItem is not null && conceptMap.TryGetValue(specItem.Concept.ConceptId, out var meta)
            ? meta
            : null;
        if (_blinding.IsBlindedCollectionItem(form.CollectionSpecId, form.CollectionTitle, specItem, rawValue, conceptMeta, designs))
        {
            return true;
        }

        BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptMap.TryGetValue(id, out var m) ? m : null;
        return _blinding.IsBlindedVariable(form.CollectionSpecId, itemId, designs, ResolveConcept);
    }

    private bool IsBlindedViolation(
        string dataset,
        ReadOnlySpan<RuleObservedValue> observed,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap)
    {
        if (_blinding.IsBlindedCollectionSpec(dataset, designs: designs))
        {
            return true;
        }

        BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptMap.TryGetValue(id, out var meta) ? meta : null;
        for (var i = 0; i < observed.Length; i++)
        {
            if (_blinding.IsBlindedVariable(dataset, observed[i].Variable, designs, ResolveConcept))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<ProtocolDesign>> LoadDesignsAsync(
        TenantId tenant,
        StudyId studyId,
        IReadOnlyList<CollectionInstanceRecord> forms,
        CancellationToken cancellationToken)
    {
        if (protocols is null)
        {
            return [];
        }

        var designs = new List<ProtocolDesign>();
        var seenVersions = new HashSet<ProtocolVersionId>();
        foreach (var versionId in forms.Select(f => f.ProtocolVersionId).Distinct())
        {
            if (seenVersions.Add(versionId)
                && await protocols.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                designs.Add(design);
            }
        }

        if (designs.Count > 0)
        {
            return designs;
        }

        var summaries = await protocols.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        foreach (var summaryId in summaries.Select(summary => summary.Id).Where(seenVersions.Add))
        {
            if (await protocols.GetDesignAsync(tenant, summaryId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                designs.Add(design);
            }
        }

        return designs;
    }

    private async Task<IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>> LoadConceptsAsync(
        TenantId tenant,
        IReadOnlyList<CollectionInstanceRecord> forms,
        IReadOnlyList<ProtocolDesign> designs,
        CancellationToken cancellationToken)
    {
        if (concepts is null)
        {
            return new Dictionary<ConceptId, BlindedConceptMetadata>();
        }

        var resolved = new Dictionary<ConceptId, BlindedConceptMetadata>();
        var refs = forms.SelectMany(f => f.Items).Select(i => i.Concept)
            .Concat(designs.SelectMany(d => d.CollectionSpecs).SelectMany(s => s.Items).Select(i => i.Concept))
            .Distinct();
        foreach (var conceptRef in refs)
        {
            var view = await concepts.ResolveAsync(tenant, conceptRef, cancellationToken).ConfigureAwait(false)
                ?? await concepts.GetCurrentAsync(tenant, conceptRef.ConceptId, cancellationToken).ConfigureAwait(false);
            if (view is not null)
            {
                resolved[conceptRef.ConceptId] = new BlindedConceptMetadata(view.ExternalCode, view.PreferredLabel, view.Definition);
            }
        }

        return resolved;
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

    private static IndexedCollectionForm? ResolveTargetForm(
        string recordKey,
        CollectionFormCursor cursor,
        IndexedCollectionForm[] forms,
        ref Dictionary<string, IndexedCollectionForm>? formsByKey)
    {
        if (cursor.CurrentForm is { } current && string.Equals(current.Key, recordKey, StringComparison.Ordinal))
        {
            return current;
        }

        if (formsByKey is null)
        {
            formsByKey = new Dictionary<string, IndexedCollectionForm>(forms.Length, StringComparer.Ordinal);
            for (var i = 0; i < forms.Length; i++)
            {
                var form = forms[i];
                formsByKey[form.Key] = form;
            }
        }

        return formsByKey.TryGetValue(recordKey, out var resolved) ? resolved : null;
    }

    private readonly record struct QualifiedSiblingEntry(
        string SpecId,
        RuleValue Value,
        bool HasPrior,
        RuleValue PriorValue);

    private readonly record struct UnqualifiedSiblingEntry(
        string FirstSpecId,
        RuleValue FirstValue,
        bool HasSecond,
        RuleValue SecondValue);

    private sealed record EnrollmentSiblingIndex(
        Dictionary<string, QualifiedSiblingEntry> QualifiedValues,
        Dictionary<string, UnqualifiedSiblingEntry> UnqualifiedValues)
    {
        public static readonly EnrollmentSiblingIndex Empty = new(
            new Dictionary<string, QualifiedSiblingEntry>(0, StringComparer.Ordinal),
            new Dictionary<string, UnqualifiedSiblingEntry>(0, StringComparer.Ordinal));
    }

    private sealed class IndexedCollectionForm(
        CollectionInstanceRecord form,
        Dictionary<string, RuleValue> ownValues,
        EnrollmentSiblingIndex siblings,
        bool unblinded)
    {
        private string? _key;

        public CollectionInstanceRecord Form { get; } = form;

        public string Key => _key ??= Form.CollectionInstanceId.Value.ToString("D");

        public Dictionary<string, RuleValue> OwnValues { get; } = ownValues;

        public EnrollmentSiblingIndex Siblings { get; } = siblings;

        public bool Unblinded { get; } = unblinded;
    }

    private sealed class CollectionFormCursor(IndexedCollectionForm[] forms) : IRuleRecordCursor
    {
        private int _index = -1;

        public IndexedCollectionForm? CurrentForm { get; private set; }

        public string Dataset => CurrentForm!.Form.CollectionSpecId;

        public string Key => CurrentForm!.Key;

        public RuleValue this[string name]
        {
            get
            {
                var current = CurrentForm!;
                var targetSpec = current.Form.CollectionSpecId;

                if (current.Siblings.QualifiedValues.TryGetValue(name, out var qualifiedSibling))
                {
                    if (!string.Equals(qualifiedSibling.SpecId, targetSpec, StringComparison.Ordinal))
                    {
                        return qualifiedSibling.Value;
                    }

                    if (qualifiedSibling.HasPrior)
                    {
                        return qualifiedSibling.PriorValue;
                    }
                }

                if (current.OwnValues.TryGetValue(name, out var ownValue))
                {
                    return ownValue;
                }

                if (name.Length <= FindingTarget.MaxNameLength
                    && TryStripSpecPrefix(name, targetSpec, out var stripped)
                    && current.OwnValues.TryGetValue(stripped, out var ownQualifiedValue))
                {
                    return ownQualifiedValue;
                }

                if (current.Siblings.UnqualifiedValues.TryGetValue(name, out var unqualifiedSibling))
                {
                    if (!string.Equals(unqualifiedSibling.FirstSpecId, targetSpec, StringComparison.Ordinal))
                    {
                        return unqualifiedSibling.FirstValue;
                    }

                    return unqualifiedSibling.HasSecond ? unqualifiedSibling.SecondValue : RuleValue.Null;
                }

                return RuleValue.Null;
            }
        }

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next < forms.Length)
            {
                _index = next;
                CurrentForm = forms[next];
                return true;
            }

            _index = forms.Length;
            CurrentForm = null;
            return false;
        }
    }

    private sealed class FormPreviewWarningSink(
        CollectionValidationService owner,
        string packKey,
        CollectionInstanceRecord target,
        Dictionary<string, Rule> rulesById,
        bool unblinded,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        List<CollectionPreviewWarning> warnings) : IRuleViolationSink
    {
        public void OnViolation(RuleViolationHit violation)
        {
            if (!unblinded && owner.IsBlindedViolation(violation.Dataset, violation.Observed, designs, conceptMap))
            {
                return;
            }

            var itemId = ResolveTargetPrimaryItemId(target, violation.Observed) ?? string.Empty;
            rulesById.TryGetValue(violation.RuleId, out var rule);
            warnings.Add(new CollectionPreviewWarning(
                packKey,
                violation.RuleId,
                violation.RuleVersion,
                violation.Severity,
                violation.Dataset,
                itemId,
                violation.Explanation,
                rule?.RemediationGuidance));
        }

        private static string? ResolveTargetPrimaryItemId(
            CollectionInstanceRecord targetRecord,
            ReadOnlySpan<RuleObservedValue> observed)
        {
            var specId = targetRecord.CollectionSpecId;
            for (var i = 0; i < observed.Length; i++)
            {
                var variable = observed[i].Variable;
                if (HasTargetItem(targetRecord, variable))
                {
                    return variable;
                }

                if (TryStripSpecPrefix(variable, specId, out var stripped) && HasTargetItem(targetRecord, stripped))
                {
                    return stripped;
                }
            }

            if (targetRecord.Items.Count > 0)
            {
                return targetRecord.Items[0].Id;
            }

            var fallback = observed.Length > 0 ? observed[0].Variable : null;
            return fallback is not null && fallback.Length <= FindingTarget.MaxNameLength ? fallback : null;
        }

        private static bool HasTargetItem(CollectionInstanceRecord targetRecord, string itemId)
        {
            for (var i = 0; i < targetRecord.Items.Count; i++)
            {
                if (string.Equals(targetRecord.Items[i].Id, itemId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            for (var i = 0; i < targetRecord.Values.Count; i++)
            {
                if (string.Equals(targetRecord.Values[i].ItemId, itemId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class RulePackPreviewSink(
        CollectionValidationService owner,
        string packKey,
        CollectionFormCursor cursor,
        IndexedCollectionForm[] forms,
        Dictionary<string, Rule> rulesById,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        List<CollectionRulePackPreviewViolation> violations) : IRuleViolationSink
    {
        private Dictionary<string, IndexedCollectionForm>? _formsByKey;

        public void OnViolation(RuleViolationHit violation)
        {
            var targetForm = ResolveTargetForm(violation.RecordKey, cursor, forms, ref _formsByKey);
            if (targetForm is null)
            {
                return;
            }

            if (!targetForm.Unblinded && owner.IsBlindedViolation(violation.Dataset, violation.Observed, designs, conceptMap))
            {
                return;
            }

            var itemId = ResolvePrimaryItemId(targetForm.Form, targetForm.OwnValues, violation.Observed);
            rulesById.TryGetValue(violation.RuleId, out var rule);
            violations.Add(new CollectionRulePackPreviewViolation(
                targetForm.Form.CollectionInstanceId,
                packKey,
                violation.RuleId,
                violation.RuleVersion,
                violation.Severity,
                violation.Dataset,
                itemId,
                violation.Explanation,
                rule?.RemediationGuidance));
        }
    }

    private sealed class StudyCollectionDetectionSink(
        TenantId tenant,
        StudyId studyId,
        string packKey,
        CollectionFormCursor cursor,
        IndexedCollectionForm[] forms,
        HashSet<ValidationFindingId> seenIdentities,
        List<ValidationDetection> detections) : IRuleViolationSink
    {
        private Dictionary<string, IndexedCollectionForm>? _formsByKey;

        public void OnViolation(RuleViolationHit violation)
        {
            var targetForm = ResolveTargetForm(violation.RecordKey, cursor, forms, ref _formsByKey);
            var primaryVariable = targetForm is null
                ? null
                : ResolvePrimaryItemId(targetForm.Form, targetForm.OwnValues, violation.Observed);
            var target = new FindingTarget(FindingTargetKind.DatasetRecord, violation.Dataset, violation.RecordKey, primaryVariable);
            var identity = new FindingIdentity(studyId, ValidationStage.Collection, packKey, violation.RuleId, target);
            if (!seenIdentities.Add(ValidationFindingId.For(tenant, identity)))
            {
                return;
            }

            ObservedVariable[] observed;
            if (violation.Observed.Length == 0)
            {
                observed = [];
            }
            else
            {
                observed = new ObservedVariable[violation.Observed.Length];
                for (var i = 0; i < violation.Observed.Length; i++)
                {
                    var item = violation.Observed[i];
                    observed[i] = new ObservedVariable(item.Variable, item.Value.IsNull ? null : Shorten(item.Value.ToString()));
                }
            }

            detections.Add(new ValidationDetection(
                packKey,
                violation.RuleId,
                violation.RuleVersion,
                violation.Severity,
                target,
                violation.Explanation,
                Expected: null,
                observed));
        }

        private static string Shorten(string value) =>
            value.Length <= ValidationDetection.MaxTextLength
                ? value
                : string.Concat(value.AsSpan(0, ValidationDetection.MaxTextLength - 1), "…");
    }
}
