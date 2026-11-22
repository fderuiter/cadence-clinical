using System;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "transactional_outbox",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "ctms_delegation_periods",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DelegationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TaskCodes = table.Column<string>(type: "jsonb", nullable: false),
                StartDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                EndDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_delegation_periods", x => x.Id);
                table.ForeignKey(
                    name: "FK_ctms_delegation_periods_ctms_delegations_DelegationId",
                    column: x => x.DelegationId,
                    principalTable: "ctms_delegations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "doa_delegation_periods",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DelegationRecordId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TaskCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PiSignatureHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                PiApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_doa_delegation_periods", x => x.Id);
                table.ForeignKey(
                    name: "FK_doa_delegation_periods_doa_delegation_records_DelegationRec~",
                    column: x => x.DelegationRecordId,
                    principalTable: "doa_delegation_records",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegation_periods_DelegationId",
            table: "ctms_delegation_periods",
            column: "DelegationId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegation_periods_SiteCode_StudyCode",
            table: "ctms_delegation_periods",
            columns: new[] { "SiteCode", "StudyCode" });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegation_periods_tenant_id",
            table: "ctms_delegation_periods",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_periods_DelegationRecordId",
            table: "doa_delegation_periods",
            column: "DelegationRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_periods_SiteCode",
            table: "doa_delegation_periods",
            column: "SiteCode");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_periods_tenant_id",
            table: "doa_delegation_periods",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("ctms", ["ctms_delegation_periods", "doa_delegation_periods"]);
        migrationBuilder.EnableStandaloneBitemporalGuards("ctms", "ctms_delegation_periods", "DelegationId");
        migrationBuilder.EnableStandaloneBitemporalGuards("ctms", "doa_delegation_periods", "DelegationRecordId");
        migrationBuilder.EnableStandaloneInsertOnlyGuards("ctms", ["audit_log_entries", "ctms_audit_logs"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("ctms", ["ctms_delegation_periods", "doa_delegation_periods"]);
        migrationBuilder.DisableStandaloneBitemporalGuards("ctms", "ctms_delegation_periods");
        migrationBuilder.DisableStandaloneBitemporalGuards("ctms", "doa_delegation_periods");
        migrationBuilder.DisableStandaloneInsertOnlyGuards("ctms", ["audit_log_entries", "ctms_audit_logs"]);

        migrationBuilder.DropTable(
            name: "ctms_delegation_periods");

        migrationBuilder.DropTable(
            name: "doa_delegation_periods");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "transactional_outbox");
    }
}
