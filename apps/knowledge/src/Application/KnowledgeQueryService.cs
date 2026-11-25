using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Knowledge.Application;

public interface IKnowledgeQueryService
{
    Task<IResult> ResolveContextualHelpAsync(
        string? route,
        string? persona,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> ListCategoriesAsync(
        string? persona,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> ListArticlesAsync(
        string? statusFilter,
        string? categoryId,
        string? query,
        string? persona,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> GetArticleAsync(
        string articleId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> ListArticleVersionsAsync(
        string articleId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> GetArticleAuditLogAsync(
        string articleId,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class KnowledgeQueryService(
    KnowledgeDbContext db,
    IClock clock) : IKnowledgeQueryService
{
    public async Task<IResult> ResolveContextualHelpAsync(
        string? route,
        string? persona,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        string effectivePersona = KnowledgeDtoMapper.ResolveEffectivePersona(persona, httpContext);
        var categories = await db.KnowledgeCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var visibleCategoriesById = KnowledgeDtoMapper.FilterCategoriesByPersona(categories, effectivePersona)
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
                return KnowledgeDtoMapper.ToArticleSummaryDto(rel, relCat?.Name, rel.VersionIndex, rel.VersionLabel, rel.CreatedAt);
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
            PrimaryArticle: KnowledgeDtoMapper.ToArticleDto(resolution.PrimaryArticle, primaryCategory?.Name, primaryVersion),
            PrimaryVersion: primaryVersion is null ? null : KnowledgeDtoMapper.ToVersionDto(primaryVersion),
            SectionAnchor: resolution.SectionAnchor,
            RelatedArticles: relatedDtos));
    }

    public async Task<IResult> ListCategoriesAsync(
        string? persona,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        string effectivePersona = KnowledgeDtoMapper.ResolveEffectivePersona(persona, httpContext);
        var categories = await db.KnowledgeCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var visible = KnowledgeDtoMapper.FilterCategoriesByPersona(categories, effectivePersona)
            .Select(c => new KnowledgeCategoryDto(
                c.Id,
                c.Name,
                c.Slug,
                c.Description,
                c.ParentId,
                KnowledgeDtoMapper.ParseJsonArray(c.PersonaVisibility),
                c.IsDeleted,
                c.CreatedAt,
                c.CreatedBy))
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KnowledgeCategoryDto>>(visible);
    }

    public async Task<IResult> ListArticlesAsync(
        string? statusFilter,
        string? categoryId,
        string? query,
        string? persona,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        string effectivePersona = KnowledgeDtoMapper.ResolveEffectivePersona(persona, httpContext);
        var categories = await db.KnowledgeCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var visibleCategoriesById = KnowledgeDtoMapper.FilterCategoriesByPersona(categories, effectivePersona)
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
                return KnowledgeDtoMapper.ToArticleSummaryDto(r.Article, cat?.Name, versionIndex, versionLabel, updatedAt);
            })
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KnowledgeArticleDto>>(dtos);
    }

    public async Task<IResult> GetArticleAsync(
        string articleId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        string key = articleId.Trim();
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

        string effectivePersona = KnowledgeDtoMapper.ResolveEffectivePersona(null, httpContext);
        var category = await db.KnowledgeCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == article.CategoryId, ct)
            .ConfigureAwait(false);

        if (category is not null && KnowledgeDtoMapper.FilterCategoriesByPersona([category], effectivePersona).Count == 0)
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

        return TypedResults.Ok(KnowledgeDtoMapper.ToArticleDto(article, category?.Name, version));
    }

    public async Task<IResult> ListArticleVersionsAsync(
        string articleId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        var (article, notFoundResult) = await LoadVisibleArticleAsync(articleId, httpContext, ct).ConfigureAwait(false);
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
            .Select(KnowledgeDtoMapper.ToVersionDto)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KnowledgeArticleVersionDto>>(versions);
    }

    public async Task<IResult> GetArticleAuditLogAsync(
        string articleId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock, ct).ConfigureAwait(false);

        var (article, notFoundResult) = await LoadVisibleArticleAsync(articleId, httpContext, ct).ConfigureAwait(false);
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
            .Select(e => KnowledgeDtoMapper.ToArticleAuditDto(article, e))
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KnowledgeArticleAuditEntryDto>>(auditDtos);
    }

    private async Task<(KnowledgeArticle? Article, IResult? NotFoundResult)> LoadVisibleArticleAsync(
        string rawArticleId,
        HttpContext httpContext,
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

        string effectivePersona = KnowledgeDtoMapper.ResolveEffectivePersona(null, httpContext);
        var category = await db.KnowledgeCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == article.CategoryId, ct)
            .ConfigureAwait(false);

        if (category is not null && KnowledgeDtoMapper.FilterCategoriesByPersona([category], effectivePersona).Count == 0)
        {
            return (null, Results.Problem(
                title: "Knowledge article not found",
                detail: $"No published or active knowledge article matched '{key}'.",
                statusCode: StatusCodes.Status404NotFound));
        }

        return (article, null);
    }
}
