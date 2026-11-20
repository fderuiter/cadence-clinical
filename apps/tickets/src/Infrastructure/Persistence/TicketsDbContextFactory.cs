using Cadence.Database;

namespace Cadence.Apps.Tickets.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="TicketsDbContext"/>.
/// </summary>
public sealed class TicketsDbContextFactory()
    : CadenceDesignTimeDbContextFactory<TicketsDbContext>(options => new TicketsDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "tickets";
}
