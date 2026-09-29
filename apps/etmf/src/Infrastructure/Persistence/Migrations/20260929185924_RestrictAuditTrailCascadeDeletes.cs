using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Etmf.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RestrictAuditTrailCascadeDeletes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_tmf_document_expiration_alert_states_tmf_documents_Document~",
            table: "tmf_document_expiration_alert_states");

        migrationBuilder.DropForeignKey(
            name: "FK_tmf_document_qc_transitions_tmf_documents_DocumentId",
            table: "tmf_document_qc_transitions");

        migrationBuilder.AddForeignKey(
            name: "FK_tmf_document_expiration_alert_states_tmf_documents_Document~",
            table: "tmf_document_expiration_alert_states",
            column: "DocumentId",
            principalTable: "tmf_documents",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_tmf_document_qc_transitions_tmf_documents_DocumentId",
            table: "tmf_document_qc_transitions",
            column: "DocumentId",
            principalTable: "tmf_documents",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_tmf_document_expiration_alert_states_tmf_documents_Document~",
            table: "tmf_document_expiration_alert_states");

        migrationBuilder.DropForeignKey(
            name: "FK_tmf_document_qc_transitions_tmf_documents_DocumentId",
            table: "tmf_document_qc_transitions");

        migrationBuilder.AddForeignKey(
            name: "FK_tmf_document_expiration_alert_states_tmf_documents_Document~",
            table: "tmf_document_expiration_alert_states",
            column: "DocumentId",
            principalTable: "tmf_documents",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_tmf_document_qc_transitions_tmf_documents_DocumentId",
            table: "tmf_document_qc_transitions",
            column: "DocumentId",
            principalTable: "tmf_documents",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
