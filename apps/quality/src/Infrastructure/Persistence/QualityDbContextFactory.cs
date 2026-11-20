using Cadence.Database;

namespace Cadence.Apps.Quality.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct a <see cref="QualityDbContext"/>.
/// </summary>
public sealed class QualityDbContextFactory()
    : CadenceDesignTimeDbContextFactory<QualityDbContext>(options => new QualityDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "quality";
}
