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
    private readonly CollectionBlindingClassifier _blinding = new(
        blindingClassifier ?? BlindedTreatmentClassifier.Instance,
        protocols,
        concepts,
        visibility);

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

        IReadOnlyList<ProtocolDesign> designs = [];
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap = new Dictionary<ConceptId, BlindedConceptMetadata>();
        if (!unblinded)
        {
            designs = await _blinding.LoadDesignsAsync(tenant, target.StudyId, siblings.Select(s => s.ProtocolVersionId), cancellationToken).ConfigureAwait(false);
            conceptMap = await _blinding.LoadConceptsAsync(tenant, siblings.SelectMany(s => s.Items), designs, cancellationToken).ConfigureAwait(false);
        }

        var targetOwnValues = _blinding.BuildServiceOwnItemValues(
            targetInput,
            draftValues,
            unblinded: unblinded,
            designs,
            conceptMap);
        var latestSiblings = CollectionRuleRecordBuilder.SelectLatestFormsBySpec(
            siblings,
            static f => f.CollectionInstanceId,
            static f => f.CollectionSpecId,
            static f => f.OpenedAt,
            targetInput.CollectionInstanceId,
            targetInput.CollectionSpecId);
        var siblingIndex = CollectionRuleRecordBuilder.BuildSiblingIndex(
            latestSiblings,
            static sibling => sibling.CollectionSpecId,
            sibling => _blinding.BuildServiceOwnItemValues(sibling, draftOverlay: null, unblinded: unblinded, designs, conceptMap));
        IndexedCollectionForm[] singleForm =
        [
            new IndexedCollectionForm(target, targetOwnValues, siblingIndex, unblinded),
        ];

        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var warnings = new List<CollectionPreviewWarning>();
        foreach (var pack in packs)
        {
            var rulesById = CollectionRuleRecordBuilder.BuildRulesById(pack.Pack);
            var sink = new FormPreviewWarningSink(_blinding, pack.Key, target, rulesById, unblinded, designs, conceptMap, warnings);
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
        var loaded = await LoadPreviewRulePackAsync(tenant, rulePackId, cancellationToken).ConfigureAwait(false);
        if (loaded is not var (item, pack))
        {
            return new CollectionRulePackPreviewResult(0, 0, []);
        }

        var allInstances = await collections.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        return await PreviewLoadedRulePackAsync(
            tenant,
            studyId,
            item,
            pack,
            allInstances,
            asOf,
            caller,
            unblindedEnrollments,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
        TenantId tenant,
        StudyId studyId,
        LibraryItemId rulePackId,
        IReadOnlyList<CollectionInstanceRecord> studyForms,
        DateOnly asOf,
        Actor? caller,
        IReadOnlySet<EnrollmentId>? unblindedEnrollments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(studyForms);

        var loaded = await LoadPreviewRulePackAsync(tenant, rulePackId, cancellationToken).ConfigureAwait(false);
        if (loaded is not var (item, pack))
        {
            return new CollectionRulePackPreviewResult(0, 0, []);
        }

        return await PreviewLoadedRulePackAsync(
            tenant,
            studyId,
            item,
            pack,
            studyForms,
            asOf,
            caller,
            unblindedEnrollments,
            cancellationToken).ConfigureAwait(false);
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

        var indexedForms = _blinding.IndexStudyForms(
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
        return CollectionRuleRecordBuilder.BuildRuleRecordCore(
            target,
            enrollmentForms,
            static f => f.CollectionInstanceId,
            static f => f.CollectionSpecId,
            static f => f.OpenedAt,
            CollectionRuleRecordBuilder.BuildStaticOwnItemValues(target, draftOverlay, isBlindedVariable),
            sibling => CollectionRuleRecordBuilder.BuildStaticOwnItemValues(sibling, draftOverlay: null, isBlindedVariable));
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
        return CollectionRuleRecordBuilder.BuildRuleRecordCore(
            target,
            enrollmentForms,
            static f => f.CollectionInstanceId,
            static f => f.CollectionSpecId,
            static f => f.OpenedAt,
            CollectionRuleRecordBuilder.BuildStaticOwnItemValues(target, draftOverlay, isBlindedVariable),
            sibling => CollectionRuleRecordBuilder.BuildStaticOwnItemValues(sibling, draftOverlay: null, isBlindedVariable));
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

            if (CollectionRuleRecordBuilder.TryStripSpecPrefix(variable, specId, out var stripped) && targetItems.Contains(stripped))
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

    internal static List<TForm> SelectLatestFormsBySpec<TForm>(
        IReadOnlyList<TForm> enrollmentForms,
        Func<TForm, CollectionInstanceId> getId,
        Func<TForm, string> getSpecId,
        Func<TForm, DateTimeOffset> getOpenedAt,
        CollectionInstanceId? excludeFormId = null,
        string? excludeSpecId = null) =>
        CollectionRuleRecordBuilder.SelectLatestFormsBySpec(enrollmentForms, getId, getSpecId, getOpenedAt, excludeFormId, excludeSpecId);

    internal static EnrollmentSiblingIndex BuildSiblingIndex<TForm>(
        List<TForm> latestBySpec,
        Func<TForm, string> getSpecId,
        Func<TForm, Dictionary<string, RuleValue>> resolveOwnValues) =>
        CollectionRuleRecordBuilder.BuildSiblingIndex(latestBySpec, getSpecId, resolveOwnValues);

    internal static bool TryStripSpecPrefix(string variable, string specId, out string stripped) =>
        CollectionRuleRecordBuilder.TryStripSpecPrefix(variable, specId, out stripped);

    private async Task<(LibraryItemView Item, RulePack Pack)?> LoadPreviewRulePackAsync(
        TenantId tenant,
        LibraryItemId rulePackId,
        CancellationToken cancellationToken)
    {
        var item = await registry.FindLibraryItemAsync(tenant, rulePackId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.Kind != LibraryItemKind.RulePack)
        {
            return null;
        }

        var (pack, problems) = RulePackJson.Read(item.Content);
        if (pack is null || problems.Count > 0)
        {
            return null;
        }

        return (item, pack);
    }

    private async Task<CollectionRulePackPreviewResult> PreviewLoadedRulePackAsync(
        TenantId tenant,
        StudyId studyId,
        LibraryItemView item,
        RulePack pack,
        IReadOnlyList<CollectionInstanceRecord> allInstances,
        DateOnly asOf,
        Actor? caller,
        IReadOnlySet<EnrollmentId>? unblindedEnrollments,
        CancellationToken cancellationToken)
    {
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
            designs = await _blinding.LoadDesignsAsync(tenant, studyId, instances.Select(i => i.ProtocolVersionId), cancellationToken).ConfigureAwait(false);
            conceptMap = await _blinding.LoadConceptsAsync(tenant, instances.SelectMany(i => i.Items), designs, cancellationToken).ConfigureAwait(false);
        }

        var structuralItemCache = new Dictionary<StructuralRuleItemKey, bool>();
        var indexedForms = _blinding.IndexStudyForms(
            instances,
            (enrollmentId, enrollmentForms) => _blinding.IsEnrollmentUnblinded(
                enrollmentId,
                enrollmentForms,
                caller,
                unblindedEnrollments,
                masks,
                designs,
                conceptMap,
                structuralItemCache),
            designs,
            conceptMap,
            masks,
            structuralItemCache);

        var rulesById = CollectionRuleRecordBuilder.BuildRulesById(pack);
        var context = new RuleEvaluationContext(ValidationStage.Collection, asOf);
        var violations = new List<CollectionRulePackPreviewViolation>();
        var cursor = new CollectionFormCursor(indexedForms);
        var sink = new RulePackPreviewSink(_blinding, item.Key, cursor, indexedForms, rulesById, designs, conceptMap, violations);
        evaluator.Evaluate(pack, context, cursor, sink);

        return new CollectionRulePackPreviewResult(instances.Count, pack.Rules.Count, violations);
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
}
