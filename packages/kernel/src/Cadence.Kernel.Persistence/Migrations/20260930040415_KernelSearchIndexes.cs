using Cadence.Kernel.Persistence.Search;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Full-text search indexes (ADR-2484): one GIN expression index per searched table, on the same code-and-title
/// expression <see cref="KernelSearchStore"/> queries. No table or data changes.
/// </summary>
public partial class KernelSearchIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(KernelSearchSql.CreateIndexes("kernel"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(KernelSearchSql.DropIndexes("kernel"));
}
