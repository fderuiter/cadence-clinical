using System;
using System.Collections.Generic;

using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Authority (spec §9.3, ADR-2455): roles, grants and delegations with bitemporal validity, and the authorization
/// decision on every provenance activity (K-AUTH-2). All three tables are tenant-isolated (ADR-2250); roles are
/// insert-only (<see cref="InsertOnlySql"/>) and grant periods carry the K-TIME-1/K-TIME-2 guards
/// (<see cref="BitemporalSql"/>).
/// </summary>
public partial class GrantsRolesAndDelegation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "authorization",
            schema: "kernel",
            table: "provenance_activities",
            type: "jsonb",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "roles",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                capabilities = table.Column<List<string>>(type: "text[]", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roles", x => x.id);
                table.UniqueConstraint("AK_roles_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_roles_capabilities", "cardinality(capabilities) > 0");
                table.ForeignKey(
                    name: "FK_roles_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "grants",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                grantee_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                grantee_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                capability = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                role_id = table.Column<Guid>(type: "uuid", nullable: true),
                scope_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                scope_target_id = table.Column<Guid>(type: "uuid", nullable: true),
                scope_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                with_grant_option = table.Column<bool>(type: "boolean", nullable: false),
                parent_grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                issued_by = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_grants", x => x.id);
                table.UniqueConstraint("AK_grants_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_grants_scope", "(scope_level = 'Tenant' AND scope_target_id IS NULL AND scope_country IS NULL) OR (scope_level IN ('Organization', 'Study', 'StudySite') AND scope_target_id IS NOT NULL AND scope_country IS NULL) OR (scope_level = 'StudyCountry' AND scope_target_id IS NOT NULL AND scope_country ~ '^[A-Z]{2}$')");
                table.CheckConstraint("ck_grants_target", "(capability IS NULL) <> (role_id IS NULL)");
                table.ForeignKey(
                    name: "FK_grants_grants_tenant_id_parent_grant_id",
                    columns: x => new { x.tenant_id, x.parent_grant_id },
                    principalSchema: "kernel",
                    principalTable: "grants",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_grants_roles_tenant_id_role_id",
                    columns: x => new { x.tenant_id, x.role_id },
                    principalSchema: "kernel",
                    principalTable: "roles",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_grants_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "grant_periods",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_grant_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_grant_periods_grants_tenant_id_grant_id",
                    columns: x => new { x.tenant_id, x.grant_id },
                    principalSchema: "kernel",
                    principalTable: "grants",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_grant_periods_tenant_id_grant_id",
            schema: "kernel",
            table: "grant_periods",
            columns: new[] { "tenant_id", "grant_id" });

        migrationBuilder.CreateIndex(
            name: "IX_grants_tenant_id_grantee_kind_grantee_id",
            schema: "kernel",
            table: "grants",
            columns: new[] { "tenant_id", "grantee_kind", "grantee_id" });

        migrationBuilder.CreateIndex(
            name: "IX_grants_tenant_id_parent_grant_id",
            schema: "kernel",
            table: "grants",
            columns: new[] { "tenant_id", "parent_grant_id" });

        migrationBuilder.CreateIndex(
            name: "IX_grants_tenant_id_role_id",
            schema: "kernel",
            table: "grants",
            columns: new[] { "tenant_id", "role_id" });

        migrationBuilder.CreateIndex(
            name: "IX_roles_tenant_id_code",
            schema: "kernel",
            table: "roles",
            columns: new[] { "tenant_id", "code" },
            unique: true);

        migrationBuilder.EnableTenantIsolation("kernel", "grant_periods", "grants", "roles");
        migrationBuilder.CreateBitemporalGuards("kernel", "grant_periods", "grant_id");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "roles");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "roles");
        migrationBuilder.DropBitemporalGuards("kernel", "grant_periods");
        migrationBuilder.DisableTenantIsolation("kernel", "grant_periods", "grants", "roles");

        migrationBuilder.DropTable(
            name: "grant_periods",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "grants",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "roles",
            schema: "kernel");

        migrationBuilder.DropColumn(
            name: "authorization",
            schema: "kernel",
            table: "provenance_activities");
    }
}
