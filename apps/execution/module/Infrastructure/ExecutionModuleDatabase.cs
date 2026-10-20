using System;
using System.Text.RegularExpressions;

using Cadence.Kernel.Modules;

namespace Cadence.Modules.Execution.Infrastructure;

/// <summary>
/// The Execution module's role grant (ADR-2250): what the application role may do in the <c>execution</c> schema. The
/// host applies the module's migrations after the kernel's (<c>AddModuleMigrations</c>); a deployment that connects as
/// the application role runs this grant after them.
/// </summary>
public static partial class ExecutionModuleDatabase
{
    /// <summary>
    /// SQL that grants <paramref name="role"/> read, insert and update on the module's tables, no delete, and nothing on
    /// the migration history. Re-run it after every module migration that adds a table.
    /// </summary>
    /// <param name="role">An existing lower-case role name, for example <c>cadence_app</c>.</param>
    public static string GrantAppRoleSql(string role)
    {
        if (role is null || !RoleName().IsMatch(role))
        {
            throw new ArgumentException($"'{role}' is not a lower-case role name.", nameof(role));
        }

        const string schema = ExecutionModuleDbContext.Schema;
        return $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA "{schema}" TO "{role}";
            REVOKE ALL ON "{schema}"."{ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable}" FROM "{role}";
            """;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleName();
}
