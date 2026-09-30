using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Evidence artifacts (spec §8.3, ADR-2454): an artifact and its immutable versions, each naming the hash, size, media
/// type and storage location of its bytes. Both tables are tenant-isolated (ADR-2250); versions are insert-only
/// (<see cref="InsertOnlySql"/>, KA001).
/// </summary>
public partial class ArtifactsAndVersions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "artifacts",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: true),
                title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artifacts", x => x.id);
                table.UniqueConstraint("AK_artifacts_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_artifacts_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_artifacts_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "artifact_versions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<int>(type: "integer", nullable: false),
                content_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                media_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                storage_ref = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artifact_versions", x => x.id);
                table.UniqueConstraint("AK_artifact_versions_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_artifact_versions_content_hash", "content_hash ~ '^sha256:[0-9a-f]{64}$'");
                table.CheckConstraint("ck_artifact_versions_number", "number > 0");
                table.CheckConstraint("ck_artifact_versions_size", "size_bytes > 0");
                table.ForeignKey(
                    name: "FK_artifact_versions_artifacts_tenant_id_artifact_id",
                    columns: x => new { x.tenant_id, x.artifact_id },
                    principalSchema: "kernel",
                    principalTable: "artifacts",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_artifact_versions_tenant_id_artifact_id_content_hash",
            schema: "kernel",
            table: "artifact_versions",
            columns: new[] { "tenant_id", "artifact_id", "content_hash" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_artifact_versions_tenant_id_artifact_id_number",
            schema: "kernel",
            table: "artifact_versions",
            columns: new[] { "tenant_id", "artifact_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_artifacts_tenant_id_study_id_kind",
            schema: "kernel",
            table: "artifacts",
            columns: new[] { "tenant_id", "study_id", "kind" });

        migrationBuilder.EnableTenantIsolation("kernel", "artifact_versions", "artifacts");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "artifact_versions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "artifact_versions");
        migrationBuilder.DisableTenantIsolation("kernel", "artifact_versions", "artifacts");

        migrationBuilder.DropTable(
            name: "artifact_versions",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "artifacts",
            schema: "kernel");
    }
}
