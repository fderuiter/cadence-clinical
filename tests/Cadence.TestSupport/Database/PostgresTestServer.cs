using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Testing.Persistence;

using Testcontainers.PostgreSql;

namespace Cadence.TestSupport.Database;

/// <summary>Where the PostgreSQL integration tests get their server from (docs/TESTING.md).</summary>
public enum PostgresTestServerKind
{
    /// <summary>Neither opt-in is set: the tests report as skipped.</summary>
    None,

    /// <summary><c>CADENCE_TEST_POSTGRES</c>: a connection string to a server you run, as a superuser.</summary>
    External,

    /// <summary><c>CADENCE_TEST_DOCKER=1</c>: a Testcontainers container of <see cref="PostgresTestImage.Image"/>.</summary>
    Docker,
}

/// <summary>
/// A PostgreSQL server for a test fixture, chosen as every database fixture chooses one: <c>CADENCE_TEST_POSTGRES</c>
/// first, then <c>CADENCE_TEST_DOCKER=1</c>. <see cref="SuperuserConnectionString"/> connects as a superuser, so a
/// fixture can create databases and roles. Disposing stops the container, if one was started.
/// </summary>
public sealed class PostgresTestServer : IAsyncDisposable
{
    /// <summary>The image <see cref="PostgresTestServerKind.Docker"/> starts; the version the kernel targets.</summary>
    public const string DockerImage = PostgresTestImage.Image;

    /// <summary>The skip message for a fixture with neither opt-in set.</summary>
    public const string SkipReason =
        "PostgreSQL integration tests need CADENCE_TEST_POSTGRES=<connection string> or CADENCE_TEST_DOCKER=1.";

    private readonly PostgreSqlContainer? _container;

    private PostgresTestServer(PostgresTestServerKind kind, string superuserConnectionString, PostgreSqlContainer? container)
    {
        Kind = kind;
        SuperuserConnectionString = superuserConnectionString;
        _container = container;
    }

    /// <summary>Where this server came from.</summary>
    public PostgresTestServerKind Kind { get; }

    /// <summary>A superuser connection string to the server's default database.</summary>
    public string SuperuserConnectionString { get; }

    /// <summary>Which server the environment asks for. A connection string wins over the Docker switch.</summary>
    public static PostgresTestServerKind Select(Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(getVariable);
        if (!string.IsNullOrWhiteSpace(getVariable("CADENCE_TEST_POSTGRES")))
        {
            return PostgresTestServerKind.External;
        }

        return getVariable("CADENCE_TEST_DOCKER") == "1" ? PostgresTestServerKind.Docker : PostgresTestServerKind.None;
    }

    /// <summary>
    /// Connects to or starts the server the environment asks for, or returns null when it asks for none. Once a
    /// server is asked for, failing to start it throws: an opted-in run never turns into skips.
    /// </summary>
    public static async Task<PostgresTestServer?> StartAsync(Func<string, string?>? getVariable = null)
    {
        getVariable ??= Environment.GetEnvironmentVariable;
        switch (Select(getVariable))
        {
            case PostgresTestServerKind.External:
                return new PostgresTestServer(PostgresTestServerKind.External, getVariable("CADENCE_TEST_POSTGRES")!, null);

            case PostgresTestServerKind.Docker:
                // The image's default user is the superuser that initdb creates, which the kernel fixture needs to
                // create its database and the non-owner application role.
                var container = new PostgreSqlBuilder().WithImage(DockerImage).WithUsername("postgres").Build();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    await container.StartAsync(cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    await container.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                return new PostgresTestServer(PostgresTestServerKind.Docker, container.GetConnectionString(), container);

            default:
                return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
