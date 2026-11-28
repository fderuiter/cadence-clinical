using Cadence.Kernel.Sql;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Row-Level Security for a module's tenant-scoped tables (K-ID-2, ADR-2250, ADR-2600). A module table gets the same
/// policy as a kernel table: RLS enabled <b>and forced</b>, rows visible and insertable only when their
/// <c>tenant_id</c> equals <c>kernel.current_tenant_id()</c>, the tenant the kernel sets on the command's connection.
/// A module migration calls <see cref="EnableForTenantColumn"/> for each table it creates.
/// </summary>
public static class ModuleTenantIsolationSql
{
    /// <summary>The policy name, the same as on kernel tables.</summary>
    public const string IsolationPolicy = PostgresTenantRlsSql.IsolationPolicy;

    /// <summary>
    /// The session setting the kernel function reads: the same one the kernel's connections set. Module contexts made by
    /// <see cref="IModuleTenantContextFactory{TContext}"/> set it the same way.
    /// </summary>
    public const string TenantSetting = PostgresTenantRlsSql.TenantSetting;

    /// <summary>The kernel function that returns the connection's tenant, or <c>NULL</c> when none is set.</summary>
    public const string CurrentTenantFunction = PostgresTenantRlsSql.KernelCurrentTenantExpression;

    /// <summary>Enables and forces RLS on <paramref name="schema"/>.<paramref name="table"/>, keyed by its <c>tenant_id</c> column.</summary>
    /// <param name="schema">The module's schema, lower snake case.</param>
    /// <param name="table">The table, lower snake case.</param>
    public static string EnableForTenantColumn(string schema, string table) =>
        PostgresTenantRlsSql.EnableForTenantColumnSql(schema, table, CurrentTenantFunction);
}
