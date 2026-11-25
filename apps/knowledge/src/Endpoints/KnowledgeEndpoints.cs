using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Application;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Knowledge.Endpoints;

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

    private static Task<IResult> ResolveContextualHelpAsync(
        [FromQuery(Name = "route")] string? route,
        [FromQuery(Name = "persona")] string? persona,
        HttpContext httpContext,
        [FromServices] KnowledgeQueryApplicationService service,
        CancellationToken ct) =>
        service.ResolveContextualHelpAsync(route, persona, httpContext, ct);

    private static Task<IResult> ListCategoriesAsync(
        [FromQuery(Name = "persona")] string? persona,
        HttpContext httpContext,
        [FromServices] KnowledgeQueryApplicationService service,
        CancellationToken ct) =>
        service.ListCategoriesAsync(persona, httpContext, ct);

    private static Task<IResult> ListArticlesAsync(
        [FromQuery(Name = "status_filter")] string? statusFilter,
        [FromQuery(Name = "category_id")] string? categoryId,
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "persona")] string? persona,
        HttpContext httpContext,
        [FromServices] KnowledgeQueryApplicationService service,
        CancellationToken ct) =>
        service.ListArticlesAsync(statusFilter, categoryId, query, persona, httpContext, ct);

    private static Task<IResult> GetArticleAsync(
        string article_id,
        HttpContext httpContext,
        [FromServices] KnowledgeQueryApplicationService service,
        CancellationToken ct) =>
        service.GetArticleAsync(article_id, httpContext, ct);

    private static Task<IResult> ListArticleVersionsAsync(
        string article_id,
        HttpContext httpContext,
        [FromServices] KnowledgeQueryApplicationService service,
        CancellationToken ct) =>
        service.ListArticleVersionsAsync(article_id, httpContext, ct);

    private static Task<IResult> GetArticleAuditLogAsync(
        string article_id,
        HttpContext httpContext,
        [FromServices] KnowledgeQueryApplicationService service,
        CancellationToken ct) =>
        service.GetArticleAuditLogAsync(article_id, httpContext, ct);
}
