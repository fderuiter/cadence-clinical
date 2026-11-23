using System;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Eisf.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "isf_audit_logs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "eisf_section_taxonomies",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "eisf_document_revisions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SectionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Sha256Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                VersionMajor = table.Column<int>(type: "integer", nullable: false),
                VersionMinor = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ExpirationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RecordedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_eisf_document_revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_eisf_document_revisions_eisf_document_records_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "eisf_document_records",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_eisf_document_revisions_DocumentId",
            table: "eisf_document_revisions",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_eisf_document_revisions_DocumentId_RevisionNumber",
            table: "eisf_document_revisions",
            columns: new[] { "DocumentId", "RevisionNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_eisf_document_revisions_tenant_id",
            table: "eisf_document_revisions",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("eisf", ["eisf_document_revisions"]);
        migrationBuilder.EnableStandaloneInsertOnlyGuards("eisf", ["audit_log_entries", "eisf_document_revisions", "isf_audit_logs"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("eisf", ["eisf_document_revisions"]);
        migrationBuilder.DisableStandaloneInsertOnlyGuards("eisf", ["audit_log_entries", "eisf_document_revisions", "isf_audit_logs"]);

        migrationBuilder.DropTable(
            name: "eisf_document_revisions");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "isf_audit_logs");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "eisf_section_taxonomies");
    }
}
