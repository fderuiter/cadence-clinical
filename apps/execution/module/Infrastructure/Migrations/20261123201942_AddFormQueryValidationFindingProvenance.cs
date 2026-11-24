using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Execution.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddFormQueryValidationFindingProvenance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "pack_key",
            schema: "execution",
            table: "form_queries",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "rule_id",
            schema: "execution",
            table: "form_queries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "rule_pack_id",
            schema: "execution",
            table: "form_queries",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "rule_version",
            schema: "execution",
            table: "form_queries",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "validation_finding_id",
            schema: "execution",
            table: "form_queries",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_form_queries_tenant_form_finding",
            schema: "execution",
            table: "form_queries",
            columns: ["tenant_id", "form_id", "validation_finding_id"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_form_queries_tenant_form_finding",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "pack_key",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "rule_id",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "rule_pack_id",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "rule_version",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "validation_finding_id",
            schema: "execution",
            table: "form_queries");
    }
}
