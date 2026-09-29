using Cadence.Kernel.Persistence.Bitemporal;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Database support for bitemporal tables (ADR-2246, spec §4.1): the <c>btree_gist</c> extension that K-TIME-2
/// exclusion constraints need, and the shared K-TIME-1 append-only trigger function. Tables opt in with
/// <see cref="BitemporalMigrationBuilderExtensions.CreateBitemporalGuards"/>.
/// </summary>
public partial class BitemporalFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

        migrationBuilder.CreateBitemporalAppendOnlyFunction(KernelDbContext.Schema);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropBitemporalAppendOnlyFunction(KernelDbContext.Schema);

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");
    }
}
