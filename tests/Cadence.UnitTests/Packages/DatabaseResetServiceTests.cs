using System;
using System.Collections.Generic;

using Cadence.Cli.Services;

using FluentAssertions;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class DatabaseResetServiceTests
{
    [Theory]
    [InlineData("CADENCE_ENV", "Production")]
    [InlineData("CADENCE_ENV", "prod")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Production")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "PROD")]
    [InlineData("ENVIRONMENT", "production")]
    public void ValidateLocalEnvironment_RejectsProductionEnvironments(string key, string value)
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value };

        Action act = () => DatabaseResetService.ValidateLocalEnvironment(k => env.GetValueOrDefault(k));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData(null)]
    public void ValidateLocalEnvironment_AllowsLocalAndTestEnvironments(string? cadenceEnv)
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CADENCE_ENV"] = cadenceEnv };

        Action act = () => DatabaseResetService.ValidateLocalEnvironment(k => env.GetValueOrDefault(k));

        act.Should().NotThrow();
    }

    [Fact]
    public void ResolvePostgresConnectionString_PrefersConnectionStringsCadenceBeforeLegacyKey()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings__cadence"] = "Host=127.0.0.1;Port=5433;Database=cadence_primary;Username=cadence",
            ["POSTGRES_CONNECTION_STRING"] = "Host=localhost;Port=5432;Database=cadence_legacy;Username=cadence",
        };

        string resolved = DatabaseResetService.ResolvePostgresConnectionString(k => env.GetValueOrDefault(k));
        var builder = new NpgsqlConnectionStringBuilder(resolved);

        builder.Host.Should().Be("127.0.0.1");
        builder.Port.Should().Be(5433);
        builder.Database.Should().Be("cadence_primary");
    }

    [Fact]
    public void ResolvePostgresConnectionString_FallsBackToPostgresConnectionStringWhenPrimaryUnset()
    {
        const string localCredential = "dev-only-token";
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings__cadence"] = "   ",
            ["POSTGRES_CONNECTION_STRING"] = $"postgresql://cadence:{localCredential}@localhost:5434/cadence_dev",
        };

        string resolved = DatabaseResetService.ResolvePostgresConnectionString(k => env.GetValueOrDefault(k));
        var builder = new NpgsqlConnectionStringBuilder(resolved);

        builder.Host.Should().Be("localhost");
        builder.Port.Should().Be(5434);
        builder.Database.Should().Be("cadence_dev");
        builder.Username.Should().Be("cadence");
        builder.Password.Should().Be(localCredential);
    }

    [Fact]
    public void ResolvePostgresConnectionString_FailsWhenBothSupportedKeysMissing()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["UNSUPPORTED_DB_URL"] = "postgresql://cadence@localhost:5432/cadence",
        };

        Action act = () => DatabaseResetService.ResolvePostgresConnectionString(k => env.GetValueOrDefault(k));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__cadence*POSTGRES_CONNECTION_STRING*");
    }

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Host=::1;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Host=[::1]:5432;Database=cadence;Username=cadence")]
    [InlineData("Host=postgres;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Host=db;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Host=host.docker.internal;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Host=workstation.local;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("postgresql://cadence@localhost:5432/cadence")]
    [InlineData("postgres://cadence@127.0.0.1:5432/cadence?sslmode=Disable")]
    public void ValidateLocalOnly_AcceptsLocalNpgsqlAndUriConnectionStrings(string connectionString)
    {
        Action act = () => DatabaseResetService.ValidateLocalOnly("Postgres", connectionString);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Database=cadence;Username=cadence")]
    [InlineData("Host=db.internal.test;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Server=10.20.30.40;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Host=localhost,db.internal.test;Port=5432;Database=cadence;Username=cadence")]
    [InlineData("Host=db.internal.test:5432;Database=cadence;Username=cadence")]
    [InlineData("postgresql://cadence@db.internal.test:5432/cadence")]
    [InlineData("postgres://cadence@192.168.1.50:5432/cadence")]
    [InlineData("postgresql://cadence@localhost:5432,db.internal.test:5432/cadence")]
    [InlineData("postgres+custom://cadence@localhost:5432/cadence")]
    [InlineData("sqlite:///./local.db")]
    [InlineData("Host=localhost;Database=cadence_rds;Username=cadence")]
    [InlineData("Host=localhost;Database=cadence_prod;Username=cadence")]
    [InlineData("NotAValidKeyValuePair")]
    [InlineData("postgresql://:bad-uri")]
    public void ValidateLocalOnly_FailsClosedOnNonLocalMalformedOrUnsupportedConnectionStrings(string connectionString)
    {
        Action act = () => DatabaseResetService.ValidateLocalOnly("Postgres", connectionString);

        act.Should().Throw<InvalidOperationException>();
    }
}
