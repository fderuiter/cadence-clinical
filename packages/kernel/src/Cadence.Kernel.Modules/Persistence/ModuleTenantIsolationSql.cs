using System;
using System.Text.RegularExpressions;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Row-Level Security for a module's tenant-scoped tables (K-ID-2, ADR-2250, ADR-2600). A module table gets the same
/// policy as a kernel table: RLS enabled <b>and forced</b>, rows visible and insertable only when their
/// <c>tenant_id</c> equals <c>kernel.current_tenant_id()</c>, the tenant the kernel sets on the command's connection.
/// A module migration calls <see cref="EnableForTenantColumn"/> for each table it creates.
/// </summary>
public static partial class ModuleTenantIsolationSql
{
    /// <summary>The policy name, the same as on kernel tables.</summary>
    public const string IsolationPolicy = "tenant_isolation";

    /// <summary>The kernel function that returns the connection's tenant, or <c>NULL</c> when none is set.</summary>
    public const string CurrentTenantFunction = "kernel.current_tenant_id()";

    /// <summary>Enables and forces RLS on <paramref name="schema"/>.<paramref name="table"/>, keyed by its <c>tenant_id</c> column.</summary>
    /// <param name="schema">The module's schema, lower snake case.</param>
    /// <param name="table">The table, lower snake case.</param>
    public static string EnableForTenantColumn(string schema, string table)
    {
        var qualified = $"{Quote(schema)}.{Quote(table)}";
        return $"""
            ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {qualified} FORCE ROW LEVEL SECURITY;
            CREATE POLICY {IsolationPolicy} ON {qualified}
                USING (tenant_id = {CurrentTenantFunction})
                WITH CHECK (tenant_id = {CurrentTenantFunction});
            """;
    }

    private static string Quote(string identifier) =>
        identifier is not null && SafeIdentifier().IsMatch(identifier)
            ? $"\"{identifier}\""
            : throw new ArgumentException($"'{identifier}' is not a lower-case snake_case identifier.", nameof(identifier));

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifier();
}
