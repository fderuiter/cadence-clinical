using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "AuditLogEntries",
        "bulk_lab_range_import_jobs",
        "bulk_synonym_import_jobs",
        "cec_chair_adjudications",
        "cec_dossiers",
        "cgt_bedside_verifications",
        "cgt_chains_of_custody",
        "cgt_destruction_certificates",
        "cgt_milestone_events",
        "cgt_vial_return_reconciliations",
        "clinical_subjects",
        "device_deficiencies",
        "device_inventory_items",
        "device_quarantine_records",
        "ip_kit_inventory_items",
        "ip_kit_reconciliation_batches",
        "ip_kit_reconciliation_certificates",
        "lab_reference_ranges",
        "randomization_allocations",
        "randomization_list_slots",
        "randomization_schemes",
        "site_compliance_caches",
        "staged_adverse_event_reviews",
        "subject_consents",
        "synonym_rules",
    ];

protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "synonym_rules",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "subject_consents",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "staged_adverse_event_reviews",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "site_compliance_caches",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "randomization_schemes",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "randomization_list_slots",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "randomization_allocations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "lab_reference_ranges",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ip_kit_reconciliation_certificates",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ip_kit_reconciliation_batches",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ip_kit_inventory_items",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "device_quarantine_records",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "device_inventory_items",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "device_deficiencies",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "clinical_subjects",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "cgt_vial_return_reconciliations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "cgt_milestone_events",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "cgt_destruction_certificates",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "cgt_chains_of_custody",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "cgt_bedside_verifications",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "cec_dossiers",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "cec_chair_adjudications",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "bulk_synonym_import_jobs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "bulk_lab_range_import_jobs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "AuditLogEntries",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.CreateIndex(
            name: "IX_synonym_rules_tenant_id",
            table: "synonym_rules",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consents_tenant_id",
            table: "subject_consents",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_staged_adverse_event_reviews_tenant_id",
            table: "staged_adverse_event_reviews",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_site_compliance_caches_tenant_id",
            table: "site_compliance_caches",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_randomization_schemes_tenant_id",
            table: "randomization_schemes",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_randomization_list_slots_tenant_id",
            table: "randomization_list_slots",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_randomization_allocations_tenant_id",
            table: "randomization_allocations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_lab_reference_ranges_tenant_id",
            table: "lab_reference_ranges",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ip_kit_reconciliation_certificates_tenant_id",
            table: "ip_kit_reconciliation_certificates",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ip_kit_reconciliation_batches_tenant_id",
            table: "ip_kit_reconciliation_batches",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ip_kit_inventory_items_tenant_id",
            table: "ip_kit_inventory_items",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_device_quarantine_records_tenant_id",
            table: "device_quarantine_records",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_device_inventory_items_tenant_id",
            table: "device_inventory_items",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_device_deficiencies_tenant_id",
            table: "device_deficiencies",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_tenant_id",
            table: "clinical_subjects",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_vial_return_reconciliations_tenant_id",
            table: "cgt_vial_return_reconciliations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_milestone_events_tenant_id",
            table: "cgt_milestone_events",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_destruction_certificates_tenant_id",
            table: "cgt_destruction_certificates",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_chains_of_custody_tenant_id",
            table: "cgt_chains_of_custody",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_bedside_verifications_tenant_id",
            table: "cgt_bedside_verifications",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_cec_dossiers_tenant_id",
            table: "cec_dossiers",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_cec_chair_adjudications_tenant_id",
            table: "cec_chair_adjudications",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_bulk_synonym_import_jobs_tenant_id",
            table: "bulk_synonym_import_jobs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_bulk_lab_range_import_jobs_tenant_id",
            table: "bulk_lab_range_import_jobs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogEntries_tenant_id",
            table: "AuditLogEntries",
            column: "tenant_id");
            migrationBuilder.EnableStandaloneTenantIsolation("edc", TenantTables);
}

protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.DisableStandaloneTenantIsolation("edc", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_synonym_rules_tenant_id",
            table: "synonym_rules");

        migrationBuilder.DropIndex(
            name: "IX_subject_consents_tenant_id",
            table: "subject_consents");

        migrationBuilder.DropIndex(
            name: "IX_staged_adverse_event_reviews_tenant_id",
            table: "staged_adverse_event_reviews");

        migrationBuilder.DropIndex(
            name: "IX_site_compliance_caches_tenant_id",
            table: "site_compliance_caches");

        migrationBuilder.DropIndex(
            name: "IX_randomization_schemes_tenant_id",
            table: "randomization_schemes");

        migrationBuilder.DropIndex(
            name: "IX_randomization_list_slots_tenant_id",
            table: "randomization_list_slots");

        migrationBuilder.DropIndex(
            name: "IX_randomization_allocations_tenant_id",
            table: "randomization_allocations");

        migrationBuilder.DropIndex(
            name: "IX_lab_reference_ranges_tenant_id",
            table: "lab_reference_ranges");

        migrationBuilder.DropIndex(
            name: "IX_ip_kit_reconciliation_certificates_tenant_id",
            table: "ip_kit_reconciliation_certificates");

        migrationBuilder.DropIndex(
            name: "IX_ip_kit_reconciliation_batches_tenant_id",
            table: "ip_kit_reconciliation_batches");

        migrationBuilder.DropIndex(
            name: "IX_ip_kit_inventory_items_tenant_id",
            table: "ip_kit_inventory_items");

        migrationBuilder.DropIndex(
            name: "IX_device_quarantine_records_tenant_id",
            table: "device_quarantine_records");

        migrationBuilder.DropIndex(
            name: "IX_device_inventory_items_tenant_id",
            table: "device_inventory_items");

        migrationBuilder.DropIndex(
            name: "IX_device_deficiencies_tenant_id",
            table: "device_deficiencies");

        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_tenant_id",
            table: "clinical_subjects");

        migrationBuilder.DropIndex(
            name: "IX_cgt_vial_return_reconciliations_tenant_id",
            table: "cgt_vial_return_reconciliations");

        migrationBuilder.DropIndex(
            name: "IX_cgt_milestone_events_tenant_id",
            table: "cgt_milestone_events");

        migrationBuilder.DropIndex(
            name: "IX_cgt_destruction_certificates_tenant_id",
            table: "cgt_destruction_certificates");

        migrationBuilder.DropIndex(
            name: "IX_cgt_chains_of_custody_tenant_id",
            table: "cgt_chains_of_custody");

        migrationBuilder.DropIndex(
            name: "IX_cgt_bedside_verifications_tenant_id",
            table: "cgt_bedside_verifications");

        migrationBuilder.DropIndex(
            name: "IX_cec_dossiers_tenant_id",
            table: "cec_dossiers");

        migrationBuilder.DropIndex(
            name: "IX_cec_chair_adjudications_tenant_id",
            table: "cec_chair_adjudications");

        migrationBuilder.DropIndex(
            name: "IX_bulk_synonym_import_jobs_tenant_id",
            table: "bulk_synonym_import_jobs");

        migrationBuilder.DropIndex(
            name: "IX_bulk_lab_range_import_jobs_tenant_id",
            table: "bulk_lab_range_import_jobs");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogEntries_tenant_id",
            table: "AuditLogEntries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "synonym_rules");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "subject_consents");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "staged_adverse_event_reviews");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "site_compliance_caches");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "randomization_schemes");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "randomization_list_slots");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "randomization_allocations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "lab_reference_ranges");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ip_kit_reconciliation_certificates");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ip_kit_reconciliation_batches");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ip_kit_inventory_items");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "device_quarantine_records");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "device_inventory_items");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "device_deficiencies");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "clinical_subjects");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "cgt_vial_return_reconciliations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "cgt_milestone_events");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "cgt_destruction_certificates");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "cgt_chains_of_custody");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "cgt_bedside_verifications");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "cec_chair_adjudications");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "bulk_synonym_import_jobs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "bulk_lab_range_import_jobs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "AuditLogEntries");
    }
}
