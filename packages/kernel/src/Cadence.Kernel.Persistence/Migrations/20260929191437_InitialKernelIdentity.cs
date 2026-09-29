using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialKernelIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "kernel");

        migrationBuilder.CreateTable(
            name: "audit_log_entries",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                entity_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                entity_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false),
                changes_json = table.Column<string>(type: "text", nullable: true),
                predecessor_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                block_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_log_entries", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "people",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                first_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                last_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_people", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "tenants",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenants", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "organizations",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_organizations", x => x.id);
                table.ForeignKey(
                    name: "FK_organizations_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sites",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                site_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sites", x => x.id);
                table.ForeignKey(
                    name: "FK_sites_organizations_institution_id",
                    column: x => x.institution_id,
                    principalSchema: "kernel",
                    principalTable: "organizations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sites_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "studies",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sponsor_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_studies", x => x.id);
                table.ForeignKey(
                    name: "FK_studies_organizations_sponsor_id",
                    column: x => x.sponsor_id,
                    principalSchema: "kernel",
                    principalTable: "organizations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_studies_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "study_sites",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                site_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_study_sites", x => x.id);
                table.ForeignKey(
                    name: "FK_study_sites_sites_site_id",
                    column: x => x.site_id,
                    principalSchema: "kernel",
                    principalTable: "sites",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_study_sites_studies_study_id",
                    column: x => x.study_id,
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_study_sites_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_entity_name_timestamp",
            schema: "kernel",
            table: "audit_log_entries",
            columns: new[] { "entity_name", "timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_organizations_tenant_id_code",
            schema: "kernel",
            table: "organizations",
            columns: new[] { "tenant_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_people_email",
            schema: "kernel",
            table: "people",
            column: "email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sites_institution_id_site_code",
            schema: "kernel",
            table: "sites",
            columns: new[] { "institution_id", "site_code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sites_tenant_id",
            schema: "kernel",
            table: "sites",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_studies_sponsor_id",
            schema: "kernel",
            table: "studies",
            column: "sponsor_id");

        migrationBuilder.CreateIndex(
            name: "IX_studies_tenant_id_study_code",
            schema: "kernel",
            table: "studies",
            columns: new[] { "tenant_id", "study_code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_study_sites_site_id",
            schema: "kernel",
            table: "study_sites",
            column: "site_id");

        migrationBuilder.CreateIndex(
            name: "IX_study_sites_study_id_site_id",
            schema: "kernel",
            table: "study_sites",
            columns: new[] { "study_id", "site_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_study_sites_tenant_id",
            schema: "kernel",
            table: "study_sites",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tenants_code",
            schema: "kernel",
            table: "tenants",
            column: "code",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "people",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "study_sites",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "sites",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "studies",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "organizations",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "tenants",
            schema: "kernel");
    }
}
