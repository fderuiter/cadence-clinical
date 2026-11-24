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

        var targetInput = CollectionRuleInput.From(target);
        var enrollmentForms = await collections.ListRuleInputsForEnrollmentAsync(tenant, target.EnrollmentId, cancellationToken).ConfigureAwait(false);
        var siblings = enrollmentForms.Count > 0 ? enrollmentForms : [targetInput];

        Func<CollectionRuleInput, string, ObservationValue?, bool>? isBlindedVariable = null;
        IReadOnlyList<ProtocolDesign> designs = [];
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap = new Dictionary<ConceptId, BlindedConceptMetadata>();
        if (!unblinded)
        {
            designs = await LoadDesignsAsync(tenant, target.StudyId, siblings.Select(s => s.ProtocolVersionId), cancellationToken).ConfigureAwait(false);
            conceptMap = await LoadConceptsAsync(tenant, siblings.SelectMany(s => s.Items), designs, cancellationToken).ConfigureAwait(false);
            isBlindedVariable = (form, itemId, raw) => IsBlindedRuleInputVariable(form, itemId, raw, designs, conceptMap);
        }

        var record = BuildRuleRecord(targetInput, siblings, draftValues, isBlindedVariable);
        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var warnings = new List<CollectionPreviewWarning>();
        foreach (var pack in packs)
        {
            var rulesById = pack.Pack.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
            foreach (var violation in evaluator.Evaluate(pack.Pack, context, [record]))
            {
                if (!unblinded && IsBlindedViolation(violation, designs, conceptMap))
                {
                    continue;
                }

                var itemId = ResolvePrimaryItemId(target, violation) ?? string.Empty;
                rulesById.TryGetValue(violation.RuleId, out var rule);
                warnings.Add(new CollectionPreviewWarning(
                    pack.Key,
                    violation.RuleId,
                    violation.RuleVersion,
                    violation.Severity,
                    violation.Dataset,
                    itemId,
                    violation.MessageOrExplanation(),
                    rule?.RemediationGuidance));
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
            designs = await LoadDesignsAsync(tenant, studyId, instances.Select(i => i.ProtocolVersionId), cancellationToken).ConfigureAwait(false);
            conceptMap = await LoadConceptsAsync(tenant, instances.SelectMany(i => i.Items), designs, cancellationToken).ConfigureAwait(false);
        }

        var byEnrollment = instances
            .GroupBy(i => i.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstanceRecord>)[.. g]);
        var unblindedByEnrollment = new Dictionary<EnrollmentId, bool>(byEnrollment.Count);
        foreach (var (enrollmentId, enrollmentForms) in byEnrollment)
        {
            unblindedByEnrollment[enrollmentId] = IsEnrollmentUnblinded(
                enrollmentId,
                enrollmentForms,
                caller,
                unblindedEnrollments,
                masks,
                designs,
                conceptMap);
        }

        var instancesByKey = instances.ToDictionary(i => i.CollectionInstanceId.Value.ToString("D"), StringComparer.Ordinal);
        var records = instances
            .Select(i =>
            {
                var enrollmentUnblinded = unblindedByEnrollment[i.EnrollmentId];
                Func<CollectionInstanceRecord, string, ObservationValue?, bool>? isBlindedVariable = enrollmentUnblinded
                    ? null
                    : (form, itemId, raw) => IsBlindedFormVariable(form, itemId, raw, designs, conceptMap, masks);
                return BuildRuleRecord(i, byEnrollment[i.EnrollmentId], draftOverlay: null, isBlindedVariable);
            })
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

            if (!unblindedByEnrollment[targetInstance.EnrollmentId]
                && IsBlindedViolation(violation, designs, conceptMap))
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
                violation.MessageOrExplanation(),
                rule?.RemediationGuidance));
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
        return BuildRuleRecordCore(
            target,
            enrollmentForms,
            f => f.CollectionInstanceId,
            f => f.CollectionSpecId,
            f => f.OpenedAt,
            f => f.Items,
            f => f.Values.Select(v => (v.ItemId, v.Value)),
            draftOverlay,
            isBlindedVariable);
    }

    /// <summary>
    /// Builds the <see cref="RuleRecord"/> for a narrow <paramref name="target"/> input, replacing any item matched by
    /// <paramref name="isBlindedVariable"/> with <see cref="RuleValue.Null"/> (K-AUTH-4).
    /// </summary>
    public static RuleRecord BuildRuleRecord(
        CollectionRuleInput target,
        IReadOnlyList<CollectionRuleInput> enrollmentForms,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        Func<CollectionRuleInput, string, ObservationValue?, bool>? isBlindedVariable = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(enrollmentForms);
        return BuildRuleRecordCore(
            target,
            enrollmentForms,
            f => f.CollectionInstanceId,
            f => f.CollectionSpecId,
            f => f.OpenedAt,
            f => f.Items,
            f => f.Values.Select(v => (v.ItemId, v.Value)),
            draftOverlay,
            isBlindedVariable);
    }

    private static RuleRecord BuildRuleRecordCore<TForm>(
        TForm target,
        IReadOnlyList<TForm> enrollmentForms,
        Func<TForm, CollectionInstanceId> getId,
        Func<TForm, string> getSpecId,
        Func<TForm, DateTimeOffset> getOpenedAt,
        Func<TForm, IReadOnlyList<CollectionItem>> getItems,
        Func<TForm, IEnumerable<(string ItemId, ObservationValue Value)>> getValues,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        Func<TForm, string, ObservationValue?, bool>? isBlindedVariable)
    {
        var targetId = getId(target);
        var targetSpecId = getSpecId(target);
        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        var recordedByItem = getValues(target).ToDictionary(
            v => v.ItemId,
            v => (ObservationValue?)v.Value,
            StringComparer.Ordinal);
        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                recordedByItem[itemId] = draftValue;
            }
        }

        var allTargetItemIds = getItems(target).Select(i => i.Id)
            .Concat(recordedByItem.Keys)
            .Distinct(StringComparer.Ordinal);
        foreach (var itemId in allTargetItemIds)
        {
            recordedByItem.TryGetValue(itemId, out var raw);
            var converted = isBlindedVariable is not null && isBlindedVariable(target, itemId, raw)
                ? RuleValue.Null
                : ToRuleValue(raw);
            values[itemId] = converted;
            var qualified = $"{targetSpecId}.{itemId}";
            if (qualified.Length <= FindingTarget.MaxNameLength)
            {
                values[qualified] = converted;
            }
        }

        // Bind latest sibling form values per CollectionSpecId on the same enrollment for cross-form edit checks.
        var latestBySpec = enrollmentForms
            .Where(f => getId(f) != targetId
                && !string.Equals(getSpecId(f), targetSpecId, StringComparison.Ordinal))
            .GroupBy(getSpecId, StringComparer.Ordinal)
            .Select(g => g.OrderBy(getOpenedAt).ThenBy(getId).Last());

        foreach (var sibling in latestBySpec)
        {
            var siblingSpecId = getSpecId(sibling);
            var siblingRecorded = getValues(sibling).ToDictionary(
                v => v.ItemId,
                v => (ObservationValue?)v.Value,
                StringComparer.Ordinal);
            var siblingItemIds = getItems(sibling).Select(i => i.Id)
                .Concat(siblingRecorded.Keys)
                .Distinct(StringComparer.Ordinal);
            foreach (var siblingItemId in siblingItemIds)
            {
                siblingRecorded.TryGetValue(siblingItemId, out var raw);
                var converted = isBlindedVariable is not null && isBlindedVariable(sibling, siblingItemId, raw)
                    ? RuleValue.Null
                    : ToRuleValue(raw);
                var qualified = $"{siblingSpecId}.{siblingItemId}";
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

        return new RuleRecord(targetSpecId, targetId.Value.ToString("D"), values);
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
        if (masks is not null)
        {
            var recorded = form.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal));
            if (recorded is not null && masks.ContainsKey(NodeRef.For(recorded.ObservationId)))
            {
                return true;
            }
        }

        if (ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance)
            && form.BlindedItemIds is not null
            && form.BlindedItemIds.Contains(itemId))
        {
            return true;
        }

        return IsBlindedSpecVariable(form.CollectionSpecId, form.CollectionTitle, form.Items, itemId, rawValue, designs, conceptMap);
    }

    private bool IsBlindedRuleInputVariable(
        CollectionRuleInput form,
        string itemId,
        ObservationValue? rawValue,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap) =>
        IsBlindedSpecVariable(form.CollectionSpecId, form.CollectionTitle, form.Items, itemId, rawValue, designs, conceptMap);

    private bool IsBlindedSpecVariable(
        string collectionSpecId,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        string itemId,
        ObservationValue? rawValue,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap)
    {
        var specItem = items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
        BlindedConceptMetadata? conceptMeta = specItem is not null && conceptMap.TryGetValue(specItem.Concept.ConceptId, out var meta)
            ? meta
            : null;
        if (_blinding.IsBlindedCollectionItem(collectionSpecId, collectionTitle, specItem, rawValue, conceptMeta, designs))
        {
            return true;
        }

        BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptMap.TryGetValue(id, out var m) ? m : null;
        return _blinding.IsBlindedVariable(collectionSpecId, itemId, designs, ResolveConcept);
    }

    private bool IsBlindedViolation(
        RuleViolation violation,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap)
    {
        if (_blinding.IsBlindedCollectionSpec(violation.Dataset, designs: designs))
        {
            return true;
        }

        BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptMap.TryGetValue(id, out var meta) ? meta : null;
        return violation.Observed.Any(o => _blinding.IsBlindedVariable(violation.Dataset, o.Variable, designs, ResolveConcept));
    }

    private async Task<IReadOnlyList<ProtocolDesign>> LoadDesignsAsync(
        TenantId tenant,
        StudyId studyId,
        IEnumerable<ProtocolVersionId> versionIds,
        CancellationToken cancellationToken)
    {
        if (protocols is null)
        {
            return [];
        }

        var distinctVersionIds = versionIds.Distinct().ToList();
        var studyDesigns = await protocols.GetDesignsForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        if (studyDesigns.Count > 0)
        {
            var formDesigns = new List<ProtocolDesign>();
            foreach (var versionId in distinctVersionIds)
            {
                if (studyDesigns.TryGetValue(versionId, out var design))
                {
                    formDesigns.Add(design);
                }
            }

            return formDesigns.Count > 0 ? formDesigns : [.. studyDesigns.Values];
        }

        var fallbackDesigns = new List<ProtocolDesign>();
        foreach (var versionId in distinctVersionIds.Where(id => !id.IsEmpty))
        {
            if (await protocols.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                fallbackDesigns.Add(design);
            }
        }

        return fallbackDesigns;
    }

    private Task<IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>> LoadConceptsAsync(
        TenantId tenant,
        IEnumerable<CollectionItem> items,
        IReadOnlyList<ProtocolDesign> designs,
        CancellationToken cancellationToken) =>
        BlindedConceptMetadata.ResolveManyAsync(
            concepts,
            tenant,
            items.Select(i => i.Concept)
                .Concat(designs.SelectMany(d => d.CollectionSpecs).SelectMany(s => s.Items).Select(i => i.Concept)),
            cancellationToken);

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

internal static class RuleViolationExtensions
{
    public static string MessageOrExplanation(this RuleViolation violation) => violation.Explanation;
}
