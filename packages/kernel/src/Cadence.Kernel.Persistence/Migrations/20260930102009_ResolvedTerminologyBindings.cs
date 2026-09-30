using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Terminology bindings resolved to the Concept Registry (ADR-2460 §4.2, #4888): a standard version's binding keeps the
/// code-system version the tenant had imported when it was registered, and a study declaration keeps the version of
/// every terminology it declares. Rows written before this migration are not backfilled: <c>standard_terminology</c>
/// is insert-only, so an older binding stays by natural key, and an older declaration resolves when it is next declared.
/// </summary>
public partial class ResolvedTerminologyBindings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid[]>(
            name: "controlled_terminology_versions",
            schema: "kernel",
            table: "study_standards_profiles",
            type: "uuid[]",
            nullable: false,
            defaultValue: new Guid[0]);

        migrationBuilder.AddColumn<Guid>(
            name: "code_system_version_id",
            schema: "kernel",
            table: "standard_terminology",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_standard_terminology_tenant_id_code_system_version_id",
            schema: "kernel",
            table: "standard_terminology",
            columns: new[] { "tenant_id", "code_system_version_id" });

        migrationBuilder.AddForeignKey(
            name: "FK_standard_terminology_code_system_versions_tenant_id_code_sy~",
            schema: "kernel",
            table: "standard_terminology",
            columns: new[] { "tenant_id", "code_system_version_id" },
            principalSchema: "kernel",
            principalTable: "code_system_versions",
            principalColumns: new[] { "tenant_id", "id" },
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_standard_terminology_code_system_versions_tenant_id_code_sy~",
            schema: "kernel",
            table: "standard_terminology");

        migrationBuilder.DropIndex(
            name: "IX_standard_terminology_tenant_id_code_system_version_id",
            schema: "kernel",
            table: "standard_terminology");

        migrationBuilder.DropColumn(
            name: "controlled_terminology_versions",
            schema: "kernel",
            table: "study_standards_profiles");

        migrationBuilder.DropColumn(
            name: "code_system_version_id",
            schema: "kernel",
            table: "standard_terminology");
    }
}
