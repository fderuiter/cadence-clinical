using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Eisf.Infrastructure.Persistence.Migrations;

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
            name: "eisf_document_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SectionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Sha256Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                VersionMajor = table.Column<int>(type: "integer", nullable: false),
                VersionMinor = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_eisf_document_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "eisf_section_taxonomies",
            columns: table => new
            {
                SectionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SectionNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                IsMandatory = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_eisf_section_taxonomies", x => x.SectionCode);
            });

        migrationBuilder.CreateTable(
            name: "isf_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ActorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ActorRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                TenantId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PreviousDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Sha256Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_isf_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "isf_documents",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                BinderClassification = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Content = table.Column<string>(type: "text", nullable: true),
                MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                IssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DocumentOwnerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                CorrelationKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ContentChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                SyncStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SourceSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_isf_documents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_eisf_document_records_SectionCode",
            table: "eisf_document_records",
            column: "SectionCode");

        migrationBuilder.CreateIndex(
            name: "IX_eisf_document_records_SiteId",
            table: "eisf_document_records",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_eisf_document_records_StudyId",
            table: "eisf_document_records",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_eisf_section_taxonomies_SectionNumber",
            table: "eisf_section_taxonomies",
            column: "SectionNumber");

        migrationBuilder.CreateIndex(
            name: "IX_isf_audit_logs_ActorId",
            table: "isf_audit_logs",
            column: "ActorId");

        migrationBuilder.CreateIndex(
            name: "IX_isf_audit_logs_DocumentId",
            table: "isf_audit_logs",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_isf_audit_logs_Timestamp",
            table: "isf_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_BinderClassification",
            table: "isf_documents",
            column: "BinderClassification");

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_CorrelationKey",
            table: "isf_documents",
            column: "CorrelationKey");

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_DocumentOwnerId",
            table: "isf_documents",
            column: "DocumentOwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_ExpirationDate",
            table: "isf_documents",
            column: "ExpirationDate");

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_IssueDate",
            table: "isf_documents",
            column: "IssueDate");

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_ObjectKey",
            table: "isf_documents",
            column: "ObjectKey");

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_SiteId",
            table: "isf_documents",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_StudyId",
            table: "isf_documents",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "eisf_document_records");

        migrationBuilder.DropTable(
            name: "eisf_section_taxonomies");

        migrationBuilder.DropTable(
            name: "isf_audit_logs");

        migrationBuilder.DropTable(
            name: "isf_documents");
    }
}
