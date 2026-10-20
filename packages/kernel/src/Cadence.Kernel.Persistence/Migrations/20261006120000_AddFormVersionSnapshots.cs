using System;

using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Migration adding kernel.form_version_snapshots table.
/// Persists compiled USDM form version snapshots created on protocol version approval.
/// </summary>
public partial class AddFormVersionSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "form_version_snapshots",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                protocol_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                form_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                form_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                open_rosa_xml = table.Column<string>(type: "text", nullable: false),
                ddf_ir = table.Column<string>(type: "jsonb", nullable: false),
                odm_xml = table.Column<string>(type: "text", nullable: true),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_version_snapshots", x => x.id);
                table.CheckConstraint("ck_form_version_snapshots_form_hash", "form_hash ~ '^sha256:[0-9a-f]{64}$'");
                table.ForeignKey(
                    name: "FK_form_version_snapshots_protocol_versions_tenant_id_protoc~",
                    columns: x => new { x.tenant_id, x.protocol_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_form_version_snapshots_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_form_version_snapshots_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_form_version_snapshots_tenant_id_protocol_version_id",
            schema: "kernel",
            table: "form_version_snapshots",
            columns: new[] { "tenant_id", "protocol_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_form_version_snapshots_tenant_id_study_id_protocol_versio~",
            schema: "kernel",
            table: "form_version_snapshots",
            columns: new[] { "tenant_id", "study_id", "protocol_version", "form_id" },
            unique: true);

        migrationBuilder.EnableTenantIsolation("kernel", "form_version_snapshots");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableTenantIsolation("kernel", "form_version_snapshots");

        migrationBuilder.DropTable(
            name: "form_version_snapshots",
            schema: "kernel");
    }
}
