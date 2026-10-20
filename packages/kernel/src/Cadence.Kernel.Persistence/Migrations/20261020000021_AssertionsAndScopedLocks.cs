using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Assertions and scoped locks (spec §8.2, §10.5, ADR-2621): <c>kernel.assertions</c> replaces
/// <c>kernel.observation_locks</c>, which held no data (Cadence is pre-deployment). Assertions are tenant-isolated
/// (ADR-2250) and insert-only (<see cref="InsertOnlySql"/>): a later assertion retracts an earlier one.
/// </summary>
public partial class AssertionsAndScopedLocks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableTenantIsolation("kernel", "observation_locks");
        migrationBuilder.DropTable(
            name: "observation_locks",
            schema: "kernel");

        migrationBuilder.CreateTable(
            name: "assertions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: true),
                target_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_revision = table.Column<int>(type: "integer", nullable: true),
                text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                retracts_id = table.Column<Guid>(type: "uuid", nullable: true),
                attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                asserted_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                asserted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_assertions", x => x.id);
                table.UniqueConstraint("AK_assertions_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_assertions_lock_attested", "kind NOT IN ('Locked', 'Frozen') OR attestation_id IS NOT NULL");
                table.CheckConstraint("ck_assertions_target_revision", "target_revision IS NULL OR target_revision > 0");
                table.ForeignKey(
                    name: "FK_assertions_assertions_tenant_id_retracts_id",
                    columns: x => new { x.tenant_id, x.retracts_id },
                    principalSchema: "kernel",
                    principalTable: "assertions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_assertions_attestations_tenant_id_attestation_id",
                    columns: x => new { x.tenant_id, x.attestation_id },
                    principalSchema: "kernel",
                    principalTable: "attestations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_assertions_enrollments_tenant_id_enrollment_id",
                    columns: x => new { x.tenant_id, x.enrollment_id },
                    principalSchema: "kernel",
                    principalTable: "enrollments",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_assertions_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_assertions_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_assertions_tenant_id_attestation_id",
            schema: "kernel",
            table: "assertions",
            columns: new[] { "tenant_id", "attestation_id" });

        migrationBuilder.CreateIndex(
            name: "IX_assertions_tenant_id_enrollment_id",
            schema: "kernel",
            table: "assertions",
            columns: new[] { "tenant_id", "enrollment_id" });

        migrationBuilder.CreateIndex(
            name: "IX_assertions_tenant_id_retracts_id",
            schema: "kernel",
            table: "assertions",
            columns: new[] { "tenant_id", "retracts_id" },
            unique: true,
            filter: "retracts_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_assertions_tenant_id_study_id",
            schema: "kernel",
            table: "assertions",
            columns: new[] { "tenant_id", "study_id" });

        migrationBuilder.CreateIndex(
            name: "IX_assertions_tenant_id_target_id_kind",
            schema: "kernel",
            table: "assertions",
            columns: new[] { "tenant_id", "target_id", "kind" });

        migrationBuilder.EnableTenantIsolation("kernel", "assertions");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "assertions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "assertions");
        migrationBuilder.DisableTenantIsolation("kernel", "assertions");
        migrationBuilder.DropTable(
            name: "assertions",
            schema: "kernel");

        migrationBuilder.CreateTable(
            name: "observation_locks",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_observation_locks", x => x.id);
                table.ForeignKey(
                    name: "FK_observation_locks_observations_tenant_id_observation_id",
                    columns: x => new { x.tenant_id, x.observation_id },
                    principalSchema: "kernel",
                    principalTable: "observations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_observation_locks_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_observation_locks_tenant_id_observation_id",
            schema: "kernel",
            table: "observation_locks",
            columns: new[] { "tenant_id", "observation_id" },
            unique: true);

        migrationBuilder.EnableTenantIsolation("kernel", "observation_locks");
    }
}
