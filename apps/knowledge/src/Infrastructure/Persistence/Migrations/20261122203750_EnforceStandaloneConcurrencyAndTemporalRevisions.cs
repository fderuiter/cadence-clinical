using System;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "TrainingCurricula",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "TrainingCertificates",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "TrainingAssignments",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "OrgAuditLogs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "DelegationRecords",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "delegation_record_periods",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DelegationRecordId = table.Column<string>(type: "text", nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Role = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TaskCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Duties = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                PiSignatureHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                PiApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                BatchTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_delegation_record_periods", x => x.Id);
                table.ForeignKey(
                    name: "FK_delegation_record_periods_DelegationRecords_DelegationRecor~",
                    column: x => x.DelegationRecordId,
                    principalTable: "DelegationRecords",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_knowledge_article_versions_ArticleId_VersionIndex",
            table: "knowledge_article_versions",
            columns: new[] { "ArticleId", "VersionIndex" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_delegation_record_periods_DelegationRecordId",
            table: "delegation_record_periods",
            column: "DelegationRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_delegation_record_periods_StudyCode_SiteCode",
            table: "delegation_record_periods",
            columns: new[] { "StudyCode", "SiteCode" });

        migrationBuilder.CreateIndex(
            name: "IX_delegation_record_periods_tenant_id",
            table: "delegation_record_periods",
            column: "tenant_id");

        migrationBuilder.AddForeignKey(
            name: "FK_knowledge_article_versions_knowledge_articles_ArticleId",
            table: "knowledge_article_versions",
            column: "ArticleId",
            principalTable: "knowledge_articles",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.EnableStandaloneTenantIsolation("knowledge", ["delegation_record_periods"]);
        migrationBuilder.EnableStandaloneBitemporalGuards("knowledge", "delegation_record_periods", "DelegationRecordId");
        migrationBuilder.EnableStandaloneInsertOnlyGuards("knowledge", ["OrgAuditLogs", "audit_log_entries", "knowledge_article_audit_logs", "knowledge_article_versions"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("knowledge", ["delegation_record_periods"]);
        migrationBuilder.DisableStandaloneBitemporalGuards("knowledge", "delegation_record_periods");
        migrationBuilder.DisableStandaloneInsertOnlyGuards("knowledge", ["OrgAuditLogs", "audit_log_entries", "knowledge_article_audit_logs", "knowledge_article_versions"]);

        migrationBuilder.DropForeignKey(
            name: "FK_knowledge_article_versions_knowledge_articles_ArticleId",
            table: "knowledge_article_versions");

        migrationBuilder.DropTable(
            name: "delegation_record_periods");

        migrationBuilder.DropIndex(
            name: "IX_knowledge_article_versions_ArticleId_VersionIndex",
            table: "knowledge_article_versions");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "TrainingCurricula");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "TrainingCertificates");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "TrainingAssignments");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "OrgAuditLogs");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "DelegationRecords");
    }
}
