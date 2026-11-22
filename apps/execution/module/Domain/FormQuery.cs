using System;
using System.Collections.Generic;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Evidence;

namespace Cadence.Modules.Execution.Domain;

/// <summary>Where a query is in its workflow.</summary>
public enum FormQueryStatus
{
    /// <summary>Raised or reopened, waiting for the site's answer.</summary>
    Open,

    /// <summary>The site answered; the monitor closes it or reopens it.</summary>
    Answered,

    /// <summary>The monitor accepted the answer or the corrected value.</summary>
    Closed,

    /// <summary>Withdrawn as raised in error; final.</summary>
    Cancelled,
}

/// <summary>What happened to a query, in the order it happened.</summary>
public enum FormQueryMessageKind
{
    /// <summary>The query was raised.</summary>
    Raised,

    /// <summary>The site answered.</summary>
    Answered,

    /// <summary>The monitor reopened it.</summary>
    Reopened,

    /// <summary>The monitor closed it.</summary>
    Closed,

    /// <summary>It was cancelled.</summary>
    Cancelled,
}

/// <summary>One step of a query's conversation.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Text">The query, answer or reopening text, or the reason for closing or cancelling.</param>
/// <param name="By">Who did it.</param>
/// <param name="At">When it was recorded.</param>
public sealed record FormQueryMessage(FormQueryMessageKind Kind, string Text, string By, DateTimeOffset At);

/// <summary>
/// What the kernel must assert for one step of a query (spec §8.2): raise a <c>Queried</c> assertion about the
/// observation, answer the open one, or retract an assertion. The query chooses the assertion ids, so the consumer that
/// writes them asserts each once however often it is delivered.
/// </summary>
/// <param name="Kind">The kernel command to send.</param>
/// <param name="AssertionId">The id of the assertion, answer or retraction the step records.</param>
/// <param name="TargetId">The query an answer answers, or the assertion a retraction retracts; null when raising.</param>
/// <param name="Text">The query or answer text; null for a retraction.</param>
public sealed record KernelAssertionStep(KernelAssertionStepKind Kind, AssertionId AssertionId, AssertionId? TargetId, string? Text);

/// <summary>The kernel assertion commands a query mirrors to.</summary>
public enum KernelAssertionStepKind
{
    /// <summary>Raise a <c>Queried</c> assertion about the observation.</summary>
    Raise,

    /// <summary>Answer the open <c>Queried</c> assertion.</summary>
    Answer,

    /// <summary>Retract an assertion: close a query or withdraw an answer.</summary>
    Retract,
}

/// <summary>
/// A data query about one revision of one eCRF value (#5461, spec §11: the query workflow is EDC's). It targets the
/// kernel <c>(ObservationId, Revision)</c> the monitor questioned, so a later correction shows the value changed. The
/// workflow is Execution's; each step also states a kernel <c>Queried</c> or <c>QueryAnswered</c> assertion, or retracts
/// one, so other applications count open queries from the kernel (spec §8.2).
/// </summary>
public sealed class FormQuery : AggregateRoot<Guid>
{
    private readonly List<FormQueryMessage> _messages = [];

    private FormQuery()
    {
    }

    private FormQuery(
        Guid id, TenantId tenantId, CollectionInstanceId formId, EnrollmentId enrollmentId, string itemId, ObservationId? observationId, int revision)
        : base(id)
    {
        TenantId = tenantId;
        FormId = formId;
        EnrollmentId = enrollmentId;
        ItemId = itemId;
        ObservationId = observationId;
        Revision = revision;
    }

    /// <summary>The tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The form the queried value belongs to.</summary>
    public CollectionInstanceId FormId { get; private set; }

    /// <summary>The enrollment the form is for; every query command is authorized against it.</summary>
    public EnrollmentId EnrollmentId { get; private set; }

    /// <summary>The CollectionSpec item whose value is queried.</summary>
    public string ItemId { get; private set; } = string.Empty;

    /// <summary>The kernel observation holding the value, or null when raised on a blank item.</summary>
    public ObservationId? ObservationId { get; private set; }

    /// <summary>The revision of the value the monitor questioned, or 0 when raised on a blank item.</summary>
    public int Revision { get; private set; }

    /// <summary>Where the query is in its workflow.</summary>
    public FormQueryStatus Status { get; private set; }

    /// <summary>The kernel <c>Queried</c> assertion that stands for the query while it is open or answered, or null for a blank-item query.</summary>
    public AssertionId? KernelQueryId { get; private set; }

    /// <summary>The kernel <c>QueryAnswered</c> assertion while the query is answered and the answer stands.</summary>
    public AssertionId? KernelAnswerId { get; private set; }

    /// <summary>What happened to the query, oldest first.</summary>
    public IReadOnlyList<FormQueryMessage> Messages => _messages;

    /// <summary>
    /// Raises a query about revision <paramref name="revision"/> of an observation (mirrored as the kernel assertion
    /// <paramref name="kernelQueryId"/>), or about a blank item (<paramref name="observationId"/> null, <paramref name="revision"/> 0).
    /// </summary>
    /// <exception cref="ArgumentException">An id is empty, the revision does not match the observation presence, or the text is not query text.</exception>
    public static (FormQuery Query, KernelAssertionStep? Step) Raise(
        Guid id,
        TenantId tenantId,
        CollectionInstanceId formId,
        EnrollmentId enrollmentId,
        string itemId,
        ObservationId? observationId,
        int revision,
        string text,
        AssertionId? kernelQueryId,
        string by,
        DateTimeOffset at)
    {
        if (id == Guid.Empty || tenantId.IsEmpty || formId.IsEmpty || enrollmentId.IsEmpty || string.IsNullOrWhiteSpace(itemId))
        {
            throw new ArgumentException("A query names its id, tenant, form, enrollment and item.");
        }

        if (observationId is { } recordedObservation)
        {
            if (recordedObservation.IsEmpty || kernelQueryId is not { IsEmpty: false } assertionId || revision < 1)
            {
                throw new ArgumentException("A query on a recorded observation names a non-empty observation, a revision from 1 and its kernel assertion.");
            }

            var trimmed = Text(text);
            var query = new FormQuery(id, tenantId, formId, enrollmentId, itemId, recordedObservation, revision)
            {
                Status = FormQueryStatus.Open,
                KernelQueryId = assertionId,
            };
            query._messages.Add(new FormQueryMessage(FormQueryMessageKind.Raised, trimmed, by, at));
            return (query, new KernelAssertionStep(KernelAssertionStepKind.Raise, assertionId, null, trimmed));
        }

        if (revision != 0 || kernelQueryId is not null)
        {
            throw new ArgumentException("A query on a blank item has revision 0 and no kernel assertion.");
        }

        var blankTrimmed = Text(text);
        var blankQuery = new FormQuery(id, tenantId, formId, enrollmentId, itemId, observationId: null, revision: 0)
        {
            Status = FormQueryStatus.Open,
            KernelQueryId = null,
        };
        blankQuery._messages.Add(new FormQueryMessage(FormQueryMessageKind.Raised, blankTrimmed, by, at));
        return (blankQuery, null);
    }

    /// <summary>True when <paramref name="text"/> is non-empty and fits a kernel assertion.</summary>
    public static bool IsText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= AssertionText.MaxLength;

    /// <summary>The site answers an open query, as the kernel answer <paramref name="answerId"/> when backed by an observation.</summary>
    /// <returns>True when the query was open and transitioned to answered.</returns>
    public bool TryAnswer(string text, AssertionId answerId, string by, DateTimeOffset at, out KernelAssertionStep? step)
    {
        if (Status != FormQueryStatus.Open)
        {
            step = null;
            return false;
        }

        var trimmed = Text(text);
        Status = FormQueryStatus.Answered;
        _messages.Add(new FormQueryMessage(FormQueryMessageKind.Answered, trimmed, by, at));
        if (ObservationId is not null && KernelQueryId is { } queryAssertion)
        {
            KernelAnswerId = answerId;
            step = new KernelAssertionStep(KernelAssertionStepKind.Answer, answerId, queryAssertion, trimmed);
        }
        else
        {
            KernelAnswerId = null;
            step = null;
        }

        return true;
    }

    /// <summary>The site answers an open observation-backed query, as the kernel answer <paramref name="answerId"/>.</summary>
    /// <returns>The kernel step, or null when the query is not open or has no kernel observation.</returns>
    public KernelAssertionStep? Answer(string text, AssertionId answerId, string by, DateTimeOffset at) =>
        TryAnswer(text, answerId, by, at, out var step) ? step : null;

    /// <summary>
    /// The monitor reopens an answered or closed query. When backed by an observation, an answered query withdraws its
    /// answer as <paramref name="kernelId"/> and a closed one is raised again in the kernel as <paramref name="kernelId"/>.
    /// </summary>
    /// <returns>True when the query was answered or closed and transitioned to open.</returns>
    public bool TryReopen(string text, AssertionId kernelId, string by, DateTimeOffset at, out KernelAssertionStep? step)
    {
        var trimmed = Text(text);
        switch (Status)
        {
            case FormQueryStatus.Answered:
                step = ObservationId is not null && KernelAnswerId is { } answerId
                    ? new KernelAssertionStep(KernelAssertionStepKind.Retract, kernelId, answerId, null)
                    : null;
                break;
            case FormQueryStatus.Closed:
                if (ObservationId is not null)
                {
                    step = new KernelAssertionStep(KernelAssertionStepKind.Raise, kernelId, null, trimmed);
                    KernelQueryId = kernelId;
                }
                else
                {
                    step = null;
                    KernelQueryId = null;
                }

                break;
            default:
                step = null;
                return false;
        }

        Status = FormQueryStatus.Open;
        KernelAnswerId = null;
        _messages.Add(new FormQueryMessage(FormQueryMessageKind.Reopened, trimmed, by, at));
        return true;
    }

    /// <summary>Reopens an answered or closed observation-backed query.</summary>
    /// <returns>The kernel step, or null when the query is open, cancelled, or has no kernel observation.</returns>
    public KernelAssertionStep? Reopen(string text, AssertionId kernelId, string by, DateTimeOffset at) =>
        TryReopen(text, kernelId, by, at, out var step) ? step : null;

    /// <summary>The monitor closes an open or answered query; when backed by an observation, the kernel query is retracted as <paramref name="retractionId"/>.</summary>
    /// <returns>True when the query was open or answered and transitioned to closed.</returns>
    public bool TryClose(string reason, AssertionId retractionId, string by, DateTimeOffset at, out KernelAssertionStep? step) =>
        TryEnd(FormQueryStatus.Closed, FormQueryMessageKind.Closed, reason, retractionId, by, at, out step);

    /// <summary>The monitor closes an open or answered observation-backed query; the kernel query is retracted as <paramref name="retractionId"/>.</summary>
    /// <returns>The kernel step, or null when the query is closed, cancelled, or has no kernel observation.</returns>
    public KernelAssertionStep? Close(string reason, AssertionId retractionId, string by, DateTimeOffset at) =>
        TryClose(reason, retractionId, by, at, out var step) ? step : null;

    /// <summary>An open or answered query is withdrawn as raised in error; when backed by an observation, the kernel query is retracted.</summary>
    /// <returns>True when the query was open or answered and transitioned to cancelled.</returns>
    public bool TryCancel(string reason, AssertionId retractionId, string by, DateTimeOffset at, out KernelAssertionStep? step) =>
        TryEnd(FormQueryStatus.Cancelled, FormQueryMessageKind.Cancelled, reason, retractionId, by, at, out step);

    /// <summary>An open or answered observation-backed query is withdrawn as raised in error; the kernel query is retracted.</summary>
    /// <returns>The kernel step, or null when the query is closed, cancelled, or has no kernel observation.</returns>
    public KernelAssertionStep? Cancel(string reason, AssertionId retractionId, string by, DateTimeOffset at) =>
        TryCancel(reason, retractionId, by, at, out var step) ? step : null;

    private bool TryEnd(
        FormQueryStatus status,
        FormQueryMessageKind kind,
        string reason,
        AssertionId retractionId,
        string by,
        DateTimeOffset at,
        out KernelAssertionStep? step)
    {
        if (Status is not (FormQueryStatus.Open or FormQueryStatus.Answered))
        {
            step = null;
            return false;
        }

        Status = status;
        _messages.Add(new FormQueryMessage(kind, Text(reason), by, at));
        step = ObservationId is not null && KernelQueryId is { } queryAssertion
            ? new KernelAssertionStep(KernelAssertionStepKind.Retract, retractionId, queryAssertion, null)
            : null;
        return true;
    }

    private static string Text(string text) =>
        IsText(text) ? text.Trim() : throw new ArgumentException($"Query text is non-empty and at most {AssertionText.MaxLength} characters.", nameof(text));
}
