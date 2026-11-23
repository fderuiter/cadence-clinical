using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {

        migrationBuilder.EnableStandaloneInsertOnlyGuards("fileshare", ["audit_log_entries", "file_audit_logs", "playback_audit_logs"]);

    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneInsertOnlyGuards("fileshare", ["audit_log_entries", "file_audit_logs", "playback_audit_logs"]);


    }
}
