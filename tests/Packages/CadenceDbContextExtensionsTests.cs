using System;

using Cadence.Database;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class ExtensionsTestDbContext : DbContext
{
    public ExtensionsTestDbContext(DbContextOptions<ExtensionsTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).IsRequired();
        });
    }
}

public class TestEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CadenceDbContextExtensionsTests
{
    [Fact]
    public void GetCompiledModel_ReturnsNull_WhenNoCompiledModelExists()
    {
        var model = CadenceDbContextExtensions.GetCompiledModel<ExtensionsTestDbContext>();
        model.Should().BeNull();
    }

    [Fact]
    public void AddCadenceDbContext_RegistersDbContextInServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddCadenceDbContext<ExtensionsTestDbContext>(options => options.UseInMemoryDatabase("TestDb"));

        var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetService<ExtensionsTestDbContext>();

        dbContext.Should().NotBeNull();
    }

    [Fact]
    public void AddCadenceDbContext_ExecutesOptionsActionWithServiceProvider()
    {
        var services = new ServiceCollection();
        var optionsExecuted = false;

        services.AddCadenceDbContext<ExtensionsTestDbContext>((sp, options) =>
        {
            optionsExecuted = true;
            options.UseInMemoryDatabase("TestDbProvider");
        });

        var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetService<ExtensionsTestDbContext>();

        dbContext.Should().NotBeNull();
        optionsExecuted.Should().BeTrue();
    }
}
