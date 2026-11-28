using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Etmf.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class IndexQueuedExportTenants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_tmf_export_jobs_queued_tenant_created",
            table: "tmf_export_jobs",
            columns: new[] { "tenant_id", "CreatedAt" },
            filter: "\"Status\" = 'QUEUED'");

        migrationBuilder.Sql(
            StandaloneTenantIsolationSql.EnablePendingWorkDirectoryFunctionSql(
                "etmf",
                "tmf_export_jobs",
                "queued_export_tenant_ids",
                "t.\"Status\" = 'QUEUED'"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            StandaloneTenantIsolationSql.DisablePendingWorkDirectoryFunctionSql(
                "etmf",
                "tmf_export_jobs",
                "queued_export_tenant_ids",
                dropPolicy: true));

        migrationBuilder.DropIndex(
            name: "IX_tmf_export_jobs_queued_tenant_created",
            table: "tmf_export_jobs");
    }
}
