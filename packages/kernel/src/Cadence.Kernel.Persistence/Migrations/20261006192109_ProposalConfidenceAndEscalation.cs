using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <inheritdoc />
public partial class ProposalConfidenceAndEscalation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "confidence_score",
            schema: "kernel",
            table: "proposals",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "escalated_at",
            schema: "kernel",
            table: "proposals",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "escalated_by_id",
            schema: "kernel",
            table: "proposals",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "escalated_by_kind",
            schema: "kernel",
            table: "proposals",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "escalated_to",
            schema: "kernel",
            table: "proposals",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "escalation_policy",
            schema: "kernel",
            table: "proposals",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "escalation_reason",
            schema: "kernel",
            table: "proposals",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "is_escalated",
            schema: "kernel",
            table: "proposals",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddCheckConstraint(
            name: "ck_proposals_confidence_score",
            schema: "kernel",
            table: "proposals",
            sql: "confidence_score IS NULL OR (confidence_score >= 0.0 AND confidence_score <= 1.0)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_proposals_confidence_score",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "confidence_score",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "escalated_at",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "escalated_by_id",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "escalated_by_kind",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "escalated_to",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "escalation_policy",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "escalation_reason",
            schema: "kernel",
            table: "proposals");

        migrationBuilder.DropColumn(
            name: "is_escalated",
            schema: "kernel",
            table: "proposals");
    }
}
