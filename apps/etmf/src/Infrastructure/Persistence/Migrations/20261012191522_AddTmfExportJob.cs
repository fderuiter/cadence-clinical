using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Etmf.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTmfExportJob : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "tmf_export_jobs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                JobId = table.Column<string>(type: "text", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                RequestedBy = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ProgressPercentage = table.Column<double>(type: "double precision", nullable: false),
                TotalDocuments = table.Column<int>(type: "integer", nullable: false),
                ProcessedDocuments = table.Column<int>(type: "integer", nullable: false),
                DownloadUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tmf_export_jobs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tmf_export_jobs_CreatedAt",
            table: "tmf_export_jobs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_export_jobs_Status",
            table: "tmf_export_jobs",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_export_jobs_StudyId",
            table: "tmf_export_jobs",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tmf_export_jobs");
    }
}
