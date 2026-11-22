using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Modules;

/// <summary>
/// A module's own schema migrations, as the composition host applies them (ADR-2600, ADR-2601): after the kernel's, in
/// registration order, each with its migrations history in the module's schema. Like the kernel's, they run as the role
/// that owns the schemas, never as the application role, and only when the host is asked to migrate.
/// </summary>
/// <param name="Module">The module's name, lower snake case.</param>
/// <param name="Schema">The schema the module's migrations create and own.</param>
/// <param name="MigrateAsync">Applies the module's pending migrations on the given owner connection string.</param>
/// <param name="GrantAppRoleSql">
/// SQL granting an application role exactly what the module needs on its schema, for the deployment step that grants the
/// kernel's (<c>KernelDatabase.GrantAppRoleSql</c>).
/// </param>
public sealed record ModuleMigrationRegistration(
    string Module,
    string Schema,
    Func<string, CancellationToken, Task> MigrateAsync,
    Func<string, string> GrantAppRoleSql);

/// <summary>Registers a module's schema migrations with the composition host (ADR-2601).</summary>
public static partial class ModuleMigrationServiceCollectionExtensions
{
    /// <summary>The migrations history table in every module schema, named like the kernel's.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Credential-free placeholder connection string for offline EF Core design-time model building (ADR-2260, ADR-2498).</summary>
    public const string DesignTimePlaceholderConnectionString = "Host=localhost;Database=cadence_design";

    /// <summary>
    /// Resolves the PostgreSQL connection string for module <c>IDesignTimeDbContextFactory</c> implementations in order:
    /// <c>--connection</c> argument, <c>ConnectionStrings__cadence</c>, <c>POSTGRES_CONNECTION_STRING</c>, or the
    /// credential-free offline placeholder <see cref="DesignTimePlaceholderConnectionString"/> (ADR-2260, ADR-2498).
    /// </summary>
    /// <param name="args">Design-time CLI arguments passed after <c>--</c>.</param>
    public static string ResolveDesignTimeConnectionString(string[]? args = null) =>
        ResolveDesignTimeConnectionStringCore(
            args,
            static key => key == "ConnectionStrings__cadence"
                ? Environment.GetEnvironmentVariable("ConnectionStrings__cadence")
                : Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING"));

    internal static string ResolveDesignTimeConnectionStringCore(string[]? args, Func<string, string?> getEnvironmentVariable)
    {
        int argIdx = args is null ? -1 : Array.IndexOf(args, "--connection");
        if (args is not null && argIdx >= 0 && argIdx + 1 < args.Length && args[argIdx + 1] is { Length: > 0 } fromArgs)
        {
            return fromArgs;
        }

        if (getEnvironmentVariable("ConnectionStrings__cadence") is { Length: > 0 } named)
        {
            return named;
        }

        return getEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } legacy
            ? legacy
            : DesignTimePlaceholderConnectionString;
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/>'s migrations as <paramref name="module"/>'s. The host applies them after
    /// the kernel's when <c>Database:MigrateOnStartup</c> or <c>Database:MigrateOnly</c> is set; a deployment that
    /// connects as the application role applies them and <paramref name="grantAppRoleSql"/> in its migration job.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="module">The module's name, lower snake case.</param>
    /// <param name="schema">The module's schema, lower snake case; its migrations history lives there.</param>
    /// <param name="create">Creates the context from options, usually <c>options =&gt; new TContext(options)</c>.</param>
    /// <param name="grantAppRoleSql">SQL that grants a named application role what the module needs.</param>
    /// <exception cref="InvalidOperationException">The module or the schema already has migrations registered.</exception>
    public static IServiceCollection AddModuleMigrations<TContext>(
        this IServiceCollection services,
        string module,
        string schema,
        Func<DbContextOptions<TContext>, TContext> create,
        Func<string, string> grantAppRoleSql)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(grantAppRoleSql);
        if (module is null || !SnakeCase().IsMatch(module))
        {
            throw new ArgumentException($"'{module}' is not a module name: lower snake case, starting with a letter.", nameof(module));
        }

        if (schema is null || !SnakeCase().IsMatch(schema) || schema == "kernel")
        {
            throw new ArgumentException($"'{schema}' is not a module schema: lower snake case, and not the kernel's.", nameof(schema));
        }

        if (services.Any(d => d.ImplementationInstance is ModuleMigrationRegistration r && (r.Module == module || r.Schema == schema)))
        {
            throw new InvalidOperationException($"Module '{module}' or schema '{schema}' already has migrations registered.");
        }

        services.AddSingleton(new ModuleMigrationRegistration(
            module,
            schema,
            async (connectionString, cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
                var options = new DbContextOptionsBuilder<TContext>()
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
                    .Options;
                await using var db = create(options);
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            },
            grantAppRoleSql));
        return services;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex SnakeCase();
}
