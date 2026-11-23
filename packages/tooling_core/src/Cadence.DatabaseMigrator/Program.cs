using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Persistence;
using Cadence.ToolingCore.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Npgsql;

namespace Cadence.DatabaseMigrator;

/// <summary>
/// Applies pending EF Core PostgreSQL migrations across all 18 design-time <see cref="DbContext"/> assemblies
/// in <see cref="PersistenceModuleCatalog"/> (19 schemas: <c>kernel</c>, <c>identity_vault</c>, <c>execution</c>,
/// <c>intelligence</c>, and the 15 <c>SearchPath</c>-isolated service schemas) in one non-AOT process so
/// <c>Cadence.Cli</c> stays <c>PublishAot=true</c> without trim or dynamic-code warnings.
/// </summary>
public static partial class Program
{
    private const string ConnectionArgument = "--connection";

    /// <summary>
    /// Assemblies in <see cref="PersistenceModuleCatalog.All"/> that own a <see cref="DbContext"/> with an
    /// <see cref="IDesignTimeDbContextFactory{TContext}"/>, ordered so <c>Cadence.Kernel.Persistence</c>
    /// (<c>kernel</c> and <c>identity_vault</c>) runs before kernel modules and standalone services.
    /// </summary>
    public static readonly IReadOnlyList<string> ModuleAssemblies =
        PersistenceModuleCatalog.All
            .OrderBy(static m => m.Module switch
            {
                "kernel" => 0,
                "executionmodule" => 1,
                "intelligence" => 2,
                _ => 3,
            })
            .ThenBy(static m => m.Module, StringComparer.Ordinal)
            .Select(static m => m.AssemblyName)
            .ToArray();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaNameRegex();

    public static async Task<int> Main(string[] args)
    {
        try
        {
            string connectionString = ResolveConnectionString(args);
            await MigrateAllAsync(connectionString).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[ERROR] Database migration failed: {ex.GetBaseException().Message}")
                .ConfigureAwait(false);
            return 1;
        }
    }

    public static async Task MigrateAllAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        string normalizedConnectionString = NormalizeConnectionString(connectionString);
        string[] designTimeArgs = [ConnectionArgument, normalizedConnectionString];

        var factories = ModuleAssemblies
            .SelectMany(name => DesignTimeFactories(Assembly.Load(name)))
            .ToList();

        var seenContexts = new HashSet<Type>();
        var contextsToDispose = new List<DbContext>(factories.Count);
        var searchPathSchemas = new List<string>();
        var migrationPlan = new List<(Type ContextType, DbContext Context, string? SearchPathSchema)>(factories.Count);

        try
        {
            foreach (Type factoryType in factories)
            {
                object factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
                MethodInfo createMethod = factoryType
                    .GetInterfaces()
                    .Single(IsFactoryInterface)
                    .GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;

                var context = (DbContext)createMethod.Invoke(factory, [designTimeArgs])!;
                contextsToDispose.Add(context);

                Type contextType = context.GetType();
                if (!seenContexts.Add(contextType))
                {
                    throw new InvalidOperationException(
                        $"More than one design-time factory produces DbContext '{contextType.FullName}'.");
                }

                string? contextConnectionString = context.Database.GetConnectionString();
                string? searchPathSchema = ExtractPrimarySearchPathSchema(contextConnectionString);

                if (searchPathSchema is not null)
                {
                    if (!SchemaNameRegex().IsMatch(searchPathSchema))
                    {
                        throw new InvalidOperationException(
                            $"'{searchPathSchema}' is not a valid PostgreSQL schema identifier for {contextType.Name}.");
                    }

                    searchPathSchemas.Add(searchPathSchema);
                }
                else
                {
                    // Kernel and kernel-module contexts declare explicit table and history schemas instead of SearchPath,
                    // so point any design-time placeholder factory directly at the resolved database.
                    context.Database.SetConnectionString(normalizedConnectionString);
                }

                migrationPlan.Add((contextType, context, searchPathSchema));
            }

            // Pre-create only the 15 SearchPath service schemas before MigrateAsync. KernelDatabase, IdentityVaultSql,
            // and the execution and intelligence module migrations create kernel, identity_vault, execution, and intelligence.
            if (searchPathSchemas.Count > 0)
            {
                await EnsureSchemasExistAsync(normalizedConnectionString, searchPathSchemas, cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach ((Type contextType, DbContext context, string? searchPathSchema) in migrationPlan)
            {
                if (string.Equals(contextType.Name, "KernelDbContext", StringComparison.Ordinal))
                {
                    await KernelDatabase.MigrateAsync(normalizedConnectionString, cancellationToken).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("  Applied migrations for KernelDbContext (kernel, identity_vault).")
                        .ConfigureAwait(false);
                    continue;
                }

                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                string targetLabel = searchPathSchema ?? context.Model.GetDefaultSchema() ?? "module";
                await Console.Out.WriteLineAsync($"  Applied migrations for {contextType.Name} ({targetLabel}).")
                    .ConfigureAwait(false);
            }

            await Console.Out.WriteLineAsync($"Completed migrations across {migrationPlan.Count} DbContext assemblies.")
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (DbContext context in contextsToDispose)
            {
                await context.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public static string ResolveConnectionString(string[]? args)
    {
        if (args is not null)
        {
            int index = Array.IndexOf(args, ConnectionArgument);
            if (index >= 0 && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return NormalizeConnectionString(args[index + 1]);
            }
        }

        if (Environment.GetEnvironmentVariable("ConnectionStrings__cadence") is { Length: > 0 } named &&
            !string.IsNullOrWhiteSpace(named))
        {
            return NormalizeConnectionString(named);
        }

        if (Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } legacy &&
            !string.IsNullOrWhiteSpace(legacy))
        {
            return NormalizeConnectionString(legacy);
        }

        throw new InvalidOperationException(
            "No PostgreSQL connection string configured. Set ConnectionStrings__cadence or POSTGRES_CONNECTION_STRING, " +
            "or pass --connection \"...\".");
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        string trimmed = connectionString.Trim();

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return ParsePostgresUri(trimmed).ConnectionString;
        }

        return new NpgsqlConnectionStringBuilder(trimmed).ConnectionString;
    }

    private static NpgsqlConnectionStringBuilder ParsePostgresUri(string uriText)
    {
        if (!uriText.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
            !uriText.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported PostgreSQL URI scheme; expected postgres:// or postgresql://.");
        }

        if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("Invalid PostgreSQL connection URI.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
        };

        if (uri.Port > 0)
        {
            builder.Port = uri.Port;
        }

        string database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.Database = database;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            string[] parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        string query = uri.Query.TrimStart('?');
        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = pair.Split('=', 2);
                string key = Uri.UnescapeDataString(kv[0]).Replace('_', ' ');
                string value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                builder[key] = value;
            }
        }

        return builder;
    }

    private static string? ExtractPrimarySearchPathSchema(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        string? searchPath = new NpgsqlConnectionStringBuilder(connectionString).SearchPath;
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            return null;
        }

        string primary = searchPath.Split(',')[0].Trim();
        return primary.Length > 0 ? primary : null;
    }

    private static async Task EnsureSchemasExistAsync(
        string connectionString,
        IEnumerable<string> schemas,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (string schema in schemas.Distinct(StringComparer.Ordinal))
        {
            if (!SchemaNameRegex().IsMatch(schema))
            {
                throw new InvalidOperationException($"Refusing to create invalid schema identifier '{schema}'.");
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE SCHEMA IF NOT EXISTS \"" + schema + "\";";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<Type> DesignTimeFactories(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(IsFactoryInterface))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static bool IsFactoryInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>);
}
