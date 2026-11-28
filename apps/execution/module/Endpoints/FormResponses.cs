using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
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
/// <param name="VisitTiming">Assessed visit timing (<c>Early</c>, <c>InWindow</c>, or <c>Late</c>) when scheduled on a timed activity.</param>
/// <param name="DaysFromTarget">Calendar days from the planned target date (negative when early, positive when late).</param>
/// <param name="DaysOutsideWindow">Calendar days outside the allowed visit window (0 when in window).</param>
/// <param name="TargetDate">Planned target date in site calendar time, when timed.</param>
/// <param name="WindowStart">First allowed date of the visit window, when timed.</param>
/// <param name="WindowEnd">Last allowed date of the visit window, when timed.</param>
/// <param name="DeviationCategory">Emitted deviation category (<c>VISIT_WINDOW</c>) when out of window, otherwise null.</param>
public sealed record FormStateResponse(
    Guid FormId,
    Guid EnrollmentId,
    Guid ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    long Version,
    string? VisitTiming = null,
    int? DaysFromTarget = null,
    int? DaysOutsideWindow = null,
    DateOnly? TargetDate = null,
    DateOnly? WindowStart = null,
    DateOnly? WindowEnd = null,
    string? DeviationCategory = null)
{
    internal static FormStateResponse From(CollectionInstanceState state, VisitTimingAssessment? assessment = null)
    {
        var effective = assessment ?? state.VisitTimingAssessment;
        return new(
            state.CollectionInstanceId.Value,
            state.EnrollmentId.Value,
            state.ProtocolVersionId.Value,
            state.CollectionSpecId,
            state.PlannedActivityId,
            state.CollectedAt,
            state.Version,
            effective?.Timing.ToString(),
            effective?.DaysFromTarget,
            effective?.DaysOutsideWindow,
            effective?.Expected.Target,
            effective?.Expected.WindowStart,
            effective?.Expected.WindowEnd,
            VisitTimingEvaluator.DeviationCategoryOf(effective));
    }
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
/// <param name="VisitTiming">Assessed visit timing (<c>Early</c>, <c>InWindow</c>, or <c>Late</c>) when scheduled on a timed activity.</param>
/// <param name="DaysFromTarget">Calendar days from the planned target date (negative when early, positive when late).</param>
/// <param name="DaysOutsideWindow">Calendar days outside the allowed visit window (0 when in window).</param>
/// <param name="TargetDate">Planned target date in site calendar time, when timed.</param>
/// <param name="WindowStart">First allowed date of the visit window, when timed.</param>
/// <param name="WindowEnd">Last allowed date of the visit window, when timed.</param>
/// <param name="DeviationCategory">Emitted deviation category (<c>VISIT_WINDOW</c>) when out of window, otherwise null.</param>
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
    IReadOnlyList<string>? ReadinessBlockers = null,
    string? VisitTiming = null,
    int? DaysFromTarget = null,
    int? DaysOutsideWindow = null,
    DateOnly? TargetDate = null,
    DateOnly? WindowStart = null,
    DateOnly? WindowEnd = null,
    string? DeviationCategory = null)
{
    internal static FormSummaryResponse From(
        CollectionInstanceSummary summary,
        int? openQueryCount = null,
        int? answeredQueryCount = null,
        VisitTimingAssessment? assessment = null)
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
            blockers,
            assessment?.Timing.ToString(),
            assessment?.DaysFromTarget,
            assessment?.DaysOutsideWindow,
            assessment?.Expected.Target,
            assessment?.Expected.WindowStart,
            assessment?.Expected.WindowEnd,
            VisitTimingEvaluator.DeviationCategoryOf(assessment));
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
/// <param name="VisitTiming">Assessed visit timing (<c>Early</c>, <c>InWindow</c>, or <c>Late</c>) when scheduled on a timed activity.</param>
/// <param name="DaysFromTarget">Calendar days from the planned target date (negative when early, positive when late).</param>
/// <param name="DaysOutsideWindow">Calendar days outside the allowed visit window (0 when in window).</param>
/// <param name="TargetDate">Planned target date in site calendar time, when timed.</param>
/// <param name="WindowStart">First allowed date of the visit window, when timed.</param>
/// <param name="WindowEnd">Last allowed date of the visit window, when timed.</param>
/// <param name="DeviationCategory">Emitted deviation category (<c>VISIT_WINDOW</c>) when out of window, otherwise null.</param>
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
    IReadOnlyList<string>? ReadinessBlockers = null,
    string? VisitTiming = null,
    int? DaysFromTarget = null,
    int? DaysOutsideWindow = null,
    DateOnly? TargetDate = null,
    DateOnly? WindowStart = null,
    DateOnly? WindowEnd = null,
    string? DeviationCategory = null)
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
        int? answeredQueryCount = null,
        VisitTimingAssessment? assessment = null)
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
        var recordedCount = record.Values.Count(v => v.Value is not null and not MissingValue { Reason: NullFlavor.NotAsked });
        var effectiveOpen = openQueryCount ?? record.OpenQueryCount;
        var effectiveAnswered = answeredQueryCount ?? record.AnsweredQueryCount;
        var complete = (items.Count == 0 || recordedCount > 0)
            && CollectionReadiness.IsComplete(
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
            blockers,
            assessment?.Timing.ToString(),
            assessment?.DaysFromTarget,
            assessment?.DaysOutsideWindow,
            assessment?.Expected.Target,
            assessment?.Expected.WindowStart,
            assessment?.Expected.WindowEnd,
            VisitTimingEvaluator.DeviationCategoryOf(assessment));
    }
}
