using System;
using System.Collections.Generic;

using Cadence.Database;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }
}

public class DatabaseUnitTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void AddCadencePostgresDbContext_RegistersDbContextWithDefaultConnectionString()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var options = scope.ServiceProvider.GetService<DbContextOptions<TestDbContext>>();

        options.Should().NotBeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void AddCadencePostgresDbContext_UsesConfiguredConnectionString()
    {
        var builder = WebApplication.CreateBuilder();
        string customConn = "Host=remotehost;Port=5432;Database=custom_db;Username=user;Password=pass";
        builder.Configuration["POSTGRES_CONNECTION_STRING"] = customConn;

        builder.AddCadencePostgresDbContext<TestDbContext>("custom_fallback");

        using var app = builder.Build();
        var options = app.Services.GetService<DbContextOptions<TestDbContext>>();

        options.Should().NotBeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void AddCadencePostgresDbContext_ThrowsOnNullBuilder()
    {
        WebApplicationBuilder nullBuilder = null!;
        Action act = () => CadenceDatabaseExtensions.AddCadencePostgresDbContext<TestDbContext>(nullBuilder);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void AddCadencePostgresDbContext_OutsideDevelopmentWithoutConnectionString_FailsFast()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });

        Action act = () => builder.AddCadencePostgresDbContext<TestDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__cadence*POSTGRES_CONNECTION_STRING*");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void AddCadencePostgresDbContext_RegistersReadinessHealthCheck()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        var registrations = app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        registrations.Should().ContainSingle(r => r.Name == nameof(TestDbContext))
            .Which.Tags.Should().Contain(CadenceDatabaseExtensions.ReadinessTag);
    }

    [Theory]
    [GxPRequirement("PRD-SYS-053")]
    [InlineData("Host=named", "Host=legacy", "Host=named")]
    [InlineData(null, "Host=legacy", "Host=legacy")]
    public void ResolvePostgresConnectionString_PrefersNamedConnectionStringOverLegacyKey(
        string? named,
        string? legacy,
        string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:cadence"] = named,
                ["POSTGRES_CONNECTION_STRING"] = legacy,
            })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production, ApplicationName = "Test" };

        CadenceDatabaseExtensions.ResolvePostgresConnectionString(configuration, environment)
            .Should().Be(expected);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void ResolvePostgresConnectionString_InDevelopment_FallsBackToLocalDatabase()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development, ApplicationName = "Test" };

        CadenceDatabaseExtensions.ResolvePostgresConnectionString(configuration, environment, "cadence_ctms")
            .Should().Contain("Host=localhost").And.Contain("Database=cadence_ctms");
    }
}
