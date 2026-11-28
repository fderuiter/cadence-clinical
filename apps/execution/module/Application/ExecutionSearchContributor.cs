using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Contributes <see cref="NodeKinds.FormQuery"/> hits to kernel search from <c>execution.form_queries</c> and
/// <c>execution.form_query_messages</c> under the caller's tenant Row-Level Security (<c>K-ID-2</c>, <c>K-XS-2</c>, ADR-2484).
/// </summary>
internal sealed class ExecutionSearchContributor(
    IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
    ICollectionInstanceRecords forms,
    IKernelNavigator navigator) : IKernelSearchContributor
{
    private readonly Dictionary<string, ExecutionSearchSource> _sources =
        ExecutionSearchSql.Sources.ToDictionary(s => s.Name, StringComparer.Ordinal);

    public IReadOnlyList<SearchContributorSource> Sources { get; } =
        [.. ExecutionSearchSql.Sources.Select(s => new SearchContributorSource(s.Name, NodeKinds.FormQuery))];

    public async Task<IReadOnlyList<SearchHit>> QueryAsync(
        TenantId tenant,
        SearchContributorQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var selected = query.Sources
            .Where(_sources.ContainsKey)
            .Select(name => _sources[name])
            .ToList();
        if (selected.Count == 0)
        {
            return [];
        }

        await using var db = contexts.Create(tenant);
        List<MatchedFormQueryRow> rows;
        if (db.Database.IsRelational())
        {
            var sql = new StringBuilder("""SELECT * FROM (SELECT DISTINCT ON ("QueryId") * FROM (""");
            for (var i = 0; i < selected.Count; i++)
            {
                var s = selected[i];
                if (i > 0)
                {
                    sql.Append(" UNION ALL ");
                }

                sql.Append($"""
                    SELECT {s.QueryId} AS "QueryId", {s.FormId} AS "FormId", {s.EnrollmentId} AS "EnrollmentId",
                      ({s.ItemId})::text AS "ItemId", ({s.Status})::text AS "Status", ({s.VersionIndex})::bigint AS "VersionIndex",
                      ({s.Title})::text AS "Title", ({s.Code})::text AS "Code",
                      coalesce(lower(({s.Code})::text) = lower(@text), false) AS "ExactCode",
                      ts_rank({s.Document}, to_tsquery('simple'::regconfig, @query))::double precision AS "Rank"
                    FROM {s.From}
                    WHERE t.tenant_id = @tenant
                      AND ({s.Document} @@ to_tsquery('simple'::regconfig, @query) OR lower(({s.Code})::text) = lower(@text))
                    """);
            }

            sql.Append(""") AS arms ORDER BY "QueryId", "ExactCode" DESC, "Rank" DESC, "VersionIndex" DESC) AS hits ORDER BY "ExactCode" DESC, "Rank" DESC, "Title", "QueryId" LIMIT @limit""");

            rows = await db.Database.SqlQueryRaw<MatchedFormQueryRow>(
                    sql.ToString(),
                    new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query.PrefixQuery },
                    new NpgsqlParameter("text", NpgsqlDbType.Text) { Value = query.Text },
                    new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant.Value },
                    new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = query.Limit })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var candidates = await db.FormQueries.AsNoTracking()
                .Where(q => q.TenantId == tenant)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var needle = query.Text.Trim();
            rows = candidates
                .Select(q =>
                {
                    var code = $"QRY-{q.Id.Value.ToString("N")[^8..].ToUpperInvariant()}";
                    var messageMatch = q.MessageRecords.FirstOrDefault(m =>
                        m.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || m.ReasonForChange.Contains(needle, StringComparison.OrdinalIgnoreCase));
                    var headerMatch = code.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || q.ItemId.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || q.Status.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase);
                    if (!headerMatch && messageMatch is null)
                    {
                        return null;
                    }

                    var title = messageMatch is not null
                        ? messageMatch.Text
                        : $"Query on {q.ItemId} ({q.Status})";
                    var exact = string.Equals(code, needle, StringComparison.OrdinalIgnoreCase);
                    return new MatchedFormQueryRow(
                        q.Id.Value,
                        q.FormId.Value,
                        q.EnrollmentId.Value,
                        q.ItemId,
                        q.Status.ToString(),
                        Math.Max(1L, q.VersionIndex),
                        title,
                        code,
                        exact,
                        exact ? 1.0 : 0.5);
                })
                .Where(r => r is not null)
                .Select(r => r!)
                .OrderByDescending(r => r.ExactCode)
                .ThenByDescending(r => r.Rank)
                .ThenBy(r => r.Title, StringComparer.Ordinal)
                .ThenBy(r => r.QueryId)
                .Take(query.Limit)
                .ToList();
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var formIds = rows.Select(r => CollectionInstanceId.FromGuid(r.FormId)).Distinct().ToList();
        var formHeaders = await forms.FindHeadersAsync(tenant, formIds, cancellationToken).ConfigureAwait(false);

        var nodesToResolve = new List<NodeRef>(rows.Count * 3);
        foreach (var row in rows)
        {
            nodesToResolve.Add(NodeRef.For(FormQueryId.FromGuid(row.QueryId)));
            nodesToResolve.Add(NodeRef.For(EnrollmentId.FromGuid(row.EnrollmentId)));
            if (formHeaders.TryGetValue(CollectionInstanceId.FromGuid(row.FormId), out var header))
            {
                if (!header.StudyId.IsEmpty)
                {
                    nodesToResolve.Add(NodeRef.For(header.StudyId));
                }

                if (!header.StudySiteId.IsEmpty)
                {
                    nodesToResolve.Add(NodeRef.For(header.StudySiteId));
                }
            }
        }

        var summaries = await navigator.ResolveManyAsync(
            tenant,
            nodesToResolve.Distinct().ToList(),
            cancellationToken).ConfigureAwait(false);

        var hits = new List<SearchHit>(rows.Count);
        foreach (var row in rows)
        {
            var queryId = FormQueryId.FromGuid(row.QueryId);
            var querySummary = summaries.GetValueOrDefault(NodeRef.For(queryId));
            var version = querySummary?.Ref.Version ?? Math.Max(1L, row.VersionIndex);
            var pinnedRef = NodeRef.For(queryId, version);

            var enrollmentSummary = summaries.GetValueOrDefault(NodeRef.For(EnrollmentId.FromGuid(row.EnrollmentId)));
            var subjectCode = enrollmentSummary?.Code ?? enrollmentSummary?.Title;

            string? studyCode = null;
            string? siteCode = null;
            if (formHeaders.TryGetValue(CollectionInstanceId.FromGuid(row.FormId), out var header))
            {
                if (!header.StudyId.IsEmpty && summaries.TryGetValue(NodeRef.For(header.StudyId), out var studySummary))
                {
                    studyCode = studySummary.Code ?? studySummary.Title;
                }

                if (!header.StudySiteId.IsEmpty && summaries.TryGetValue(NodeRef.For(header.StudySiteId), out var siteSummary))
                {
                    siteCode = siteSummary.Code ?? siteSummary.Title;
                }
            }

            var detailParts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(studyCode))
            {
                detailParts.Add(studyCode);
            }

            if (!string.IsNullOrWhiteSpace(siteCode))
            {
                detailParts.Add(siteCode);
            }

            if (!string.IsNullOrWhiteSpace(subjectCode))
            {
                detailParts.Add(subjectCode);
            }

            detailParts.Add(row.Status);
            var detail = string.Join(" · ", detailParts);

            var title = row.Title.Length <= 140 ? row.Title : $"{row.Title[..139]}…";
            hits.Add(new SearchHit(pinnedRef, title, row.Code, detail, row.ExactCode, row.Rank));
        }

        return hits;
    }

    private sealed record MatchedFormQueryRow(
        Guid QueryId,
        Guid FormId,
        Guid EnrollmentId,
        string ItemId,
        string Status,
        long VersionIndex,
        string Title,
        string Code,
        bool ExactCode,
        double Rank);
}
