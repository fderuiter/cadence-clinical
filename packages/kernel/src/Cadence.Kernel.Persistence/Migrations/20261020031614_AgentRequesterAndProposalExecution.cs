using System;

using Cadence.Kernel.Persistence.Standards;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Agents act for a requesting person, and approved proposals execute (ADR-2632, K-AI-2, K-AI-6). A proposal by an
/// automation agent records the person it was made for, fixed with the other content columns; the immutable-columns
/// guard now also fixes <c>confidence_score</c>, which the content hash already covered. A provenance activity records
/// the person an agent acted for and the approved proposal a command executed.
/// </summary>
public partial class AgentRequesterAndProposalExecution : Migration
{
    private static readonly string[] PreviousContentColumns =
    [
        "id", "tenant_id", "study_id", "intent", "plan_artifact_version_id", "affected", "simulated_impact",
        "requested_capabilities", "expires_at", "proposed_at", "proposed_by_id", "proposed_by_kind", "model_id",
        "content_hash", "created_at", "created_by",
    ];

    private static readonly string[] ContentColumns =
    [
        .. PreviousContentColumns, "requested_by_id", "confidence_score",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "proposal_id",
            schema: "kernel",
            table: "provenance_activities",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "requested_by",
            schema: "kernel",
            table: "provenance_activities",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "requested_by_id",
            schema: "kernel",
            table: "proposals",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_provenance_activities_tenant_id_proposal_id",
            schema: "kernel",
            table: "provenance_activities",
            columns: new[] { "tenant_id", "proposal_id" },
            filter: "proposal_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_proposals_requested_by",
            schema: "kernel",
            table: "proposals",
            sql: "(proposed_by_kind = 'AutomationAgent' AND requested_by_id IS NOT NULL AND char_length(requested_by_id) > 0) OR (proposed_by_kind <> 'AutomationAgent' AND requested_by_id IS NULL)");

        migrationBuilder.DropImmutableColumnsGuard("kernel", "proposals");
        migrationBuilder.CreateImmutableColumnsGuard("kernel", "proposals", ContentColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropImmutableColumnsGuard("kernel", "proposals");
        migrationBuilder.CreateImmutableColumnsGuard("kernel", "proposals", PreviousContentColumns);

        migrationBuilder.DropIndex(
            name: "IX_provenance_activities_tenant_id_proposal_id",
            schema: "kernel",
            table: "provenance_activities");

        migrationBuilder.DropCheckConstraint(
            name: "ck_proposals_requested_by",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "proposal_id",
            schema: "kernel",
            table: "provenance_activities");

        migrationBuilder.DropColumn(
            name: "requested_by",
            schema: "kernel",
            table: "provenance_activities");

        migrationBuilder.DropColumn(
            name: "requested_by_id",
            schema: "kernel",
            table: "proposals");
    }
}
