using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExecutionModelChanges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cec_chair_adjudications",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DossierId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ChairId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ChairName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FinalClassification = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FinalSubtype = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FinalAdjudicatedOnsetDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                BindingReader = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                criteria_confirmed = table.Column<string>(type: "jsonb", nullable: false),
                AdjudicationRationale = table.Column<string>(type: "text", nullable: false),
                signature_manifest = table.Column<string>(type: "jsonb", nullable: false),
                FinalizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cec_chair_adjudications", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "cec_dossiers",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                EventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                MaskedSubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                MaskedSiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CandidateEventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ReportedTerm = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ReportedOnsetDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                WorkflowStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IsDiscordant = table.Column<bool>(type: "boolean", nullable: false),
                discordance_reasons = table.Column<string>(type: "jsonb", nullable: false),
                FinalConsensusClassification = table.Column<string>(type: "text", nullable: true),
                FinalConsensusSubtype = table.Column<string>(type: "text", nullable: true),
                FinalAdjudicatedOnsetDate = table.Column<string>(type: "text", nullable: true),
                AdjudicationRationale = table.Column<string>(type: "text", nullable: true),
                Reader1Status = table.Column<string>(type: "text", nullable: false),
                Reader2Status = table.Column<string>(type: "text", nullable: false),
                signature_manifest = table.Column<string>(type: "jsonb", nullable: true),
                FinalizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cec_dossiers", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_cec_chair_adjudications_dossier_id",
            table: "cec_chair_adjudications",
            column: "DossierId");

        migrationBuilder.CreateIndex(
            name: "ix_cec_dossiers_study_id",
            table: "cec_dossiers",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "ix_cec_dossiers_workflow_status",
            table: "cec_dossiers",
            column: "WorkflowStatus");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cec_chair_adjudications");

        migrationBuilder.DropTable(
            name: "cec_dossiers");
    }
}
