using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RetireStandaloneDoaTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ctms_delegation_periods");

        migrationBuilder.DropTable(
            name: "doa_delegation_periods");

        migrationBuilder.DropTable(
            name: "ctms_delegations");

        migrationBuilder.DropTable(
            name: "doa_delegation_records");

        migrationBuilder.CreateTable(
            name: "ctms_doa_task_endorsements",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                KernelGrantId = table.Column<Guid>(type: "uuid", nullable: true),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TaskCodes = table.Column<string>(type: "jsonb", nullable: false),
                StartDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                EndDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                SignedOffAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SignedOffBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RevokedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_doa_task_endorsements", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_doa_task_endorsements_KernelGrantId",
            table: "ctms_doa_task_endorsements",
            column: "KernelGrantId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_doa_task_endorsements_SiteId",
            table: "ctms_doa_task_endorsements",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_doa_task_endorsements_StaffUserId",
            table: "ctms_doa_task_endorsements",
            column: "StaffUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_doa_task_endorsements_StudyId",
            table: "ctms_doa_task_endorsements",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_doa_task_endorsements_tenant_id",
            table: "ctms_doa_task_endorsements",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("ctms", ["ctms_doa_task_endorsements"]);
        migrationBuilder.Sql(StandaloneTenantIsolationSql.GrantAppRoleSql("ctms"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("ctms", ["ctms_doa_task_endorsements"]);

        migrationBuilder.DropTable(
            name: "ctms_doa_task_endorsements");

        migrationBuilder.CreateTable(
            name: "ctms_delegations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                EndDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StartDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TaskCodes = table.Column<string>(type: "jsonb", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_delegations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "doa_delegation_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                PiApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PiSignatureHash = table.Column<string>(type: "text", nullable: true),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                SiteId = table.Column<string>(type: "text", nullable: false),
                StaffUserId = table.Column<string>(type: "text", nullable: false),
                StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                TaskCode = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_doa_delegation_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_delegation_periods",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DelegationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                EndDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StartDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TaskCodes = table.Column<string>(type: "jsonb", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
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
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DelegationRecordId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                PiApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PiSignatureHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TaskCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
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
            name: "IX_ctms_delegations_SiteId",
            table: "ctms_delegations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegations_StaffUserId",
            table: "ctms_delegations",
            column: "StaffUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegations_StudyId",
            table: "ctms_delegations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegations_tenant_id",
            table: "ctms_delegations",
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

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_records_SiteId",
            table: "doa_delegation_records",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_records_StaffUserId",
            table: "doa_delegation_records",
            column: "StaffUserId");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_records_TaskCode",
            table: "doa_delegation_records",
            column: "TaskCode");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_records_tenant_id",
            table: "doa_delegation_records",
            column: "tenant_id");
    }
}
