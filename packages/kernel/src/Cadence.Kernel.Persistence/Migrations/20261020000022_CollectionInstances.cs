using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Form instances (spec §8.1, K-FACT-6, ADR-2622): <c>kernel.collection_instances</c> and their insert-only members. Both
/// are tenant-isolated (ADR-2250); membership never changes (<see cref="InsertOnlySql"/>).
/// </summary>
public partial class CollectionInstances : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "collection_instances",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_site_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                collection_spec_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                planned_activity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                opened_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                saves = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_collection_instances", x => x.id);
                table.UniqueConstraint("AK_collection_instances_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_collection_instances_enrollments_tenant_id_enrollment_id_pa~",
                    columns: x => new { x.tenant_id, x.enrollment_id, x.participant_id, x.study_site_id },
                    principalSchema: "kernel",
                    principalTable: "enrollments",
                    principalColumns: new[] { "tenant_id", "id", "participant_id", "study_site_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_collection_instances_protocol_versions_tenant_id_protocol_v~",
                    columns: x => new { x.tenant_id, x.protocol_version_id },
                    principalSchema: "kernel",
                    principalTable: "protocol_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_collection_instances_study_sites_tenant_id_study_site_id_st~",
                    columns: x => new { x.tenant_id, x.study_site_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "study_sites",
                    principalColumns: new[] { "tenant_id", "id", "study_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_collection_instances_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "collection_instance_members",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                collection_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                item_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_collection_instance_members", x => x.id);
                table.ForeignKey(
                    name: "FK_collection_instance_members_collection_instances_tenant_id_~",
                    columns: x => new { x.tenant_id, x.collection_instance_id },
                    principalSchema: "kernel",
                    principalTable: "collection_instances",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_collection_instance_members_observations_tenant_id_observat~",
                    columns: x => new { x.tenant_id, x.observation_id },
                    principalSchema: "kernel",
                    principalTable: "observations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_collection_instance_members_tenant_id_collection_instance_i~",
            schema: "kernel",
            table: "collection_instance_members",
            columns: new[] { "tenant_id", "collection_instance_id", "item_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_collection_instance_members_tenant_id_observation_id",
            schema: "kernel",
            table: "collection_instance_members",
            columns: new[] { "tenant_id", "observation_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_collection_instances_tenant_id_enrollment_id_participant_id~",
            schema: "kernel",
            table: "collection_instances",
            columns: new[] { "tenant_id", "enrollment_id", "participant_id", "study_site_id" });

        migrationBuilder.CreateIndex(
            name: "IX_collection_instances_tenant_id_protocol_version_id",
            schema: "kernel",
            table: "collection_instances",
            columns: new[] { "tenant_id", "protocol_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_collection_instances_tenant_id_study_site_id_study_id",
            schema: "kernel",
            table: "collection_instances",
            columns: new[] { "tenant_id", "study_site_id", "study_id" });

        migrationBuilder.EnableTenantIsolation("kernel", "collection_instances");
        migrationBuilder.EnableTenantIsolation("kernel", "collection_instance_members");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "collection_instance_members");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "collection_instance_members");
        migrationBuilder.DisableTenantIsolation("kernel", "collection_instance_members");
        migrationBuilder.DisableTenantIsolation("kernel", "collection_instances");
        migrationBuilder.DropTable(
            name: "collection_instance_members",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "collection_instances",
            schema: "kernel");
    }
}
