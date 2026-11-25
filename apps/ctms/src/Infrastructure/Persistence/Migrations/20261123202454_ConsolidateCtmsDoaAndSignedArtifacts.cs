using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ConsolidateCtmsDoaAndSignedArtifacts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "PdfBytes",
            table: "ctms_generated_letters",
            type: "bytea",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BatchTransactionId",
            table: "ctms_delegations",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Duties",
            table: "ctms_delegations",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EtmfArtifactDocId",
            table: "ctms_delegations",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PiApprovedAt",
            table: "ctms_delegations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PiSignatureHash",
            table: "ctms_delegations",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StaffName",
            table: "ctms_delegations",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StaffRole",
            table: "ctms_delegations",
            type: "character varying(150)",
            maxLength: 150,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BatchTransactionId",
            table: "ctms_delegation_periods",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Duties",
            table: "ctms_delegation_periods",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EtmfArtifactDocId",
            table: "ctms_delegation_periods",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PiApprovedAt",
            table: "ctms_delegation_periods",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PiSignatureHash",
            table: "ctms_delegation_periods",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StaffName",
            table: "ctms_delegation_periods",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StaffRole",
            table: "ctms_delegation_periods",
            type: "character varying(150)",
            maxLength: 150,
            nullable: true);

        migrationBuilder.Sql(StandaloneTenantIsolationSql.GrantAppRoleSql("ctms"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PdfBytes",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "BatchTransactionId",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "Duties",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "EtmfArtifactDocId",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "PiApprovedAt",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "PiSignatureHash",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "StaffName",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "StaffRole",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "BatchTransactionId",
            table: "ctms_delegation_periods");

        migrationBuilder.DropColumn(
            name: "Duties",
            table: "ctms_delegation_periods");

        migrationBuilder.DropColumn(
            name: "EtmfArtifactDocId",
            table: "ctms_delegation_periods");

        migrationBuilder.DropColumn(
            name: "PiApprovedAt",
            table: "ctms_delegation_periods");

        migrationBuilder.DropColumn(
            name: "PiSignatureHash",
            table: "ctms_delegation_periods");

        migrationBuilder.DropColumn(
            name: "StaffName",
            table: "ctms_delegation_periods");

        migrationBuilder.DropColumn(
            name: "StaffRole",
            table: "ctms_delegation_periods");
    }
}
