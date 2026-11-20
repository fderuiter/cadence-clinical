using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "biometric_liveness_verifications",
        "comprehension_checks",
        "comprehension_results",
        "consent_audit_logs",
        "consent_clauses",
        "consent_documents",
        "consent_signatures",
        "consent_templates",
        "consent_translations",
        "consent_withdrawals",
        "etmf_archival_deliveries",
        "granular_consent_options",
        "reconsent_requirements",
        "subject_consent_option_selections",
        "subject_consents",
    ];

protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "TenantId",
            table: "consent_audit_logs",
            newName: "tenant_id");

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "subject_consents",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "subject_consent_option_selections",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "reconsent_requirements",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "granular_consent_options",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "etmf_archival_deliveries",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "consent_withdrawals",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "consent_translations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "consent_templates",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "consent_signatures",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "consent_documents",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "consent_clauses",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.Sql(
        """
        ALTER TABLE "consent_audit_logs"
            ALTER COLUMN tenant_id TYPE uuid
            USING CASE
                WHEN tenant_id::text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN tenant_id::text::uuid
                ELSE '00000000-0000-0000-0000-000000000000'::uuid
            END;
        ALTER TABLE "consent_audit_logs" ALTER COLUMN tenant_id SET NOT NULL;
        """);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "comprehension_results",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "comprehension_checks",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "biometric_liveness_verifications",
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
            name: "IX_subject_consents_tenant_id",
            table: "subject_consents",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consent_option_selections_tenant_id",
            table: "subject_consent_option_selections",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_reconsent_requirements_tenant_id",
            table: "reconsent_requirements",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_granular_consent_options_tenant_id",
            table: "granular_consent_options",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_etmf_archival_deliveries_tenant_id",
            table: "etmf_archival_deliveries",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_consent_withdrawals_tenant_id",
            table: "consent_withdrawals",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_consent_translations_tenant_id",
            table: "consent_translations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_consent_templates_tenant_id",
            table: "consent_templates",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_consent_signatures_tenant_id",
            table: "consent_signatures",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_consent_documents_tenant_id",
            table: "consent_documents",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_consent_clauses_tenant_id",
            table: "consent_clauses",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_consent_audit_logs_tenant_id",
            table: "consent_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_comprehension_results_tenant_id",
            table: "comprehension_results",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_comprehension_checks_tenant_id",
            table: "comprehension_checks",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_biometric_liveness_verifications_tenant_id",
            table: "biometric_liveness_verifications",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
            migrationBuilder.EnableStandaloneTenantIsolation("econsent", TenantTables);
}

protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.DisableStandaloneTenantIsolation("econsent", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_subject_consents_tenant_id",
            table: "subject_consents");

        migrationBuilder.DropIndex(
            name: "IX_subject_consent_option_selections_tenant_id",
            table: "subject_consent_option_selections");

        migrationBuilder.DropIndex(
            name: "IX_reconsent_requirements_tenant_id",
            table: "reconsent_requirements");

        migrationBuilder.DropIndex(
            name: "IX_granular_consent_options_tenant_id",
            table: "granular_consent_options");

        migrationBuilder.DropIndex(
            name: "IX_etmf_archival_deliveries_tenant_id",
            table: "etmf_archival_deliveries");

        migrationBuilder.DropIndex(
            name: "IX_consent_withdrawals_tenant_id",
            table: "consent_withdrawals");

        migrationBuilder.DropIndex(
            name: "IX_consent_translations_tenant_id",
            table: "consent_translations");

        migrationBuilder.DropIndex(
            name: "IX_consent_templates_tenant_id",
            table: "consent_templates");

        migrationBuilder.DropIndex(
            name: "IX_consent_signatures_tenant_id",
            table: "consent_signatures");

        migrationBuilder.DropIndex(
            name: "IX_consent_documents_tenant_id",
            table: "consent_documents");

        migrationBuilder.DropIndex(
            name: "IX_consent_clauses_tenant_id",
            table: "consent_clauses");

        migrationBuilder.DropIndex(
            name: "IX_consent_audit_logs_tenant_id",
            table: "consent_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_comprehension_results_tenant_id",
            table: "comprehension_results");

        migrationBuilder.DropIndex(
            name: "IX_comprehension_checks_tenant_id",
            table: "comprehension_checks");

        migrationBuilder.DropIndex(
            name: "IX_biometric_liveness_verifications_tenant_id",
            table: "biometric_liveness_verifications");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "subject_consents");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "subject_consent_option_selections");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "reconsent_requirements");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "granular_consent_options");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "etmf_archival_deliveries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "consent_withdrawals");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "consent_translations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "consent_templates");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "consent_signatures");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "consent_documents");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "consent_clauses");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "comprehension_results");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "comprehension_checks");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "biometric_liveness_verifications");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");

        migrationBuilder.RenameColumn(
            name: "tenant_id",
            table: "consent_audit_logs",
            newName: "TenantId");

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "consent_audit_logs",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid");
    }
}
