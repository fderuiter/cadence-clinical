using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Observation values become typed (ADR-2623): <c>value</c> on <c>kernel.observations</c> and
/// <c>kernel.observation_revisions</c> holds an <c>ObservationValue</c> as <c>jsonb</c>. A value recorded before this was
/// free text, so it becomes a text value and nothing is lost. Changing a column's type rewrites rows without firing the
/// revisions table's insert-only triggers.
/// </summary>
public partial class TypedObservationValues : Migration
{
    private static readonly string[] Tables = ["observations", "observation_revisions"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql(
                $"""
                ALTER TABLE kernel.{table}
                    ALTER COLUMN value TYPE jsonb USING jsonb_build_object('type', 'text', 'value', value);
                """);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql(
                $"""
                ALTER TABLE kernel.{table}
                    ALTER COLUMN value TYPE character varying(4000) USING left(value ->> 'value', 4000);
                """);
        }
    }
}
