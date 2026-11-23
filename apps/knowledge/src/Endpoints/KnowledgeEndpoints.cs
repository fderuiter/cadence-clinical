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
            .WithSummary("Resolves persona-filtered contextual SOP guidance for the active workspace route.")
            .Produces<ContextualHelpResponseDto>(StatusCodes.Status200OK)
            .RequirePermission("knowledge:read");

        group.MapGet("/categories", ListCategoriesAsync)
            .WithName("ListKnowledgeCategories")
            .WithSummary("Lists knowledge base SOP categories visible to the caller's clinical persona.")
            .Produces<IReadOnlyList<KnowledgeCategoryDto>>(StatusCodes.Status200OK)
            .RequirePermission("knowledge:read");

        group.MapGet("/articles", ListArticlesAsync)
            .WithName("ListKnowledgeArticles")
            .WithSummary("Searches and lists published SOP knowledge base articles.")
            .Produces<IReadOnlyList<KnowledgeArticleDto>>(StatusCodes.Status200OK)
            .RequirePermission("knowledge:read");

        group.MapGet("/articles/{article_id}", GetArticleAsync)
            .WithName("GetKnowledgeArticle")
            .WithSummary("Retrieves a published SOP knowledge base article by article ID.")
            .Produces<KnowledgeArticleDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("knowledge:read");

        group.MapGet("/articles/{article_id}/versions", ListArticleVersionsAsync)
            .WithName("ListKnowledgeArticleVersions")
            .WithSummary("Lists immutable version snapshots for a knowledge base article.")
            .Produces<IReadOnlyList<KnowledgeArticleVersionDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("knowledge:read");

        group.MapGet("/articles/{article_id}/audit-log", GetArticleAuditLogAsync)
            .WithName("GetKnowledgeArticleAuditLog")
            .WithSummary("Retrieves the 21 CFR Part 11 hash-chained audit log for a knowledge base article.")
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

        var publishedArticles = await db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.Status == "PUBLISHED")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var allowedArticlesById = publishedArticles
            .Where(a => visibleCategoriesById.ContainsKey(a.CategoryId))
            .ToDictionary(a => a.Id, StringComparer.Ordinal);

        var versionIds = allowedArticlesById.Values
            .Select(a => a.CurrentPublishedVersionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var versionsById = await db.KnowledgeArticleVersions
            .AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var activeMappings = await db.ContextualHelpMappings
            .AsNoTracking()
            .Where(m => m.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var resolution = ContextualHelpScorer.Resolve(
            route,
            effectivePersona,
            activeMappings,
            allowedArticlesById,
            versionsById);

        if (resolution.PrimaryArticle is null || resolution.MatchedMapping is null)
        {
            return TypedResults.Ok(new ContextualHelpResponseDto(
                MatchedMapping: null,
                PrimaryArticle: null,
                PrimaryVersion: null,
                SectionAnchor: null,
                RelatedArticles: []));
        }

        var relatedDtos = resolution.RelatedArticles
            .Select(rel =>
            {
                KnowledgeArticleVersion? relVer = !string.IsNullOrWhiteSpace(rel.CurrentPublishedVersionId)
                    && versionsById.TryGetValue(rel.CurrentPublishedVersionId, out var found)
                        ? found
                        : null;
                visibleCategoriesById.TryGetValue(rel.CategoryId, out var relCat);
                return ToArticleDto(rel, relCat?.Name, relVer);
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
            PrimaryArticle: ToArticleDto(resolution.PrimaryArticle, primaryCategory?.Name, resolution.PrimaryVersion),
            PrimaryVersion: resolution.PrimaryVersion is null ? null : ToVersionDto(resolution.PrimaryVersion),
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

        IQueryable<KnowledgeArticle> articlesQuery = db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => !a.IsDeleted);

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

        var rawArticles = await articlesQuery
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var articles = rawArticles
            .Where(a => visibleCategoriesById.ContainsKey(a.CategoryId))
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Title, StringComparer.Ordinal)
            .ToList();

        var articleIds = articles.Select(a => a.Id).ToList();
        var versions = await db.KnowledgeArticleVersions
            .AsNoTracking()
            .Where(v => articleIds.Contains(v.ArticleId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var versionByArticleId = ResolveActiveVersions(articles, versions);

        string trimmedSearch = (query ?? string.Empty).Trim();
        var dtos = new List<KnowledgeArticleDto>();
        foreach (var article in articles)
        {
            versionByArticleId.TryGetValue(article.Id, out var version);
            visibleCategoriesById.TryGetValue(article.CategoryId, out var cat);
            var dto = ToArticleDto(article, cat?.Name, version);

            if (trimmedSearch.Length > 0 && !MatchesKeyword(dto, trimmedSearch))
            {
                continue;
            }

            dtos.Add(dto);
        }

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

    private static Dictionary<string, KnowledgeArticleVersion> ResolveActiveVersions(
        IReadOnlyList<KnowledgeArticle> articles,
        IReadOnlyList<KnowledgeArticleVersion> versions)
    {
        var byVersionId = versions.ToDictionary(v => v.Id, StringComparer.Ordinal);
        var byArticleId = versions
            .GroupBy(v => v.ArticleId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.VersionIndex).First(), StringComparer.Ordinal);

        var resolved = new Dictionary<string, KnowledgeArticleVersion>(StringComparer.Ordinal);
        foreach (var article in articles)
        {
            if (!string.IsNullOrWhiteSpace(article.CurrentPublishedVersionId)
                && byVersionId.TryGetValue(article.CurrentPublishedVersionId, out var published))
            {
                resolved[article.Id] = published;
            }
            else if (byArticleId.TryGetValue(article.Id, out var latest))
            {
                resolved[article.Id] = latest;
            }
        }

        return resolved;
    }

    private static bool MatchesKeyword(KnowledgeArticleDto article, string keyword)
    {
        return article.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || article.Slug.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || (article.BodyMarkdown?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
               || (article.BodyHtml?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
               || article.Tags.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static KnowledgeArticleDto ToArticleDto(
        KnowledgeArticle article,
        string? categoryName,
        KnowledgeArticleVersion? version) =>
        new(
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
            BodyHtml: version?.BodyHtml,
            CreatedAt: article.CreatedAt,
            UpdatedAt: version?.CreatedAt ?? article.CreatedAt);

    private static KnowledgeArticleVersionDto ToVersionDto(KnowledgeArticleVersion version) =>
        new(
            Id: version.Id,
            ArticleId: version.ArticleId,
            VersionIndex: version.VersionIndex,
            VersionLabel: version.VersionLabel,
            BodyMarkdown: version.BodyMarkdown,
            BodyHtml: version.BodyHtml,
            StatusAtSnapshot: version.StatusAtSnapshot,
            IsLocked: version.IsLocked,
            CreatedAt: version.CreatedAt,
            CreatedBy: version.CreatedBy,
            ReasonForChange: version.ReasonForChange);

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
