using System;
using System.Collections.Generic;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <inheritdoc />
public partial class ConceptRegistry : Migration
{
    private static readonly string[] KnowledgeTables =
        ["code_systems", "code_system_versions", "code_system_entries", "concepts", "concept_revisions", "concept_mappings"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "code_systems",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                uri = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                publisher = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                license = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_code_systems", x => x.id);
                table.UniqueConstraint("AK_code_systems_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_code_systems_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "concepts",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                external_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                preferred_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                definition = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_concepts", x => x.id);
                table.UniqueConstraint("AK_concepts_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_concepts_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "code_system_versions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code_system_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                released_on = table.Column<DateOnly>(type: "date", nullable: false),
                package_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                package_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                package_source_uri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                package_byte_length = table.Column<long>(type: "bigint", nullable: false),
                package_format = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                entry_count = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_code_system_versions", x => x.id);
                table.UniqueConstraint("AK_code_system_versions_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_code_system_versions_code_systems_tenant_id_code_system_id",
                    columns: x => new { x.tenant_id, x.code_system_id },
                    principalSchema: "kernel",
                    principalTable: "code_systems",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_code_system_versions_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "concept_revisions",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                concept_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                preferred_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                definition = table.Column<string>(type: "text", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_concept_revisions", x => new { x.tenant_id, x.concept_id, x.version });
                table.ForeignKey(
                    name: "FK_concept_revisions_concepts_tenant_id_concept_id",
                    columns: x => new { x.tenant_id, x.concept_id },
                    principalSchema: "kernel",
                    principalTable: "concepts",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "code_system_entries",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code_system_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                ordinal = table.Column<int>(type: "integer", nullable: false),
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                display = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                group_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                submission_value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                definition = table.Column<string>(type: "text", nullable: true),
                synonyms = table.Column<List<string>>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_code_system_entries", x => new { x.tenant_id, x.code_system_version_id, x.ordinal });
                table.ForeignKey(
                    name: "FK_code_system_entries_code_system_versions_tenant_id_code_sys~",
                    columns: x => new { x.tenant_id, x.code_system_version_id },
                    principalSchema: "kernel",
                    principalTable: "code_system_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "concept_mappings",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                concept_id = table.Column<Guid>(type: "uuid", nullable: false),
                concept_version = table.Column<int>(type: "integer", nullable: false),
                code_system_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                relation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_concept_mappings", x => x.id);
                table.UniqueConstraint("AK_concept_mappings_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_concept_mappings_code_system_versions_tenant_id_code_system~",
                    columns: x => new { x.tenant_id, x.code_system_version_id },
                    principalSchema: "kernel",
                    principalTable: "code_system_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_concept_mappings_concept_revisions_tenant_id_concept_id_con~",
                    columns: x => new { x.tenant_id, x.concept_id, x.concept_version },
                    principalSchema: "kernel",
                    principalTable: "concept_revisions",
                    principalColumns: new[] { "tenant_id", "concept_id", "version" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_concept_mappings_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_code_system_entries_tenant_id_code_system_version_id_code",
            schema: "kernel",
            table: "code_system_entries",
            columns: new[] { "tenant_id", "code_system_version_id", "code" });

        migrationBuilder.CreateIndex(
            name: "IX_code_system_entries_tenant_id_code_system_version_id_group_~",
            schema: "kernel",
            table: "code_system_entries",
            columns: new[] { "tenant_id", "code_system_version_id", "group_code", "code" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.CreateIndex(
            name: "IX_code_system_versions_tenant_id_code_system_id_version",
            schema: "kernel",
            table: "code_system_versions",
            columns: new[] { "tenant_id", "code_system_id", "version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_code_systems_tenant_id_uri",
            schema: "kernel",
            table: "code_systems",
            columns: new[] { "tenant_id", "uri" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_concept_mappings_tenant_id_code_system_version_id_code",
            schema: "kernel",
            table: "concept_mappings",
            columns: new[] { "tenant_id", "code_system_version_id", "code" });

        migrationBuilder.CreateIndex(
            name: "IX_concept_mappings_tenant_id_concept_id",
            schema: "kernel",
            table: "concept_mappings",
            columns: new[] { "tenant_id", "concept_id" });

        migrationBuilder.CreateIndex(
            name: "IX_concept_mappings_tenant_id_concept_id_concept_version",
            schema: "kernel",
            table: "concept_mappings",
            columns: new[] { "tenant_id", "concept_id", "concept_version" });

        migrationBuilder.CreateIndex(
            name: "IX_concepts_tenant_id_external_code",
            schema: "kernel",
            table: "concepts",
            columns: new[] { "tenant_id", "external_code" },
            unique: true,
            filter: "external_code IS NOT NULL");

        // ADR-2250: forced RLS on every new table. ADR-2470: a code-system version, its entries and each concept
        // version never change once written.
        migrationBuilder.EnableTenantIsolation("kernel", KnowledgeTables);
        migrationBuilder.CreateInsertOnlyGuards("kernel", "code_system_versions", "code_system_entries", "concept_revisions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "code_system_versions", "code_system_entries", "concept_revisions");
        migrationBuilder.DisableTenantIsolation("kernel", KnowledgeTables);

        migrationBuilder.DropTable(
            name: "code_system_entries",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "concept_mappings",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "code_system_versions",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "concept_revisions",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "code_systems",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "concepts",
            schema: "kernel");
    }
}
