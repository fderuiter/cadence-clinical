using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSiteActivationState : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ActivatedAt",
            table: "site_compliance_caches",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ActivatedBy",
            table: "site_compliance_caches",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsActivated",
            table: "site_compliance_caches",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ActivatedAt",
            table: "site_compliance_caches");

        migrationBuilder.DropColumn(
            name: "ActivatedBy",
            table: "site_compliance_caches");

        migrationBuilder.DropColumn(
            name: "IsActivated",
            table: "site_compliance_caches");
    }
}
