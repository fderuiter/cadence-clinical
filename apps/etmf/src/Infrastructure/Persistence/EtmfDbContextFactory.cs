using Cadence.Database;

namespace Cadence.Apps.Etmf.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct an <see cref="EtmfDbContext"/>.
/// </summary>
public sealed class EtmfDbContextFactory()
    : CadenceDesignTimeDbContextFactory<EtmfDbContext>(options => new EtmfDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "etmf";
}
