using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Application;
using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Knowledge.Endpoints;

public sealed record KnowledgeCategoryDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("parent_id")] string? ParentId,
    [property: JsonPropertyName("persona_visibility")] IReadOnlyList<string> PersonaVisibility,
    [property: JsonPropertyName("is_deleted")] bool IsDeleted,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy);

public sealed record KnowledgeArticleVersionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("article_id")] string ArticleId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("version_label")] string VersionLabel,
    [property: JsonPropertyName("body_markdown")] string BodyMarkdown,
    [property: JsonPropertyName("body_html")] string? BodyHtml,
    [property: JsonPropertyName("status_at_snapshot")] string StatusAtSnapshot,
    [property: JsonPropertyName("is_locked")] bool IsLocked,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange);

public sealed record KnowledgeArticleDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("category_name")] string? CategoryName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("version_label")] string VersionLabel,
    [property: JsonPropertyName("current_published_version_id")] string? CurrentPublishedVersionId,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("author_user_id")] string AuthorUserId,
    [property: JsonPropertyName("last_edited_by")] string? LastEditedBy,
    [property: JsonPropertyName("approved_by")] string? ApprovedBy,
    [property: JsonPropertyName("body_markdown")] string? BodyMarkdown,
    [property: JsonPropertyName("body_html")] string? BodyHtml,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record ContextualHelpMappingDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("route_pattern")] string RoutePattern,
    [property: JsonPropertyName("persona")] string? Persona,
    [property: JsonPropertyName("article_id")] string ArticleId,
    [property: JsonPropertyName("section_anchor")] string? SectionAnchor,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("is_active")] bool IsActive);

public sealed record ContextualHelpResponseDto(
    [property: JsonPropertyName("matched_mapping")] ContextualHelpMappingDto? MatchedMapping,
    [property: JsonPropertyName("primary_article")] KnowledgeArticleDto? PrimaryArticle,
    [property: JsonPropertyName("primary_version")] KnowledgeArticleVersionDto? PrimaryVersion,
    [property: JsonPropertyName("section_anchor")] string? SectionAnchor,
    [property: JsonPropertyName("related_articles")] IReadOnlyList<KnowledgeArticleDto> RelatedArticles);

public sealed record KnowledgeArticleAuditEntryDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("article_id")] string ArticleId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("actor_user_id")] string ActorUserId,
    [property: JsonPropertyName("target_status")] string? TargetStatus,
    [property: JsonPropertyName("version_label")] string? VersionLabel,
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("sha256_hash")] string Sha256Hash,
    [property: JsonPropertyName("predecessor_hash")] string PredecessorHash,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

/// <summary>
/// Minimal API endpoints for contextual SOP resolution, published SOP search, article retrieval,
/// version snapshot history, 21 CFR Part 11 audit trail inspection, and category taxonomy
/// (PRD-KNB-001, PRD-KNB-002).
/// @req:PRD-KNB-001
/// @req:PRD-KNB-002
/// </summary>
public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/knowledge")
            .WithTags("Knowledge")
            .RequireTenantScope();

        group.MapGet("/contextual-help", ResolveContextualHelpAsync)
            .WithName("ResolveContextualHelp")
            .Produces<ContextualHelpResponseDto>(StatusCodes.Status200OK)
            .RequirePermission("knowledge:read");

        group.MapGet("/categories", ListCategoriesAsync)
            .WithName("ListKnowledgeCategories")
            .Produces<IReadOnlyList<KnowledgeCategoryDto>>(StatusCodes.Status200OK)
            .RequirePermission("knowledge:read");

        group.MapGet("/articles", ListArticlesAsync)
            .WithName("ListKnowledgeArticles")
            .Produces<IReadOnlyList<KnowledgeArticleDto>>(StatusCodes.Status200OK)
            .RequirePermission("knowledge:read");

        group.MapGet("/articles/{article_id}", GetArticleAsync)
            .WithName("GetKnowledgeArticle")
            .Produces<KnowledgeArticleDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("knowledge:read");

        group.MapGet("/articles/{article_id}/versions", ListArticleVersionsAsync)
            .WithName("ListKnowledgeArticleVersions")
            .Produces<IReadOnlyList<KnowledgeArticleVersionDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("knowledge:read");

        group.MapGet("/articles/{article_id}/audit-log", GetArticleAuditLogAsync)
            .WithName("GetKnowledgeArticleAuditLog")
            .Produces<IReadOnlyList<KnowledgeArticleAuditEntryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("knowledge:read");

        return app;
    }

    private static async Task<IResult> ResolveContextualHelpAsync(
        [FromQuery(Name = "route")] string? route,
        [FromQuery(Name = "persona")] string? persona,
        HttpContext httpContext,
        KnowledgeDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        string effectivePersona = ResolveEffectivePersona(persona, httpContext);
        var categories = await db.KnowledgeCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var visibleCategoriesById = FilterCategoriesByPersona(categories, effectivePersona)
            .ToDictionary(c => c.Id, StringComparer.Ordinal);
        if (visibleCategoriesById.Count == 0)
        {
            return TypedResults.Ok(new ContextualHelpResponseDto(
                MatchedMapping: null,
                PrimaryArticle: null,
                PrimaryVersion: null,
                SectionAnchor: null,
                RelatedArticles: []));
        }

        var visibleCategoryIds = visibleCategoriesById.Keys.ToList();
        var publishedArticles = await db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.Status == "PUBLISHED" && visibleCategoryIds.Contains(a.CategoryId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var allowedArticlesById = publishedArticles
            .ToDictionary(a => a.Id, StringComparer.Ordinal);

        var activeMappings = await db.ContextualHelpMappings
            .AsNoTracking()
            .Where(m => m.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var resolution = ContextualHelpScorer.Resolve(
            route,
            effectivePersona,
            activeMappings,
            allowedArticlesById);

        if (resolution.PrimaryArticle is null || resolution.MatchedMapping is null)
        {
            return TypedResults.Ok(new ContextualHelpResponseDto(
                MatchedMapping: null,
                PrimaryArticle: null,
                PrimaryVersion: null,
                SectionAnchor: null,
                RelatedArticles: []));
        }

        KnowledgeArticleVersion? primaryVersion = null;
        if (!string.IsNullOrWhiteSpace(resolution.PrimaryArticle.CurrentPublishedVersionId))
        {
            string primaryVersionId = resolution.PrimaryArticle.CurrentPublishedVersionId;
            primaryVersion = await db.KnowledgeArticleVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == primaryVersionId, ct)
                .ConfigureAwait(false);
        }

        var relatedDtos = resolution.RelatedArticles
            .Select(rel =>
            {
                visibleCategoriesById.TryGetValue(rel.CategoryId, out var relCat);
                return ToArticleSummaryDto(rel, relCat?.Name, rel.VersionIndex, rel.VersionLabel, rel.CreatedAt);
            })
            .ToList();

        visibleCategoriesById.TryGetValue(resolution.PrimaryArticle.CategoryId, out var primaryCategory);
        return TypedResults.Ok(new ContextualHelpResponseDto(
            MatchedMapping: new ContextualHelpMappingDto(
                resolution.MatchedMapping.Id,
                resolution.MatchedMapping.RoutePattern,
                resolution.MatchedMapping.Persona,
                resolution.MatchedMapping.ArticleId,
                resolution.MatchedMapping.SectionAnchor,
                resolution.MatchedMapping.Priority,
                resolution.MatchedMapping.IsActive),
            PrimaryArticle: ToArticleDto(resolution.PrimaryArticle, primaryCategory?.Name, primaryVersion),
            PrimaryVersion: primaryVersion is null ? null : ToVersionDto(primaryVersion),
            SectionAnchor: resolution.SectionAnchor,
            RelatedArticles: relatedDtos));
    }

    private static async Task<IResult> ListCategoriesAsync(
        [FromQuery(Name = "persona")] string? persona,
        HttpContext httpContext,
        KnowledgeDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        string effectivePersona = ResolveEffectivePersona(persona, httpContext);
        var categories = await db.KnowledgeCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var visible = FilterCategoriesByPersona(categories, effectivePersona)
            .Select(c => new KnowledgeCategoryDto(
                c.Id,
                c.Name,
                c.Slug,
                c.Description,
                c.ParentId,
                ParseJsonArray(c.PersonaVisibility),
                c.IsDeleted,
                c.CreatedAt,
                c.CreatedBy))
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KnowledgeCategoryDto>>(visible);
    }

    private static async Task<IResult> ListArticlesAsync(
        [FromQuery(Name = "status_filter")] string? statusFilter,
        [FromQuery(Name = "category_id")] string? categoryId,
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "persona")] string? persona,
        HttpContext httpContext,
        KnowledgeDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        string effectivePersona = ResolveEffectivePersona(persona, httpContext);
        var categories = await db.KnowledgeCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var visibleCategoriesById = FilterCategoriesByPersona(categories, effectivePersona)
            .ToDictionary(c => c.Id, StringComparer.Ordinal);
        if (visibleCategoriesById.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<KnowledgeArticleDto>>([]);
        }

        var visibleCategoryIds = visibleCategoriesById.Keys.ToList();
        IQueryable<KnowledgeArticle> articlesQuery = db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => !a.IsDeleted && visibleCategoryIds.Contains(a.CategoryId));

        string normalizedStatus = string.IsNullOrWhiteSpace(statusFilter)
            ? "PUBLISHED"
            : statusFilter.Trim().ToUpperInvariant();

        if (normalizedStatus != "ALL")
        {
            articlesQuery = articlesQuery.Where(a => a.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            string trimmedCat = categoryId.Trim();
            articlesQuery = articlesQuery.Where(a => a.CategoryId == trimmedCat);
        }

        string trimmedSearch = (query ?? string.Empty).Trim();
        string lowerSearch = trimmedSearch.ToLowerInvariant();

        var joinedQuery =
            from a in articlesQuery
            join pv in db.KnowledgeArticleVersions.AsNoTracking()
                on a.CurrentPublishedVersionId equals pv.Id into pvGroup
            from pv in pvGroup.DefaultIfEmpty()
            select new { a, pv };

        if (lowerSearch.Length > 0)
        {
            joinedQuery = joinedQuery.Where(x =>
                x.a.Title.ToLower().Contains(lowerSearch)
                || x.a.Slug.ToLower().Contains(lowerSearch)
                || (x.a.Tags != null && x.a.Tags.ToLower().Contains(lowerSearch))
                || (x.a.CurrentPublishedVersionId != null
                    ? x.pv != null && (
                        (x.pv.BodyMarkdown != null && x.pv.BodyMarkdown.ToLower().Contains(lowerSearch))
                        || (x.pv.BodyHtml != null && x.pv.BodyHtml.ToLower().Contains(lowerSearch)))
                    : db.KnowledgeArticleVersions
                        .AsNoTracking()
                        .Where(dv => dv.ArticleId == x.a.Id)
                        .OrderByDescending(dv => dv.VersionIndex)
                        .Take(1)
                        .Any(dv =>
                            (dv.BodyMarkdown != null && dv.BodyMarkdown.ToLower().Contains(lowerSearch))
                            || (dv.BodyHtml != null && dv.BodyHtml.ToLower().Contains(lowerSearch)))));
        }

        var rows = await joinedQuery
            .Select(x => new
            {
                Article = x.a,
                PublishedVersionIndex = (int?)x.pv.VersionIndex,
                PublishedVersionLabel = x.pv.VersionLabel,
                PublishedCreatedAt = (DateTimeOffset?)x.pv.CreatedAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var unpublishedArticleIds = rows
            .Where(r => r.PublishedVersionIndex is null)
            .Select(r => r.Article.Id)
            .ToList();

        Dictionary<string, (int VersionIndex, string VersionLabel, DateTimeOffset CreatedAt)> draftHeadersByArticleId =
            new(StringComparer.Ordinal);

        if (unpublishedArticleIds.Count > 0)
        {
            var latestDraftHeaders = await db.KnowledgeArticleVersions
                .AsNoTracking()
                .Where(v =>
                    unpublishedArticleIds.Contains(v.ArticleId)
                    && v.VersionIndex == db.KnowledgeArticleVersions
                        .AsNoTracking()
                        .Where(v2 => v2.ArticleId == v.ArticleId)
                        .Max(v2 => (int?)v2.VersionIndex))
                .Select(v => new
                {
                    v.ArticleId,
                    v.VersionIndex,
                    v.VersionLabel,
                    v.CreatedAt,
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var draft in latestDraftHeaders)
            {
                draftHeadersByArticleId[draft.ArticleId] = (draft.VersionIndex, draft.VersionLabel, draft.CreatedAt);
            }
        }

        var dtos = rows
            .OrderByDescending(r => r.Article.CreatedAt)
            .ThenBy(r => r.Article.Title, StringComparer.Ordinal)
            .Select(r =>
            {
                int versionIndex = r.PublishedVersionIndex ?? r.Article.VersionIndex;
                string versionLabel = r.PublishedVersionLabel ?? r.Article.VersionLabel;
                DateTimeOffset updatedAt = r.PublishedCreatedAt ?? r.Article.CreatedAt;

                if (r.PublishedVersionIndex is null
                    && draftHeadersByArticleId.TryGetValue(r.Article.Id, out var draftMeta))
                {
                    versionIndex = draftMeta.VersionIndex;
                    versionLabel = draftMeta.VersionLabel;
                    updatedAt = draftMeta.CreatedAt;
                }

                visibleCategoriesById.TryGetValue(r.Article.CategoryId, out var cat);
                return ToArticleSummaryDto(r.Article, cat?.Name, versionIndex, versionLabel, updatedAt);
            })
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KnowledgeArticleDto>>(dtos);
    }

    private static async Task<IResult> GetArticleAsync(
        string article_id,
        HttpContext httpContext,
        KnowledgeDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        string key = article_id.Trim();
        var article = await db.KnowledgeArticles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => !a.IsDeleted && (a.Id == key || a.Slug == key), ct)
            .ConfigureAwait(false);

        if (article is null)
        {
            return Results.Problem(
                title: "Knowledge article not found",
                detail: $"No published or active knowledge article matched '{key}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        string effectivePersona = ResolveEffectivePersona(null, httpContext);
        var category = await db.KnowledgeCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == article.CategoryId, ct)
            .ConfigureAwait(false);

        if (category is not null && FilterCategoriesByPersona([category], effectivePersona).Count == 0)
        {
            return Results.Problem(
                title: "Knowledge article not found",
                detail: $"No published or active knowledge article matched '{key}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        KnowledgeArticleVersion? version = null;
        if (!string.IsNullOrWhiteSpace(article.CurrentPublishedVersionId))
        {
            version = await db.KnowledgeArticleVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == article.CurrentPublishedVersionId, ct)
                .ConfigureAwait(false);
        }

        version ??= await db.KnowledgeArticleVersions
            .AsNoTracking()
            .Where(v => v.ArticleId == article.Id)
            .OrderByDescending(v => v.VersionIndex)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToArticleDto(article, category?.Name, version));
    }

    private static async Task<IResult> ListArticleVersionsAsync(
        string article_id,
        HttpContext httpContext,
        KnowledgeDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        var (article, notFoundResult) = await LoadVisibleArticleAsync(article_id, httpContext, db, ct).ConfigureAwait(false);
        if (notFoundResult is not null || article is null)
        {
            return notFoundResult!;
        }

        var rawVersions = await db.KnowledgeArticleVersions
            .AsNoTracking()
            .Where(v => v.ArticleId == article.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var versions = rawVersions
            .OrderByDescending(v => v.VersionIndex)
            .ThenByDescending(v => v.CreatedAt)
            .Select(ToVersionDto)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KnowledgeArticleVersionDto>>(versions);
    }

    private static async Task<IResult> GetArticleAuditLogAsync(
        string article_id,
        HttpContext httpContext,
        KnowledgeDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        var (article, notFoundResult) = await LoadVisibleArticleAsync(article_id, httpContext, db, ct).ConfigureAwait(false);
        if (notFoundResult is not null || article is null)
        {
            return notFoundResult!;
        }

        var versionIds = await db.KnowledgeArticleVersions
            .AsNoTracking()
            .Where(v => v.ArticleId == article.Id)
            .Select(v => v.Id)
            .ToHashSetAsync(StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var rawEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e =>
                (e.EntityName == nameof(KnowledgeArticle) && e.EntityId == article.Id)
                || (e.EntityName == nameof(KnowledgeArticleVersion) && versionIds.Contains(e.EntityId)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var auditDtos = rawEntries
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.VersionIndex)
            .Select(e => ToArticleAuditDto(article, e))
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KnowledgeArticleAuditEntryDto>>(auditDtos);
    }

    private static async Task<(KnowledgeArticle? Article, IResult? NotFoundResult)> LoadVisibleArticleAsync(
        string rawArticleId,
        HttpContext httpContext,
        KnowledgeDbContext db,
        CancellationToken ct)
    {
        string key = (rawArticleId ?? string.Empty).Trim();
        var article = await db.KnowledgeArticles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => !a.IsDeleted && (a.Id == key || a.Slug == key), ct)
            .ConfigureAwait(false);

        if (article is null)
        {
            return (null, Results.Problem(
                title: "Knowledge article not found",
                detail: $"No published or active knowledge article matched '{key}'.",
                statusCode: StatusCodes.Status404NotFound));
        }

        string effectivePersona = ResolveEffectivePersona(null, httpContext);
        var category = await db.KnowledgeCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == article.CategoryId, ct)
            .ConfigureAwait(false);

        if (category is not null && FilterCategoriesByPersona([category], effectivePersona).Count == 0)
        {
            return (null, Results.Problem(
                title: "Knowledge article not found",
                detail: $"No published or active knowledge article matched '{key}'.",
                statusCode: StatusCodes.Status404NotFound));
        }

        return (article, null);
    }

    private static KnowledgeArticleAuditEntryDto ToArticleAuditDto(KnowledgeArticle article, AuditLogEntry entry)
    {
        string? targetStatus = null;
        string? versionLabel = null;

        if (!string.IsNullOrWhiteSpace(entry.ChangesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(entry.ChangesJson);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
                    {
                        targetStatus = statusEl.GetString();
                    }
                    else if (root.TryGetProperty("Status", out var statusPropEl) && statusPropEl.ValueKind == JsonValueKind.String)
                    {
                        targetStatus = statusPropEl.GetString();
                    }
                    else if (root.TryGetProperty("StatusAtSnapshot", out var snapshotEl) && snapshotEl.ValueKind == JsonValueKind.String)
                    {
                        targetStatus = snapshotEl.GetString();
                    }

                    if (root.TryGetProperty("version_label", out var verEl) && verEl.ValueKind == JsonValueKind.String)
                    {
                        versionLabel = verEl.GetString();
                    }
                    else if (root.TryGetProperty("VersionLabel", out var verPropEl) && verPropEl.ValueKind == JsonValueKind.String)
                    {
                        versionLabel = verPropEl.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore non-object JSON payloads and fall back to article metadata.
            }
        }

        return new KnowledgeArticleAuditEntryDto(
            Id: entry.Id.ToString("D"),
            ArticleId: article.Id,
            EventType: entry.Action,
            ActorUserId: entry.UserId,
            TargetStatus: targetStatus ?? article.Status,
            VersionLabel: versionLabel ?? article.VersionLabel,
            VersionIndex: entry.VersionIndex,
            ReasonForChange: entry.ReasonForChange,
            Sha256Hash: $"sha256:{entry.BlockHash}",
            PredecessorHash: entry.PredecessorHash,
            CreatedAt: new DateTimeOffset(AuditLogEntry.NormalizeTimestamp(entry.Timestamp), TimeSpan.Zero));
    }

    private static string ResolveEffectivePersona(string? queryPersona, HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(queryPersona))
        {
            return ContextualHelpScorer.NormalizePersona(queryPersona);
        }

        var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (principal is not null)
        {
            foreach (string rawRole in principal.Roles)
            {
                if (!string.IsNullOrWhiteSpace(rawRole))
                {
                    return ContextualHelpScorer.NormalizePersona(rawRole);
                }
            }
        }

        return ContextualHelpScorer.NormalizePersona(null);
    }

    private static List<KnowledgeCategory> FilterCategoriesByPersona(
        IEnumerable<KnowledgeCategory> categories,
        string normalizedPersona)
    {
        if (string.Equals(normalizedPersona, "super_admin", StringComparison.OrdinalIgnoreCase))
        {
            return categories.ToList();
        }

        var result = new List<KnowledgeCategory>();
        foreach (var category in categories)
        {
            var allowed = ParseJsonArray(category.PersonaVisibility);
            if (allowed.Count == 0
                || allowed.Any(p => p == "*" || string.Equals(ContextualHelpScorer.NormalizePersona(p), normalizedPersona, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(category);
            }
        }

        return result;
    }

    private static KnowledgeArticleDto ToArticleSummaryDto(
        KnowledgeArticle article,
        string? categoryName,
        int versionIndex,
        string versionLabel,
        DateTimeOffset updatedAt)
    {
        return new(
            Id: article.Id,
            Slug: article.Slug,
            Title: article.Title,
            CategoryId: article.CategoryId,
            CategoryName: categoryName,
            Status: article.Status,
            VersionIndex: versionIndex,
            VersionLabel: versionLabel,
            CurrentPublishedVersionId: article.CurrentPublishedVersionId,
            Tags: ParseJsonArray(article.Tags),
            AuthorUserId: article.AuthorUserId,
            LastEditedBy: article.LastEditedBy,
            ApprovedBy: article.ApprovedBy,
            BodyMarkdown: null,
            BodyHtml: null,
            CreatedAt: article.CreatedAt,
            UpdatedAt: updatedAt);
    }

    private static KnowledgeArticleDto ToArticleDto(
        KnowledgeArticle article,
        string? categoryName,
        KnowledgeArticleVersion? version)
    {
        string? compiledHtml = SopHtmlCompiler.EnsureCompiledHtml(version?.BodyMarkdown, version?.BodyHtml);
        return new(
            Id: article.Id,
            Slug: article.Slug,
            Title: article.Title,
            CategoryId: article.CategoryId,
            CategoryName: categoryName,
            Status: article.Status,
            VersionIndex: version?.VersionIndex ?? article.VersionIndex,
            VersionLabel: version?.VersionLabel ?? article.VersionLabel,
            CurrentPublishedVersionId: article.CurrentPublishedVersionId,
            Tags: ParseJsonArray(article.Tags),
            AuthorUserId: article.AuthorUserId,
            LastEditedBy: article.LastEditedBy,
            ApprovedBy: article.ApprovedBy,
            BodyMarkdown: version?.BodyMarkdown,
            BodyHtml: compiledHtml,
            CreatedAt: article.CreatedAt,
            UpdatedAt: version?.CreatedAt ?? article.CreatedAt);
    }

    private static KnowledgeArticleVersionDto ToVersionDto(KnowledgeArticleVersion version)
    {
        string? compiledHtml = SopHtmlCompiler.EnsureCompiledHtml(version.BodyMarkdown, version.BodyHtml);
        return new(
            Id: version.Id,
            ArticleId: version.ArticleId,
            VersionIndex: version.VersionIndex,
            VersionLabel: version.VersionLabel,
            BodyMarkdown: version.BodyMarkdown,
            BodyHtml: compiledHtml,
            StatusAtSnapshot: version.StatusAtSnapshot,
            IsLocked: version.IsLocked,
            CreatedAt: version.CreatedAt,
            CreatedBy: version.CreatedBy,
            ReasonForChange: version.ReasonForChange);
    }

    private static IReadOnlyList<string> ParseJsonArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        string trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Fall through to comma-separated parsing.
            }
        }

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
