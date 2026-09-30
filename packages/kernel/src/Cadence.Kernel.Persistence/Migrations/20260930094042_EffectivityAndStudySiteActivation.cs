using System;

using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Effectivity (spec §7.1, ADR-2456): whether a protocol version is in force for a study, a country or a study site, with
/// its bitemporal status history and evidence. Both tables are tenant-isolated (ADR-2250); the status rows carry the
/// K-TIME-1 and K-TIME-2 guards keyed by effectivity (ADR-2246). It also makes <c>ck_grants_scope</c> refuse a
/// study-country grant with no country: a null country made the regular expression yield null, which a check
/// constraint accepts.
/// </summary>
public partial class EffectivityAndStudySiteActivation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_grants_scope",
            schema: "kernel",
            table: "grants");

        migrationBuilder.CreateTable(
            name: "effectivities",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                scope_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                scope_study_site_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_effectivities", x => x.id);
                table.UniqueConstraint("AK_effectivities_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_effectivities_scope", "(scope_level = 'Study' AND scope_country IS NULL AND scope_study_site_id IS NULL) OR (scope_level = 'Country' AND scope_country IS NOT NULL AND scope_country ~ '^[A-Z]{2}$' AND scope_study_site_id IS NULL) OR (scope_level = 'StudySite' AND scope_country IS NULL AND scope_study_site_id IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_effectivities_protocol_versions_tenant_id_protocol_version_~",
                    columns: x => new { x.tenant_id, x.protocol_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_effectivities_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_effectivities_study_sites_tenant_id_scope_study_site_id_stu~",
                    columns: x => new { x.tenant_id, x.scope_study_site_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "study_sites",
                    principalColumns: new[] { "tenant_id", "id", "study_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_effectivities_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "effectivity_status_periods",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                effectivity_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                evidence = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_effectivity_status_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_effectivity_status_periods_effectivities_tenant_id_effectiv~",
                    columns: x => new { x.tenant_id, x.effectivity_id },
                    principalSchema: "kernel",
                    principalTable: "effectivities",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_grants_scope",
            schema: "kernel",
            table: "grants",
            sql: "(scope_level = 'Tenant' AND scope_target_id IS NULL AND scope_country IS NULL) OR (scope_level IN ('Organization', 'Study', 'StudySite') AND scope_target_id IS NOT NULL AND scope_country IS NULL) OR (scope_level = 'StudyCountry' AND scope_target_id IS NOT NULL AND scope_country IS NOT NULL AND scope_country ~ '^[A-Z]{2}$')");

        migrationBuilder.CreateIndex(
            name: "IX_effectivities_tenant_id_protocol_version_id_scope_level_sco~",
            schema: "kernel",
            table: "effectivities",
            columns: new[] { "tenant_id", "protocol_version_id", "scope_level", "scope_country", "scope_study_site_id" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.CreateIndex(
            name: "IX_effectivities_tenant_id_scope_study_site_id_study_id",
            schema: "kernel",
            table: "effectivities",
            columns: new[] { "tenant_id", "scope_study_site_id", "study_id" });

        migrationBuilder.CreateIndex(
            name: "IX_effectivities_tenant_id_study_id",
            schema: "kernel",
            table: "effectivities",
            columns: new[] { "tenant_id", "study_id" });

        migrationBuilder.CreateIndex(
            name: "IX_effectivity_status_periods_tenant_id_effectivity_id",
            schema: "kernel",
            table: "effectivity_status_periods",
            columns: new[] { "tenant_id", "effectivity_id" });

        migrationBuilder.EnableTenantIsolation("kernel", "effectivity_status_periods", "effectivities");
        migrationBuilder.CreateBitemporalGuards("kernel", "effectivity_status_periods", "effectivity_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropBitemporalGuards("kernel", "effectivity_status_periods");
        migrationBuilder.DisableTenantIsolation("kernel", "effectivity_status_periods", "effectivities");

        migrationBuilder.DropTable(
            name: "effectivity_status_periods",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "effectivities",
            schema: "kernel");

        migrationBuilder.DropCheckConstraint(
            name: "ck_grants_scope",
            schema: "kernel",
            table: "grants");

        migrationBuilder.AddCheckConstraint(
            name: "ck_grants_scope",
            schema: "kernel",
            table: "grants",
            sql: "(scope_level = 'Tenant' AND scope_target_id IS NULL AND scope_country IS NULL) OR (scope_level IN ('Organization', 'Study', 'StudySite') AND scope_target_id IS NOT NULL AND scope_country IS NULL) OR (scope_level = 'StudyCountry' AND scope_target_id IS NOT NULL AND scope_country ~ '^[A-Z]{2}$')");
    }
}
