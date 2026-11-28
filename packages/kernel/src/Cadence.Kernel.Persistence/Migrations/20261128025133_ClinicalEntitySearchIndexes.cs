using Cadence.Kernel.Persistence.Search;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Full-text GIN expression indexes for clinical entities in <c>kernel</c> (ADR-2484, Canvas D9): enrollments,
/// protocol versions, collection instances, collection instance members, observations, validation detections,
/// validation findings, qualifications, artifacts, and proposals.
/// </summary>
public partial class ClinicalEntitySearchIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(KernelSearchSql.CreateClinicalEntityIndexes("kernel"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(KernelSearchSql.DropClinicalEntityIndexes("kernel"));
}
