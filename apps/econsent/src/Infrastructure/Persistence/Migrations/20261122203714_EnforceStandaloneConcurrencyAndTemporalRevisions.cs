using System;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "subject_consent_option_selections",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "reconsent_requirements",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "consent_withdrawals",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "consent_audit_logs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "biometric_liveness_verifications",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "consent_document_revisions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DocumentName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RecordedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_document_revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_consent_document_revisions_consent_documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "consent_documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_consent_document_revisions_DocumentId",
            table: "consent_document_revisions",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_document_revisions_DocumentId_RevisionNumber",
            table: "consent_document_revisions",
            columns: new[] { "DocumentId", "RevisionNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_consent_document_revisions_tenant_id",
            table: "consent_document_revisions",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("econsent", ["consent_document_revisions"]);
        migrationBuilder.EnableStandaloneInsertOnlyGuards("econsent", ["audit_log_entries", "consent_audit_logs", "consent_document_revisions"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("econsent", ["consent_document_revisions"]);
        migrationBuilder.DisableStandaloneInsertOnlyGuards("econsent", ["audit_log_entries", "consent_audit_logs", "consent_document_revisions"]);

        migrationBuilder.DropTable(
            name: "consent_document_revisions");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "subject_consent_option_selections");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "reconsent_requirements");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "consent_withdrawals");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "consent_audit_logs");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "biometric_liveness_verifications");
    }
}
