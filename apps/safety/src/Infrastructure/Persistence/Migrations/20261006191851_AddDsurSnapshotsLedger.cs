using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Safety.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDsurSnapshotsLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<long>(
            name: "VersionIndex",
            table: "periodic_safety_reports",
            type: "bigint",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer");

        migrationBuilder.CreateTable(
            name: "dsur_snapshots",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ReportId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ReportType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CompoundName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ReportPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                PayloadDigestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ApprovedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                EsignatureManifestId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dsur_snapshots", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_dsur_snapshots_CompoundName",
            table: "dsur_snapshots",
            column: "CompoundName");

        migrationBuilder.CreateIndex(
            name: "IX_dsur_snapshots_PayloadDigestSha256",
            table: "dsur_snapshots",
            column: "PayloadDigestSha256");

        migrationBuilder.CreateIndex(
            name: "IX_dsur_snapshots_ReportId",
            table: "dsur_snapshots",
            column: "ReportId");

        migrationBuilder.CreateIndex(
            name: "IX_dsur_snapshots_ReportType",
            table: "dsur_snapshots",
            column: "ReportType");

        migrationBuilder.CreateIndex(
            name: "IX_dsur_snapshots_Status",
            table: "dsur_snapshots",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_dsur_snapshots_StudyId",
            table: "dsur_snapshots",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "dsur_snapshots");

        migrationBuilder.AlterColumn<int>(
            name: "VersionIndex",
            table: "periodic_safety_reports",
            type: "integer",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "bigint");
    }
}

