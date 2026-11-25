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

public sealed partial class KnowledgeQueryApplicationService(KnowledgeDbContext db, IClock clock)
{
    public async Task<IResult> ResolveContextualHelpAsync(
        string? route,
        string? persona,
        HttpContext httpContext,
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

    public async Task<IResult> ListCategoriesAsync(
        string? persona,
        HttpContext httpContext,
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

    public async Task<IResult> ListArticlesAsync(
        string? statusFilter,
        string? categoryId,
        string? query,
        string? persona,
        HttpContext httpContext,
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
}
