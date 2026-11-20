using Cadence.Database;

namespace Cadence.Apps.Payments.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="PaymentsDbContext"/>.
/// It uses <see cref="Cadence.Security.FieldProtection.DesignTimeFieldEncryptor"/>, which can build the model
/// but refuses to encrypt or decrypt.
/// </summary>
public sealed class PaymentsDbContextFactory()
    : CadenceDesignTimeDbContextFactory<PaymentsDbContext>(
        (options, encryptor) => new PaymentsDbContext(options, encryptor))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "payments";
}
