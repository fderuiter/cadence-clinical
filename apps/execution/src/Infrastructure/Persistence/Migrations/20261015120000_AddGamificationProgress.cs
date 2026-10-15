using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddGamificationProgress : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "participant_gamification_progresses",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                LongestStreak = table.Column<int>(type: "integer", nullable: false),
                TotalSubmissions = table.Column<int>(type: "integer", nullable: false),
                OnTimeSubmissions = table.Column<int>(type: "integer", nullable: false),
                LastSubmissionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                MilestonesAchievedJson = table.Column<string>(type: "text", nullable: false),
                TotalBonusEarned = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_participant_gamification_progresses", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_participant_gamification_progresses_SubjectId_StudyId",
            table: "participant_gamification_progresses",
            columns: new[] { "SubjectId", "StudyId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "participant_gamification_progresses");
    }
}
