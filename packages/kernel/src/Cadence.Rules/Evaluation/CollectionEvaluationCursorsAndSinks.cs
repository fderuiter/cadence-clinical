using System;
using System.Collections.Generic;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Evaluation;

internal readonly record struct QualifiedSiblingEntry(
    string SpecId,
    RuleValue Value,
    bool HasPrior,
    RuleValue PriorValue);

internal readonly record struct UnqualifiedSiblingEntry(
    string FirstSpecId,
    RuleValue FirstValue,
    bool HasSecond,
    RuleValue SecondValue);

internal sealed record EnrollmentSiblingIndex(
    Dictionary<string, QualifiedSiblingEntry> QualifiedValues,
    Dictionary<string, UnqualifiedSiblingEntry> UnqualifiedValues)
{
    public static readonly EnrollmentSiblingIndex Empty = new(
        new Dictionary<string, QualifiedSiblingEntry>(0, StringComparer.Ordinal),
        new Dictionary<string, UnqualifiedSiblingEntry>(0, StringComparer.Ordinal));

    public RuleValue ResolveVariable(string targetSpec, Dictionary<string, RuleValue> ownValues, string name)
    {
        if (QualifiedValues.TryGetValue(name, out var qualifiedSibling))
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

        if (ownValues.TryGetValue(name, out var ownValue))
        {
            return ownValue;
        }

        if (name.Length <= FindingTarget.MaxNameLength
            && CollectionRuleRecordBuilder.TryStripSpecPrefix(name, targetSpec, out var stripped)
            && ownValues.TryGetValue(stripped, out var ownQualifiedValue))
        {
            return ownQualifiedValue;
        }

        if (UnqualifiedValues.TryGetValue(name, out var unqualifiedSibling))
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

internal sealed class IndexedCollectionForm(
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

internal sealed class CollectionFormCursor(IndexedCollectionForm[] forms) : IResettableRuleRecordCursor
{
    private int _index = -1;

    public IndexedCollectionForm? CurrentForm { get; private set; }

    public bool IsEmpty => forms.Length == 0;

    public string Dataset => CurrentForm!.Form.CollectionSpecId;

    public string Key => CurrentForm!.Key;

    public RuleValue this[string name]
    {
        get
        {
            var current = CurrentForm!;
            return current.Siblings.ResolveVariable(current.Form.CollectionSpecId, current.OwnValues, name);
        }
    }

    public void Reset()
    {
        _index = -1;
        CurrentForm = null;
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

internal sealed class FormPreviewWarningSink(
    CollectionBlindingClassifier blinding,
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
        if (!unblinded && blinding.IsBlindedViolation(violation.Dataset, violation.Observed, designs, conceptMap))
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

            if (CollectionRuleRecordBuilder.TryStripSpecPrefix(variable, specId, out var stripped)
                && HasTargetItem(targetRecord, stripped))
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

internal sealed class RulePackPreviewSink(
    CollectionBlindingClassifier blinding,
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
        var targetForm = CollectionCursorTargetResolver.ResolveTargetForm(violation.RecordKey, cursor, forms, ref _formsByKey);
        if (targetForm is null)
        {
            return;
        }

        if (!targetForm.Unblinded && blinding.IsBlindedViolation(violation.Dataset, violation.Observed, designs, conceptMap))
        {
            return;
        }

        var itemId = CollectionRuleRecordBuilder.ResolvePrimaryItemId(targetForm.Form, targetForm.OwnValues, violation.Observed);
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

internal sealed class StudyCollectionDetectionSink(
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
        var targetForm = CollectionCursorTargetResolver.ResolveTargetForm(violation.RecordKey, cursor, forms, ref _formsByKey);
        var primaryVariable = targetForm is null
            ? null
            : CollectionRuleRecordBuilder.ResolvePrimaryItemId(targetForm.Form, targetForm.OwnValues, violation.Observed);
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

internal static class CollectionCursorTargetResolver
{
    internal static IndexedCollectionForm? ResolveTargetForm(
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
}
