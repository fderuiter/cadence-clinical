using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Adds <c>kernel.tenant_id_by_code(text)</c> (<see cref="TenantIsolationSql.CodeDirectoryFunction"/>), which resolves
/// an active tenant's code to its id before any tenant-scoped read (#4780). Re-run
/// <see cref="KernelDatabase.GrantAppRoleSql"/> afterwards so the application role may execute it.
/// </summary>
public partial class TenantCodeDirectory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTenantCodeDirectory("kernel");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTenantCodeDirectory("kernel");
}
