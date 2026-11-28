using Cadence.Kernel.Modules;
using Cadence.Kernel.Sql;

namespace Cadence.Modules.Intelligence.Infrastructure.Persistence;

/// <summary>Deployment operations on the <c>intelligence</c> schema (ADR-2250 rule 1, ADR-2600).</summary>
public static class IntelligenceDatabase
{
    /// <summary>
    /// SQL that grants <paramref name="role"/> what the module needs and nothing more: read, insert and update on its
    /// tables, no delete, nothing on the migrations history. Re-run it after a migration that adds a table. The role
    /// must not own the schema and must not have <c>BYPASSRLS</c>.
    /// </summary>
    /// <param name="role">An existing lower-case role name, for example <c>cadence_app</c>.</param>
    public static string GrantAppRoleSql(string role) =>
        PostgresTenantRlsSql.GrantStrictSchemaRoleSql(
            IntelligenceDbContext.Schema,
            role,
            ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable);
}
