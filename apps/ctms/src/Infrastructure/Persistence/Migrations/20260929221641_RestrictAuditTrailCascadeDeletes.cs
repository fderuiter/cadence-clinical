using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RestrictAuditTrailCascadeDeletes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ctms_budget_line_items_ctms_investigator_grants_GrantId",
            table: "ctms_budget_line_items");

        migrationBuilder.DropForeignKey(
            name: "FK_ctms_investigator_payables_ctms_investigator_grants_GrantId",
            table: "ctms_investigator_payables");

        migrationBuilder.DropForeignKey(
            name: "FK_ctms_payment_milestones_ctms_investigator_grants_GrantId",
            table: "ctms_payment_milestones");

        migrationBuilder.AddForeignKey(
            name: "FK_ctms_budget_line_items_ctms_investigator_grants_GrantId",
            table: "ctms_budget_line_items",
            column: "GrantId",
            principalTable: "ctms_investigator_grants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ctms_investigator_payables_ctms_investigator_grants_GrantId",
            table: "ctms_investigator_payables",
            column: "GrantId",
            principalTable: "ctms_investigator_grants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ctms_payment_milestones_ctms_investigator_grants_GrantId",
            table: "ctms_payment_milestones",
            column: "GrantId",
            principalTable: "ctms_investigator_grants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ctms_budget_line_items_ctms_investigator_grants_GrantId",
            table: "ctms_budget_line_items");

        migrationBuilder.DropForeignKey(
            name: "FK_ctms_investigator_payables_ctms_investigator_grants_GrantId",
            table: "ctms_investigator_payables");

        migrationBuilder.DropForeignKey(
            name: "FK_ctms_payment_milestones_ctms_investigator_grants_GrantId",
            table: "ctms_payment_milestones");

        migrationBuilder.AddForeignKey(
            name: "FK_ctms_budget_line_items_ctms_investigator_grants_GrantId",
            table: "ctms_budget_line_items",
            column: "GrantId",
            principalTable: "ctms_investigator_grants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_ctms_investigator_payables_ctms_investigator_grants_GrantId",
            table: "ctms_investigator_payables",
            column: "GrantId",
            principalTable: "ctms_investigator_grants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_ctms_payment_milestones_ctms_investigator_grants_GrantId",
            table: "ctms_payment_milestones",
            column: "GrantId",
            principalTable: "ctms_investigator_grants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
