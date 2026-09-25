using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SyncExecutionModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""LabSource"" text;
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""LabSiteId"" text;
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""LabIndicator"" text;
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""LabOutOfRange"" boolean;
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""MatchedNormalBounds"" text;
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""RangeIndicator"" text;
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""IsOutOfRange"" boolean;
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""ReferenceRangeLow"" double precision;
            ALTER TABLE clinical_observations ADD COLUMN IF NOT EXISTS ""ReferenceRangeHigh"" double precision;
        ");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "clinical_observations");

        migrationBuilder.DropTable(
            name: "clinical_queries");

        migrationBuilder.DropTable(
            name: "form_submissions");
    }
}
