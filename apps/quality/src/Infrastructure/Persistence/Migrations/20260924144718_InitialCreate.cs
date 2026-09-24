using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Quality.Infrastructure.Persistence.Migrations;

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
            name: "quality_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UserRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                RecordId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ChangeReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "quality_deviations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IsProtocolViolation = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_deviations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "quality_root_cause_analyses",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DeviationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Methodology = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                InvestigationDetails = table.Column<string>(type: "text", nullable: false),
                RootCauseSummary = table.Column<string>(type: "text", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_root_cause_analyses", x => x.Id);
                table.ForeignKey(
                    name: "FK_quality_root_cause_analyses_quality_deviations_DeviationId",
                    column: x => x.DeviationId,
                    principalTable: "quality_deviations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "quality_capa_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DeviationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RcaId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                CapaType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ActionPlan = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PreventiveMeasures = table.Column<string>(type: "text", nullable: true),
                TargetCompletionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_capa_records", x => x.Id);
                table.ForeignKey(
                    name: "FK_quality_capa_records_quality_deviations_DeviationId",
                    column: x => x.DeviationId,
                    principalTable: "quality_deviations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_quality_capa_records_quality_root_cause_analyses_RcaId",
                    column: x => x.RcaId,
                    principalTable: "quality_root_cause_analyses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_quality_audit_logs_Timestamp",
            table: "quality_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_quality_audit_logs_UserId",
            table: "quality_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_quality_capa_records_DeviationId",
            table: "quality_capa_records",
            column: "DeviationId");

        migrationBuilder.CreateIndex(
            name: "IX_quality_capa_records_RcaId",
            table: "quality_capa_records",
            column: "RcaId");

        migrationBuilder.CreateIndex(
            name: "IX_quality_capa_records_SiteId",
            table: "quality_capa_records",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_quality_capa_records_StudyId",
            table: "quality_capa_records",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_quality_deviations_SiteId",
            table: "quality_deviations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_quality_deviations_StudyId",
            table: "quality_deviations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_quality_root_cause_analyses_DeviationId",
            table: "quality_root_cause_analyses",
            column: "DeviationId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_quality_root_cause_analyses_SiteId",
            table: "quality_root_cause_analyses",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_quality_root_cause_analyses_StudyId",
            table: "quality_root_cause_analyses",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "quality_audit_logs");

        migrationBuilder.DropTable(
            name: "quality_capa_records");

        migrationBuilder.DropTable(
            name: "quality_root_cause_analyses");

        migrationBuilder.DropTable(
            name: "quality_deviations");
    }
}
