using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Eisf.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "eisf_document_records",
        "eisf_section_taxonomies",
        "isf_audit_logs",
        "isf_documents",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "TenantId",
            table: "isf_audit_logs",
            newName: "tenant_id");

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "isf_documents",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.Sql(
        """
        ALTER TABLE "isf_audit_logs"
            ALTER COLUMN tenant_id TYPE uuid
            USING CASE
                WHEN tenant_id::text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN tenant_id::text::uuid
                ELSE repeat('0', 32)::uuid
            END;
        ALTER TABLE "isf_audit_logs" ALTER COLUMN tenant_id SET NOT NULL;
        """);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "eisf_section_taxonomies",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "eisf_document_records",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "audit_log_entries",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.CreateIndex(
            name: "IX_isf_documents_tenant_id",
            table: "isf_documents",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_isf_audit_logs_tenant_id",
            table: "isf_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_eisf_section_taxonomies_tenant_id",
            table: "eisf_section_taxonomies",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_eisf_document_records_tenant_id",
            table: "eisf_document_records",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
        migrationBuilder.EnableStandaloneTenantIsolation("eisf", TenantTables);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("eisf", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_isf_documents_tenant_id",
            table: "isf_documents");

        migrationBuilder.DropIndex(
            name: "IX_isf_audit_logs_tenant_id",
            table: "isf_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_eisf_section_taxonomies_tenant_id",
            table: "eisf_section_taxonomies");

        migrationBuilder.DropIndex(
            name: "IX_eisf_document_records_tenant_id",
            table: "eisf_document_records");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "isf_documents");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "eisf_section_taxonomies");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "eisf_document_records");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");

        migrationBuilder.RenameColumn(
            name: "tenant_id",
            table: "isf_audit_logs",
            newName: "TenantId");

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "isf_audit_logs",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid");
    }
}
