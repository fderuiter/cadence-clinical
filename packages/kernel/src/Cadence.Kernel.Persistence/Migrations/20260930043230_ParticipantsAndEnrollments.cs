using System;

using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Execution;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Participants and enrollments (spec §7.4, ADR-2453): participant identity, one enrollment per participant in a study
/// at a study site, and its bitemporal status history. All three tables are tenant-isolated (ADR-2250); status periods
/// carry the K-TIME-1/K-TIME-2 guards (<see cref="Bitemporal.BitemporalSql"/>), and enrollments the lifecycle trigger
/// and one-active-enrollment exclusion in <see cref="EnrollmentSql"/>.
/// </summary>
public partial class ParticipantsAndEnrollments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_study_sites_tenant_id_id_study_id",
            schema: "kernel",
            table: "study_sites",
            columns: new[] { "tenant_id", "id", "study_id" });

        migrationBuilder.CreateTable(
            name: "participants",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_participants", x => x.id);
                table.UniqueConstraint("AK_participants_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_participants_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "enrollments",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_site_id = table.Column<Guid>(type: "uuid", nullable: false),
                subject_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                enrollment_number = table.Column<int>(type: "integer", nullable: true),
                withdrawal_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                withdrawal_attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_enrollments", x => x.id);
                table.UniqueConstraint("AK_enrollments_tenant_id_id", x => new { x.tenant_id, x.id });
                table.UniqueConstraint("AK_enrollments_tenant_id_id_participant_id_study_site_id", x => new { x.tenant_id, x.id, x.participant_id, x.study_site_id });
                table.CheckConstraint("ck_enrollments_number", "enrollment_number IS NULL OR enrollment_number > 0");
                table.CheckConstraint("ck_enrollments_numbered", "(status IN ('Screening', 'ScreenFailed') AND enrollment_number IS NULL) OR (status IN ('Enrolled', 'Randomized', 'Active', 'Completed') AND enrollment_number IS NOT NULL) OR status = 'Withdrawn'");
                table.ForeignKey(
                    name: "FK_enrollments_attestations_tenant_id_withdrawal_attestation_id",
                    columns: x => new { x.tenant_id, x.withdrawal_attestation_id },
                    principalSchema: "kernel",
                    principalTable: "attestations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_enrollments_participants_tenant_id_participant_id",
                    columns: x => new { x.tenant_id, x.participant_id },
                    principalSchema: "kernel",
                    principalTable: "participants",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_enrollments_study_sites_tenant_id_study_site_id_study_id",
                    columns: x => new { x.tenant_id, x.study_site_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "study_sites",
                    principalColumns: new[] { "tenant_id", "id", "study_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_enrollments_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "enrollment_status_periods",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_site_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_enrollment_status_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_enrollment_status_periods_enrollments_tenant_id_enrollment_~",
                    columns: x => new { x.tenant_id, x.enrollment_id, x.participant_id, x.study_site_id },
                    principalSchema: "kernel",
                    principalTable: "enrollments",
                    principalColumns: new[] { "tenant_id", "id", "participant_id", "study_site_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_enrollment_status_periods_tenant_id_enrollment_id",
            schema: "kernel",
            table: "enrollment_status_periods",
            columns: new[] { "tenant_id", "enrollment_id" });

        migrationBuilder.CreateIndex(
            name: "IX_enrollment_status_periods_tenant_id_enrollment_id_participa~",
            schema: "kernel",
            table: "enrollment_status_periods",
            columns: new[] { "tenant_id", "enrollment_id", "participant_id", "study_site_id" });

        migrationBuilder.CreateIndex(
            name: "IX_enrollments_tenant_id_participant_id",
            schema: "kernel",
            table: "enrollments",
            columns: new[] { "tenant_id", "participant_id" });

        migrationBuilder.CreateIndex(
            name: "IX_enrollments_tenant_id_study_id_enrollment_number",
            schema: "kernel",
            table: "enrollments",
            columns: new[] { "tenant_id", "study_id", "enrollment_number" },
            unique: true,
            filter: "enrollment_number IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_enrollments_tenant_id_study_id_subject_code",
            schema: "kernel",
            table: "enrollments",
            columns: new[] { "tenant_id", "study_id", "subject_code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_enrollments_tenant_id_study_site_id_study_id",
            schema: "kernel",
            table: "enrollments",
            columns: new[] { "tenant_id", "study_site_id", "study_id" });

        migrationBuilder.CreateIndex(
            name: "IX_enrollments_tenant_id_withdrawal_attestation_id",
            schema: "kernel",
            table: "enrollments",
            columns: new[] { "tenant_id", "withdrawal_attestation_id" });

        migrationBuilder.EnableTenantIsolation("kernel", "enrollment_status_periods", "enrollments", "participants");
        migrationBuilder.CreateBitemporalGuards("kernel", "enrollment_status_periods", "enrollment_id");
        migrationBuilder.CreateEnrollmentGuards("kernel");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropEnrollmentGuards("kernel");
        migrationBuilder.DropBitemporalGuards("kernel", "enrollment_status_periods");
        migrationBuilder.DisableTenantIsolation("kernel", "enrollment_status_periods", "enrollments", "participants");

        migrationBuilder.DropTable(
            name: "enrollment_status_periods",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "enrollments",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "participants",
            schema: "kernel");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_study_sites_tenant_id_id_study_id",
            schema: "kernel",
            table: "study_sites");
    }
}
