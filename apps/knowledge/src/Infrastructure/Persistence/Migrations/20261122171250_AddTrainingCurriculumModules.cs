using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTrainingCurriculumModules : Migration
{
    private static readonly string[] TenantTables =
    [
        "training_curriculum_modules",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "training_curriculum_modules",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CurriculumId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SequenceOrder = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ModuleType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ContentText = table.Column<string>(type: "text", nullable: false),
                MinReadingTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: Guid.Empty)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_training_curriculum_modules", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_training_curriculum_modules_CurriculumId",
            table: "training_curriculum_modules",
            column: "CurriculumId");

        migrationBuilder.CreateIndex(
            name: "IX_training_curriculum_modules_CurriculumId_SequenceOrder",
            table: "training_curriculum_modules",
            columns: new[] { "CurriculumId", "SequenceOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_training_curriculum_modules_tenant_id",
            table: "training_curriculum_modules",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("knowledge", TenantTables);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("knowledge", TenantTables);

        migrationBuilder.DropTable(
            name: "training_curriculum_modules");
    }
}
