using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Search;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

using NpgsqlTypes;

namespace Cadence.Kernel.Persistence.Search;

/// <summary>
/// One searchable kernel table (ADR-2484 §4.2): which columns are the code and the title, and which object a row
/// refers to. <see cref="From"/> aliases the searched table as <c>t</c>; <see cref="Code"/> and <see cref="Title"/> may
/// only use <c>t</c>'s columns, so the full-text expression can be indexed.
/// </summary>
internal sealed record KernelSearchSource(
    string Name,
    string Kind,
    string Table,
    string From,
    string Id,
    string Version,
    string Code,
    string Title,
    string Detail)
{
    /// <summary>The indexed full-text expression: code and title, <c>simple</c> configuration (no stemming, any language).</summary>
    public string Document => $"to_tsvector('simple'::regconfig, coalesce(({Code})::text, '') || ' ' || coalesce(({Title})::text, ''))";

    public string IndexName => $"ix_{Table}_search";
}

/// <summary>
/// Searches the kernel's own tables with PostgreSQL full-text search (ADR-2484). There is no separate index table:
/// each source is queried where the data lives, through a context bound to the tenant, so Row-Level Security applies
/// exactly as it does to the source (K-ID-2) and nothing can linger after the source changes. Each source has a GIN
/// expression index on <see cref="KernelSearchSource.Document"/>.
/// </summary>
internal sealed class KernelSearchStore(KernelTenantDbContextFactory contexts) : IKernelSearchStore
{
    internal static readonly IReadOnlyList<KernelSearchSource> BuiltInSources =
    [
        new("organizations", NodeKinds.Organization, "organizations", "kernel.organizations t", "t.id", "t.version_index", "t.code", "t.name", "NULL"),
        new("sites", NodeKinds.Site, "sites", "kernel.sites t", "t.id", "t.version_index", "t.site_code", "t.name", "t.country"),
        new("studies", NodeKinds.Study, "studies", "kernel.studies t", "t.id", "t.version_index", "t.study_code", "t.title", "NULL"),
        new("concepts", NodeKinds.Concept, "concepts", "kernel.concepts t", "t.id", "t.version", "t.external_code", "t.preferred_label", "NULL"),
        new("code_systems", NodeKinds.CodeSystem, "code_systems", "kernel.code_systems t", "t.id", "t.version_index", "NULL", "t.name", "t.publisher"),
        new(
            "terminology_codes",
            NodeKinds.CodeSystemVersion,
            "code_system_entries",
            "kernel.code_system_entries t JOIN kernel.code_system_versions v ON v.tenant_id = t.tenant_id AND v.id = t.code_system_version_id "
                + "JOIN kernel.code_systems s ON s.tenant_id = v.tenant_id AND s.id = v.code_system_id",
            "v.id",
            "v.version_index",
            "t.code",
            "t.display",
            "s.name || ' ' || v.version"),
        new("standard_versions", NodeKinds.StandardVersion, "standard_versions", "kernel.standard_versions t", "t.id", "t.version_index", "t.standard_code", "t.name", "t.version"),
        new("library_items", NodeKinds.LibraryItem, "library_items", "kernel.library_items t", "t.id", "t.version_index", "t.key", "t.title", "NULL"),
    ];

    private readonly Dictionary<string, KernelSearchSource> _sources = BuiltInSources.ToDictionary(s => s.Name, StringComparer.Ordinal);

    public IReadOnlyList<SearchSourceInfo> Sources { get; } = [.. BuiltInSources.Select(s => new SearchSourceInfo(s.Name, s.Kind))];

    public async Task<IReadOnlyList<SearchHit>> QueryAsync(TenantId tenant, SearchPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var unknown = plan.Sources.FirstOrDefault(name => !_sources.ContainsKey(name));
        if (unknown is not null)
        {
            throw new ArgumentException($"Unknown search source '{unknown}'.", nameof(plan));
        }

        var selected = plan.Sources.Select(name => _sources[name]).ToList();
        if (selected.Count == 0)
        {
            return [];
        }

        var sql = new StringBuilder("SELECT * FROM (");
        for (var i = 0; i < selected.Count; i++)
        {
            var s = selected[i];
            if (i > 0)
            {
                sql.Append(" UNION ALL ");
            }

            sql.Append($"""
                SELECT '{s.Kind}' AS "Kind", {s.Id} AS "Id", ({s.Version})::bigint AS "Version", ({s.Title})::text AS "Title",
                  ({s.Code})::text AS "Code", ({s.Detail})::text AS "Detail",
                  coalesce(lower(({s.Code})::text) = lower(@text), false) AS "ExactCode",
                  ts_rank({s.Document}, to_tsquery('simple'::regconfig, @query))::double precision AS "Rank"
                FROM {s.From}
                WHERE t.tenant_id = @tenant
                  AND ({s.Document} @@ to_tsquery('simple'::regconfig, @query) OR lower(({s.Code})::text) = lower(@text))
                """);
        }

        sql.Append(""") AS hits ORDER BY "ExactCode" DESC, "Rank" DESC, "Title", "Id" LIMIT @limit""");

        await using var db = contexts.Create(tenant);
        var rows = await db.Database.SqlQueryRaw<SearchRow>(
                sql.ToString(),
                new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = plan.PrefixQuery },
                new NpgsqlParameter("text", NpgsqlDbType.Text) { Value = plan.Text },
                new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant.Value },
                new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = plan.Limit })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new SearchHit(new NodeRef(r.Kind, r.Id, r.Version), r.Title, r.Code, r.Detail, r.ExactCode, r.Rank))];
    }

    /// <summary>One result row; EF binds the columns to the constructor by name.</summary>
    private sealed record SearchRow(string Kind, Guid Id, long Version, string Title, string? Code, string? Detail, bool ExactCode, double Rank);
}

/// <summary>DDL for the search indexes, used by the kernel migration that creates them.</summary>
internal static partial class KernelSearchSql
{
    public static string CreateIndexes(string schema) => string.Concat(KernelSearchStore.BuiltInSources
        .DistinctBy(s => s.Table)
        .Select(s => $"CREATE INDEX IF NOT EXISTS {SqlIdentifier.Quote(s.IndexName)} ON {SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(s.Table)} USING gin (({TableAlias().Replace(s.Document, string.Empty)}));\n"));

    public static string DropIndexes(string schema) => string.Concat(KernelSearchStore.BuiltInSources
        .DistinctBy(s => s.Table)
        .Select(s => $"DROP INDEX IF EXISTS {SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(s.IndexName)};\n"));

    /// <summary>The <c>t.</c> alias prefix on a column, which an index expression cannot carry.</summary>
    [GeneratedRegex(@"\bt\.", RegexOptions.CultureInvariant)]
    private static partial Regex TableAlias();
}

/// <summary>Registers the kernel search store.</summary>
public static class KernelSearchPersistenceServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IKernelSearchStore"/> over the kernel tables. Requires <c>AddKernelPersistence</c>.</summary>
    public static IServiceCollection AddKernelSearchStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IKernelSearchStore, KernelSearchStore>();
        return services;
    }
}
