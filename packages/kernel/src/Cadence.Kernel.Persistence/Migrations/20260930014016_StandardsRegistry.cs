using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// The Standards Registry and Metadata Registry (spec §6.3, ADR-2460): standard versions with their dataset, variable,
/// value-level and terminology definitions, versioned library items, and each study's declared standards. Every table
/// is tenant-scoped with forced Row-Level Security (K-ID-2); definition tables are insert-only, and the identity and
/// content columns of standard versions and library items are fixed once written.
/// </summary>
public partial class StandardsRegistry : Migration
{
    private static readonly string[] Tables =
    [
        "standard_versions", "standard_datasets", "standard_variables", "standard_value_level_definitions",
        "standard_terminology", "library_items", "study_standards_profiles",
    ];

    private static readonly string[] DefinitionTables =
        ["standard_datasets", "standard_variables", "standard_value_level_definitions", "standard_terminology"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "library_items",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                item_version = table.Column<int>(type: "integer", nullable: false),
                title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                content_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                standard_version_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                approval_attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                deprecation_attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_library_items", x => x.id);
                table.UniqueConstraint("AK_library_items_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_library_items_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "standard_versions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                standard_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                publisher = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                source = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                content_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                approval_attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                deprecation_attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_standard_versions", x => x.id);
                table.UniqueConstraint("AK_standard_versions_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_standard_versions_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "study_standards_profiles",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                standard_version_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                controlled_terminology = table.Column<string[]>(type: "text[]", nullable: false),
                library_pins = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_study_standards_profiles", x => x.id);
                table.UniqueConstraint("AK_study_standards_profiles_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_study_standards_profiles_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_study_standards_profiles_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "standard_datasets",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                standard_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                @class = table.Column<string>(name: "class", type: "character varying(255)", maxLength: 255, nullable: true),
                structure = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ordinal = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_standard_datasets", x => new { x.tenant_id, x.standard_version_id, x.name });
                table.ForeignKey(
                    name: "FK_standard_datasets_standard_versions_tenant_id_standard_vers~",
                    columns: x => new { x.tenant_id, x.standard_version_id },
                    principalSchema: "kernel",
                    principalTable: "standard_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "standard_terminology",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                standard_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                code_system = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_standard_terminology", x => new { x.tenant_id, x.standard_version_id, x.code_system, x.version });
                table.ForeignKey(
                    name: "FK_standard_terminology_standard_versions_tenant_id_standard_v~",
                    columns: x => new { x.tenant_id, x.standard_version_id },
                    principalSchema: "kernel",
                    principalTable: "standard_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "standard_variables",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                standard_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                dataset_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ordinal = table.Column<int>(type: "integer", nullable: false),
                data_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                role = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                core = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                codelists = table.Column<string[]>(type: "text[]", nullable: false),
                value_list = table.Column<string[]>(type: "text[]", nullable: false),
                described_value_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                description = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_standard_variables", x => new { x.tenant_id, x.standard_version_id, x.dataset_name, x.name });
                table.ForeignKey(
                    name: "FK_standard_variables_standard_datasets_tenant_id_standard_ver~",
                    columns: x => new { x.tenant_id, x.standard_version_id, x.dataset_name },
                    principalSchema: "kernel",
                    principalTable: "standard_datasets",
                    principalColumns: new[] { "tenant_id", "standard_version_id", "name" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "standard_value_level_definitions",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                standard_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                ordinal = table.Column<int>(type: "integer", nullable: false),
                dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                variable = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                where = table.Column<string>(type: "jsonb", nullable: false),
                data_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                length = table.Column<int>(type: "integer", nullable: true),
                codelists = table.Column<string[]>(type: "text[]", nullable: false),
                value_list = table.Column<string[]>(type: "text[]", nullable: false),
                description = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_standard_value_level_definitions", x => new { x.tenant_id, x.standard_version_id, x.ordinal });
                table.ForeignKey(
                    name: "FK_standard_value_level_definitions_standard_variables_tenant_~",
                    columns: x => new { x.tenant_id, x.standard_version_id, x.dataset, x.variable },
                    principalSchema: "kernel",
                    principalTable: "standard_variables",
                    principalColumns: new[] { "tenant_id", "standard_version_id", "dataset_name", "name" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_standard_value_level_definitions_standard_versions_tenant_i~",
                    columns: x => new { x.tenant_id, x.standard_version_id },
                    principalSchema: "kernel",
                    principalTable: "standard_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_library_items_tenant_id_kind_key_item_version",
            schema: "kernel",
            table: "library_items",
            columns: new[] { "tenant_id", "kind", "key", "item_version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_standard_value_level_definitions_tenant_id_standard_version~",
            schema: "kernel",
            table: "standard_value_level_definitions",
            columns: new[] { "tenant_id", "standard_version_id", "dataset", "variable" });

        migrationBuilder.CreateIndex(
            name: "IX_standard_versions_tenant_id_standard_code_version",
            schema: "kernel",
            table: "standard_versions",
            columns: new[] { "tenant_id", "standard_code", "version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_study_standards_profiles_tenant_id_study_id",
            schema: "kernel",
            table: "study_standards_profiles",
            columns: new[] { "tenant_id", "study_id" },
            unique: true);

        migrationBuilder.EnableTenantIsolation("kernel", Tables);
        migrationBuilder.CreateInsertOnlyGuards("kernel", DefinitionTables);
        migrationBuilder.CreateImmutableColumnsFunction("kernel");
        migrationBuilder.CreateImmutableColumnsGuard(
            "kernel", "standard_versions",
            "id", "tenant_id", "standard_code", "version", "name", "publisher", "source", "effective_date", "content_hash", "created_at", "created_by");
        migrationBuilder.CreateImmutableColumnsGuard(
            "kernel", "library_items",
            "id", "tenant_id", "kind", "key", "item_version", "title", "content", "content_hash", "standard_version_ids", "created_at", "created_by");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropImmutableColumnsGuard("kernel", "library_items");
        migrationBuilder.DropImmutableColumnsGuard("kernel", "standard_versions");
        migrationBuilder.DropImmutableColumnsFunction("kernel");
        migrationBuilder.DropInsertOnlyGuards("kernel", DefinitionTables);
        migrationBuilder.DisableTenantIsolation("kernel", Tables);

        migrationBuilder.DropTable(
            name: "library_items",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "standard_terminology",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "standard_value_level_definitions",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "study_standards_profiles",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "standard_variables",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "standard_datasets",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "standard_versions",
            schema: "kernel");
    }
}
