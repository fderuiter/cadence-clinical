using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDataLockRecords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "data_lock_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FormId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FieldName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ScopeId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                LockType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SigToken = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                PreflightSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                OpenQueryCount = table.Column<int>(type: "integer", nullable: false),
                UnansweredQueryCount = table.Column<int>(type: "integer", nullable: false),
                UnverifiedSourceDataCount = table.Column<int>(type: "integer", nullable: false),
                IsOverride = table.Column<bool>(type: "boolean", nullable: false),
                OverrideJustification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                UnlockJustification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                UnlockedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                UnlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_data_lock_records", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "idx_data_locks_scope",
            table: "data_lock_records",
            columns: new[] { "StudyId", "ScopeType", "ScopeId", "IsActive" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "data_lock_records");
    }
}
