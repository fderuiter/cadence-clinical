using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Org.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddUserOnboardingPreferences : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_onboarding_preferences",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Persona = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CurrentStep = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Disabled = table.Column<bool>(type: "boolean", nullable: false),
                EventsJson = table.Column<string>(type: "jsonb", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_onboarding_preferences", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_user_onboarding_preferences_UserId",
            table: "user_onboarding_preferences",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_user_onboarding_preferences_UserId_Persona",
            table: "user_onboarding_preferences",
            columns: new[] { "UserId", "Persona" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_onboarding_preferences");
    }
}
