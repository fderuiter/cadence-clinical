using System;
using System.Collections.Generic;

using Cadence.Kernel;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Evidence;
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

internal static class FormQueryTextPolicy
{
    public static bool IsText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= AssertionText.MaxLength;

    public static string Text(string text) =>
        IsText(text) ? text.Trim() : throw new ArgumentException($"Query text is non-empty and at most {AssertionText.MaxLength} characters.", nameof(text));

    public static string NormalizeReason(string? reason, string fallback) =>
        string.IsNullOrWhiteSpace(reason) ? fallback : reason.Trim();

    public static string? NormalizeAssignee(string? assignee)
    {
        if (string.IsNullOrWhiteSpace(assignee))
        {
            return null;
        }

        var trimmed = assignee.Trim();
        return trimmed.Length <= FormQuery.AssigneeMaxLength
            ? trimmed
            : throw new ArgumentException($"Assignee may be at most {FormQuery.AssigneeMaxLength} characters.", nameof(assignee));
    }

    public static string BuildReassignNote(
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
