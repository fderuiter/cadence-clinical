using System;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Etmf.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "tmf_document_qc_transitions",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "tmf_audit_ledger_seals",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "tmf_document_revisions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Zone = table.Column<int>(type: "integer", nullable: false),
                Section = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ArtifactCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ArtifactType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ApprovalStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TaxonomyVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ContentChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RecordedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tmf_document_revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_tmf_document_revisions_tmf_documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "tmf_documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_revisions_DocumentId",
            table: "tmf_document_revisions",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_revisions_DocumentId_RevisionNumber",
            table: "tmf_document_revisions",
            columns: new[] { "DocumentId", "RevisionNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_revisions_tenant_id",
            table: "tmf_document_revisions",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("etmf", ["tmf_document_revisions"]);
        migrationBuilder.EnableStandaloneInsertOnlyGuards("etmf", ["audit_log_entries", "tmf_audit_ledger_seals", "tmf_audit_logs", "tmf_document_qc_transitions", "tmf_document_revisions"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("etmf", ["tmf_document_revisions"]);
        migrationBuilder.DisableStandaloneInsertOnlyGuards("etmf", ["audit_log_entries", "tmf_audit_ledger_seals", "tmf_audit_logs", "tmf_document_qc_transitions", "tmf_document_revisions"]);

        migrationBuilder.DropTable(
            name: "tmf_document_revisions");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "tmf_document_qc_transitions");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "tmf_audit_ledger_seals");
    }
}
