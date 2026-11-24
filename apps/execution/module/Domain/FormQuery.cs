using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Time;

namespace Cadence.Modules.Execution.Domain;

/// <summary>Where a query is in its workflow.</summary>
public enum FormQueryStatus
{
    /// <summary>Raised or reopened, waiting for the site's answer.</summary>
    Open,

    /// <summary>The site answered; the monitor closes it or reopens it.</summary>
    Answered,

    /// <summary>The monitor accepted the answer or the system rule cleared.</summary>
    Closed,

    /// <summary>Withdrawn as raised in error; final.</summary>
    Cancelled,
}

/// <summary>How a query originated.</summary>
public enum FormQueryOrigin
{
    /// <summary>Raised manually by a monitor or data manager.</summary>
    Manual,

    /// <summary>Raised automatically by a post-commit edit-check rule.</summary>
    System,
}

/// <summary>Which role currently owns the next action on a query.</summary>
public enum FormQueryOwnerRole
{
    /// <summary>Site staff (clinical research coordinator or investigator).</summary>
    Site,

    /// <summary>Clinical research associate / monitor.</summary>
    Monitor,

    /// <summary>Sponsor or CRO data manager.</summary>
    DataManager,
}

/// <summary>Urgency level of a query.</summary>
public enum FormQueryPriority
{
    /// <summary>Low priority discrepancy.</summary>
    Low,

    /// <summary>Standard discrepancy priority.</summary>
    Normal,

    /// <summary>High priority or overdue (&gt;= 14 calendar days open) discrepancy.</summary>
    High,
}

/// <summary>What happened to a query, in the order it happened.</summary>
public enum FormQueryMessageKind
{
    /// <summary>The query was raised.</summary>
    Raised,

    /// <summary>The site answered (explicitly or by correcting the queried value).</summary>
    Answered,

    /// <summary>The monitor reopened it.</summary>
    Reopened,

    /// <summary>The monitor or system rule closed it.</summary>
    Closed,

    /// <summary>It was cancelled as raised in error.</summary>
    Cancelled,

    /// <summary>It escalated after remaining open for 14 calendar days.</summary>
    Escalated,

    /// <summary>Its assignee or responsible role was explicitly reassigned.</summary>
    Reassigned,
}

/// <summary>Why closing a query failed.</summary>
public enum FormQueryCloseDenial
{
    /// <summary>The query's status (or manual open state) does not allow closing.</summary>
    InvalidTransition,

    /// <summary>An answered manual query cannot be closed by the same user who answered it.</summary>
    SeparationOfDuties,
}

/// <summary>Outcome of attempting to close a <see cref="FormQuery"/>.</summary>
/// <param name="Step">The kernel retraction step when closure succeeded; null when denied.</param>
/// <param name="Denial">Why closure was denied when <paramref name="Step"/> is null.</param>
public sealed record FormQueryCloseOutcome(KernelAssertionStep? Step, FormQueryCloseDenial? Denial = null);

/// <summary>One step of a query's conversation.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Text">The query, answer or reopening text, or the closing/cancelling/escalation note.</param>
/// <param name="By">Who recorded it.</param>
/// <param name="At">When it was recorded.</param>
/// <param name="ReasonForChange">The verbatim change reason recorded for the step.</param>
public sealed record FormQueryMessage(
    FormQueryMessageKind Kind,
    string Text,
    string By,
    DateTimeOffset At,
    string ReasonForChange = "");

/// <summary>
/// Insert-only persisted conversation message row in <c>execution.form_query_messages</c> (<c>KA001</c>).
/// </summary>
public sealed class FormQueryMessageRecord
{
    private FormQueryMessageRecord()
    {
    }

    internal FormQueryMessageRecord(
        Guid id,
        TenantId tenantId,
        FormQueryId queryId,
        int sequence,
        FormQueryMessageKind kind,
        string text,
        string recordedBy,
        string reasonForChange,
        DateTimeOffset recordedAt)
    {
        Id = id;
        TenantId = tenantId;
        QueryId = queryId;
        Sequence = sequence;
        Kind = kind;
        Text = text;
        RecordedBy = recordedBy;
        ReasonForChange = reasonForChange;
        RecordedAt = recordedAt;
    }

    /// <summary>Row identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The owning query.</summary>
    public FormQueryId QueryId { get; private set; }

    /// <summary>1-based message order within the query.</summary>
    public int Sequence { get; private set; }

    /// <summary>What happened at this step.</summary>
    public FormQueryMessageKind Kind { get; private set; }

    /// <summary>The message text.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>Who recorded the message.</summary>
    public string RecordedBy { get; private set; } = string.Empty;

    /// <summary>Verbatim change reason supplied for the command.</summary>
    public string ReasonForChange { get; private set; } = string.Empty;

    /// <summary>When the message was recorded.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Projects this persisted record as a value message.</summary>
    public FormQueryMessage ToMessage() => new(Kind, Text, RecordedBy, RecordedAt, ReasonForChange);
}

/// <summary>
/// One bitemporal state interval of a <see cref="FormQuery"/> in <c>execution.form_query_periods</c> (<c>K-TIME-1</c>, <c>K-TIME-2</c>).
/// </summary>
public sealed class FormQueryPeriod : IBitemporalEntity
{
    private FormQueryPeriod()
    {
    }

    internal FormQueryPeriod(
        Guid id,
        TenantId tenantId,
        FormQueryId queryId,
        FormQueryStatus status,
        FormQueryOwnerRole ownerRole,
        string? assignee,
        FormQueryPriority priority,
        DateTimeOffset statusSince,
        DateTimeOffset? dueAt,
        DateTimeOffset? escalatedAt,
        string? lastAnsweredBy,
        AssertionId kernelQueryId,
        AssertionId? kernelAnswerId,
        string recordedBy,
        string reasonForChange,
        TemporalPeriod period)
    {
        Id = id;
        TenantId = tenantId;
        QueryId = queryId;
        Status = status;
        OwnerRole = ownerRole;
        Assignee = assignee;
        Priority = priority;
        StatusSince = statusSince;
        DueAt = dueAt;
        EscalatedAt = escalatedAt;
        LastAnsweredBy = lastAnsweredBy;
        KernelQueryId = kernelQueryId;
        KernelAnswerId = kernelAnswerId;
        RecordedBy = recordedBy;
        ReasonForChange = reasonForChange;
        Period = period;
    }

    /// <summary>Row identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The owning query.</summary>
    public FormQueryId QueryId { get; private set; }

    /// <summary>Workflow status during this interval.</summary>
    public FormQueryStatus Status { get; private set; }

    /// <summary>Owner role during this interval.</summary>
    public FormQueryOwnerRole OwnerRole { get; private set; }

    /// <summary>Optional assignee identifier during this interval.</summary>
    public string? Assignee { get; private set; }

    /// <summary>Query priority during this interval.</summary>
    public FormQueryPriority Priority { get; private set; }

    /// <summary>When the query entered <see cref="Status"/>.</summary>
    public DateTimeOffset StatusSince { get; private set; }

    /// <summary>14-day SLA due timestamp while <see cref="FormQueryStatus.Open"/>; null otherwise.</summary>
    public DateTimeOffset? DueAt { get; private set; }

    /// <summary>When the query escalated during its current open window, if escalated.</summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>Who last answered the query in this cycle, for separation-of-duties enforcement.</summary>
    public string? LastAnsweredBy { get; private set; }

    /// <summary>Active kernel <c>Queried</c> assertion id during this interval.</summary>
    public AssertionId KernelQueryId { get; private set; }

    /// <summary>Active kernel <c>QueryAnswered</c> assertion id during this interval, if any.</summary>
    public AssertionId? KernelAnswerId { get; private set; }

    /// <summary>Actor that recorded this interval.</summary>
    public string RecordedBy { get; private set; } = string.Empty;

    /// <summary>Verbatim change reason associated with this interval.</summary>
    public string ReasonForChange { get; private set; } = string.Empty;

    /// <inheritdoc />
    public TemporalPeriod Period { get; private set; }

    /// <summary>True when this row represents the un-superseded open-ended truth.</summary>
    public bool IsCurrent => Period.Recorded.IsOpen && Period.Valid.IsOpen;

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}

/// <summary>Point-in-time snapshot of a <see cref="FormQuery"/> reconstructed from bitemporal history.</summary>
public sealed record FormQueryTemporalSnapshot(
    FormQueryStatus Status,
    FormQueryOwnerRole OwnerRole,
    string? Assignee,
    FormQueryPriority Priority,
    DateTimeOffset StatusSince,
    DateTimeOffset? DueAt,
    DateTimeOffset? EscalatedAt,
    string? LastAnsweredBy,
    IReadOnlyList<FormQueryMessage> Messages);

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
/// Bitemporal data query aggregate about one revision of one eCRF value (<c>PRD-QRY-001</c>, <c>PRD-QRY-002</c>).
/// Tracks origin (<c>manual</c> | <c>system</c>), ownership (<c>site</c> | <c>monitor</c> | <c>data_manager</c>),
/// optional assignee, priority, 14-day SLA due date, escalation, separation of duties on closure, append-only
/// bitemporal state intervals (<c>K-TIME-1</c>/<c>K-TIME-2</c>), and insert-only messages (<c>KA001</c>).
/// </summary>
[DerivedObject]
public sealed class FormQuery : AggregateRoot<FormQueryId>
{
    /// <summary>Calendar days from entering or reopening <see cref="FormQueryStatus.Open"/> until escalation.</summary>
    public const int OpenSlaDays = 14;

    /// <summary>Maximum length of an optional assignee identifier.</summary>
    public const int AssigneeMaxLength = 256;

    private readonly List<FormQueryPeriod> _periods = [];
    private readonly List<FormQueryMessageRecord> _messageRecords = [];

    private FormQuery()
    {
    }

    private FormQuery(
        FormQueryId id,
        TenantId tenantId,
        CollectionInstanceId formId,
        EnrollmentId enrollmentId,
        string itemId,
        ObservationId observationId,
        int revision,
        FormQueryOrigin origin)
        : base(id)
    {
        TenantId = tenantId;
        FormId = formId;
        EnrollmentId = enrollmentId;
        ItemId = itemId;
        ObservationId = observationId;
        Revision = revision;
        Origin = origin;
    }

    /// <summary>The tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The form the queried value belongs to.</summary>
    public CollectionInstanceId FormId { get; private set; }

    /// <summary>The enrollment the form is for; every query command is authorized against it.</summary>
    public EnrollmentId EnrollmentId { get; private set; }

    /// <summary>The CollectionSpec item whose value is queried.</summary>
    public string ItemId { get; private set; } = string.Empty;

    /// <summary>The kernel observation holding the value.</summary>
    public ObservationId ObservationId { get; private set; }

    /// <summary>The revision of the value questioned when the query was raised.</summary>
    public int Revision { get; private set; }

    /// <summary>How the query originated (<c>manual</c> or <c>system</c>).</summary>
    public FormQueryOrigin Origin { get; private set; }

    /// <summary>Where the query currently is in its workflow.</summary>
    public FormQueryStatus Status { get; private set; }

    /// <summary>Role currently responsible for acting on the query.</summary>
    public FormQueryOwnerRole OwnerRole { get; private set; }

    /// <summary>Optional specific assignee identifier.</summary>
    public string? Assignee { get; private set; }

    /// <summary>Current query priority.</summary>
    public FormQueryPriority Priority { get; private set; }

    /// <summary>When the query was initially opened.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>When the query entered its current <see cref="Status"/>.</summary>
    public DateTimeOffset StatusSince { get; private set; }

    /// <summary>14 calendar days from <see cref="StatusSince"/> while <see cref="FormQueryStatus.Open"/>; null otherwise.</summary>
    public DateTimeOffset? DueAt { get; private set; }

    /// <summary>When the query escalated past its 14-day SLA during the current open window, if escalated.</summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>Actor who provided the active answer when <see cref="FormQueryStatus.Answered"/>.</summary>
    public string? LastAnsweredBy { get; private set; }

    /// <summary>The kernel <c>Queried</c> assertion that stands for the query while it is open or answered.</summary>
    public AssertionId KernelQueryId { get; private set; }

    /// <summary>The kernel <c>QueryAnswered</c> assertion while the query is answered and the answer stands.</summary>
    public AssertionId? KernelAnswerId { get; private set; }

    /// <summary>Every bitemporal state row of this query, including superseded intervals.</summary>
    public IReadOnlyList<FormQueryPeriod> Periods => _periods;

    /// <summary>Insert-only persisted conversation message rows.</summary>
    public IReadOnlyList<FormQueryMessageRecord> MessageRecords => _messageRecords;

    /// <summary>What happened to the query, oldest first.</summary>
    public IReadOnlyList<FormQueryMessage> Messages =>
        [.. _messageRecords.OrderBy(m => m.Sequence).Select(m => m.ToMessage())];

    /// <summary>
    /// Raises a query about revision <paramref name="revision"/> of an observation, as the kernel assertion
    /// <paramref name="kernelQueryId"/>.
    /// </summary>
    public static (FormQuery Query, KernelAssertionStep Step) Raise(
        FormQueryId id,
        TenantId tenantId,
        CollectionInstanceId formId,
        EnrollmentId enrollmentId,
        string itemId,
        ObservationId observationId,
        int revision,
        string text,
        AssertionId kernelQueryId,
        string by,
        DateTimeOffset at,
        string reasonForChange = "",
        FormQueryOrigin origin = FormQueryOrigin.Manual,
        FormQueryOwnerRole ownerRole = FormQueryOwnerRole.Site,
        FormQueryPriority priority = FormQueryPriority.Normal,
        string? assignee = null)
    {
        if (id.IsEmpty || tenantId.IsEmpty || formId.IsEmpty || enrollmentId.IsEmpty || observationId.IsEmpty || kernelQueryId.IsEmpty
            || string.IsNullOrWhiteSpace(itemId) || revision < 1 || !Enum.IsDefined(origin) || !Enum.IsDefined(ownerRole) || !Enum.IsDefined(priority))
        {
            throw new ArgumentException("A query names its form, enrollment, item, observation, a revision from 1 and its kernel assertion.");
        }

        var recordedAt = KernelTimestamps.ToStoredPrecision(at);
        var normalizedAssignee = NormalizeAssignee(assignee);
        var trimmed = Text(text);
        var normalizedReason = NormalizeReason(reasonForChange, trimmed);
        var dueAt = recordedAt.AddDays(OpenSlaDays);

        var query = new FormQuery(id, tenantId, formId, enrollmentId, itemId.Trim(), observationId, revision, origin)
        {
            Status = FormQueryStatus.Open,
            OwnerRole = ownerRole,
            Assignee = normalizedAssignee,
            Priority = priority,
            OpenedAt = recordedAt,
            StatusSince = recordedAt,
            DueAt = dueAt,
            EscalatedAt = null,
            LastAnsweredBy = null,
            KernelQueryId = kernelQueryId,
            KernelAnswerId = null,
        };

        query.AppendPeriod(recordedAt, recordedAt, by, normalizedReason);
        query.AppendMessage(FormQueryMessageKind.Raised, trimmed, by, normalizedReason, recordedAt);
        return (query, new KernelAssertionStep(KernelAssertionStepKind.Raise, kernelQueryId, null, trimmed));
    }

    /// <summary>True when <paramref name="text"/> is non-empty and fits a kernel assertion.</summary>
    public static bool IsText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= AssertionText.MaxLength;

    /// <summary>The site answers an open query (explicitly or via value edit), moving ownership to <see cref="FormQueryOwnerRole.Monitor"/>.</summary>
    /// <returns>The kernel step, or null when the query is not open.</returns>
    public KernelAssertionStep? Answer(
        string text,
        AssertionId answerId,
        string by,
        DateTimeOffset at,
        string reasonForChange = "",
        string? assignee = null)
    {
        if (Status != FormQueryStatus.Open)
        {
            return null;
        }

        var trimmed = Text(text);
        var normalizedReason = NormalizeReason(reasonForChange, trimmed);
        var replacementAssignee = NormalizeAssignee(assignee);
        var previousOwnerRole = OwnerRole;
        var recordedAt = NextInstant(at);

        SupersedeCurrent(recordedAt);
        Status = FormQueryStatus.Answered;
        OwnerRole = FormQueryOwnerRole.Monitor;
        if (replacementAssignee is not null)
        {
            Assignee = replacementAssignee;
        }
        else if (previousOwnerRole != OwnerRole)
        {
            Assignee = null;
        }

        StatusSince = recordedAt;
        DueAt = null;
        EscalatedAt = null;
        LastAnsweredBy = by;
        KernelAnswerId = answerId;

        AppendPeriod(recordedAt, recordedAt, by, normalizedReason);
        AppendMessage(FormQueryMessageKind.Answered, trimmed, by, normalizedReason, recordedAt);
        return new KernelAssertionStep(KernelAssertionStepKind.Answer, answerId, KernelQueryId, trimmed);
    }

    /// <summary>
    /// The monitor reopens an answered or closed query, returning ownership to <see cref="FormQueryOwnerRole.Site"/>
    /// with a fresh 14-day SLA window.
    /// </summary>
    /// <returns>The kernel step, or null when the query is open or cancelled.</returns>
    public KernelAssertionStep? Reopen(
        string text,
        AssertionId kernelId,
        string by,
        DateTimeOffset at,
        string reasonForChange = "",
        string? assignee = null)
    {
        KernelAssertionStep step;
        var trimmed = Text(text);
        var normalizedReason = NormalizeReason(reasonForChange, trimmed);
        var replacementAssignee = NormalizeAssignee(assignee);
        var previousOwnerRole = OwnerRole;
        switch (Status)
        {
            case FormQueryStatus.Answered:
                step = new KernelAssertionStep(KernelAssertionStepKind.Retract, kernelId, KernelAnswerId, null);
                break;
            case FormQueryStatus.Closed:
                step = new KernelAssertionStep(KernelAssertionStepKind.Raise, kernelId, null, trimmed);
                break;
            default:
                return null;
        }

        var recordedAt = NextInstant(at);
        SupersedeCurrent(recordedAt);
        if (Status == FormQueryStatus.Closed)
        {
            KernelQueryId = kernelId;
        }

        Status = FormQueryStatus.Open;
        OwnerRole = FormQueryOwnerRole.Site;
        if (replacementAssignee is not null)
        {
            Assignee = replacementAssignee;
        }
        else if (previousOwnerRole != OwnerRole)
        {
            Assignee = null;
        }

        StatusSince = recordedAt;
        DueAt = recordedAt.AddDays(OpenSlaDays);
        EscalatedAt = null;
        LastAnsweredBy = null;
        KernelAnswerId = null;

        AppendPeriod(recordedAt, recordedAt, by, normalizedReason);
        AppendMessage(FormQueryMessageKind.Reopened, trimmed, by, normalizedReason, recordedAt);
        return step;
    }

    /// <summary>
    /// Reassigns an <see cref="FormQueryStatus.Open"/> or <see cref="FormQueryStatus.Answered"/> query to a new
    /// <paramref name="assignee"/> (or clears the assignee when null/empty) and optional <paramref name="ownerRole"/>,
    /// recording both a bitemporal <see cref="FormQueryPeriod"/> and an insert-only <see cref="FormQueryMessageRecord"/>.
    /// </summary>
    public bool Reassign(
        string? assignee,
        FormQueryOwnerRole? ownerRole,
        string by,
        DateTimeOffset at,
        string reasonForChange,
        string? note = null)
    {
        if (Status is not (FormQueryStatus.Open or FormQueryStatus.Answered))
        {
            return false;
        }

        if (ownerRole is { } role && !Enum.IsDefined(role))
        {
            throw new ArgumentException("Owner role is not valid.", nameof(ownerRole));
        }

        if (string.IsNullOrWhiteSpace(reasonForChange))
        {
            throw new ArgumentException("Reason for change is required when reassigning a query.", nameof(reasonForChange));
        }

        var nextAssignee = NormalizeAssignee(assignee);
        var previousOwnerRole = OwnerRole;
        var nextOwnerRole = ownerRole ?? OwnerRole;
        var normalizedReason = reasonForChange.Trim();
        var messageText = !string.IsNullOrWhiteSpace(note)
            ? Text(note)
            : BuildReassignNote(nextAssignee, previousOwnerRole, nextOwnerRole);
        var recordedAt = NextInstant(at);

        SupersedeCurrent(recordedAt);
        OwnerRole = nextOwnerRole;
        Assignee = nextAssignee;

        AppendPeriod(recordedAt, recordedAt, by, normalizedReason);
        AppendMessage(FormQueryMessageKind.Reassigned, messageText, by, normalizedReason, recordedAt);
        return true;
    }

    /// <summary>
    /// Closes a query and retracts its kernel <c>Queried</c> assertion. Enforces separation of duties on manual queries:
    /// open manual queries cannot be closed directly, and answered manual queries cannot be closed by <see cref="LastAnsweredBy"/>.
    /// </summary>
    public FormQueryCloseOutcome Close(string note, AssertionId retractionId, string by, DateTimeOffset at, string? reasonForChange = null)
    {
        if (Status is FormQueryStatus.Closed or FormQueryStatus.Cancelled)
        {
            return new FormQueryCloseOutcome(null, FormQueryCloseDenial.InvalidTransition);
        }

        if (Origin == FormQueryOrigin.Manual)
        {
            if (Status != FormQueryStatus.Answered)
            {
                return new FormQueryCloseOutcome(null, FormQueryCloseDenial.InvalidTransition);
            }

            if (!string.IsNullOrWhiteSpace(LastAnsweredBy)
                && string.Equals(by, LastAnsweredBy, StringComparison.Ordinal))
            {
                return new FormQueryCloseOutcome(null, FormQueryCloseDenial.SeparationOfDuties);
            }
        }

        var trimmed = Text(note);
        var normalizedReason = NormalizeReason(reasonForChange, trimmed);
        var recordedAt = NextInstant(at);
        SupersedeCurrent(recordedAt);

        Status = FormQueryStatus.Closed;
        StatusSince = recordedAt;
        DueAt = null;
        EscalatedAt = null;

        AppendPeriod(recordedAt, recordedAt, by, normalizedReason);
        AppendMessage(FormQueryMessageKind.Closed, trimmed, by, normalizedReason, recordedAt);
        return new FormQueryCloseOutcome(new KernelAssertionStep(KernelAssertionStepKind.Retract, retractionId, KernelQueryId, null));
    }

    /// <summary>An open or answered query is withdrawn as raised in error; the kernel query is retracted.</summary>
    /// <returns>The kernel step, or null when the query is closed or cancelled.</returns>
    public KernelAssertionStep? Cancel(string note, AssertionId retractionId, string by, DateTimeOffset at, string? reasonForChange = null)
    {
        if (Status is not (FormQueryStatus.Open or FormQueryStatus.Answered))
        {
            return null;
        }

        var trimmed = Text(note);
        var normalizedReason = NormalizeReason(reasonForChange, trimmed);
        var recordedAt = NextInstant(at);
        SupersedeCurrent(recordedAt);

        Status = FormQueryStatus.Cancelled;
        StatusSince = recordedAt;
        DueAt = null;
        EscalatedAt = null;

        AppendPeriod(recordedAt, recordedAt, by, normalizedReason);
        AppendMessage(FormQueryMessageKind.Cancelled, trimmed, by, normalizedReason, recordedAt);
        return new KernelAssertionStep(KernelAssertionStepKind.Retract, retractionId, KernelQueryId, null);
    }

    /// <summary>
    /// Escalates an open query that has reached or passed its 14-day <see cref="DueAt"/> timestamp to
    /// <see cref="FormQueryPriority.High"/> and records <see cref="EscalatedAt"/> in bitemporal history.
    /// </summary>
    public bool Escalate(string by, DateTimeOffset at, string reasonForChange = "")
    {
        var checkAt = KernelTimestamps.ToStoredPrecision(at);
        if (Status != FormQueryStatus.Open || EscalatedAt is not null || DueAt is not { } due || checkAt < due)
        {
            return false;
        }

        var recordedAt = NextInstant(checkAt);
        var note = $"Query open past {OpenSlaDays} calendar days; escalated to high priority.";
        var normalizedReason = NormalizeReason(reasonForChange, note);
        SupersedeCurrent(recordedAt);

        Priority = FormQueryPriority.High;
        EscalatedAt = recordedAt;

        AppendPeriod(recordedAt, recordedAt, by, normalizedReason);
        AppendMessage(FormQueryMessageKind.Escalated, note, by, normalizedReason, recordedAt);
        return true;
    }

    /// <summary>
    /// Reconstructs the query's state at <paramref name="validAt"/> as known at <paramref name="knownAt"/>, or null when
    /// the query had not yet been opened or recorded at those coordinates.
    /// </summary>
    public FormQueryTemporalSnapshot? StateAsOf(DateTimeOffset validAt, DateTimeOffset knownAt)
    {
        var v = KernelTimestamps.ToStoredPrecision(validAt);
        var k = KernelTimestamps.ToStoredPrecision(knownAt);
        var period = _periods.SingleOrDefault(p => p.Period.IsEffectiveAsOf(v, k));
        if (period is null)
        {
            return null;
        }

        var messages = _messageRecords
            .Where(m => m.RecordedAt <= v && m.RecordedAt <= k)
            .OrderBy(m => m.Sequence)
            .Select(m => m.ToMessage())
            .ToList();
        if (messages.Count == 0 && _messageRecords.Count > 0)
        {
            messages.Add(_messageRecords.OrderBy(m => m.Sequence).First().ToMessage());
        }

        return new FormQueryTemporalSnapshot(
            period.Status,
            period.OwnerRole,
            period.Assignee,
            period.Priority,
            period.StatusSince,
            period.DueAt,
            period.EscalatedAt,
            period.LastAnsweredBy,
            messages);
    }

    private FormQueryPeriod CurrentPeriod() =>
        _periods.SingleOrDefault(p => p.IsCurrent)
        ?? throw new InvalidOperationException($"FormQuery {Id} has no current bitemporal period; load it with its periods.");

    private DateTimeOffset NextInstant(DateTimeOffset at)
    {
        var stored = KernelTimestamps.ToStoredPrecision(at);
        var current = CurrentPeriod();
        var minNext = current.Period.Recorded.Start > current.Period.Valid.Start
            ? current.Period.Recorded.Start
            : current.Period.Valid.Start;
        return stored <= minNext ? minNext.AddMicroseconds(1) : stored;
    }

    private void SupersedeCurrent(DateTimeOffset effectiveAndRecordedAt)
    {
        var current = CurrentPeriod();
        current.Supersede(effectiveAndRecordedAt);
        _periods.Add(new FormQueryPeriod(
            Guid.CreateVersion7(),
            TenantId,
            Id,
            current.Status,
            current.OwnerRole,
            current.Assignee,
            current.Priority,
            current.StatusSince,
            current.DueAt,
            current.EscalatedAt,
            current.LastAnsweredBy,
            current.KernelQueryId,
            current.KernelAnswerId,
            current.RecordedBy,
            current.ReasonForChange,
            new TemporalPeriod(new TimeRange(current.Period.Valid.Start, effectiveAndRecordedAt), TimeRange.From(effectiveAndRecordedAt))));
    }

    private void AppendPeriod(DateTimeOffset validFrom, DateTimeOffset recordedAt, string by, string reasonForChange) =>
        _periods.Add(new FormQueryPeriod(
            Guid.CreateVersion7(),
            TenantId,
            Id,
            Status,
            OwnerRole,
            Assignee,
            Priority,
            StatusSince,
            DueAt,
            EscalatedAt,
            LastAnsweredBy,
            KernelQueryId,
            KernelAnswerId,
            by,
            reasonForChange,
            new TemporalPeriod(TimeRange.From(validFrom), TimeRange.From(recordedAt))));

    private void AppendMessage(FormQueryMessageKind kind, string text, string by, string reasonForChange, DateTimeOffset at) =>
        _messageRecords.Add(new FormQueryMessageRecord(
            Guid.CreateVersion7(),
            TenantId,
            Id,
            _messageRecords.Count + 1,
            kind,
            text,
            by,
            reasonForChange,
            at));

    private static string Text(string text) =>
        IsText(text) ? text.Trim() : throw new ArgumentException($"Query text is non-empty and at most {AssertionText.MaxLength} characters.", nameof(text));

    private static string NormalizeReason(string? reason, string fallback) =>
        string.IsNullOrWhiteSpace(reason) ? fallback : reason.Trim();

    private static string? NormalizeAssignee(string? assignee)
    {
        if (string.IsNullOrWhiteSpace(assignee))
        {
            return null;
        }

        var trimmed = assignee.Trim();
        return trimmed.Length <= AssigneeMaxLength
            ? trimmed
            : throw new ArgumentException($"Assignee may be at most {AssigneeMaxLength} characters.", nameof(assignee));
    }

    private static string BuildReassignNote(
        string? nextAssignee,
        FormQueryOwnerRole previousRole,
        FormQueryOwnerRole nextRole)
    {
        var roleWire = nextRole switch
        {
            FormQueryOwnerRole.Site => "site",
            FormQueryOwnerRole.Monitor => "monitor",
            FormQueryOwnerRole.DataManager => "data_manager",
            _ => nextRole.ToString().ToLowerInvariant(),
        };

        if (nextAssignee is not null)
        {
            return previousRole != nextRole
                ? $"Reassigned to {nextAssignee} ({roleWire})."
                : $"Reassigned to {nextAssignee}.";
        }

        return previousRole != nextRole
            ? $"Cleared assignee and set owner role to {roleWire}."
            : "Cleared query assignee.";
    }
}
