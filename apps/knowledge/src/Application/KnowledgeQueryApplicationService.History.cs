using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Core.Domain.Audit;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Knowledge.Application;

public sealed partial class KnowledgeQueryApplicationService
{
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
            .Select(ToVersionDto)
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
            .Select(e => ToArticleAuditDto(article, e))
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
