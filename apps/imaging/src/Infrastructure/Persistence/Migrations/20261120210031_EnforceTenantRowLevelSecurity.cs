using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Imaging.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "imaging_adjudication_sessions",
        "imaging_audit_logs",
        "imaging_caliper_annotations",
        "imaging_deid_certificates",
        "imaging_instance_envelopes",
        "imaging_series_envelopes",
        "imaging_study_envelopes",
        "imaging_tumor_response_assessments",
    ];

protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "TenantId",
            table: "imaging_study_envelopes",
            newName: "tenant_id");

        migrationBuilder.RenameIndex(
            name: "IX_imaging_study_envelopes_TenantId",
            table: "imaging_study_envelopes",
            newName: "IX_imaging_study_envelopes_tenant_id");

        migrationBuilder.RenameColumn(
            name: "TenantId",
            table: "imaging_adjudication_sessions",
            newName: "tenant_id");

        migrationBuilder.RenameIndex(
            name: "IX_imaging_adjudication_sessions_TenantId",
            table: "imaging_adjudication_sessions",
            newName: "IX_imaging_adjudication_sessions_tenant_id");

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "imaging_tumor_response_assessments",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.Sql(
        """
        ALTER TABLE "imaging_study_envelopes"
            ALTER COLUMN tenant_id TYPE uuid
            USING CASE
                WHEN tenant_id::text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN tenant_id::text::uuid
                ELSE '00000000-0000-0000-0000-000000000000'::uuid
            END;
        ALTER TABLE "imaging_study_envelopes" ALTER COLUMN tenant_id SET NOT NULL;
        """);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "imaging_series_envelopes",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "imaging_instance_envelopes",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "imaging_deid_certificates",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "imaging_caliper_annotations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "imaging_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.Sql(
        """
        ALTER TABLE "imaging_adjudication_sessions"
            ALTER COLUMN tenant_id TYPE uuid
            USING CASE
                WHEN tenant_id::text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN tenant_id::text::uuid
                ELSE '00000000-0000-0000-0000-000000000000'::uuid
            END;
        ALTER TABLE "imaging_adjudication_sessions" ALTER COLUMN tenant_id SET NOT NULL;
        """);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "audit_log_entries",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.CreateIndex(
            name: "IX_imaging_tumor_response_assessments_tenant_id",
            table: "imaging_tumor_response_assessments",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_series_envelopes_tenant_id",
            table: "imaging_series_envelopes",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_instance_envelopes_tenant_id",
            table: "imaging_instance_envelopes",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_tenant_id",
            table: "imaging_deid_certificates",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_tenant_id",
            table: "imaging_caliper_annotations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_audit_logs_tenant_id",
            table: "imaging_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
            migrationBuilder.EnableStandaloneTenantIsolation("imaging", TenantTables);
}

protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.DisableStandaloneTenantIsolation("imaging", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_imaging_tumor_response_assessments_tenant_id",
            table: "imaging_tumor_response_assessments");

        migrationBuilder.DropIndex(
            name: "IX_imaging_series_envelopes_tenant_id",
            table: "imaging_series_envelopes");

        migrationBuilder.DropIndex(
            name: "IX_imaging_instance_envelopes_tenant_id",
            table: "imaging_instance_envelopes");

        migrationBuilder.DropIndex(
            name: "IX_imaging_deid_certificates_tenant_id",
            table: "imaging_deid_certificates");

        migrationBuilder.DropIndex(
            name: "IX_imaging_caliper_annotations_tenant_id",
            table: "imaging_caliper_annotations");

        migrationBuilder.DropIndex(
            name: "IX_imaging_audit_logs_tenant_id",
            table: "imaging_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "imaging_tumor_response_assessments");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "imaging_series_envelopes");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "imaging_instance_envelopes");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "imaging_deid_certificates");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "imaging_caliper_annotations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "imaging_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");

        migrationBuilder.RenameColumn(
            name: "tenant_id",
            table: "imaging_study_envelopes",
            newName: "TenantId");

        migrationBuilder.RenameIndex(
            name: "IX_imaging_study_envelopes_tenant_id",
            table: "imaging_study_envelopes",
            newName: "IX_imaging_study_envelopes_TenantId");

        migrationBuilder.RenameColumn(
            name: "tenant_id",
            table: "imaging_adjudication_sessions",
            newName: "TenantId");

        migrationBuilder.RenameIndex(
            name: "IX_imaging_adjudication_sessions_tenant_id",
            table: "imaging_adjudication_sessions",
            newName: "IX_imaging_adjudication_sessions_TenantId");

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "imaging_study_envelopes",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "imaging_adjudication_sessions",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid");
    }
}
