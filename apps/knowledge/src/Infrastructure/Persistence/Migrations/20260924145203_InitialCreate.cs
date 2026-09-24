using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS audit_log_entries (
                    ""Id"" uuid NOT NULL,
                    ""EntityName"" character varying(255) NOT NULL,
                    ""EntityId"" character varying(255) NOT NULL,
                    ""Action"" character varying(50) NOT NULL,
                    ""Timestamp"" timestamp with time zone NOT NULL,
                    ""UserId"" character varying(255) NOT NULL,
                    ""ReasonForChange"" character varying(1000) NOT NULL,
                    ""VersionIndex"" bigint NOT NULL,
                    ""ChangesJson"" text,
                    ""PredecessorHash"" character varying(64) NOT NULL,
                    ""BlockHash"" character varying(64) NOT NULL,
                    CONSTRAINT ""PK_audit_log_entries"" PRIMARY KEY (""Id"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_audit_log_entries_EntityName_Timestamp"" ON audit_log_entries (""EntityName"", ""Timestamp"");
            ");

        migrationBuilder.CreateTable(
            name: "knowledge_article_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ArticleId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                PreviousStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                NewStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                PreviousDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Sha256Digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_knowledge_article_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "knowledge_article_versions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ArticleId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                VersionLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StatusAtSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                BodyHtml = table.Column<string>(type: "text", nullable: true),
                IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_knowledge_article_versions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "knowledge_articles",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CategoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                VersionLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CurrentPublishedVersionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                Tags = table.Column<string>(type: "text", nullable: true),
                AuthorUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                LastEditedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_knowledge_articles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "knowledge_categories",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                ParentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                PersonaVisibility = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_knowledge_categories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "knowledge_contextual_help_mappings",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RoutePattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Persona = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ArticleId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SectionAnchor = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Priority = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_knowledge_contextual_help_mappings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "protocol_knowledge_chunks",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProtocolVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DocumentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SectionNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                SectionTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                PageNumber = table.Column<int>(type: "integer", nullable: false),
                ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                ChunkText = table.Column<string>(type: "text", nullable: false),
                EmbeddingJson = table.Column<string>(type: "text", nullable: true),
                TokenCount = table.Column<int>(type: "integer", nullable: false),
                IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_protocol_knowledge_chunks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_audit_logs_Action",
            table: "knowledge_article_audit_logs",
            column: "Action");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_audit_logs_ArticleId",
            table: "knowledge_article_audit_logs",
            column: "ArticleId");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_audit_logs_CreatedAt",
            table: "knowledge_article_audit_logs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_audit_logs_CreatedBy",
            table: "knowledge_article_audit_logs",
            column: "CreatedBy");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_versions_ArticleId",
            table: "knowledge_article_versions",
            column: "ArticleId");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_versions_VersionIndex",
            table: "knowledge_article_versions",
            column: "VersionIndex");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_articles_CategoryId",
            table: "knowledge_articles",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_articles_CurrentPublishedVersionId",
            table: "knowledge_articles",
            column: "CurrentPublishedVersionId");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_articles_Slug",
            table: "knowledge_articles",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_articles_Status",
            table: "knowledge_articles",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_categories_Name",
            table: "knowledge_categories",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_categories_ParentId",
            table: "knowledge_categories",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_categories_Slug",
            table: "knowledge_categories",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_contextual_help_mappings_ArticleId",
            table: "knowledge_contextual_help_mappings",
            column: "ArticleId");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_contextual_help_mappings_Persona",
            table: "knowledge_contextual_help_mappings",
            column: "Persona");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_contextual_help_mappings_RoutePattern",
            table: "knowledge_contextual_help_mappings",
            column: "RoutePattern");

        migrationBuilder.CreateIndex(
            name: "IX_protocol_knowledge_chunks_DocumentId",
            table: "protocol_knowledge_chunks",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_protocol_knowledge_chunks_PageNumber",
            table: "protocol_knowledge_chunks",
            column: "PageNumber");

        migrationBuilder.CreateIndex(
            name: "IX_protocol_knowledge_chunks_ProtocolVersion",
            table: "protocol_knowledge_chunks",
            column: "ProtocolVersion");

        migrationBuilder.CreateIndex(
            name: "IX_protocol_knowledge_chunks_StudyId",
            table: "protocol_knowledge_chunks",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "knowledge_article_audit_logs");

        migrationBuilder.DropTable(
            name: "knowledge_article_versions");

        migrationBuilder.DropTable(
            name: "knowledge_articles");

        migrationBuilder.DropTable(
            name: "knowledge_categories");

        migrationBuilder.DropTable(
            name: "knowledge_contextual_help_mappings");

        migrationBuilder.DropTable(
            name: "protocol_knowledge_chunks");
    }
}
