using System;

using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <inheritdoc />
public partial class BitemporalApplicabilityAndConsentStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "applicabilities",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_site_id = table.Column<Guid>(type: "uuid", nullable: false),
                governing_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                is_inconsistent = table.Column<bool>(type: "boolean", nullable: false),
                inconsistency_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_applicabilities", x => x.id);
                table.UniqueConstraint("AK_applicabilities_tenant_id_id", x => new { x.tenant_id, x.id });
                table.UniqueConstraint("AK_applicabilities_tenant_id_id_enrollment_id", x => new { x.tenant_id, x.id, x.enrollment_id });
                table.UniqueConstraint("AK_applicabilities_tenant_id_id_enrollment_id_participant_id_s~", x => new { x.tenant_id, x.id, x.enrollment_id, x.participant_id, x.study_id });
                table.ForeignKey(
                    name: "FK_applicabilities_enrollments_tenant_id_enrollment_id",
                    columns: x => new { x.tenant_id, x.enrollment_id },
                    principalSchema: "kernel",
                    principalTable: "enrollments",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicabilities_participants_tenant_id_participant_id",
                    columns: x => new { x.tenant_id, x.participant_id },
                    principalSchema: "kernel",
                    principalTable: "participants",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicabilities_protocol_versions_tenant_id_governing_versi~",
                    columns: x => new { x.tenant_id, x.governing_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicabilities_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicabilities_study_sites_tenant_id_study_site_id_study_id",
                    columns: x => new { x.tenant_id, x.study_site_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "study_sites",
                    principalColumns: new[] { "tenant_id", "id", "study_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicabilities_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "consent_statuses",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_site_id = table.Column<Guid>(type: "uuid", nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                signed = table.Column<bool>(type: "boolean", nullable: false),
                signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                basis_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                artifact_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_statuses", x => x.id);
                table.UniqueConstraint("AK_consent_statuses_tenant_id_id", x => new { x.tenant_id, x.id });
                table.UniqueConstraint("AK_consent_statuses_tenant_id_id_enrollment_id_protocol_versio~", x => new { x.tenant_id, x.id, x.enrollment_id, x.protocol_version_id });
                table.ForeignKey(
                    name: "FK_consent_statuses_artifact_versions_tenant_id_artifact_versi~",
                    columns: x => new { x.tenant_id, x.artifact_version_id },
                    principalSchema: "kernel",
                    principalTable: "artifact_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_consent_statuses_attestations_tenant_id_attestation_id",
                    columns: x => new { x.tenant_id, x.attestation_id },
                    principalSchema: "kernel",
                    principalTable: "attestations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_consent_statuses_enrollments_tenant_id_enrollment_id",
                    columns: x => new { x.tenant_id, x.enrollment_id },
                    principalSchema: "kernel",
                    principalTable: "enrollments",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_consent_statuses_participants_tenant_id_participant_id",
                    columns: x => new { x.tenant_id, x.participant_id },
                    principalSchema: "kernel",
                    principalTable: "participants",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_consent_statuses_protocol_versions_tenant_id_protocol_versi~",
                    columns: x => new { x.tenant_id, x.protocol_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_consent_statuses_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_consent_statuses_study_sites_tenant_id_study_site_id_study_~",
                    columns: x => new { x.tenant_id, x.study_site_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "study_sites",
                    principalColumns: new[] { "tenant_id", "id", "study_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_consent_statuses_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "applicability_pending_transitions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                applicability_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                from_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                target_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                effectivity_id = table.Column<Guid>(type: "uuid", nullable: false),
                required_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_applicability_pending_transitions", x => x.id);
                table.ForeignKey(
                    name: "FK_applicability_pending_transitions_applicabilities_tenant_id~",
                    columns: x => new { x.tenant_id, x.applicability_id, x.enrollment_id },
                    principalSchema: "kernel",
                    principalTable: "applicabilities",
                    principalColumns: new[] { "tenant_id", "id", "enrollment_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicability_pending_transitions_effectivities_tenant_id_e~",
                    columns: x => new { x.tenant_id, x.effectivity_id },
                    principalSchema: "kernel",
                    principalTable: "effectivities",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicability_pending_transitions_protocol_versions_tenant_~",
                    columns: x => new { x.tenant_id, x.from_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicability_pending_transitions_protocol_versions_tenant~1",
                    columns: x => new { x.tenant_id, x.target_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "applicability_periods",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                applicability_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                effectivity_id = table.Column<Guid>(type: "uuid", nullable: true),
                consent_status_id = table.Column<Guid>(type: "uuid", nullable: true),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_applicability_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_applicability_periods_applicabilities_tenant_id_applicabili~",
                    columns: x => new { x.tenant_id, x.applicability_id, x.enrollment_id, x.participant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "applicabilities",
                    principalColumns: new[] { "tenant_id", "id", "enrollment_id", "participant_id", "study_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicability_periods_consent_statuses_tenant_id_consent_st~",
                    columns: x => new { x.tenant_id, x.consent_status_id },
                    principalSchema: "kernel",
                    principalTable: "consent_statuses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicability_periods_effectivities_tenant_id_effectivity_id",
                    columns: x => new { x.tenant_id, x.effectivity_id },
                    principalSchema: "kernel",
                    principalTable: "effectivities",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_applicability_periods_protocol_versions_tenant_id_protocol_~",
                    columns: x => new { x.tenant_id, x.protocol_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "consent_status_periods",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                consent_status_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                signed = table.Column<bool>(type: "boolean", nullable: false),
                basis_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                artifact_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                scopes = table.Column<string>(type: "jsonb", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_status_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_consent_status_periods_consent_statuses_tenant_id_consent_s~",
                    columns: x => new { x.tenant_id, x.consent_status_id, x.enrollment_id, x.protocol_version_id },
                    principalSchema: "kernel",
                    principalTable: "consent_statuses",
                    principalColumns: new[] { "tenant_id", "id", "enrollment_id", "protocol_version_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_applicabilities_tenant_id_enrollment_id",
            schema: "kernel",
            table: "applicabilities",
            columns: new[] { "tenant_id", "enrollment_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_applicabilities_tenant_id_governing_version_id",
            schema: "kernel",
            table: "applicabilities",
            columns: new[] { "tenant_id", "governing_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicabilities_tenant_id_participant_id",
            schema: "kernel",
            table: "applicabilities",
            columns: new[] { "tenant_id", "participant_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicabilities_tenant_id_study_id_participant_id",
            schema: "kernel",
            table: "applicabilities",
            columns: new[] { "tenant_id", "study_id", "participant_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicabilities_tenant_id_study_site_id_study_id",
            schema: "kernel",
            table: "applicabilities",
            columns: new[] { "tenant_id", "study_site_id", "study_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_pending_transitions_tenant_id_applicability_i~",
            schema: "kernel",
            table: "applicability_pending_transitions",
            columns: new[] { "tenant_id", "applicability_id", "enrollment_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_pending_transitions_tenant_id_effectivity_id",
            schema: "kernel",
            table: "applicability_pending_transitions",
            columns: new[] { "tenant_id", "effectivity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_pending_transitions_tenant_id_enrollment_id_t~",
            schema: "kernel",
            table: "applicability_pending_transitions",
            columns: new[] { "tenant_id", "enrollment_id", "target_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_pending_transitions_tenant_id_from_version_id",
            schema: "kernel",
            table: "applicability_pending_transitions",
            columns: new[] { "tenant_id", "from_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_pending_transitions_tenant_id_target_version_~",
            schema: "kernel",
            table: "applicability_pending_transitions",
            columns: new[] { "tenant_id", "target_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_periods_tenant_id_applicability_id_enrollment~",
            schema: "kernel",
            table: "applicability_periods",
            columns: new[] { "tenant_id", "applicability_id", "enrollment_id", "participant_id", "study_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_periods_tenant_id_consent_status_id",
            schema: "kernel",
            table: "applicability_periods",
            columns: new[] { "tenant_id", "consent_status_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_periods_tenant_id_effectivity_id",
            schema: "kernel",
            table: "applicability_periods",
            columns: new[] { "tenant_id", "effectivity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_periods_tenant_id_enrollment_id",
            schema: "kernel",
            table: "applicability_periods",
            columns: new[] { "tenant_id", "enrollment_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_periods_tenant_id_protocol_version_id",
            schema: "kernel",
            table: "applicability_periods",
            columns: new[] { "tenant_id", "protocol_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_applicability_periods_tenant_id_study_id_participant_id",
            schema: "kernel",
            table: "applicability_periods",
            columns: new[] { "tenant_id", "study_id", "participant_id" });

        migrationBuilder.CreateIndex(
            name: "IX_consent_status_periods_tenant_id_consent_status_id_enrollme~",
            schema: "kernel",
            table: "consent_status_periods",
            columns: new[] { "tenant_id", "consent_status_id", "enrollment_id", "protocol_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_consent_status_periods_tenant_id_enrollment_id_protocol_ver~",
            schema: "kernel",
            table: "consent_status_periods",
            columns: new[] { "tenant_id", "enrollment_id", "protocol_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_consent_statuses_tenant_id_artifact_version_id",
            schema: "kernel",
            table: "consent_statuses",
            columns: new[] { "tenant_id", "artifact_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_consent_statuses_tenant_id_attestation_id",
            schema: "kernel",
            table: "consent_statuses",
            columns: new[] { "tenant_id", "attestation_id" });

        migrationBuilder.CreateIndex(
            name: "IX_consent_statuses_tenant_id_enrollment_id_protocol_version_id",
            schema: "kernel",
            table: "consent_statuses",
            columns: new[] { "tenant_id", "enrollment_id", "protocol_version_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_consent_statuses_tenant_id_participant_id",
            schema: "kernel",
            table: "consent_statuses",
            columns: new[] { "tenant_id", "participant_id" });

        migrationBuilder.CreateIndex(
            name: "IX_consent_statuses_tenant_id_protocol_version_id",
            schema: "kernel",
            table: "consent_statuses",
            columns: new[] { "tenant_id", "protocol_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_consent_statuses_tenant_id_study_id_participant_id",
            schema: "kernel",
            table: "consent_statuses",
            columns: new[] { "tenant_id", "study_id", "participant_id" });

        migrationBuilder.CreateIndex(
            name: "IX_consent_statuses_tenant_id_study_site_id_study_id",
            schema: "kernel",
            table: "consent_statuses",
            columns: new[] { "tenant_id", "study_site_id", "study_id" });

        migrationBuilder.EnableTenantIsolation(
            "kernel",
            "applicability_periods",
            "applicability_pending_transitions",
            "applicabilities",
            "consent_status_periods",
            "consent_statuses");
        migrationBuilder.CreateBitemporalGuards("kernel", "applicability_periods", "study_id", "participant_id");
        migrationBuilder.CreateBitemporalGuards("kernel", "applicability_pending_transitions", "enrollment_id", "target_version_id");
        migrationBuilder.CreateBitemporalGuards("kernel", "consent_status_periods", "enrollment_id", "protocol_version_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropBitemporalGuards("kernel", "consent_status_periods");
        migrationBuilder.DropBitemporalGuards("kernel", "applicability_pending_transitions");
        migrationBuilder.DropBitemporalGuards("kernel", "applicability_periods");
        migrationBuilder.DisableTenantIsolation(
            "kernel",
            "applicability_periods",
            "applicability_pending_transitions",
            "applicabilities",
            "consent_status_periods",
            "consent_statuses");

        migrationBuilder.DropTable(
            name: "applicability_pending_transitions",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "applicability_periods",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "consent_status_periods",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "applicabilities",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "consent_statuses",
            schema: "kernel");
    }
}
