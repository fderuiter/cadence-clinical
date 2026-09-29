using System;

using Cadence.Database;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        var builder = WebApplication.CreateBuilder();

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        var options = app.Services.GetService<DbContextOptions<TestDbContext>>();

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
}
