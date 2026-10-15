using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBulkLabRangeImportStore : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "bulk_lab_range_import_jobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                FileType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TotalRows = table.Column<int>(type: "integer", nullable: false),
                SuccessRows = table.Column<int>(type: "integer", nullable: false),
                ErrorRows = table.Column<int>(type: "integer", nullable: false),
                ErrorSummaryJson = table.Column<string>(type: "text", nullable: true),
                PreviewSummaryJson = table.Column<string>(type: "text", nullable: true),
                SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bulk_lab_range_import_jobs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "lab_reference_ranges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TestName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Unit = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                NormalizedUnit = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SexApplicability = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AgeLow = table.Column<double>(type: "double precision", nullable: true),
                AgeHigh = table.Column<double>(type: "double precision", nullable: true),
                LowBound = table.Column<double>(type: "double precision", nullable: true),
                HighBound = table.Column<double>(type: "double precision", nullable: true),
                CriticalLow = table.Column<double>(type: "double precision", nullable: true),
                CriticalHigh = table.Column<double>(type: "double precision", nullable: true),
                LoincCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_lab_reference_ranges", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_bulk_lab_range_import_jobs_JobId",
            table: "bulk_lab_range_import_jobs",
            column: "JobId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_bulk_lab_range_import_jobs_StudyId_SubmittedAt",
            table: "bulk_lab_range_import_jobs",
            columns: new[] { "StudyId", "SubmittedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_lab_reference_ranges_StudyId_IsActive",
            table: "lab_reference_ranges",
            columns: new[] { "StudyId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_lab_reference_ranges_StudyId_TestCode_Source_SiteId_IsActive",
            table: "lab_reference_ranges",
            columns: new[] { "StudyId", "TestCode", "Source", "SiteId", "IsActive" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "bulk_lab_range_import_jobs");

        migrationBuilder.DropTable(
            name: "lab_reference_ranges");
    }
}
