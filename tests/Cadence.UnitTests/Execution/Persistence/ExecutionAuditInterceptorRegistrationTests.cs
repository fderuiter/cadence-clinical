using System.Linq;

using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// The GxP audit interceptor is the single audit writer (ADR-2446), so a context must carry it exactly once: a
/// second copy writes a second, unlabelled entry for every change (#4922).
/// </summary>
public sealed class ExecutionAuditInterceptorRegistrationTests
{
    [Fact]
    public void ProgramCompositionRoot_RegistersEachInterceptorOnce()
    {
        using var host = new WebApplicationFactory<Cadence.Apps.Execution.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestDemographicsKey()
            .UseTestPostgresConnectionString());

        ShouldHaveEachInterceptorOnce(host.Services);
    }

    /// <summary>
    /// Transient failures are retried by EF Core's execution strategy around a whole save or unit of work; the Polly
    /// pipeline around SaveChanges that could not retry inside a transaction is gone (#5383).
    /// </summary>
    [Fact]
    public void ProgramCompositionRoot_RetriesTransientFailuresThroughTheExecutionStrategy()
    {
        using var host = new WebApplicationFactory<Cadence.Apps.Execution.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestDemographicsKey()
            .UseTestPostgresConnectionString());
        using var scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().Database.CreateExecutionStrategy()
            .RetriesOnFailure.Should().BeTrue();
    }

    [Fact]
    public void ExecutionApiFactory_ReplacesTheContextWithoutDuplicatingInterceptors()
    {
        using var factory = new ExecutionApiFactory();

        ShouldHaveEachInterceptorOnce(factory.Services);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().Database.ProviderName
            .Should().Be("Microsoft.EntityFrameworkCore.InMemory");
    }

    private static void ShouldHaveEachInterceptorOnce(System.IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.ToList();

        interceptors.Count(i => i is GxPAuditInterceptor).Should().Be(1);
        interceptors.Count(i => i is ConcurrencyTokenIncrementInterceptor).Should().Be(1);
    }
}
