using System;
using System.Collections.Generic;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "randomization_list_slots",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "site_activation_periods",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SiteComplianceCacheId = table.Column<Guid>(type: "uuid", nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Milestone = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                missing_documents = table.Column<List<string>>(type: "text[]", nullable: false),
                IsActivated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ActivatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_site_activation_periods", x => x.Id);
                table.ForeignKey(
                    name: "FK_site_activation_periods_site_compliance_caches_SiteComplian~",
                    column: x => x.SiteComplianceCacheId,
                    principalTable: "site_compliance_caches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_site_activation_periods_SiteComplianceCacheId",
            table: "site_activation_periods",
            column: "SiteComplianceCacheId");

        migrationBuilder.CreateIndex(
            name: "IX_site_activation_periods_StudyCode_SiteCode_Milestone",
            table: "site_activation_periods",
            columns: new[] { "StudyCode", "SiteCode", "Milestone" });

        migrationBuilder.CreateIndex(
            name: "IX_site_activation_periods_tenant_id",
            table: "site_activation_periods",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("edc", ["site_activation_periods"]);
        migrationBuilder.EnableStandaloneBitemporalGuards("edc", "site_activation_periods", "SiteComplianceCacheId");
        migrationBuilder.EnableStandaloneInsertOnlyGuards("edc", ["AuditLogEntries"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("edc", ["site_activation_periods"]);
        migrationBuilder.DisableStandaloneBitemporalGuards("edc", "site_activation_periods");
        migrationBuilder.DisableStandaloneInsertOnlyGuards("edc", ["AuditLogEntries"]);

        migrationBuilder.DropTable(
            name: "site_activation_periods");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "randomization_list_slots");
    }
}
