using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Cadence.Kernel.Modules;

/// <summary>
/// How the kernel command pipeline opens a module's <see cref="ModuleDbContext"/> for one command (ADR-2600): on the
/// command's own connection, which the kernel has already scoped to the command's tenant, so the module's rows, the
/// kernel audit chain link, events and the idempotency record commit in one PostgreSQL transaction.
/// </summary>
public interface IModuleCommandStore
{
    /// <summary>The module's name, lower snake case; it prefixes the aggregate type on audit entries (<c>execution.FormRecord</c>).</summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Kernel module SDK vocabulary (ADR-2600); modules are written in C#, where it is not a keyword.")]
    string Module { get; }

    /// <summary>Creates the module's context on <paramref name="connection"/>, with no retrying execution strategy.</summary>
    ModuleDbContext Create(DbConnection connection);
}

/// <summary>The <see cref="IModuleCommandStore"/> for one module context type.</summary>
/// <typeparam name="TContext">The module's context.</typeparam>
internal sealed partial class ModuleCommandStore<TContext>(
    string module,
    Func<DbContextOptions<TContext>, TContext> create,
    Action<NpgsqlDbContextOptionsBuilder>? npgsql) : IModuleCommandStore
    where TContext : ModuleDbContext
{
    public string Module { get; } = ValidModuleName(module);

    public ModuleDbContext Create(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connection, o => npgsql?.Invoke(o))
            .Options;
        var context = create(options);
        if (context.Database.CreateExecutionStrategy().RetriesOnFailure)
        {
            context.Dispose();

            // A retry cannot replay a command's transaction; the client retries the whole command with its idempotency
            // key instead (K-TX-3, ADR-2600 §4.2).
            throw new InvalidOperationException(
                $"{typeof(TContext).Name} enables a retrying execution strategy, which a command's transaction cannot use (ADR-2600).");
        }

        return context;
    }

    private static string ValidModuleName(string module) =>
        module is not null && ModuleName().IsMatch(module)
            ? module
            : throw new ArgumentException($"'{module}' is not a module name: lower snake case, starting with a letter.", nameof(module));

    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleName();
}
