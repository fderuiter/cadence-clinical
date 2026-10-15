using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.IntegrationTests.Database;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.IntegrationTests.Interop;

/// <summary>
/// Boots the real interop <c>Program</c> against a throwaway PostgreSQL database and applies its migrations. Only
/// the sandbox driver is replaced, by one that succeeds without Docker, because these tests are about who may run
/// and read statistical programs, not about the container. The database follows the opt-ins of
/// <see cref="ThrowawayPostgresDatabase"/>; without one every test calls <see cref="SkipIfUnavailable"/> and skips.
/// </summary>
public sealed class InteropPostgresApiFactory : WebApplicationFactory<Cadence.Apps.Interop.Program>, IAsyncLifetime
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
        await scope.ServiceProvider.GetRequiredService<InteropDbContext>().Database.MigrateAsync().ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _database.DisposeAsync().ConfigureAwait(false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret().UseTestFieldProtectionKeys().UseTestSceSigningKey();
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
            services.RemoveAll<ISceDockerEngineDriver>();
            services.AddSingleton<ISceDockerEngineDriver, SucceedingSandboxDriver>();
        });

        base.ConfigureWebHost(builder);
    }

    private sealed class SucceedingSandboxDriver : ISceDockerEngineDriver
    {
        public Task<SandboxExecutionResult> ExecuteInSandboxAsync(SandboxExecutionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new SandboxExecutionResult(
                ExitCode: 0,
                Stdout: "ok",
                Stderr: "",
                DurationMs: 1,
                ContainerImageDigest: "sha256:test-image",
                Artifacts:
                [
                    new GeneratedArtifactDto("art-1", "table.csv", "TABLE", "/out/table.csv", "text/csv", 3, "abc", "a,b"),
                ],
                OutputArtifactsDigest: "digest"));
    }
}
