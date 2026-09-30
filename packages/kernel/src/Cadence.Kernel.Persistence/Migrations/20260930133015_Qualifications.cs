using System;

using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Qualifications (spec §9.3, K-AUTH-5, ADR-2459): qualifications held by actors with their evidence, and the
/// requirements that gate a capability on a current qualification, each with bitemporal validity. All four tables are
/// tenant-isolated (ADR-2250), and both period tables carry the K-TIME-1/K-TIME-2 guards (<see cref="BitemporalSql"/>).
/// </summary>
public partial class Qualifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "qualification_requirements",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                capability = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                qualification_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                scope_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                scope_target_id = table.Column<Guid>(type: "uuid", nullable: true),
                scope_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                defined_by = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qualification_requirements", x => x.id);
                table.UniqueConstraint("AK_qualification_requirements_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_qualification_requirements_code", "qualification_code ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'");
                table.CheckConstraint("ck_qualification_requirements_scope", "(scope_level = 'Tenant' AND scope_target_id IS NULL AND scope_country IS NULL) OR (scope_level IN ('Organization', 'Study', 'StudySite') AND scope_target_id IS NOT NULL AND scope_country IS NULL) OR (scope_level = 'StudyCountry' AND scope_target_id IS NOT NULL AND scope_country IS NOT NULL AND scope_country ~ '^[A-Z]{2}$')");
                table.ForeignKey(
                    name: "FK_qualification_requirements_protocol_versions_tenant_id_prot~",
                    columns: x => new { x.tenant_id, x.protocol_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_qualification_requirements_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "qualifications",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                holder_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                holder_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                evidence_artifact_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded_by = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qualifications", x => x.id);
                table.UniqueConstraint("AK_qualifications_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_qualifications_code", "code ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'");
                table.CheckConstraint("ck_qualifications_protocol_version", "(kind = 'ProtocolTraining') = (protocol_version_id IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_qualifications_artifact_versions_tenant_id_evidence_artifac~",
                    columns: x => new { x.tenant_id, x.evidence_artifact_version_id },
                    principalSchema: "kernel",
                    principalTable: "artifact_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_qualifications_protocol_versions_tenant_id_protocol_version~",
                    columns: x => new { x.tenant_id, x.protocol_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_qualifications_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "qualification_requirement_periods",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qualification_requirement_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_qualification_requirement_periods_qualification_requirement~",
                    columns: x => new { x.tenant_id, x.requirement_id },
                    principalSchema: "kernel",
                    principalTable: "qualification_requirements",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "qualification_periods",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                qualification_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qualification_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_qualification_periods_qualifications_tenant_id_qualificatio~",
                    columns: x => new { x.tenant_id, x.qualification_id },
                    principalSchema: "kernel",
                    principalTable: "qualifications",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_qualification_periods_tenant_id_qualification_id",
            schema: "kernel",
            table: "qualification_periods",
            columns: new[] { "tenant_id", "qualification_id" });

        migrationBuilder.CreateIndex(
            name: "IX_qualification_requirement_periods_tenant_id_requirement_id",
            schema: "kernel",
            table: "qualification_requirement_periods",
            columns: new[] { "tenant_id", "requirement_id" });

        migrationBuilder.CreateIndex(
            name: "IX_qualification_requirements_tenant_id_capability",
            schema: "kernel",
            table: "qualification_requirements",
            columns: new[] { "tenant_id", "capability" });

        migrationBuilder.CreateIndex(
            name: "IX_qualification_requirements_tenant_id_protocol_version_id",
            schema: "kernel",
            table: "qualification_requirements",
            columns: new[] { "tenant_id", "protocol_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_qualifications_tenant_id_evidence_artifact_version_id",
            schema: "kernel",
            table: "qualifications",
            columns: new[] { "tenant_id", "evidence_artifact_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_qualifications_tenant_id_holder_kind_holder_id",
            schema: "kernel",
            table: "qualifications",
            columns: new[] { "tenant_id", "holder_kind", "holder_id" });

        migrationBuilder.CreateIndex(
            name: "IX_qualifications_tenant_id_protocol_version_id",
            schema: "kernel",
            table: "qualifications",
            columns: new[] { "tenant_id", "protocol_version_id" });

        migrationBuilder.EnableTenantIsolation(
            "kernel", "qualification_periods", "qualification_requirement_periods", "qualification_requirements", "qualifications");
        migrationBuilder.CreateBitemporalGuards("kernel", "qualification_periods", "qualification_id");
        migrationBuilder.CreateBitemporalGuards("kernel", "qualification_requirement_periods", "requirement_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropBitemporalGuards("kernel", "qualification_requirement_periods");
        migrationBuilder.DropBitemporalGuards("kernel", "qualification_periods");
        migrationBuilder.DisableTenantIsolation(
            "kernel", "qualification_periods", "qualification_requirement_periods", "qualification_requirements", "qualifications");

        migrationBuilder.DropTable(
            name: "qualification_periods",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "qualification_requirement_periods",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "qualifications",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "qualification_requirements",
            schema: "kernel");
    }
}
