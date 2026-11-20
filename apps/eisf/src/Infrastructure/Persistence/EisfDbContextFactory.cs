using Cadence.Database;

namespace Cadence.Apps.Eisf.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct an <see cref="EisfDbContext"/>.
/// </summary>
public sealed class EisfDbContextFactory()
    : CadenceDesignTimeDbContextFactory<EisfDbContext>(options => new EisfDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "eisf";
}
