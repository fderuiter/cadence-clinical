using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>A query to raise on one of a form's values at the revision the form read showed, or on a blank item by item id.</summary>
/// <param name="ObservationId">The kernel observation holding the value; omit or pass empty when raising on a blank item by <paramref name="ItemId"/>.</param>
/// <param name="Revision">The value's current revision when <paramref name="ObservationId"/> is supplied.</param>
/// <param name="Text">The question.</param>
/// <param name="QueryId">The query's id, when the client chooses it; otherwise the server does.</param>
/// <param name="ItemId">The CollectionSpec item id when raising a missing-data query on a blank item.</param>
/// <param name="OwnerRole">Optional initial owner role (<c>site</c>, <c>monitor</c>, <c>data_manager</c>); defaults to <c>site</c>.</param>
/// <param name="Priority">Optional initial priority (<c>low</c>, <c>normal</c>, <c>high</c>); defaults to <c>normal</c>.</param>
/// <param name="Assignee">Optional specific assignee identifier.</param>
public sealed record RaiseFormQueryRequest(
    Guid? ObservationId,
    int? Revision,
    string Text,
    Guid? QueryId = null,
    string? ItemId = null,
    string? OwnerRole = null,
    string? Priority = null,
    string? Assignee = null);

/// <summary>An answer to a query, or why it is reopened.</summary>
/// <param name="Text">The text.</param>
/// <param name="Assignee">Optional replacement assignee when ownership flips; omit to clear stale assignees.</param>
public sealed record FormQueryTextRequest(string Text, string? Assignee = null);

/// <summary>Request body for reassigning an open or answered query.</summary>
/// <param name="Assignee">New assignee identifier, or null/empty to clear the current assignee.</param>
/// <param name="OwnerRole">Optional updated owner role (<c>site</c>, <c>monitor</c>, <c>data_manager</c>).</param>
/// <param name="Text">Optional note recorded in the query message thread.</param>
public sealed record ReassignFormQueryRequest(
    string? Assignee,
    string? OwnerRole = null,
    string? Text = null);

/// <summary>Optional note body when closing or cancelling a query separately from <c>X-Change-Reason</c>.</summary>
/// <param name="Text">Optional closing or cancellation note.</param>
public sealed record FormQueryNoteRequest(string? Text = null);

/// <summary>Study, site, participant and form metadata attached to a query response when resolved.</summary>
internal sealed record FormQueryContext(
    Guid? StudyId = null,
    Guid? StudySiteId = null,
    string? SiteCode = null,
    string? SubjectCode = null,
    string? CollectionSpecId = null,
    string? PlannedActivityId = null);

/// <summary>A data query about one revision of one eCRF value (#5461, <c>PRD-QRY-001</c>, <c>PRD-QRY-002</c>).</summary>
public sealed record FormQueryResponse(
    Guid QueryId,
    Guid FormId,
    Guid EnrollmentId,
    string ItemId,
    Guid ObservationId,
    int Revision,
    int? CurrentRevision,
    bool ValueChanged,
    string Status,
    string Text,
    IReadOnlyList<FormQueryMessageResponse> Messages,
    string RaisedBy,
    DateTimeOffset RaisedAt,
    long Version,
    Guid? StudyId = null,
    Guid? StudySiteId = null,
    string? SiteCode = null,
    string? SubjectCode = null,
    string? CollectionSpecId = null,
    string? PlannedActivityId = null,
    string Origin = "manual",
    string OwnerRole = "site",
    string? Assignee = null,
    string Priority = "normal",
    DateTimeOffset? OpenedAt = null,
    DateTimeOffset? StatusSince = null,
    DateTimeOffset? DueAt = null,
    DateTimeOffset? EscalatedAt = null,
    string? LastAnsweredBy = null)
{
    private const string MaskedLiteral = "masked";

    /// <summary>A stored query, with the current revisions of its form's values.</summary>
    internal static FormQueryResponse From(
        FormQuery query,
        IReadOnlyDictionary<ObservationId, int> revisions,
        FormQueryContext? context = null,
        bool maskBlinded = false,
        int? currentRevisionOverride = null) =>
        From(FormQueryState.Of(query, query.VersionIndex), revisions, context, maskBlinded, currentRevisionOverride);

    /// <summary>A query after a command (or reconstructed snapshot), with the current revisions of its form's values.</summary>
    internal static FormQueryResponse From(
        FormQueryState state,
        IReadOnlyDictionary<ObservationId, int> revisions,
        FormQueryContext? context = null,
        bool maskBlinded = false,
        int? currentRevisionOverride = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(revisions);
        int? current = currentRevisionOverride
            ?? (revisions.TryGetValue(state.ObservationId, out var revision) ? revision : null);
        var raised = state.Messages[0];
        var question = state.Messages.Last(m => m.Kind is FormQueryMessageKind.Raised or FormQueryMessageKind.Reopened);
        return new FormQueryResponse(
            state.QueryId.Value,
            state.FormId.Value,
            state.EnrollmentId.Value,
            state.ItemId,
            maskBlinded ? Guid.Empty : state.ObservationId.Value,
            state.Revision,
            current,
            current > state.Revision,
            Lower(state.Status),
            maskBlinded ? MaskedLiteral : question.Text,
            [.. state.Messages.Select(m => new FormQueryMessageResponse(
                Lower(m.Kind),
                maskBlinded ? MaskedLiteral : m.Text,
                maskBlinded ? MaskedLiteral : m.By,
                m.At,
                maskBlinded ? MaskedLiteral : m.ReasonForChange))],
            maskBlinded ? MaskedLiteral : raised.By,
            raised.At,
            state.Version,
            context?.StudyId,
            context?.StudySiteId,
            context?.SiteCode,
            context?.SubjectCode,
            context?.CollectionSpecId,
            context?.PlannedActivityId,
            Lower(state.Origin),
            FormQueryEscalationService.ToSnakeCase(state.OwnerRole),
            maskBlinded && state.Assignee is not null ? MaskedLiteral : state.Assignee,
            Lower(state.Priority),
            state.OpenedAt,
            state.StatusSince,
            state.DueAt,
            state.EscalatedAt,
            maskBlinded && state.LastAnsweredBy is not null ? MaskedLiteral : state.LastAnsweredBy);
    }

    private static string Lower<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();
}

/// <summary>One step of a query's conversation.</summary>
/// <param name="Kind">raised, answered, reopened, reassigned, closed, cancelled or escalated.</param>
/// <param name="Text">The query, answer or reopening text, or the closing/cancelling/escalation note.</param>
/// <param name="By">Who recorded it.</param>
/// <param name="At">When it was recorded.</param>
/// <param name="ReasonForChange">Verbatim change reason supplied for the step.</param>
public sealed record FormQueryMessageResponse(
    string Kind,
    string Text,
    string By,
    DateTimeOffset At,
    string ReasonForChange = "");

/// <summary>Response shape for a daily de-identified query aging digest (<c>PRD-QRY-002</c>).</summary>
public sealed record FormQueryAgingDigestResponse(
    Guid DigestId,
    Guid StudyId,
    Guid? StudySiteId,
    string ScopeKey,
    string RecipientRole,
    DateOnly DigestDate,
    DateTimeOffset GeneratedAt,
    string StudyCode,
    string? SiteCode,
    int EscalatedCount,
    IReadOnlyList<FormQueryAgingDigestEntry> Entries)
{
    internal static FormQueryAgingDigestResponse From(
        FormQueryAgingDigest digest,
        IReadOnlyList<FormQueryAgingDigestEntry>? entriesOverride = null) =>
        new(
            digest.Id,
            digest.StudyId.Value,
            digest.StudySiteId?.Value,
            digest.ScopeKey,
            digest.RecipientRole,
            digest.DigestDate,
            digest.GeneratedAt,
            digest.StudyCode,
            digest.SiteCode,
            digest.EscalatedCount,
            entriesOverride ?? [.. digest.Entries]);

    internal static FormQueryAgingDigestResponse From(
        FormQueryAgingDigestState state,
        IReadOnlyList<FormQueryAgingDigestEntry>? entriesOverride = null) =>
        new(
            state.DigestId,
            state.StudyId.Value,
            state.StudySiteId?.Value,
            state.ScopeKey,
            state.RecipientRole,
            state.DigestDate,
            state.GeneratedAt,
            state.StudyCode,
            state.SiteCode,
            state.EscalatedCount,
            entriesOverride ?? state.Entries);
}

/// <summary>Response returned after running a study query escalation and digest pass.</summary>
public sealed record FormQueryEscalationPassResponse(
    int EscalatedQueries,
    IReadOnlyList<FormQueryAgingDigestResponse> Digests);
