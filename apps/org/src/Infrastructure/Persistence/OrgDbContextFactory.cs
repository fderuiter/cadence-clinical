using Cadence.Database;

namespace Cadence.Apps.Org.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="OrgDbContext"/>.
/// </summary>
public sealed class OrgDbContextFactory()
    : CadenceDesignTimeDbContextFactory<OrgDbContext>(options => new OrgDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "org";
}
