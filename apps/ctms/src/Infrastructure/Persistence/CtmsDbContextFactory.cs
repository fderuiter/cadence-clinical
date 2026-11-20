using Cadence.Database;

namespace Cadence.Apps.Ctms.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct a <see cref="CtmsDbContext"/>.
/// </summary>
public sealed class CtmsDbContextFactory()
    : CadenceDesignTimeDbContextFactory<CtmsDbContext>(options => new CtmsDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "ctms";
}
