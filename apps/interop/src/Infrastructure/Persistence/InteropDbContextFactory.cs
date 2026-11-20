using Cadence.Database;

namespace Cadence.Apps.Interop.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="InteropDbContext"/>.
/// </summary>
public sealed class InteropDbContextFactory()
    : CadenceDesignTimeDbContextFactory<InteropDbContext>(options => new InteropDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "interop";
}
