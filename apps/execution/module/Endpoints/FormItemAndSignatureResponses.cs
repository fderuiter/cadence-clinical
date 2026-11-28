using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Endpoints;

internal static class ExecutionQueryMerger
{
    internal static (int OpenCount, int AnsweredCount) Merge(
        IReadOnlySet<AssertionId> activeQueryAssertionIds,
        IReadOnlySet<AssertionId>? answeredQueryAssertionIds,
        IReadOnlyCollection<(Guid KernelQueryId, FormQueryStatus Status)> executionQueries)
    {
        var ownedKernelIds = executionQueries.Select(q => AssertionId.FromGuid(q.KernelQueryId)).ToHashSet();
        var unownedActiveCount = 0;
        var unownedAnsweredCount = 0;
        foreach (var assertionId in activeQueryAssertionIds)
        {
            if (ownedKernelIds.Contains(assertionId))
            {
                continue;
            }

            unownedActiveCount++;
            if (answeredQueryAssertionIds?.Contains(assertionId) ?? false)
            {
                unownedAnsweredCount++;
            }
        }

        var kernelOpen = unownedActiveCount - unownedAnsweredCount;
        var execOpen = executionQueries.Count(q => q.Status == FormQueryStatus.Open);
        var execAnswered = executionQueries.Count(q => q.Status == FormQueryStatus.Answered);
        return (kernelOpen + execOpen, unownedAnsweredCount + execAnswered);
    }

    internal static (int OpenCount, int AnsweredCount) Merge(
        IReadOnlyList<AssertionView> kernelAssertions,
        IReadOnlyCollection<(Guid KernelQueryId, FormQueryStatus Status)> executionQueries)
    {
        var ownedKernelIds = executionQueries.Select(q => q.KernelQueryId).ToHashSet();
        var kernelActiveIds = kernelAssertions
            .Where(a => a.Kind == AssertionKind.Queried
                && a.RetractedAt is null
                && !ownedKernelIds.Contains(a.AssertionId.Value))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();
        var kernelAnsweredIds = kernelActiveIds.Count == 0
            ? []
            : kernelAssertions
                .Where(a => a.Kind == AssertionKind.QueryAnswered
                    && a.RetractedAt is null
                    && a.Target.Kind == NodeKinds.Assertion
                    && kernelActiveIds.Contains(a.Target.Id))
                .Select(a => a.Target.Id)
                .ToHashSet();
        var kernelOpen = kernelActiveIds.Count - kernelAnsweredIds.Count;
        var kernelAnswered = kernelAnsweredIds.Count;
        var execOpen = executionQueries.Count(q => q.Status == FormQueryStatus.Open);
        var execAnswered = executionQueries.Count(q => q.Status == FormQueryStatus.Answered);
        return (kernelOpen + execOpen, kernelAnswered + execAnswered);
    }
}

/// <summary>One item of a form.</summary>
/// <param name="ItemId">The item's id in the CollectionSpec.</param>
/// <param name="Concept">What it records, as <c>&lt;concept id&gt;@&lt;version&gt;</c>; null when the governing version no longer has the item.</param>
/// <param name="Unit">The UCUM unit a quantity is collected in.</param>
/// <param name="ValueKind">The kind of value it takes, when fixed.</param>
/// <param name="Required">Whether it must have a value.</param>
/// <param name="Value">Its current value, or null when it has none.</param>
/// <param name="ObservationId">The observation holding the value.</param>
/// <param name="Revision">The value's current revision, to send as the expected revision of its next edit; 0 when it has no value.</param>
/// <param name="RecordedBy">Who entered the current revision.</param>
/// <param name="RecordedAt">When the current revision was entered.</param>
/// <param name="CodeSystemVersionId">Pinned code-system version from the item's terminology binding, when governed by controlled terminology.</param>
/// <param name="Codelist">Codelist group code within the pinned code-system version, when restricted to a codelist.</param>
/// <param name="CodelistOptions">Hydrated codelist options when <paramref name="Codelist"/> is set.</param>
public sealed record FormItemResponse(
    string ItemId,
    string? Concept,
    string? Unit,
    string? ValueKind,
    bool Required,
    ObservationValue? Value,
    Guid? ObservationId,
    int Revision,
    string? RecordedBy,
    DateTimeOffset? RecordedAt,
    Guid? CodeSystemVersionId = null,
    string? Codelist = null,
    IReadOnlyList<CodelistOptionView>? CodelistOptions = null)
{
    internal static FormItemResponse From(
        string id,
        CollectionItem? item,
        CollectedValueView? value,
        bool blinded = false,
        IReadOnlyList<CodelistOptionView>? codelistOptions = null)
    {
        ObservationValue? effectiveValue = value?.Value;
        if (blinded && value is not null && value.Value is not MissingValue { Reason: NullFlavor.NotAsked })
        {
            effectiveValue = new MissingValue(NullFlavor.Masked);
        }

        return new(
            id,
            item?.Concept.ToString(),
            item?.Unit,
            item?.ExpectedKind?.ToString(),
            item?.Required ?? false,
            effectiveValue,
            blinded || value is null || value.ObservationId.IsEmpty ? null : value.ObservationId.Value,
            value?.Revision ?? 0,
            blinded ? null : value?.RecordedBy,
            value?.RecordedAt,
            blinded ? null : item?.Terminology?.CodeSystemVersionId.Value,
            blinded || string.IsNullOrWhiteSpace(item?.Terminology?.Codelist) ? null : item.Terminology.Codelist,
            blinded ? null : codelistOptions);
    }
}

/// <summary>A lock in force over a form.</summary>
/// <param name="LockId">The lock.</param>
/// <param name="Kind">Locked or frozen.</param>
/// <param name="Scope">What it covers, as a NodeRef.</param>
/// <param name="LockedAt">When it took effect.</param>
/// <param name="LockedBy">Who locked it.</param>
/// <param name="Reason">Why.</param>
public sealed record FormLockResponse(Guid LockId, string Kind, string Scope, DateTimeOffset LockedAt, string LockedBy, string Reason);

/// <summary>A verification, query or answer about a form's value.</summary>
/// <param name="AssertionId">The assertion.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Target">The observation, or the query an answer answers, as a NodeRef.</param>
/// <param name="Revision">The revision a verification judged.</param>
/// <param name="Text">A query's or an answer's text.</param>
/// <param name="AssertedBy">Who made it.</param>
/// <param name="AssertedAt">When.</param>
/// <param name="Reason">The reason for change.</param>
/// <param name="RetractedAt">When it was retracted, if it was.</param>
public sealed record FormAssertionResponse(
    Guid AssertionId, string Kind, string Target, int? Revision, string? Text, string AssertedBy, DateTimeOffset AssertedAt, string Reason, DateTimeOffset? RetractedAt);

/// <summary>Body of <c>POST /api/v1/execution/forms/{formId}/signatures</c> (#5463).</summary>
/// <param name="Meaning"><c>approved</c> for an investigator's sign-off, or <c>responsibility</c>.</param>
/// <param name="ContentHash">The form's <c>content_hash</c> as the signer read it; a form changed since is refused with 409.</param>
/// <param name="SignatureId">The signature's id, when the client chooses it; otherwise the server does.</param>
public sealed record SignFormRequest(string Meaning, string ContentHash, Guid? SignatureId);

/// <summary>A signature over a form.</summary>
/// <param name="AttestationId">The signature.</param>
/// <param name="Meaning">What the signer attested.</param>
/// <param name="SignedBy">Who signed.</param>
/// <param name="SignedAt">When.</param>
/// <param name="Reason">The signer's reason.</param>
/// <param name="ContentHash">The hash of the revisions it signed.</param>
/// <param name="Superseded">True once the form changed after it was signed: it stands for the revisions it signed, and the
/// form needs a new signature for its current values.</param>
/// <param name="SignedValues">The revisions it signed, by item id, to show beside the current values; null when they cannot
/// be rebuilt from the values' history.</param>
public sealed record FormSignatureResponse(
    Guid AttestationId,
    string Meaning,
    string SignedBy,
    DateTimeOffset SignedAt,
    string Reason,
    string ContentHash,
    bool Superseded,
    IReadOnlyList<FormSignedValueResponse>? SignedValues)
{
    internal static FormSignatureResponse From(
        FormAttestationView signature,
        IReadOnlySet<string>? blindedItemIds = null,
        IReadOnlySet<ObservationId>? blindedObservationIds = null)
    {
        var hasBlinded = (blindedItemIds is { Count: > 0 })
            || (blindedObservationIds is { Count: > 0 })
            || string.Equals(signature.ContentHash, "masked", StringComparison.Ordinal);
        return new(
            signature.AttestationId.Value,
            signature.Meaning.ToString(),
            signature.SignedBy,
            signature.SignedAt,
            hasBlinded ? "masked" : signature.Reason,
            hasBlinded ? "masked" : signature.ContentHash,
            signature.Superseded,
            signature.SignedValues?.Select(v =>
            {
                var isBlinded = v.ObservationId.IsEmpty
                    || (blindedItemIds is not null && blindedItemIds.Contains(v.ItemId))
                    || (blindedObservationIds is not null && blindedObservationIds.Contains(v.ObservationId));
                return isBlinded
                    ? new FormSignedValueResponse(v.ItemId, Guid.Empty, v.Revision, new MissingValue(NullFlavor.Masked))
                    : new FormSignedValueResponse(v.ItemId, v.ObservationId.Value, v.Revision, v.Value);
            }).ToList());
    }
}

/// <summary>One value as a signature signed it.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ObservationId">The observation holding the value.</param>
/// <param name="Revision">The revision signed.</param>
/// <param name="Value">That revision's value.</param>
public sealed record FormSignedValueResponse(string ItemId, Guid ObservationId, int Revision, ObservationValue Value);

/// <summary>A form after a page save: every value it holds and its hydrated state, so the client marks values saved from one round-trip.</summary>
/// <param name="FormId">The form.</param>
/// <param name="Version">Its version after the save.</param>
/// <param name="Values">Every value of the form, ordered by item id.</param>
/// <param name="Form">Hydrated form state after the save, avoiding a follow-up <c>GET</c> round-trip.</param>
public sealed record FormSavedResponse(
    Guid FormId,
    long Version,
    IReadOnlyList<FormSavedValueResponse> Values,
    FormResponse? Form = null)
{
    internal static FormSavedResponse From(
        CollectedValuesState state,
        FormResponse? form = null,
        IReadOnlySet<string>? blindedItemIds = null,
        IReadOnlySet<ObservationId>? blindedObservationIds = null) =>
        new(
            state.CollectionInstanceId.Value,
            state.Version,
            [.. state.Values.Select(v =>
            {
                var isBlinded = (blindedItemIds is not null && blindedItemIds.Contains(v.ItemId))
                    || (blindedObservationIds is not null && blindedObservationIds.Contains(v.ObservationId));
                if (!isBlinded)
                {
                    return new FormSavedValueResponse(v.ItemId, v.ObservationId.Value, v.Value, v.Revision, v.Changed);
                }

                var maskedValue = v.Value is MissingValue { Reason: NullFlavor.NotAsked }
                    ? v.Value
                    : new MissingValue(NullFlavor.Masked);
                return new FormSavedValueResponse(v.ItemId, Guid.Empty, maskedValue, v.Revision, v.Changed);
            })],
            form);
}

/// <summary>One value after a save.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ObservationId">The observation holding it.</param>
/// <param name="Value">Its current value.</param>
/// <param name="Revision">Its current revision.</param>
/// <param name="Changed">Whether this save recorded or corrected it.</param>
public sealed record FormSavedValueResponse(string ItemId, Guid ObservationId, ObservationValue Value, int Revision, bool Changed);

/// <summary>Body of <c>POST /api/v1/execution/forms/{formId}/preview</c>: draft values to overlay in memory before saving.</summary>
/// <param name="Values">Draft item values to overlay on the form's current values.</param>
public sealed record PreviewFormValuesRequest(IReadOnlyList<PreviewFormItemValueRequest> Values);

/// <summary>One draft item value in a pre-commit form preview request.</summary>
/// <param name="ItemId">The item identifier on the form.</param>
/// <param name="Value">The draft typed value, or null when cleared.</param>
public sealed record PreviewFormItemValueRequest(string ItemId, ObservationValue? Value);

/// <summary>One non-blocking edit-check warning surfaced during pre-commit form preview (K-RULE-3).</summary>
/// <param name="PackKey">Rule pack key.</param>
/// <param name="RuleId">Rule identifier.</param>
/// <param name="RuleVersion">Rule version number.</param>
/// <param name="Severity">Rule severity (<c>Error</c>, <c>Warning</c>, <c>Notice</c>).</param>
/// <param name="Dataset">Collection specification identifier.</param>
/// <param name="ItemId">Primary item identifier on the form.</param>
/// <param name="Message">Inline discrepancy message.</param>
/// <param name="RemediationGuidance">Optional guidance for resolving the discrepancy.</param>
public sealed record FormPreviewWarningResponse(
    string PackKey,
    string RuleId,
    int RuleVersion,
    string Severity,
    string Dataset,
    string ItemId,
    string Message,
    string? RemediationGuidance = null)
{
    internal static FormPreviewWarningResponse From(CollectionPreviewWarning warning) =>
        new(
            warning.PackKey,
            warning.RuleId,
            warning.RuleVersion,
            warning.Severity.ToString(),
            warning.Dataset,
            warning.ItemId,
            warning.Message,
            warning.RemediationGuidance);
}

/// <summary>Result of evaluating pinned study edit checks in memory over draft form values without mutating state.</summary>
/// <param name="FormId">The previewed form identifier.</param>
/// <param name="Warnings">Item-level warnings and errors detected in memory.</param>
public sealed record FormPreviewResponse(Guid FormId, IReadOnlyList<FormPreviewWarningResponse> Warnings);
