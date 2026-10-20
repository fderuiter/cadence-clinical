using System;
using System.Text.RegularExpressions;

using Cadence.Kernel.Modules;

namespace Cadence.Modules.Intelligence.Infrastructure.Persistence;

/// <summary>Deployment operations on the <c>intelligence</c> schema (ADR-2250 rule 1, ADR-2600).</summary>
public static partial class IntelligenceDatabase
{
    /// <summary>
    /// SQL that grants <paramref name="role"/> what the module needs and nothing more: read, insert and update on its
    /// tables, no delete, nothing on the migrations history. Re-run it after a migration that adds a table. The role
    /// must not own the schema and must not have <c>BYPASSRLS</c>.
    /// </summary>
    /// <param name="role">An existing lower-case role name, for example <c>cadence_app</c>.</param>
    public static string GrantAppRoleSql(string role)
    {
        if (role is null || !RoleName().IsMatch(role))
        {
            throw new ArgumentException($"'{role}' is not a lower-case role name.", nameof(role));
        }

        const string s = IntelligenceDbContext.Schema;
        return $"""
            GRANT USAGE ON SCHEMA "{s}" TO "{role}";
            GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA "{s}" TO "{role}";
            REVOKE ALL ON "{s}"."{ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable}" FROM "{role}";
            """;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleName();
}
