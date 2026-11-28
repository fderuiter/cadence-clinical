using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class IndexPendingWorkTenants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_file_records_pending_conversion_tenant",
            table: "file_records",
            column: "tenant_id",
            filter: "\"ConversionStatus\" = 'PENDING' AND NOT \"IsDeleted\"");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_pending_transcode_tenant",
            table: "file_records",
            column: "tenant_id",
            filter: "\"TranscodeStatus\" = 'PENDING' AND NOT \"IsDeleted\"");

        migrationBuilder.Sql(
            StandaloneTenantIsolationSql.EnablePendingWorkDirectoryFunctionSql(
                "fileshare",
                "file_records",
                "pending_transcode_tenant_ids",
                "t.\"TranscodeStatus\" = 'PENDING' AND NOT t.\"IsDeleted\""));

        migrationBuilder.Sql(
            StandaloneTenantIsolationSql.EnablePendingWorkDirectoryFunctionSql(
                "fileshare",
                "file_records",
                "pending_conversion_tenant_ids",
                "t.\"ConversionStatus\" = 'PENDING' AND NOT t.\"IsDeleted\""));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            StandaloneTenantIsolationSql.DisablePendingWorkDirectoryFunctionSql(
                "fileshare",
                "file_records",
                "pending_conversion_tenant_ids",
                dropPolicy: false));

        migrationBuilder.Sql(
            StandaloneTenantIsolationSql.DisablePendingWorkDirectoryFunctionSql(
                "fileshare",
                "file_records",
                "pending_transcode_tenant_ids",
                dropPolicy: true));

        migrationBuilder.DropIndex(
            name: "IX_file_records_pending_conversion_tenant",
            table: "file_records");

        migrationBuilder.DropIndex(
            name: "IX_file_records_pending_transcode_tenant",
            table: "file_records");
    }
}
