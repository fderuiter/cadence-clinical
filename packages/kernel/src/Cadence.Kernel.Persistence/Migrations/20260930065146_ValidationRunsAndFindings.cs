using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Validation runs and findings (spec §10.3, ADR-2462): runs with the rule packs, datasets and inputs they evaluated and
/// the problems they detected, and the decisions people made on findings. Every table is tenant-scoped with forced
/// Row-Level Security (K-ID-2); runs and their child tables are insert-only, and a decisions record keeps its identity.
/// </summary>
public partial class ValidationRunsAndFindings : Migration
{
    private static readonly string[] Tables =
    [
        "validation_runs", "validation_run_packs", "validation_run_datasets", "validation_run_inputs", "validation_detections",
        "validation_findings",
    ];

    private static readonly string[] RunTables =
        ["validation_runs", "validation_run_packs", "validation_run_datasets", "validation_run_inputs", "validation_detections"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "validation_runs",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                as_of = table.Column<DateOnly>(type: "date", nullable: false),
                standard = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                standard_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                agency = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                detection_count = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_validation_runs", x => x.id);
                table.UniqueConstraint("AK_validation_runs_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_validation_runs_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_runs_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "validation_findings",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                decided_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                decision_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                decided_on_run = table.Column<Guid>(type: "uuid", nullable: true),
                attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_validation_findings", x => x.id);
                table.UniqueConstraint("AK_validation_findings_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_validation_findings_attestations_tenant_id_attestation_id",
                    columns: x => new { x.tenant_id, x.attestation_id },
                    principalSchema: "kernel",
                    principalTable: "attestations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_findings_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_findings_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_findings_validation_runs_tenant_id_decided_on_run",
                    columns: x => new { x.tenant_id, x.decided_on_run },
                    principalSchema: "kernel",
                    principalTable: "validation_runs",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "validation_run_datasets",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_validation_run_datasets", x => new { x.tenant_id, x.run_id, x.dataset });
                table.ForeignKey(
                    name: "FK_validation_run_datasets_validation_runs_tenant_id_run_id",
                    columns: x => new { x.tenant_id, x.run_id },
                    principalSchema: "kernel",
                    principalTable: "validation_runs",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "validation_run_inputs",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                input = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_validation_run_inputs", x => new { x.tenant_id, x.run_id, x.input });
                table.ForeignKey(
                    name: "FK_validation_run_inputs_validation_runs_tenant_id_run_id",
                    columns: x => new { x.tenant_id, x.run_id },
                    principalSchema: "kernel",
                    principalTable: "validation_runs",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "validation_run_packs",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                pack_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                library_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                pack_version = table.Column<int>(type: "integer", nullable: false),
                content_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_validation_run_packs", x => new { x.tenant_id, x.run_id, x.pack_key });
                table.ForeignKey(
                    name: "FK_validation_run_packs_library_items_tenant_id_library_item_id",
                    columns: x => new { x.tenant_id, x.library_item_id },
                    principalSchema: "kernel",
                    principalTable: "library_items",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_run_packs_validation_runs_tenant_id_run_id",
                    columns: x => new { x.tenant_id, x.run_id },
                    principalSchema: "kernel",
                    principalTable: "validation_runs",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "validation_detections",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                pack_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                pack_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                rule_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                rule_version = table.Column<int>(type: "integer", nullable: false),
                severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                target_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                variable = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                expected = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                observed = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_validation_detections", x => new { x.tenant_id, x.run_id, x.finding_id });
                table.ForeignKey(
                    name: "FK_validation_detections_validation_run_datasets_tenant_id_run~",
                    columns: x => new { x.tenant_id, x.run_id, x.dataset },
                    principalSchema: "kernel",
                    principalTable: "validation_run_datasets",
                    principalColumns: new[] { "tenant_id", "run_id", "dataset" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_detections_validation_run_packs_tenant_id_run_id~",
                    columns: x => new { x.tenant_id, x.run_id, x.pack_key },
                    principalSchema: "kernel",
                    principalTable: "validation_run_packs",
                    principalColumns: new[] { "tenant_id", "run_id", "pack_key" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_detections_validation_runs_tenant_id_run_id",
                    columns: x => new { x.tenant_id, x.run_id },
                    principalSchema: "kernel",
                    principalTable: "validation_runs",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_validation_detections_tenant_id_finding_id",
            schema: "kernel",
            table: "validation_detections",
            columns: new[] { "tenant_id", "finding_id" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_detections_tenant_id_run_id_dataset",
            schema: "kernel",
            table: "validation_detections",
            columns: new[] { "tenant_id", "run_id", "dataset" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_detections_tenant_id_run_id_pack_key",
            schema: "kernel",
            table: "validation_detections",
            columns: new[] { "tenant_id", "run_id", "pack_key" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_detections_tenant_id_study_id_stage",
            schema: "kernel",
            table: "validation_detections",
            columns: new[] { "tenant_id", "study_id", "stage" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_findings_tenant_id_attestation_id",
            schema: "kernel",
            table: "validation_findings",
            columns: new[] { "tenant_id", "attestation_id" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_findings_tenant_id_decided_on_run",
            schema: "kernel",
            table: "validation_findings",
            columns: new[] { "tenant_id", "decided_on_run" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_findings_tenant_id_study_id",
            schema: "kernel",
            table: "validation_findings",
            columns: new[] { "tenant_id", "study_id" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_run_packs_tenant_id_library_item_id",
            schema: "kernel",
            table: "validation_run_packs",
            columns: new[] { "tenant_id", "library_item_id" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_runs_tenant_id_study_id_stage_created_at",
            schema: "kernel",
            table: "validation_runs",
            columns: new[] { "tenant_id", "study_id", "stage", "created_at" });

        migrationBuilder.EnableTenantIsolation("kernel", Tables);
        migrationBuilder.CreateInsertOnlyGuards("kernel", RunTables);
        migrationBuilder.CreateImmutableColumnsGuard(
            "kernel", "validation_findings", "id", "tenant_id", "study_id", "created_at", "created_by");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropImmutableColumnsGuard("kernel", "validation_findings");
        migrationBuilder.DropInsertOnlyGuards("kernel", RunTables);
        migrationBuilder.DisableTenantIsolation("kernel", Tables);

        migrationBuilder.DropTable(
            name: "validation_detections",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "validation_findings",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "validation_run_inputs",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "validation_run_datasets",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "validation_run_packs",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "validation_runs",
            schema: "kernel");
    }
}
