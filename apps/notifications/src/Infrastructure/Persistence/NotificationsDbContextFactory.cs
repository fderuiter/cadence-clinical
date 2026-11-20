using Cadence.Database;

namespace Cadence.Apps.Notifications.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="NotificationsDbContext"/>.
/// </summary>
public sealed class NotificationsDbContextFactory()
    : CadenceDesignTimeDbContextFactory<NotificationsDbContext>(options => new NotificationsDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "notifications";
}
