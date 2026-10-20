using Cadence.Database;

using Cadence.Security.FieldProtection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Econsent.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct an <see cref="EconsentDbContext"/>.
/// It uses <see cref="DesignTimeFieldEncryptor"/>, which can build the model but refuses to encrypt or decrypt.
/// </summary>
public sealed class EconsentDbContextFactory : IDesignTimeDbContextFactory<EconsentDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "econsent";

    public EconsentDbContext CreateDbContext(string[] args)
    {
        var connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<EconsentDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new EconsentDbContext(optionsBuilder.Options, DesignTimeFieldEncryptor.Instance);
    }
}
