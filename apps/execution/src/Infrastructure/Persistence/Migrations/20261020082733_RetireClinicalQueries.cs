using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RetireClinicalQueries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "clinical_queries");

        migrationBuilder.DropColumn(
            name: "query_id",
            table: "staged_adverse_event_reviews");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "query_id",
            table: "staged_adverse_event_reviews",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "clinical_queries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActionRequired = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                Domain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                EscalatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                FieldId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FieldLink = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FormId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ObservationId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Origin = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                QueryType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Resolver = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Responder = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Response = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                RuleId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_clinical_queries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "idx_query_observation_id",
            table: "clinical_queries",
            column: "ObservationId");

        migrationBuilder.CreateIndex(
            name: "idx_query_subject_status",
            table: "clinical_queries",
            columns: new[] { "SubjectId", "Status" });

        migrationBuilder.CreateIndex(
            name: "idx_query_target",
            table: "clinical_queries",
            columns: new[] { "StudyId", "SubjectId", "VisitId", "Domain", "TestCode" });

        migrationBuilder.CreateIndex(
            name: "ix_clinical_queries_site_id",
            table: "clinical_queries",
            column: "SiteId");
    }
}
