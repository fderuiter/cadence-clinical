using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>Body of <c>POST /api/v1/execution/forms</c>.</summary>
/// <param name="FormId">The new form's id; the kernel assigns one when absent. Retrying with the same idempotency key must send the same id.</param>
/// <param name="EnrollmentId">The participant's enrollment.</param>
/// <param name="CollectionSpecId">The CollectionSpec the form collects, from the protocol design.</param>
/// <param name="PlannedActivityId">The visit the form belongs to; null for an unscheduled or log form.</param>
/// <param name="CollectedAt">When the values were collected in the trial world.</param>
public sealed record OpenFormRequest(Guid? FormId, Guid EnrollmentId, string CollectionSpecId, string? PlannedActivityId, DateTimeOffset CollectedAt);

/// <summary>Body of <c>PUT /api/v1/execution/forms/{formId}/values</c>: one page of values.</summary>
/// <param name="Values">At most one per item.</param>
public sealed record SaveFormValuesRequest(IReadOnlyList<FormValueRequest> Values);

/// <summary>One value of a page save.</summary>
/// <param name="ItemId">The item the value is for.</param>
/// <param name="Value">The value as entered, typed by its <c>type</c> member (ADR-2623); a reasoned missing value is a null flavor.</param>
/// <param name="ExpectedRevision">The revision the user edited: 0 for an item with no value yet.</param>
/// <param name="ObservationId">For a new value, the observation id the client chose; the kernel assigns one when absent.</param>
/// <param name="ObservedAt">When the value held, for a new value; the form's collection time when absent.</param>
public sealed record FormValueRequest(string ItemId, ObservationValue? Value, int ExpectedRevision, Guid? ObservationId, DateTimeOffset? ObservedAt);

/// <summary>A form after it was opened.</summary>
/// <param name="FormId">The form's id.</param>
/// <param name="EnrollmentId">Its enrollment.</param>
/// <param name="ProtocolVersionId">The protocol version that governs it (K-TIME-8).</param>
/// <param name="CollectionSpecId">What it collects.</param>
/// <param name="PlannedActivityId">Its visit, if any.</param>
/// <param name="CollectedAt">When its values were collected.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
public sealed record FormStateResponse(
    Guid FormId, Guid EnrollmentId, Guid ProtocolVersionId, string CollectionSpecId, string? PlannedActivityId, DateTimeOffset CollectedAt, long Version)
{
    internal static FormStateResponse From(CollectionInstanceState state) =>
        new(state.CollectionInstanceId.Value, state.EnrollmentId.Value, state.ProtocolVersionId.Value, state.CollectionSpecId,
            state.PlannedActivityId, state.CollectedAt, state.Version);
}

/// <summary>Summary of an opened form for an enrollment's form list.</summary>
/// <param name="FormId">The form's id.</param>
/// <param name="EnrollmentId">Its enrollment.</param>
/// <param name="ParticipantId">The enrollment's participant.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs it (K-TIME-8).</param>
/// <param name="CollectionSpecId">What it collects.</param>
/// <param name="PlannedActivityId">Its visit, if any.</param>
/// <param name="CollectedAt">When its values were collected.</param>
/// <param name="OpenedBy">Who opened it.</param>
/// <param name="OpenedAt">When it was opened.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
/// <param name="Complete">Whether every required item has a value.</param>
/// <param name="ItemCount">Total items on the form.</param>
/// <param name="RecordedCount">Items on the form that currently hold a recorded value.</param>
/// <param name="CollectionTitle">The CollectionSpec's title in the governing protocol version, when present.</param>
/// <param name="MissingRequiredCount">Required items on the form that still lack a value.</param>
/// <param name="OpenQueryCount">Queries currently open against values on this form.</param>
/// <param name="AnsweredQueryCount">Queries on this form answered by the site and awaiting monitor review.</param>
/// <param name="VerifiedCount">Recorded items whose current revision holds an active source verification.</param>
/// <param name="UnverifiedCount">Recorded items whose current revision is not source-verified.</param>
/// <param name="Locked">Whether any hard lock covers the form or its broader scope.</param>
/// <param name="Frozen">Whether any freeze covers the form or its broader scope.</param>
/// <param name="LockCount">Active lock or freeze assertions covering the form.</param>
/// <param name="Signed">Whether an attestation signs the form's current content hash.</param>
/// <param name="SignatureSuperseded">Whether the form was signed earlier and has changed since.</param>
/// <param name="SignatureCount">Total attestations recorded over this form.</param>
/// <param name="OpenErrorFindingCount">Active Open or Investigating Error validation findings targeting this form.</param>
/// <param name="ReadinessBlockers">Diagnostic blockers preventing completeness, signature, or scope lock when non-empty.</param>
public sealed record FormSummaryResponse(
    Guid FormId,
    Guid EnrollmentId,
    Guid ParticipantId,
    Guid StudyId,
    Guid StudySiteId,
    Guid ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    string OpenedBy,
    DateTimeOffset OpenedAt,
    long Version,
    bool Complete,
    int ItemCount,
    int RecordedCount,
    string? CollectionTitle = null,
    int MissingRequiredCount = 0,
    int OpenQueryCount = 0,
    int AnsweredQueryCount = 0,
    int VerifiedCount = 0,
    int UnverifiedCount = 0,
    bool Locked = false,
    bool Frozen = false,
    int LockCount = 0,
    bool Signed = false,
    bool SignatureSuperseded = false,
    int SignatureCount = 0,
    int OpenErrorFindingCount = 0,
    IReadOnlyList<string>? ReadinessBlockers = null)
{
    internal static FormSummaryResponse From(CollectionInstanceSummary summary, int? openQueryCount = null, int? answeredQueryCount = null)
    {
        var effectiveOpen = openQueryCount ?? summary.OpenQueryCount;
        var effectiveAnswered = answeredQueryCount ?? summary.AnsweredQueryCount;
        var complete = summary.Complete && CollectionReadiness.IsComplete(
            summary.MissingRequiredCount,
            effectiveOpen,
            effectiveAnswered,
            summary.OpenErrorFindingCount);
        var blockers = CollectionReadiness.BuildBlockers(
            missingRequired: null,
            summary.MissingRequiredCount,
            effectiveOpen,
            effectiveAnswered,
            summary.OpenErrorFindingCount);
        return new(
            summary.CollectionInstanceId.Value,
            summary.EnrollmentId.Value,
            summary.ParticipantId.Value,
            summary.StudyId.Value,
            summary.StudySiteId.Value,
            summary.ProtocolVersionId.Value,
            summary.CollectionSpecId,
            summary.PlannedActivityId,
            summary.CollectedAt,
            summary.OpenedBy,
            summary.OpenedAt,
            summary.Version,
            complete,
            summary.ItemCount,
            summary.RecordedCount,
            summary.CollectionTitle,
            summary.MissingRequiredCount,
            effectiveOpen,
            effectiveAnswered,
            summary.VerifiedCount,
            summary.UnverifiedCount,
            summary.Locked,
            summary.Frozen,
            summary.LockCount,
            summary.Signed,
            summary.SignatureSuperseded,
            summary.SignatureCount,
            summary.OpenErrorFindingCount,
            blockers);
    }
}

/// <summary>A form as a form screen shows it.</summary>
/// <param name="FormId">The form's id.</param>
/// <param name="EnrollmentId">Its enrollment.</param>
/// <param name="ParticipantId">The enrollment's participant.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs it (K-TIME-8).</param>
/// <param name="CollectionSpecId">What it collects.</param>
/// <param name="PlannedActivityId">Its visit, if any.</param>
/// <param name="CollectedAt">When its values were collected.</param>
/// <param name="OpenedBy">Who opened it.</param>
/// <param name="OpenedAt">When it was opened.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
/// <param name="Complete">Whether every required item has a value (a reasoned missing value counts).</param>
/// <param name="MissingRequired">The required items without a value, in layout order.</param>
/// <param name="Items">Every item in layout order, with its value when it has one.</param>
/// <param name="Locks">The locks in force over the form, widest scope first (spec §10.5).</param>
/// <param name="Assertions">Verifications, queries and answers about its values, oldest first (spec §8.2).</param>
/// <param name="Signatures">The signatures over it, oldest first (spec §9.5).</param>
/// <param name="ContentHash">The hash a signature over the current values binds to: send it to sign them (#5463); null while
/// the form has no values.</param>
/// <param name="CollectionTitle">The CollectionSpec's title in the governing protocol version, when present.</param>
/// <param name="CanSign">Whether the current caller holds <c>collection.sign</c> over the form's enrollment.</param>
/// <param name="OpenQueryCount">Un-retracted clinical queries on this form awaiting an answer.</param>
/// <param name="AnsweredQueryCount">Un-retracted clinical queries on this form answered by the site and awaiting monitor closure.</param>
/// <param name="OpenErrorFindingCount">Active Open or Investigating Error validation findings targeting this form.</param>
/// <param name="ReadinessBlockers">Diagnostic blockers preventing completeness, signature, or scope lock when non-empty.</param>
public sealed record FormResponse(
    Guid FormId,
    Guid EnrollmentId,
    Guid ParticipantId,
    Guid StudyId,
    Guid StudySiteId,
    Guid ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    string OpenedBy,
    DateTimeOffset OpenedAt,
    long Version,
    bool Complete,
    IReadOnlyList<string> MissingRequired,
    IReadOnlyList<FormItemResponse> Items,
    IReadOnlyList<FormLockResponse> Locks,
    IReadOnlyList<FormAssertionResponse> Assertions,
    IReadOnlyList<FormSignatureResponse> Signatures,
    string? ContentHash,
    string? CollectionTitle = null,
    bool CanSign = true,
    int OpenQueryCount = 0,
    int AnsweredQueryCount = 0,
    int OpenErrorFindingCount = 0,
    IReadOnlyList<string>? ReadinessBlockers = null)
{
    /// <summary>
    /// Shapes the kernel's record for a form screen: items in <paramref name="layout"/> order (the specification's order
    /// when null), then any item the layout does not name, and completeness derived from required items, active queries,
    /// and active Error validation findings.
    /// </summary>
    internal static FormResponse From(
        CollectionInstanceRecord record,
        IReadOnlyList<string>? layout,
        bool canSign = true,
        IReadOnlySet<string>? blindedItemIds = null,
        IReadOnlySet<ObservationId>? blindedObservationIds = null,
        int? openQueryCount = null,
        int? answeredQueryCount = null)
    {
        var specification = record.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var values = record.Values.ToDictionary(v => v.ItemId, StringComparer.Ordinal);
        var order = (layout ?? [.. record.Items.Select(i => i.Id)])
            .Concat(record.Items.Select(i => i.Id))
            .Concat(record.Values.Select(v => v.ItemId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var items = order
            .Select(id =>
            {
                var specItem = specification.GetValueOrDefault(id);
                var currentValue = values.GetValueOrDefault(id);
                var isBlinded = (blindedItemIds is not null && blindedItemIds.Contains(id))
                    || (currentValue is not null && (currentValue.ObservationId.IsEmpty
                        || (blindedObservationIds is not null && blindedObservationIds.Contains(currentValue.ObservationId))));
                return FormItemResponse.From(
                    id,
                    specItem,
                    currentValue,
                    isBlinded,
                    record.ItemCodelists?.GetValueOrDefault(id));
            })
            .ToList();
        var missing = items
            .Where(i => i.Required && (i.Value is null || i.Value is MissingValue { Reason: NullFlavor.NotAsked }))
            .Select(i => i.ItemId)
            .ToList();
        var effectiveOpen = openQueryCount ?? record.OpenQueryCount;
        var effectiveAnswered = answeredQueryCount ?? record.AnsweredQueryCount;
        var complete = CollectionReadiness.IsComplete(
            missing.Count,
            effectiveOpen,
            effectiveAnswered,
            record.OpenErrorFindingCount);
        var blockers = CollectionReadiness.BuildBlockers(
            missing,
            missing.Count,
            effectiveOpen,
            effectiveAnswered,
            record.OpenErrorFindingCount);

        var hasBlinded = (blindedItemIds is { Count: > 0 })
            || (blindedObservationIds is { Count: > 0 })
            || items.Any(i => i.Value is MissingValue { Reason: NullFlavor.Masked });
        var blindedObsGuids = blindedObservationIds is { Count: > 0 }
            ? blindedObservationIds.Select(o => o.Value).ToHashSet()
            : null;
        var blindedQueryGuids = blindedObsGuids is not null
            ? record.Assertions
                .Where(assertion => assertion.Kind == AssertionKind.Queried
                    && assertion.Target.Kind == NodeKinds.Observation
                    && blindedObsGuids.Contains(assertion.Target.Id))
                .Select(assertion => assertion.AssertionId.Value)
                .ToHashSet()
            : [];

        return new(
            record.CollectionInstanceId.Value,
            record.EnrollmentId.Value,
            record.ParticipantId.Value,
            record.StudyId.Value,
            record.StudySiteId.Value,
            record.ProtocolVersionId.Value,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.CollectedAt,
            record.OpenedBy,
            record.OpenedAt,
            record.Version,
            complete,
            missing,
            items,
            [.. record.Locks.Select(l =>
            {
                var isBlindedLock = l.Masked
                    || (blindedObsGuids is not null
                        && l.Scope.Kind == NodeKinds.Observation
                        && blindedObsGuids.Contains(l.Scope.Id));
                return isBlindedLock
                    ? new FormLockResponse(l.LockId.Value, l.Kind.ToString(), "masked", l.LockedAt, "masked", "masked")
                    : new FormLockResponse(l.LockId.Value, l.Kind.ToString(), l.Scope.ToString(), l.LockedAt, l.LockedBy, l.ReasonForChange);
            })],
            [.. record.Assertions.Select(a =>
            {
                var isBlindedAssertion = a.Masked
                    || (blindedObsGuids is not null
                        && ((a.Target.Kind == NodeKinds.Observation && blindedObsGuids.Contains(a.Target.Id))
                            || (a.Kind == AssertionKind.QueryAnswered && a.Target.Kind == NodeKinds.Assertion && blindedQueryGuids.Contains(a.Target.Id))));
                if (isBlindedAssertion)
                {
                    var maskedText = a.Text is null ? null : "masked";
                    return new FormAssertionResponse(
                        a.AssertionId.Value,
                        a.Kind.ToString(),
                        "masked",
                        a.Revision,
                        maskedText,
                        "masked",
                        a.AssertedAt,
                        "masked",
                        a.RetractedAt);
                }

                return new FormAssertionResponse(
                    a.AssertionId.Value,
                    a.Kind.ToString(),
                    a.Target.ToString(),
                    a.Revision,
                    a.Text,
                    a.AssertedBy,
                    a.AssertedAt,
                    a.ReasonForChange,
                    a.RetractedAt);
            })],
            [.. record.Attestations.Select(s => FormSignatureResponse.From(s, blindedItemIds, blindedObservationIds))],
            hasBlinded ? null : record.ContentHash,
            record.CollectionTitle,
            canSign,
            effectiveOpen,
            effectiveAnswered,
            record.OpenErrorFindingCount,
            blockers);
    }
}

internal static class ExecutionQueryMerger
{
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
        Cadence.Kernel.Protocol.Design.CollectionItem? item,
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
