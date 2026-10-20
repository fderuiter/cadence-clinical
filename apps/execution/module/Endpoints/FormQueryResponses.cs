using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>A query to raise on one of a form's values, at the revision the form read showed.</summary>
/// <param name="ObservationId">The kernel observation holding the value.</param>
/// <param name="Revision">The value's current revision.</param>
/// <param name="Text">The question.</param>
/// <param name="QueryId">The query's id, when the client chooses it; otherwise the server does.</param>
public sealed record RaiseFormQueryRequest(Guid ObservationId, int Revision, string Text, Guid? QueryId);

/// <summary>An answer to a query, or why it is reopened.</summary>
/// <param name="Text">The text.</param>
public sealed record FormQueryTextRequest(string Text);

/// <summary>A data query about one revision of one eCRF value (#5461).</summary>
/// <param name="QueryId">The query.</param>
/// <param name="FormId">The form the value belongs to.</param>
/// <param name="EnrollmentId">The form's enrollment.</param>
/// <param name="ItemId">The item whose value is queried.</param>
/// <param name="ObservationId">The kernel observation holding the value.</param>
/// <param name="Revision">The revision questioned.</param>
/// <param name="CurrentRevision">The value's revision now; null when the form no longer shows it.</param>
/// <param name="ValueChanged">True once a correction moved the value past the questioned revision.</param>
/// <param name="Status">open, answered, closed or cancelled.</param>
/// <param name="Text">The question as it now stands: the text it was raised or last reopened with.</param>
/// <param name="Messages">What happened to the query, oldest first.</param>
/// <param name="RaisedBy">Who raised it.</param>
/// <param name="RaisedAt">When it was raised.</param>
/// <param name="Version">The query's version, 1 when raised.</param>
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
    long Version)
{
    /// <summary>A stored query, with the current revisions of its form's values.</summary>
    internal static FormQueryResponse From(FormQuery query, IReadOnlyDictionary<ObservationId, int> revisions) =>
        From(FormQueryState.Of(query, query.VersionIndex), revisions);

    /// <summary>A query after a command, with the current revisions of its form's values.</summary>
    internal static FormQueryResponse From(FormQueryState state, IReadOnlyDictionary<ObservationId, int> revisions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(revisions);
        int? current = revisions.TryGetValue(state.ObservationId, out var revision) ? revision : null;
        var raised = state.Messages[0];
        var question = state.Messages.Last(m => m.Kind is FormQueryMessageKind.Raised or FormQueryMessageKind.Reopened);
        return new FormQueryResponse(
            state.QueryId,
            state.FormId.Value,
            state.EnrollmentId.Value,
            state.ItemId,
            state.ObservationId.Value,
            state.Revision,
            current,
            current > state.Revision,
            Lower(state.Status),
            question.Text,
            [.. state.Messages.Select(m => new FormQueryMessageResponse(Lower(m.Kind), m.Text, m.By, m.At))],
            raised.By,
            raised.At,
            state.Version);
    }

    private static string Lower<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();
}

/// <summary>One step of a query's conversation.</summary>
/// <param name="Kind">raised, answered, reopened, closed or cancelled.</param>
/// <param name="Text">The query, answer or reopening text, or the reason for closing or cancelling.</param>
/// <param name="By">Who did it.</param>
/// <param name="At">When it was recorded.</param>
public sealed record FormQueryMessageResponse(string Kind, string Text, string By, DateTimeOffset At);
