using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
