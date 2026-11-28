using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Execution.Infrastructure.Migrations;

/// <summary>
/// Full-text GIN expression indexes for <c>execution.form_queries</c> and <c>execution.form_query_messages</c>
/// backing <c>ExecutionSearchContributor</c> (ADR-2484, Canvas D9).
/// </summary>
public partial class FormQuerySearchIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ExecutionSearchSql.CreateIndexes(ExecutionModuleDbContext.Schema));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ExecutionSearchSql.DropIndexes(ExecutionModuleDbContext.Schema));
}
