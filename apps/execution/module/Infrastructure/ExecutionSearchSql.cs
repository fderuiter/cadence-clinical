using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Kernel.References;

namespace Cadence.Modules.Execution.Infrastructure;

/// <summary>
/// One searchable table in the <c>execution</c> schema backing <see cref="NodeKinds.FormQuery"/> full-text search
/// (ADR-2484, Canvas D9). Every expression in <see cref="Code"/>, <see cref="Title"/>, and <see cref="ExtraSearch"/>
/// references only alias <c>t</c> with PostgreSQL immutable functions so a GIN expression index backs every query.
/// </summary>
/// <param name="Name">Stable contributor source name.</param>
/// <param name="Table">Table in the <c>execution</c> schema.</param>
/// <param name="From">SQL <c>FROM</c> clause aliasing <paramref name="Table"/> as <c>t</c>.</param>
/// <param name="QueryId">SQL expression yielding the <c>form_queries.id</c> UUID.</param>
/// <param name="FormId">SQL expression yielding the <c>form_queries.form_id</c> UUID.</param>
/// <param name="EnrollmentId">SQL expression yielding the <c>form_queries.enrollment_id</c> UUID.</param>
/// <param name="ItemId">SQL expression yielding the queried <c>item_id</c>.</param>
/// <param name="Status">SQL expression yielding the query status.</param>
/// <param name="VersionIndex">SQL expression yielding <c>form_queries.version_index</c>.</param>
/// <param name="Code">Immutable code expression on <c>t</c>.</param>
/// <param name="Title">Immutable title expression on <c>t</c>.</param>
/// <param name="ExtraSearch">Optional immutable extra full-text tokens on <c>t</c>.</param>
public sealed record ExecutionSearchSource(
    string Name,
    string Table,
    string From,
    string QueryId,
    string FormId,
    string EnrollmentId,
    string ItemId,
    string Status,
    string VersionIndex,
    string Code,
    string Title,
    string? ExtraSearch = null)
{
    /// <summary>The indexed PostgreSQL <c>simple</c> <c>tsvector</c> expression.</summary>
    public string Document => ExtraSearch is null
        ? $"to_tsvector('simple'::regconfig, coalesce(({Code})::text, '') || ' ' || coalesce(({Title})::text, ''))"
        : $"to_tsvector('simple'::regconfig, coalesce(({Code})::text, '') || ' ' || coalesce(({Title})::text, '') || ' ' || coalesce(({ExtraSearch})::text, ''))";

    /// <summary>The GIN index name on <see cref="Table"/>.</summary>
    public string IndexName => $"ix_{Table}_search";
}

/// <summary>
/// PostgreSQL GIN expression index definitions and source metadata for <c>execution.form_queries</c> and
/// <c>execution.form_query_messages</c> (ADR-2484).
/// </summary>
public static partial class ExecutionSearchSql
{
    /// <summary>The Execution tables searched for <see cref="NodeKinds.FormQuery"/> hits.</summary>
    public static IReadOnlyList<ExecutionSearchSource> Sources { get; } =
    [
        new(
            "execution_form_queries",
            ExecutionModuleDbContext.FormQueriesTable,
            "execution.form_queries t",
            "t.id",
            "t.form_id",
            "t.enrollment_id",
            "t.item_id",
            "t.status",
            "t.version_index",
            "'QRY-' || upper(right(encode(uuid_send(t.id), 'hex'), 8))",
            "'Query on ' || t.item_id || ' (' || t.status || ')'",
            ExtraSearch: "'QRY ' || right(encode(uuid_send(t.id), 'hex'), 8) || ' ' || translate(t.item_id, '_-', '  ') || ' ' || t.owner_role || ' ' || t.priority || ' ' || t.origin || ' ' || t.reason_for_change"),
        new(
            "execution_form_query_messages",
            ExecutionModuleDbContext.FormQueryMessagesTable,
            "execution.form_query_messages t JOIN execution.form_queries q ON q.tenant_id = t.tenant_id AND q.id = t.query_id",
            "q.id",
            "q.form_id",
            "q.enrollment_id",
            "q.item_id",
            "q.status",
            "q.version_index",
            "'QRY-' || upper(right(encode(uuid_send(t.query_id), 'hex'), 8))",
            "t.text",
            ExtraSearch: "'QRY ' || right(encode(uuid_send(t.query_id), 'hex'), 8) || ' ' || translate(t.text, '_-', '  ') || ' ' || t.kind || ' ' || t.reason_for_change"),
    ];

    /// <summary>Creates the GIN expression indexes for <see cref="Sources"/> in <paramref name="schema"/>.</summary>
    public static string CreateIndexes(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return string.Concat(Sources
            .DistinctBy(s => s.Table)
            .Select(s => $"CREATE INDEX IF NOT EXISTS \"{s.IndexName}\" ON \"{schema}\".\"{s.Table}\" USING gin (({TableAlias().Replace(s.Document, string.Empty)}));\n"));
    }

    /// <summary>Drops the GIN expression indexes for <see cref="Sources"/> in <paramref name="schema"/>.</summary>
    public static string DropIndexes(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return string.Concat(Sources
            .DistinctBy(s => s.Table)
            .Select(s => $"DROP INDEX IF EXISTS \"{schema}\".\"{s.IndexName}\";\n"));
    }

    [GeneratedRegex(@"\bt\.", RegexOptions.CultureInvariant)]
    private static partial Regex TableAlias();
}
