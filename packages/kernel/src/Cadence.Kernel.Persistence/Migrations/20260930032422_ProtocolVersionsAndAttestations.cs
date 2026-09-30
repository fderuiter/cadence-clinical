using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Protocol;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Protocol versions and attestations (spec §6.2, §9.5, ADR-2452): both tables tenant-isolated (ADR-2250), attestations
/// insert-only (<see cref="InsertOnlySql"/>), and protocol versions guarded by the lifecycle trigger in
/// <see cref="ProtocolVersionSql"/>.
/// </summary>
public partial class ProtocolVersionsAndAttestations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "attestations",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                meaning = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                target_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_content_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                authenticated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_attestations", x => x.id);
                table.UniqueConstraint("AK_attestations_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_attestations_target_content_hash", "target_content_hash ~ '^sha256:[0-9a-f]{64}$'");
                table.ForeignKey(
                    name: "FK_attestations_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "protocol_versions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<int>(type: "integer", nullable: false),
                parent_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                content_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: true),
                amendment = table.Column<string>(type: "jsonb", nullable: true),
                approval_attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                withdrawal_attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                withdrawal_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_protocol_versions", x => x.id);
                table.UniqueConstraint("AK_protocol_versions_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_protocol_versions_content_hash", "content_hash IS NULL OR content_hash ~ '^sha256:[0-9a-f]{64}$'");
                table.CheckConstraint("ck_protocol_versions_number", "number > 0");
                table.ForeignKey(
                    name: "FK_protocol_versions_attestations_tenant_id_approval_attestati~",
                    columns: x => new { x.tenant_id, x.approval_attestation_id },
                    principalSchema: "kernel",
                    principalTable: "attestations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_protocol_versions_attestations_tenant_id_withdrawal_attesta~",
                    columns: x => new { x.tenant_id, x.withdrawal_attestation_id },
                    principalSchema: "kernel",
                    principalTable: "attestations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_protocol_versions_protocol_versions_tenant_id_parent_versio~",
                    columns: x => new { x.tenant_id, x.parent_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_protocol_versions_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_protocol_versions_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_attestations_tenant_id_target_kind_target_id",
            schema: "kernel",
            table: "attestations",
            columns: new[] { "tenant_id", "target_kind", "target_id" });

        migrationBuilder.CreateIndex(
            name: "IX_protocol_versions_tenant_id_approval_attestation_id",
            schema: "kernel",
            table: "protocol_versions",
            columns: new[] { "tenant_id", "approval_attestation_id" });

        migrationBuilder.CreateIndex(
            name: "IX_protocol_versions_tenant_id_parent_version_id",
            schema: "kernel",
            table: "protocol_versions",
            columns: new[] { "tenant_id", "parent_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_protocol_versions_tenant_id_study_id_number",
            schema: "kernel",
            table: "protocol_versions",
            columns: new[] { "tenant_id", "study_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_protocol_versions_tenant_id_withdrawal_attestation_id",
            schema: "kernel",
            table: "protocol_versions",
            columns: new[] { "tenant_id", "withdrawal_attestation_id" });

        // Named literally, never read from a shared list, so this migration cannot change once applied.
        migrationBuilder.EnableTenantIsolation("kernel", "attestations", "protocol_versions");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "attestations");
        migrationBuilder.CreateProtocolVersionLifecycleGuards("kernel");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropProtocolVersionLifecycleGuards("kernel");
        migrationBuilder.DropInsertOnlyGuards("kernel", "attestations");
        migrationBuilder.DisableTenantIsolation("kernel", "attestations", "protocol_versions");

        migrationBuilder.DropTable(
            name: "protocol_versions",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "attestations",
            schema: "kernel");
    }
}
