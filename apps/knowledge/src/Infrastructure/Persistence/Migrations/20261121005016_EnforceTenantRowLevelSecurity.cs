using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "DelegationRecords",
        "OrgAuditLogs",
        "TrainingAssignments",
        "TrainingCertificates",
        "TrainingCurricula",
        "audit_log_entries",
        "knowledge_article_audit_logs",
        "knowledge_article_versions",
        "knowledge_articles",
        "knowledge_categories",
        "knowledge_contextual_help_mappings",
        "protocol_knowledge_chunks",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_knowledge_categories_Name",
            table: "knowledge_categories");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_categories_Slug",
            table: "knowledge_categories");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_articles_Slug",
            table: "knowledge_articles");

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "TrainingCurricula",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "TrainingCertificates",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "TrainingAssignments",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "protocol_knowledge_chunks",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "OrgAuditLogs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "knowledge_contextual_help_mappings",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "knowledge_categories",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "knowledge_articles",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "knowledge_article_versions",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "knowledge_article_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "DelegationRecords",
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
            name: "IX_TrainingCurricula_tenant_id",
            table: "TrainingCurricula",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_TrainingCertificates_tenant_id",
            table: "TrainingCertificates",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_TrainingAssignments_tenant_id",
            table: "TrainingAssignments",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_protocol_knowledge_chunks_tenant_id",
            table: "protocol_knowledge_chunks",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_OrgAuditLogs_tenant_id",
            table: "OrgAuditLogs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_contextual_help_mappings_tenant_id",
            table: "knowledge_contextual_help_mappings",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_categories_tenant_id",
            table: "knowledge_categories",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_categories_tenant_id_Name",
            table: "knowledge_categories",
            columns: ["tenant_id", "Name"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_categories_tenant_id_Slug",
            table: "knowledge_categories",
            columns: ["tenant_id", "Slug"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_articles_tenant_id",
            table: "knowledge_articles",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_articles_tenant_id_Slug",
            table: "knowledge_articles",
            columns: ["tenant_id", "Slug"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_versions_tenant_id",
            table: "knowledge_article_versions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_audit_logs_tenant_id",
            table: "knowledge_article_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_DelegationRecords_tenant_id",
            table: "DelegationRecords",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("knowledge", TenantTables);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("knowledge", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_TrainingCurricula_tenant_id",
            table: "TrainingCurricula");

        migrationBuilder.DropIndex(
            name: "IX_TrainingCertificates_tenant_id",
            table: "TrainingCertificates");

        migrationBuilder.DropIndex(
            name: "IX_TrainingAssignments_tenant_id",
            table: "TrainingAssignments");

        migrationBuilder.DropIndex(
            name: "IX_protocol_knowledge_chunks_tenant_id",
            table: "protocol_knowledge_chunks");

        migrationBuilder.DropIndex(
            name: "IX_OrgAuditLogs_tenant_id",
            table: "OrgAuditLogs");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_contextual_help_mappings_tenant_id",
            table: "knowledge_contextual_help_mappings");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_categories_tenant_id",
            table: "knowledge_categories");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_categories_tenant_id_Name",
            table: "knowledge_categories");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_categories_tenant_id_Slug",
            table: "knowledge_categories");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_articles_tenant_id",
            table: "knowledge_articles");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_articles_tenant_id_Slug",
            table: "knowledge_articles");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_article_versions_tenant_id",
            table: "knowledge_article_versions");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_article_audit_logs_tenant_id",
            table: "knowledge_article_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_DelegationRecords_tenant_id",
            table: "DelegationRecords");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "TrainingCurricula");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "TrainingCertificates");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "TrainingAssignments");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "protocol_knowledge_chunks");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "OrgAuditLogs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "knowledge_contextual_help_mappings");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "knowledge_categories");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "knowledge_articles");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "knowledge_article_versions");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "knowledge_article_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "DelegationRecords");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_categories_Name",
            table: "knowledge_categories",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_categories_Slug",
            table: "knowledge_categories",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_articles_Slug",
            table: "knowledge_articles",
            column: "Slug",
            unique: true);
    }
}
