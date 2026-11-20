using Cadence.Database;

namespace Cadence.Apps.Safety.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="SafetyDbContext"/>.
/// </summary>
public sealed class SafetyDbContextFactory()
    : CadenceDesignTimeDbContextFactory<SafetyDbContext>(options => new SafetyDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "safety";
}
