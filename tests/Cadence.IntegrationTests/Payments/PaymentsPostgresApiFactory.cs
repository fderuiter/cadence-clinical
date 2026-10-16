using System.Threading.Tasks;

using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.IntegrationTests.Database;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Payments;

/// <summary>
/// Boots the real payments <c>Program</c> against a throwaway PostgreSQL database, connected as the application role, and
/// applies its migrations. The database follows the opt-ins of <see cref="ThrowawayPostgresDatabase"/>; without one
/// every test calls <see cref="SkipIfUnavailable"/> and skips.
/// </summary>
public sealed class PaymentsPostgresApiFactory : WebApplicationFactory<Cadence.Apps.Payments.Program>, IAsyncLifetime
{
    private readonly ThrowawayPostgresDatabase _database = new();

    /// <summary>Skips the calling test unless a PostgreSQL database is available.</summary>
    public void SkipIfUnavailable() => _database.SkipIfUnavailable();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync().ConfigureAwait(false);
        if (_database.ConnectionString is null)
        {
            return;
        }

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync().ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _database.DisposeAsync().ConfigureAwait(false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret().UseTestFieldProtectionKeys();
        if (_database.ConnectionString is { } connectionString)
        {
            builder.UseSetting("ConnectionStrings:cadence", connectionString);
        }
        else
        {
            builder.UseTestPostgresConnectionString();
        }

        base.ConfigureWebHost(builder);
    }
}
