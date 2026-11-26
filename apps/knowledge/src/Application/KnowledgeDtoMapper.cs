using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Core.Domain.Audit;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Knowledge.Application;

internal static class KnowledgeDtoMapper
{
    public static KnowledgeArticleAuditEntryDto ToArticleAuditDto(KnowledgeArticle article, AuditLogEntry entry)
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

    public static string ResolveEffectivePersona(string? queryPersona, HttpContext httpContext)
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

    public static List<KnowledgeCategory> FilterCategoriesByPersona(
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

    public static KnowledgeArticleDto ToArticleSummaryDto(
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

    public static KnowledgeArticleDto ToArticleDto(
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

    public static KnowledgeArticleVersionDto ToVersionDto(KnowledgeArticleVersion version)
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

    public static IReadOnlyList<string> ParseJsonArray(string? raw)
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
