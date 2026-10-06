using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBulkSynonymImportStore : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "bulk_synonym_import_jobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                FileHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                TotalRows = table.Column<int>(type: "integer", nullable: false),
                ValidRows = table.Column<int>(type: "integer", nullable: false),
                DuplicateRows = table.Column<int>(type: "integer", nullable: false),
                InvalidRows = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bulk_synonym_import_jobs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "synonym_rules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                VerbatimText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                DictionaryType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DictionaryVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TargetCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TargetTerm = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                TargetLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_synonym_rules", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_bulk_synonym_import_jobs_CreatedAt",
            table: "bulk_synonym_import_jobs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_synonym_rules_Scope_StudyId_Status",
            table: "synonym_rules",
            columns: new[] { "Scope", "StudyId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_synonym_rules_VerbatimText_DictionaryType",
            table: "synonym_rules",
            columns: new[] { "VerbatimText", "DictionaryType" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "bulk_synonym_import_jobs");

        migrationBuilder.DropTable(
            name: "synonym_rules");
    }
}
