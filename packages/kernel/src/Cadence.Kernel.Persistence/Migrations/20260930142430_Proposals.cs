using System;

using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Proposals (ADR-2489, K033): suggested changes to a study by a person or an automation agent, with their approval,
/// rejection or withdrawal. The table is tenant-isolated (ADR-2250), and the immutable-columns trigger fixes the content
/// columns, so only the decision changes and an approval's signed content hash always matches the content (21 CFR 11.70).
/// </summary>
public partial class Proposals : Migration
{
    private static readonly string[] ContentColumns =
    [
        "id", "tenant_id", "study_id", "intent", "plan_artifact_version_id", "affected", "simulated_impact",
        "requested_capabilities", "expires_at", "proposed_at", "proposed_by_id", "proposed_by_kind", "model_id",
        "content_hash", "created_at", "created_by",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "proposals",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                intent = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                plan_artifact_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                affected = table.Column<string[]>(type: "text[]", nullable: false),
                simulated_impact = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: true),
                requested_capabilities = table.Column<string[]>(type: "text[]", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                proposed_by_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                proposed_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                content_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                decided_by_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                decided_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                approval_attestation_id = table.Column<Guid>(type: "uuid", nullable: true),
                decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_proposals", x => x.id);
                table.UniqueConstraint("AK_proposals_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_proposals_affected", "cardinality(affected) <= 200");
                table.CheckConstraint("ck_proposals_content_hash", "content_hash ~ '^sha256:[0-9a-f]{64}$'");
                table.CheckConstraint("ck_proposals_decision", "(status = 'Open' AND decided_at IS NULL AND decided_by_id IS NULL AND decided_by_kind IS NULL AND approval_attestation_id IS NULL AND decision_reason IS NULL) OR (status = 'Approved' AND decided_at IS NOT NULL AND decided_by_id IS NOT NULL AND decided_by_kind = 'Person' AND approval_attestation_id IS NOT NULL) OR (status IN ('Rejected', 'Withdrawn') AND decided_at IS NOT NULL AND decided_by_id IS NOT NULL AND decided_by_kind IS NOT NULL AND approval_attestation_id IS NULL AND decision_reason IS NOT NULL)");
                table.CheckConstraint("ck_proposals_expiry", "expires_at > proposed_at AND expires_at <= proposed_at + interval '90 days'");
                table.CheckConstraint("ck_proposals_intent", "char_length(intent) BETWEEN 1 AND 2000");
                table.CheckConstraint("ck_proposals_model", "(proposed_by_kind = 'AutomationAgent' AND model_id IS NOT NULL AND char_length(model_id) > 0) OR (proposed_by_kind <> 'AutomationAgent' AND model_id IS NULL)");
                table.CheckConstraint("ck_proposals_requested_capabilities", "cardinality(requested_capabilities) BETWEEN 1 AND 20");
                table.CheckConstraint("ck_proposals_simulated_impact", "simulated_impact IS NULL OR simulated_impact ~ '^sha256:[0-9a-f]{64}$'");
                table.ForeignKey(
                    name: "FK_proposals_artifact_versions_tenant_id_plan_artifact_version~",
                    columns: x => new { x.tenant_id, x.plan_artifact_version_id },
                    principalSchema: "kernel",
                    principalTable: "artifact_versions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_proposals_attestations_tenant_id_approval_attestation_id",
                    columns: x => new { x.tenant_id, x.approval_attestation_id },
                    principalSchema: "kernel",
                    principalTable: "attestations",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_proposals_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_proposals_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_proposals_tenant_id_approval_attestation_id",
            schema: "kernel",
            table: "proposals",
            columns: new[] { "tenant_id", "approval_attestation_id" });

        migrationBuilder.CreateIndex(
            name: "IX_proposals_tenant_id_plan_artifact_version_id",
            schema: "kernel",
            table: "proposals",
            columns: new[] { "tenant_id", "plan_artifact_version_id" });

        migrationBuilder.CreateIndex(
            name: "IX_proposals_tenant_id_study_id_status",
            schema: "kernel",
            table: "proposals",
            columns: new[] { "tenant_id", "study_id", "status" });

        migrationBuilder.EnableTenantIsolation("kernel", "proposals");
        migrationBuilder.CreateImmutableColumnsGuard("kernel", "proposals", ContentColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropImmutableColumnsGuard("kernel", "proposals");
        migrationBuilder.DisableTenantIsolation("kernel", "proposals");

        migrationBuilder.DropTable(
            name: "proposals",
            schema: "kernel");
    }
}
