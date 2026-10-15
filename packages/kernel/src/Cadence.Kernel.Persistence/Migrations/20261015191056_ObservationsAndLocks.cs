using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Observations, their insert-only revisions and their locks (spec §8.1, ADR-2620). All three tables are
/// tenant-isolated (ADR-2250); revisions are insert-only (<see cref="InsertOnlySql"/>, KA001).
/// </summary>
public partial class ObservationsAndLocks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "observations",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                concept_id = table.Column<Guid>(type: "uuid", nullable: false),
                concept_version = table.Column<int>(type: "integer", nullable: false),
                observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                revision = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_observations", x => x.id);
                table.UniqueConstraint("AK_observations_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_observations_revision", "revision > 0");
                table.ForeignKey(
                    name: "FK_observations_enrollments_tenant_id_enrollment_id",
                    columns: x => new { x.tenant_id, x.enrollment_id },
                    principalSchema: "kernel",
                    principalTable: "enrollments",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_observations_participants_tenant_id_participant_id",
                    columns: x => new { x.tenant_id, x.participant_id },
                    principalSchema: "kernel",
                    principalTable: "participants",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_observations_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_observations_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "observation_locks",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
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

        migrationBuilder.CreateTable(
            name: "observation_revisions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<int>(type: "integer", nullable: false),
                value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                recorded_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_observation_revisions", x => x.id);
                table.CheckConstraint("ck_observation_revisions_number", "number > 0");
                table.ForeignKey(
                    name: "FK_observation_revisions_observations_tenant_id_observation_id",
                    columns: x => new { x.tenant_id, x.observation_id },
                    principalSchema: "kernel",
                    principalTable: "observations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_observation_locks_tenant_id_observation_id",
            schema: "kernel",
            table: "observation_locks",
            columns: new[] { "tenant_id", "observation_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_observation_revisions_tenant_id_observation_id_number",
            schema: "kernel",
            table: "observation_revisions",
            columns: new[] { "tenant_id", "observation_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_observations_tenant_id_enrollment_id",
            schema: "kernel",
            table: "observations",
            columns: new[] { "tenant_id", "enrollment_id" });

        migrationBuilder.CreateIndex(
            name: "IX_observations_tenant_id_participant_id_concept_id",
            schema: "kernel",
            table: "observations",
            columns: new[] { "tenant_id", "participant_id", "concept_id" });

        migrationBuilder.CreateIndex(
            name: "IX_observations_tenant_id_study_id",
            schema: "kernel",
            table: "observations",
            columns: new[] { "tenant_id", "study_id" });

        migrationBuilder.EnableTenantIsolation("kernel", "observations", "observation_revisions", "observation_locks");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "observation_revisions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "observation_revisions");
        migrationBuilder.DisableTenantIsolation("kernel", "observations", "observation_revisions", "observation_locks");

        migrationBuilder.DropTable(
            name: "observation_locks",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "observation_revisions",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "observations",
            schema: "kernel");
    }
}
