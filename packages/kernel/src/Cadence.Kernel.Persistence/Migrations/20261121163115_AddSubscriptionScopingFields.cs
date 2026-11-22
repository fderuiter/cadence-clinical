using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Adds study scoping parameters and purchase-order / quote confirmation columns to <c>kernel.subscriptions</c>.
/// </summary>
public partial class AddSubscriptionScopingFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "activated_at",
            schema: "kernel",
            table: "subscriptions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "active_study_count",
            schema: "kernel",
            table: "subscriptions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "billing_confirmation",
            schema: "kernel",
            table: "subscriptions",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "countries_csv",
            schema: "kernel",
            table: "subscriptions",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "participant_count",
            schema: "kernel",
            table: "subscriptions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "site_count",
            schema: "kernel",
            table: "subscriptions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "study_duration_months",
            schema: "kernel",
            table: "subscriptions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "study_phase",
            schema: "kernel",
            table: "subscriptions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "activated_at",
            schema: "kernel",
            table: "subscriptions");

        migrationBuilder.DropColumn(
            name: "active_study_count",
            schema: "kernel",
            table: "subscriptions");

        migrationBuilder.DropColumn(
            name: "billing_confirmation",
            schema: "kernel",
            table: "subscriptions");

        migrationBuilder.DropColumn(
            name: "countries_csv",
            schema: "kernel",
            table: "subscriptions");

        migrationBuilder.DropColumn(
            name: "participant_count",
            schema: "kernel",
            table: "subscriptions");

        migrationBuilder.DropColumn(
            name: "site_count",
            schema: "kernel",
            table: "subscriptions");

        migrationBuilder.DropColumn(
            name: "study_duration_months",
            schema: "kernel",
            table: "subscriptions");

        migrationBuilder.DropColumn(
            name: "study_phase",
            schema: "kernel",
            table: "subscriptions");
    }
}
