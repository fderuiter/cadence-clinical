using System;
using System.Collections.Generic;

using Cadence.Database;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Hosting;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

using Npgsql;

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
    public void AddCadencePostgresDbContext_RegistersDbContextWithNamedConnectionString()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration["ConnectionStrings:cadence"] = TestHostSettings.PostgresConnectionString;

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var options = scope.ServiceProvider.GetService<DbContextOptions<TestDbContext>>();

        options.Should().NotBeNull();
    }

    [Fact]
    public void AddCadencePostgresDbContext_UsesConfiguredConnectionString()
    {
        var builder = WebApplication.CreateBuilder();
        string customConn = "Host=remotehost;Port=5432;Database=custom_db;Username=user;Password=pass";
        builder.Configuration["POSTGRES_CONNECTION_STRING"] = customConn;

        builder.AddCadencePostgresDbContext<TestDbContext>(schema: "custom_schema");

        using var app = builder.Build();
        var options = app.Services.GetService<DbContextOptions<TestDbContext>>();

        options.Should().NotBeNull();
    }

    [Fact]
    public void AddCadencePostgresDbContext_ThrowsOnNullBuilder()
    {
        WebApplicationBuilder nullBuilder = null!;
        Action act = () => CadenceDatabaseExtensions.AddCadencePostgresDbContext<TestDbContext>(nullBuilder);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCadencePostgresDbContext_OutsideDevelopmentWithoutConnectionString_FailsFast()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });

        Action act = () => builder.AddCadencePostgresDbContext<TestDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__cadence*POSTGRES_CONNECTION_STRING*");
    }

    [Fact]
    public void AddCadencePostgresDbContext_RegistersReadinessHealthCheck()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration["ConnectionStrings:cadence"] = TestHostSettings.PostgresConnectionString;

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        var registrations = app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        registrations.Should().ContainSingle(r => r.Name == nameof(TestDbContext))
            .Which.Tags.Should().Contain(CadenceDatabaseExtensions.ReadinessTag);
    }

    [Theory]
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

    /// <summary>
    /// Issue #4765: no environment, Development included, falls back to a hardcoded credential.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void ResolvePostgresConnectionString_WithoutConfiguration_ThrowsNamingTheVariable(string environmentName)
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new HostingEnvironment { EnvironmentName = environmentName, ApplicationName = "Test" };

        Action act = () => CadenceDatabaseExtensions.ResolvePostgresConnectionString(configuration, environment);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__cadence*POSTGRES_CONNECTION_STRING*");
    }

    /// <summary>
    /// Issue #4765: AddCadencePostgresDbContext fails startup in Development too when no connection string is set.
    /// </summary>
    [Fact]
    public void AddCadencePostgresDbContext_InDevelopmentWithoutConnectionString_Throws()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration["ConnectionStrings:cadence"] = string.Empty;
        builder.Configuration["POSTGRES_CONNECTION_STRING"] = string.Empty;

        Action act = () => builder.AddCadencePostgresDbContext<TestDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*POSTGRES_CONNECTION_STRING*");
    }

    /// <summary>
    /// ADR-2260 / ADR-2498: design-time factories resolve --connection, then ConnectionStrings__cadence, then
    /// POSTGRES_CONNECTION_STRING, falling back to a credential-free offline placeholder (no Username or Password).
    /// Environment variables are process-wide, so ambient checks read rather than mutate them, while isolated
    /// precedence branches pass an in-memory environment lookup delegate.
    /// </summary>
    [Fact]
    public void ResolveDesignTimeConnectionString_PrecedenceAndCredentialFreeFallback()
    {
        CadenceDatabaseExtensions.ResolveDesignTimeConnectionStringCore(
                [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString],
                _ => "Host=localhost;Database=ignored")
            .Should().Be(TestHostSettings.PostgresConnectionString);

        CadenceDatabaseExtensions.ResolveDesignTimeConnectionStringCore(
                [],
                key => key == "ConnectionStrings__cadence"
                    ? "Host=localhost;Database=from_primary"
                    : "Host=localhost;Database=from_legacy")
            .Should().Be("Host=localhost;Database=from_primary");

        CadenceDatabaseExtensions.ResolveDesignTimeConnectionStringCore(
                [],
                key => key == CadenceDatabaseExtensions.LegacyConnectionStringKey
                    ? "Host=localhost;Database=from_legacy"
                    : null)
            .Should().Be("Host=localhost;Database=from_legacy");

        string placeholder = CadenceDatabaseExtensions.ResolveDesignTimeConnectionStringCore([], _ => null);
        placeholder.Should().Be(CadenceDatabaseExtensions.DesignTimePlaceholderConnectionString);

        var placeholderBuilder = new NpgsqlConnectionStringBuilder(placeholder);
        placeholderBuilder.Username.Should().BeNullOrEmpty("offline placeholder must never embed credentials (ADR-2260)");
        placeholderBuilder.Password.Should().BeNullOrEmpty("offline placeholder must never embed credentials (ADR-2260)");

        string? ambientPrimary = Environment.GetEnvironmentVariable("ConnectionStrings__cadence");
        string? ambientLegacy = Environment.GetEnvironmentVariable(CadenceDatabaseExtensions.LegacyConnectionStringKey);
        string expectedAmbient = CadenceDatabaseExtensions.DesignTimePlaceholderConnectionString;
        if (!string.IsNullOrEmpty(ambientPrimary))
        {
            expectedAmbient = ambientPrimary;
        }
        else if (!string.IsNullOrEmpty(ambientLegacy))
        {
            expectedAmbient = ambientLegacy;
        }

        CadenceDatabaseExtensions.ResolveDesignTimeConnectionString([]).Should().Be(expectedAmbient);
    }

    [Fact]
    public void CadenceDesignTimeDbContextFactory_NullContextDelegate_ThrowsArgumentNullException()
    {
        Action act = () => _ = new NullDelegateTestDbContextFactory();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("createContext");
    }

    [Fact]
    public void CadenceDesignTimeDbContextFactory_NullEncryptedContextDelegate_ThrowsArgumentNullException()
    {
        Action act = () => _ = new NullEncryptedDelegateTestDbContextFactory();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("createContext");
    }

    [Fact]
    public void CadenceDesignTimeDbContextFactory_DefaultSchema_DerivesModuleSchemaAndBuildsContext()
    {
        var factory = new DefaultSchemaTestDbContextFactory();

        using var context = factory.CreateDbContext(
            [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString]);

        new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).SearchPath
            .Should().Be("test,public");
    }

    [Fact]
    public void CadenceDesignTimeDbContextFactory_ExplicitSchema_ScopesConnectionAndValidatesSchema()
    {
        var customFactory = new CustomSchemaTestDbContextFactory("edc");
        using var context = customFactory.CreateDbContext(
            [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString]);

        new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).SearchPath
            .Should().Be("edc,public");

        var invalidFactory = new CustomSchemaTestDbContextFactory("InvalidSchema");
        Action invalidAct = () => invalidFactory.CreateDbContext(
            [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString]);

        invalidAct.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CadenceDesignTimeDbContextFactory_WithoutConnectionArgument_BuildsOfflineContextWithModuleSchema()
    {
        var factory = new DefaultSchemaTestDbContextFactory();

        using var context = factory.CreateDbContext([]);

        var builder = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
        builder.SearchPath.Should().Be("test,public");
    }

    [Fact]
    public void CadenceDesignTimeDbContextFactory_EncryptedOverload_BuildsModelWithoutKeysAndRejectsRuntimeConversion()
    {
        var factory = new EncryptedTestDbContextFactory();

        using var context = factory.CreateDbContext(
            [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString]);

        context.FieldEncryptor.Should().BeSameAs(DesignTimeFieldEncryptor.Instance);
        new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).SearchPath
            .Should().Be("encryptedtest,public");

        var property = context.Model.FindEntityType(typeof(EncryptedTestRecord))!
            .FindProperty(nameof(EncryptedTestRecord.ProcessorReference))!;
        property.GetMaxLength().Should().Be(AesGcmFieldEncryptor.GetMaxStoredLength(64));

        var converter = property.GetValueConverter();
        converter.Should().BeOfType<EncryptedStringConverter>();

        Action encryptAct = () => _ = converter!.ConvertToProvider("processor-ref-value");
        Action decryptAct = () => _ = converter!.ConvertFromProvider("v1:design-time:ciphertext");

        encryptAct.Should().Throw<InvalidOperationException>();
        decryptAct.Should().Throw<InvalidOperationException>();
    }

    private sealed class DefaultSchemaTestDbContextFactory()
        : CadenceDesignTimeDbContextFactory<TestDbContext>(options => new TestDbContext(options));

    private sealed class CustomSchemaTestDbContextFactory(string schema)
        : CadenceDesignTimeDbContextFactory<TestDbContext>(options => new TestDbContext(options), schema);

    private sealed class NullDelegateTestDbContextFactory()
        : CadenceDesignTimeDbContextFactory<TestDbContext>(
            (Func<DbContextOptions<TestDbContext>, TestDbContext>)null!);

    private sealed class NullEncryptedDelegateTestDbContextFactory()
        : CadenceDesignTimeDbContextFactory<EncryptedTestDbContext>(
            (Func<DbContextOptions<EncryptedTestDbContext>, IFieldEncryptor, EncryptedTestDbContext>)null!);

    private sealed class EncryptedTestDbContextFactory()
        : CadenceDesignTimeDbContextFactory<EncryptedTestDbContext>(
            (options, encryptor) => new EncryptedTestDbContext(options, encryptor));
}

public sealed class EncryptedTestRecord
{
    public int Id { get; set; }
    public string? ProcessorReference { get; set; }
}

public sealed class EncryptedTestDbContext(
    DbContextOptions<EncryptedTestDbContext> options,
    IFieldEncryptor fieldEncryptor) : DbContext(options), IFieldEncryptingDbContext
{
    public IFieldEncryptor FieldEncryptor { get; } = fieldEncryptor;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EncryptedTestRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProcessorReference)
                .IsEncrypted(FieldEncryptor, "test.records.processor_reference", maxPlaintextLength: 64);
        });
    }
}
