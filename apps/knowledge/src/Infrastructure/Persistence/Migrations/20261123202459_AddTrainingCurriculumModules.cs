using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class AddTrainingCurriculumModules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ModulesJson",
            table: "TrainingCurricula",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ModulesJson",
            table: "TrainingCurricula");
    }
}
