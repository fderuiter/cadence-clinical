using Cadence.Database;

namespace Cadence.Apps.Imaging.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="ImagingDbContext"/>.
/// </summary>
public sealed class ImagingDbContextFactory()
    : CadenceDesignTimeDbContextFactory<ImagingDbContext>(options => new ImagingDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "imaging";
}
