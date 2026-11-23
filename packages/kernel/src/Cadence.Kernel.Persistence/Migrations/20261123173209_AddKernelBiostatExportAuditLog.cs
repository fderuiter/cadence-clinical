using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <inheritdoc />
public partial class AddKernelBiostatExportAuditLog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "biostat_export_audit_logs",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: true),
                artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                format = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                lock_filter = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                valid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                known_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                is_unblinded = table.Column<bool>(type: "boolean", nullable: false),
                details_json = table.Column<string>(type: "jsonb", nullable: false),
                sha256_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_biostat_export_audit_logs", x => x.id);
                table.ForeignKey(
                    name: "FK_biostat_export_audit_logs_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_biostat_export_audit_logs_activity_id",
            schema: "kernel",
            table: "biostat_export_audit_logs",
            column: "activity_id");

        migrationBuilder.CreateIndex(
            name: "IX_biostat_export_audit_logs_tenant_id_study_id_timestamp",
            schema: "kernel",
            table: "biostat_export_audit_logs",
            columns: new[] { "tenant_id", "study_id", "timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_biostat_export_audit_logs_tenant_id_timestamp",
            schema: "kernel",
            table: "biostat_export_audit_logs",
            columns: new[] { "tenant_id", "timestamp" });

        migrationBuilder.EnableTenantIsolation("kernel", "biostat_export_audit_logs");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "biostat_export_audit_logs");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "biostat_export_audit_logs");
        migrationBuilder.DisableTenantIsolation("kernel", "biostat_export_audit_logs");

        migrationBuilder.DropTable(
            name: "biostat_export_audit_logs",
            schema: "kernel");
    }
}
