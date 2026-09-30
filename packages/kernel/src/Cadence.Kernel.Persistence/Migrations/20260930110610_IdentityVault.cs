using Cadence.Kernel.Persistence.IdentityVault;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// The IdentityVault (spec §9.2, ADR-2253, ADR-2458): the <c>identity_vault</c> schema with its sealed identity records,
/// cross-study participant links and access functions (<see cref="IdentityVaultSql"/>). Direct identifiers leave the
/// kernel schema: <c>people</c> loses its e-mail and name columns, which no command ever wrote; a guard fails the
/// migration if any row holds a value in them. <c>tenants</c> gains its data residency (spec §13).
/// </summary>
public partial class IdentityVault : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nothing to move into the vault: no command wrote these columns, and the guard fails the migration otherwise.
        migrationBuilder.RequireNoPersonIdentifiers();

        migrationBuilder.DropIndex(
            name: "IX_people_email",
            schema: "kernel",
            table: "people");

        migrationBuilder.DropColumn(
            name: "email",
            schema: "kernel",
            table: "people");

        migrationBuilder.DropColumn(
            name: "first_name",
            schema: "kernel",
            table: "people");

        migrationBuilder.DropColumn(
            name: "last_name",
            schema: "kernel",
            table: "people");

        migrationBuilder.AddColumn<string>(
            name: "residency",
            schema: "kernel",
            table: "tenants",
            type: "character varying(8)",
            maxLength: 8,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_tenants_residency",
            schema: "kernel",
            table: "tenants",
            sql: "residency IS NULL OR residency ~ '^[A-Z]{2,8}$'");

        migrationBuilder.CreateIdentityVault();
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIdentityVault();

        migrationBuilder.DropCheckConstraint(
            name: "ck_tenants_residency",
            schema: "kernel",
            table: "tenants");

        migrationBuilder.DropColumn(
            name: "residency",
            schema: "kernel",
            table: "tenants");

        migrationBuilder.AddColumn<string>(
            name: "email",
            schema: "kernel",
            table: "people",
            type: "character varying(320)",
            maxLength: 320,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "first_name",
            schema: "kernel",
            table: "people",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "last_name",
            schema: "kernel",
            table: "people",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        // The e-mail was unique; the identifiers are gone, so each person gets a distinct placeholder.
        migrationBuilder.Sql("UPDATE kernel.people SET email = id::text;");

        migrationBuilder.CreateIndex(
            name: "IX_people_email",
            schema: "kernel",
            table: "people",
            column: "email",
            unique: true);
    }
}
