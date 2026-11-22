using System;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Apps.Org.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "org_audit_logs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "delegation_of_authority_periods",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DelegationOfAuthorityId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DelegatorId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DelegateeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DutiesJson = table.Column<string>(type: "jsonb", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Signature = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SignedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RevokedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RevocationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_delegation_of_authority_periods", x => x.Id);
                table.ForeignKey(
                    name: "FK_delegation_of_authority_periods_delegations_of_authority_De~",
                    column: x => x.DelegationOfAuthorityId,
                    principalTable: "delegations_of_authority",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_delegation_of_authority_periods_DelegationOfAuthorityId",
            table: "delegation_of_authority_periods",
            column: "DelegationOfAuthorityId");

        migrationBuilder.CreateIndex(
            name: "IX_delegation_of_authority_periods_SiteCode",
            table: "delegation_of_authority_periods",
            column: "SiteCode");

        migrationBuilder.CreateIndex(
            name: "IX_delegation_of_authority_periods_StudyCode",
            table: "delegation_of_authority_periods",
            column: "StudyCode");

        migrationBuilder.CreateIndex(
            name: "IX_delegation_of_authority_periods_tenant_id",
            table: "delegation_of_authority_periods",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("org", ["delegation_of_authority_periods"]);
        migrationBuilder.EnableStandaloneBitemporalGuards("org", "delegation_of_authority_periods", "DelegationOfAuthorityId");
        migrationBuilder.EnableStandaloneInsertOnlyGuards("org", ["audit_log_entries", "org_audit_logs"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("org", ["delegation_of_authority_periods"]);
        migrationBuilder.DisableStandaloneBitemporalGuards("org", "delegation_of_authority_periods");
        migrationBuilder.DisableStandaloneInsertOnlyGuards("org", ["audit_log_entries", "org_audit_logs"]);

        migrationBuilder.DropTable(
            name: "delegation_of_authority_periods");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "org_audit_logs");
    }
}
