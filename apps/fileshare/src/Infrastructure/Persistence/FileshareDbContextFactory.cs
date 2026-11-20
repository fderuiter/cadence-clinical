using Cadence.Database;

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct a <see cref="FileshareDbContext"/>.
/// </summary>
public sealed class FileshareDbContextFactory()
    : CadenceDesignTimeDbContextFactory<FileshareDbContext>(options => new FileshareDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "fileshare";
}
