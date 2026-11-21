using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDelegationKernelGrantIds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "KernelGrantIds",
            table: "ctms_delegations",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "KernelGrantIds",
            table: "ctms_delegations");
    }
}
