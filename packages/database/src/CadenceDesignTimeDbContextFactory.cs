using System;

using Cadence.Security.FieldProtection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Database;

/// <summary>
/// Shared design-time <see cref="DbContext"/> factory for Cadence services (<c>dotnet ef</c> and
/// <c>Cadence.SchemaDocExporter</c>). Resolves the PostgreSQL connection string fail-closed via
/// <see cref="CadenceDatabaseExtensions.ResolveDesignTimeConnectionString"/> (ADR-2260), scopes the connection
/// to the module's schema via <see cref="CadenceDatabaseExtensions.WithModuleSchema"/> (ADR-2447), configures
/// Npgsql, and invokes a typed constructor delegate.
/// </summary>
/// <typeparam name="TDbContext">The service <see cref="DbContext"/> type.</typeparam>
public abstract class CadenceDesignTimeDbContextFactory<TDbContext> : IDesignTimeDbContextFactory<TDbContext>
    where TDbContext : DbContext
{
    private readonly Func<DbContextOptions<TDbContext>, TDbContext> _createContext;
    private readonly string _schema;

    /// <summary>
    /// Initializes a design-time factory for a context constructed from <see cref="DbContextOptions{TContext}"/>.
    /// </summary>
    /// <param name="createContext">Typed constructor delegate for <typeparamref name="TDbContext"/>.</param>
    /// <param name="schema">
    /// Optional PostgreSQL module schema; defaults to <see cref="CadenceDatabaseExtensions.ModuleSchemaFor{TDbContext}"/>.
    /// </param>
    protected CadenceDesignTimeDbContextFactory(
        Func<DbContextOptions<TDbContext>, TDbContext> createContext,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(createContext);

        _createContext = createContext;
        _schema = schema ?? CadenceDatabaseExtensions.ModuleSchemaFor<TDbContext>();
    }

    /// <summary>
    /// Initializes a design-time factory for a field-encrypting context, supplying
    /// <see cref="DesignTimeFieldEncryptor.Instance"/> so EF Core can build the model without encryption keys while
    /// rejecting runtime value conversion (ADR-2494).
    /// </summary>
    /// <param name="createContext">Typed constructor delegate accepting options and <see cref="IFieldEncryptor"/>.</param>
    /// <param name="schema">
    /// Optional PostgreSQL module schema; defaults to <see cref="CadenceDatabaseExtensions.ModuleSchemaFor{TDbContext}"/>.
    /// </param>
    protected CadenceDesignTimeDbContextFactory(
        Func<DbContextOptions<TDbContext>, IFieldEncryptor, TDbContext> createContext,
        string? schema = null)
        : this(BindDesignTimeEncryptor(createContext), schema)
    {
    }

    /// <inheritdoc />
    public TDbContext CreateDbContext(string[] args)
    {
        string connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<TDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, _schema));

        return _createContext(optionsBuilder.Options);
    }

    private static Func<DbContextOptions<TDbContext>, TDbContext> BindDesignTimeEncryptor(
        Func<DbContextOptions<TDbContext>, IFieldEncryptor, TDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        return options => createContext(options, DesignTimeFieldEncryptor.Instance);
    }
}
