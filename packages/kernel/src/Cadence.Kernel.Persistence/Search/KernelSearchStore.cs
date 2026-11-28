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
/// refers to. <see cref="From"/> aliases the searched table as <c>t</c>; <see cref="Code"/>, <see cref="Title"/>, and
/// <see cref="ExtraSearch"/> may only use <c>t</c>'s columns with immutable expressions, so the full-text expression
/// can be indexed by PostgreSQL GIN.
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
    string Detail,
    string? ExtraSearch = null,
    bool IsInitialTable = false)
{
    /// <summary>The indexed full-text expression: code, title, and optional extra searchable text, <c>simple</c> configuration (no stemming, any language).</summary>
    public string Document => ExtraSearch is null
        ? $"to_tsvector('simple'::regconfig, coalesce(({Code})::text, '') || ' ' || coalesce(({Title})::text, ''))"
        : $"to_tsvector('simple'::regconfig, coalesce(({Code})::text, '') || ' ' || coalesce(({Title})::text, '') || ' ' || coalesce(({ExtraSearch})::text, ''))";

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
        new("organizations", NodeKinds.Organization, "organizations", "kernel.organizations t", "t.id", "t.version_index", "t.code", "t.name", "NULL", IsInitialTable: true),
        new("sites", NodeKinds.Site, "sites", "kernel.sites t", "t.id", "t.version_index", "t.site_code", "t.name", "t.country", IsInitialTable: true),
        new("studies", NodeKinds.Study, "studies", "kernel.studies t", "t.id", "t.version_index", "t.study_code", "t.title", "NULL", IsInitialTable: true),
        new("concepts", NodeKinds.Concept, "concepts", "kernel.concepts t", "t.id", "t.version", "t.external_code", "t.preferred_label", "NULL", IsInitialTable: true),
        new("code_systems", NodeKinds.CodeSystem, "code_systems", "kernel.code_systems t", "t.id", "t.version_index", "NULL", "t.name", "t.publisher", IsInitialTable: true),
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
            "s.name || ' ' || v.version",
            IsInitialTable: true),
        new("standard_versions", NodeKinds.StandardVersion, "standard_versions", "kernel.standard_versions t", "t.id", "t.version_index", "t.standard_code", "t.name", "t.version", IsInitialTable: true),
        new("library_items", NodeKinds.LibraryItem, "library_items", "kernel.library_items t", "t.id", "t.version_index", "t.key", "t.title", "NULL", IsInitialTable: true),
        new(
            "study_sites",
            NodeKinds.StudySite,
            "sites",
            "kernel.sites t JOIN kernel.study_sites ss ON ss.tenant_id = t.tenant_id AND ss.site_id = t.id "
                + "JOIN kernel.studies st ON st.tenant_id = ss.tenant_id AND st.id = ss.study_id",
            "ss.id",
            "ss.version_index",
            "t.site_code",
            "t.name",
            "st.study_code || ' · ' || t.country || ' (' || ss.status || ')'",
            IsInitialTable: true),
        new(
            "enrollments",
            NodeKinds.Enrollment,
            "enrollments",
            "kernel.enrollments t JOIN kernel.studies st ON st.tenant_id = t.tenant_id AND st.id = t.study_id "
                + "JOIN kernel.study_sites ss ON ss.tenant_id = t.tenant_id AND ss.id = t.study_site_id "
                + "JOIN kernel.sites s ON s.tenant_id = ss.tenant_id AND s.id = ss.site_id",
            "t.id",
            "t.version_index",
            "t.subject_code",
            "'Participant ' || t.subject_code",
            "st.study_code || ' · ' || s.site_code || ' · ' || t.status",
            ExtraSearch: "translate(t.subject_code, '_-', '  ') || ' ' || t.status"),
        new(
            "protocol_versions",
            NodeKinds.ProtocolVersion,
            "protocol_versions",
            "kernel.protocol_versions t JOIN kernel.studies st ON st.tenant_id = t.tenant_id AND st.id = t.study_id",
            "t.id",
            "t.version_index",
            "'v' || (t.number)::text",
            "'Protocol v' || (t.number)::text || ' (' || t.status || ')'",
            "st.study_code || ' · ' || t.status",
            ExtraSearch: "left(t.content, 16384)"), // deid-ignore: protocol JSON prefix length cap, not a postal code
        new(
            "collection_instances",
            NodeKinds.CollectionInstance,
            "collection_instances",
            "kernel.collection_instances t JOIN kernel.studies st ON st.tenant_id = t.tenant_id AND st.id = t.study_id "
                + "JOIN kernel.study_sites ss ON ss.tenant_id = t.tenant_id AND ss.id = t.study_site_id "
                + "JOIN kernel.sites s ON s.tenant_id = ss.tenant_id AND s.id = ss.site_id "
                + "JOIN kernel.enrollments e ON e.tenant_id = t.tenant_id AND e.id = t.enrollment_id",
            "t.id",
            "t.version_index",
            "t.collection_spec_id",
            "CASE WHEN t.planned_activity_id IS NOT NULL AND t.planned_activity_id <> '' THEN t.collection_spec_id || ' (' || t.planned_activity_id || ')' ELSE t.collection_spec_id END",
            "st.study_code || ' · ' || s.site_code || ' · ' || e.subject_code",
            ExtraSearch: "translate(t.collection_spec_id, '_-', '  ') || ' ' || coalesce(translate(t.planned_activity_id, '_-', '  '), '')"),
        new(
            "observations",
            NodeKinds.Observation,
            "observations",
            "kernel.observations t JOIN kernel.studies st ON st.tenant_id = t.tenant_id AND st.id = t.study_id "
                + "JOIN kernel.enrollments e ON e.tenant_id = t.tenant_id AND e.id = t.enrollment_id "
                + "JOIN kernel.study_sites ss ON ss.tenant_id = e.tenant_id AND ss.id = e.study_site_id "
                + "JOIN kernel.sites s ON s.tenant_id = ss.tenant_id AND s.id = ss.site_id "
                + "LEFT JOIN kernel.concepts c ON c.tenant_id = t.tenant_id AND c.id = t.concept_id "
                + "LEFT JOIN kernel.collection_instance_members cim ON cim.tenant_id = t.tenant_id AND cim.observation_id = t.id",
            "t.id",
            "t.revision",
            "t.value ->> 'code'",
            "coalesce(t.value ->> 'value', t.value ->> 'code', t.value ->> 'detail', t.value ->> 'reason', 'Observation')",
            "st.study_code || ' · ' || s.site_code || ' · ' || e.subject_code",
            ExtraSearch: "translate(coalesce(t.value ->> 'value', t.value ->> 'code', ''), '_-', '  ') || ' ' || coalesce(t.value ->> 'unit', '') || ' ' || coalesce(t.value ->> 'reason', '') || ' ' || coalesce(t.value ->> 'detail', '')"),
        new(
            "observation_concepts",
            NodeKinds.Observation,
            "concepts",
            "kernel.concepts t JOIN kernel.observations o ON o.tenant_id = t.tenant_id AND o.concept_id = t.id "
                + "JOIN kernel.studies st ON st.tenant_id = o.tenant_id AND st.id = o.study_id "
                + "JOIN kernel.enrollments e ON e.tenant_id = o.tenant_id AND e.id = o.enrollment_id "
                + "JOIN kernel.study_sites ss ON ss.tenant_id = e.tenant_id AND ss.id = e.study_site_id "
                + "JOIN kernel.sites s ON s.tenant_id = ss.tenant_id AND s.id = ss.site_id",
            "o.id",
            "o.revision",
            "t.external_code",
            "t.preferred_label",
            "st.study_code || ' · ' || s.site_code || ' · ' || e.subject_code",
            IsInitialTable: true),
        new(
            "observation_items",
            NodeKinds.Observation,
            "collection_instance_members",
            "kernel.collection_instance_members t JOIN kernel.observations o ON o.tenant_id = t.tenant_id AND o.id = t.observation_id "
                + "JOIN kernel.studies st ON st.tenant_id = o.tenant_id AND st.id = o.study_id "
                + "JOIN kernel.enrollments e ON e.tenant_id = o.tenant_id AND e.id = o.enrollment_id "
                + "JOIN kernel.study_sites ss ON ss.tenant_id = e.tenant_id AND ss.id = e.study_site_id "
                + "JOIN kernel.sites s ON s.tenant_id = ss.tenant_id AND s.id = ss.site_id",
            "o.id",
            "o.revision",
            "t.item_id",
            "t.item_id",
            "st.study_code || ' · ' || s.site_code || ' · ' || e.subject_code",
            ExtraSearch: "translate(t.item_id, '_-', '  ')"),
        new(
            "validation_detections",
            NodeKinds.ValidationFinding,
            "validation_detections",
            "kernel.validation_detections t JOIN kernel.studies st ON st.tenant_id = t.tenant_id AND st.id = t.study_id "
                + "LEFT JOIN kernel.validation_findings vf ON vf.tenant_id = t.tenant_id AND vf.id = t.finding_id",
            "t.finding_id",
            "vf.version_index",
            "t.rule_id",
            "t.message",
            "st.study_code || ' · ' || t.dataset || ' · ' || t.severity",
            ExtraSearch: "translate(t.rule_id, '_-', '  ') || ' ' || coalesce(t.dataset, '') || ' ' || coalesce(translate(t.variable, '_-', '  '), '') || ' ' || coalesce(translate(t.pack_key, '_-', '  '), '') || ' ' || coalesce(t.severity, '')"),
        new(
            "validation_findings",
            NodeKinds.ValidationFinding,
            "validation_findings",
            "kernel.validation_findings t JOIN kernel.studies st ON st.tenant_id = t.tenant_id AND st.id = t.study_id",
            "t.id",
            "t.version_index",
            "t.decision",
            "coalesce(t.decision_text, t.classification, 'Validation finding')",
            "st.study_code || ' · ' || coalesce(t.decision, 'Decided')",
            ExtraSearch: "coalesce(t.classification, '')"),
        new(
            "qualifications",
            NodeKinds.Qualification,
            "qualifications",
            "kernel.qualifications t",
            "t.id",
            "t.version_index",
            "t.code",
            "t.kind || ' · ' || t.code",
            "t.holder_kind",
            ExtraSearch: "translate(t.code, '_', ' ')"),
        new(
            "artifacts",
            NodeKinds.Artifact,
            "artifacts",
            "kernel.artifacts t LEFT JOIN kernel.studies st ON st.tenant_id = t.tenant_id AND st.id = t.study_id",
            "t.id",
            "t.version_index",
            "t.kind",
            "t.title",
            "CASE WHEN st.study_code IS NOT NULL THEN st.study_code || ' · ' || t.kind ELSE t.kind END",
            ExtraSearch: "translate(t.title, '_-', '  ') || ' ' || translate(t.kind, '_-', '  ')"),
        new(
            "proposals",
            NodeKinds.Proposal,
            "proposals",
            "kernel.proposals t JOIN kernel.studies st ON st.tenant_id = t.tenant_id AND st.id = t.study_id",
            "t.id",
            "t.version_index",
            "NULL",
            "t.intent",
            "st.study_code || ' · ' || t.status",
            ExtraSearch: "t.status"),
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

        var sql = new StringBuilder("""SELECT * FROM (SELECT DISTINCT ON ("Kind", "Id") * FROM (""");
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

        sql.Append(""") AS arms ORDER BY "Kind", "Id", "ExactCode" DESC, "Rank" DESC, "Version" DESC NULLS LAST) AS hits ORDER BY "ExactCode" DESC, "Rank" DESC, "Title", "Id" LIMIT @limit""");

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
    private sealed record SearchRow(string Kind, Guid Id, long? Version, string Title, string? Code, string? Detail, bool ExactCode, double Rank);
}

/// <summary>DDL for the search indexes, used by the kernel migrations that create them.</summary>
internal static partial class KernelSearchSql
{
    public static string CreateIndexes(string schema) => CreateIndexesFor(
        schema,
        KernelSearchStore.BuiltInSources.Where(s => s.IsInitialTable));

    public static string DropIndexes(string schema) => DropIndexesFor(
        schema,
        KernelSearchStore.BuiltInSources.Where(s => s.IsInitialTable));

    public static string CreateClinicalEntityIndexes(string schema) => CreateIndexesFor(
        schema,
        KernelSearchStore.BuiltInSources.Where(s => !s.IsInitialTable));

    public static string DropClinicalEntityIndexes(string schema) => DropIndexesFor(
        schema,
        KernelSearchStore.BuiltInSources.Where(s => !s.IsInitialTable));

    private static string CreateIndexesFor(string schema, IEnumerable<KernelSearchSource> sources) => string.Concat(sources
        .DistinctBy(s => s.Table)
        .Select(s => $"CREATE INDEX IF NOT EXISTS {SqlIdentifier.Quote(s.IndexName)} ON {SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(s.Table)} USING gin (({TableAlias().Replace(s.Document, string.Empty)}));\n"));

    private static string DropIndexesFor(string schema, IEnumerable<KernelSearchSource> sources) => string.Concat(sources
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
