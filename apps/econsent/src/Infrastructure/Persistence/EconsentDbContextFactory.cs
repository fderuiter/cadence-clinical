using Cadence.Database;

namespace Cadence.Apps.Econsent.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct an <see cref="EconsentDbContext"/>.
/// It uses <see cref="Cadence.Security.FieldProtection.DesignTimeFieldEncryptor"/>, which can build the model
/// but refuses to encrypt or decrypt.
/// </summary>
public sealed class EconsentDbContextFactory()
    : CadenceDesignTimeDbContextFactory<EconsentDbContext>(
        (options, encryptor) => new EconsentDbContext(options, encryptor))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "econsent";
}
