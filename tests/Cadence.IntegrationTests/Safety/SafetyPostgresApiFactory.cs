using System.Threading.Tasks;

using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.IntegrationTests.Database;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.IntegrationTests.Safety;

/// <summary>
/// Boots the real safety <c>Program</c> against a throwaway PostgreSQL database, connected as the application role, and
/// applies its migrations. The database follows the opt-ins of <see cref="ThrowawayPostgresDatabase"/>; without one
/// every test calls <see cref="SkipIfUnavailable"/> and skips.
/// </summary>
public sealed class SafetyPostgresApiFactory : WebApplicationFactory<Cadence.Apps.Safety.Program>, IAsyncLifetime
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
        using var _ = scope.ServiceProvider.GetService<ITenantScope>()?.Suppress();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{SafetyDbContextFactory.Schema}\"").ConfigureAwait(false);
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _database.DisposeAsync().ConfigureAwait(false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret();
        if (_database.ConnectionString is { } connectionString)
        {
            builder.UseSetting("ConnectionStrings:cadence", connectionString);
        }
        else
        {
            builder.UseTestPostgresConnectionString();
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<CurrentTenantAccessor>();
            services.AddSingleton<CurrentTenantAccessor>(sp =>
            {
                var accessor = new CurrentTenantAccessor(
                    sp.GetRequiredService<ITenantCodeResolver>(),
                    sp.GetService<IHttpContextAccessor>());
                accessor.UseNonHttpSeedingFallbackTenant(TestSecurityContext.TenantCode);
                return accessor;
            });
        });

        base.ConfigureWebHost(builder);
    }
}
