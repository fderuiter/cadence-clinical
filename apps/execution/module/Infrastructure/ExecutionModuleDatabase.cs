using Cadence.Kernel.Modules;
using Cadence.Kernel.Sql;

namespace Cadence.Modules.Execution.Infrastructure;

/// <summary>
/// The Execution module's role grant (ADR-2250): what the application role may do in the <c>execution</c> schema. The
/// host applies the module's migrations after the kernel's (<c>AddModuleMigrations</c>); a deployment that connects as
/// the application role runs this grant after them.
/// </summary>
public static class ExecutionModuleDatabase
{
    /// <summary>
    /// SQL that grants <paramref name="role"/> read, insert and update on the module's tables, delete only on the used
    /// e-signature tokens (whose expired rows the token store removes), and nothing on the migration history. Re-run it after every module migration that adds a table.
    /// </summary>
    /// <param name="role">An existing lower-case role name, for example <c>cadence_app</c>.</param>
    public static string GrantAppRoleSql(string role) =>
        PostgresTenantRlsSql.GrantStrictSchemaRoleSql(
            ExecutionModuleDbContext.Schema,
            role,
            ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable,
            deletableTables: ["consumed_signature_tokens"]);
}
