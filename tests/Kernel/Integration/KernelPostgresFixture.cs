using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Outbox;
using Cadence.Kernel.Time;
using Cadence.Testing.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// PostgreSQL fixture for the kernel schema. Opt in with <c>CADENCE_TEST_POSTGRES</c> (an Npgsql connection string
/// to an existing server): the fixture creates a throwaway database, applies the kernel migrations, wires the command
/// pipeline exactly as a host does (<see cref="KernelPersistenceServiceCollectionExtensions.AddKernelPersistence"/>
/// plus <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>) with a settable actor
/// and authorizer, and drops the database afterwards.
/// Without it, tests call <see cref="SkipIfUnavailable"/> and report as skipped rather than passing against a
/// stand-in. Once opted in, any startup failure is rethrown.
/// </summary>
public class KernelPostgresFixture : IAsyncLifetime
{
    public const string ActorId = "kernel-it@cadence.test";

    private const string SkipReason = "Kernel PostgreSQL tests need CADENCE_TEST_POSTGRES=<connection string>.";

    private string? _adminConnectionString;
    private string? _databaseName;
    private readonly ConcurrentBag<IServiceScope> _scopes = [];
    private ServiceProvider? _services;

    public string? ConnectionString { get; private set; }

    public FakeClock Clock { get; } = new();

    /// <summary>The actor the pipeline resolves; set <see cref="TestCurrentActor.Actor"/> to null to simulate none.</summary>
    public TestCurrentActor CurrentActor { get; } = new();

    /// <summary>The authorizer the pipeline consults; permits everything unless a test sets a policy.</summary>
    public TestAuthorizer Authorizer { get; } = new();

    /// <summary>Records what the outbox relay delivers; a test can make it fail.</summary>
    public TestEventPublisher Publisher { get; } = new();

    public void SkipIfUnavailable() => Assert.SkipWhen(ConnectionString is null, SkipReason);

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(external))
        {
            return;
        }

        _adminConnectionString = external;
        _databaseName = $"cadence_kernel_it_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(external))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(external) { Database = _databaseName }.ConnectionString;

        _services = new ServiceCollection()
            .AddSingleton<IClock>(Clock)
            .AddSingleton<ICurrentActor>(CurrentActor)
            .AddSingleton<IAuthorizer>(Authorizer)
            .AddSingleton<IKernelEventPublisher>(Publisher)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<KernelOutboxRelay>()
            .AddKernelPersistence(ConnectionString)
            .AddKernelCommandPipeline()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
        }

        await OnMigratedAsync(ConnectionString).ConfigureAwait(false);
    }

    /// <summary>Runs once after the kernel migrations are applied, for fixtures that add test-only objects.</summary>
    protected virtual ValueTask OnMigratedAsync(string connectionString) => ValueTask.CompletedTask;

    /// <summary>A command sender resolved from a fresh scope, as a request would get one.</summary>
    public IKernelCommandSender Sender => Scope().GetRequiredService<IKernelCommandSender>();

    /// <summary>The unit-of-work factory, whose <c>FaultInjector</c> tests use to fail a commit at a given stage.</summary>
    internal KernelUnitOfWorkFactory UnitOfWork => Scope().GetRequiredService<KernelUnitOfWorkFactory>();

    internal KernelAuditChainVerifier AuditChainVerifier => Scope().GetRequiredService<KernelAuditChainVerifier>();

    internal KernelAuditChainSealer AuditChainSealer => Scope().GetRequiredService<KernelAuditChainSealer>();

    internal KernelOutboxRelay OutboxRelay => Scope().GetRequiredService<KernelOutboxRelay>();

    /// <summary>Creates a context wired as the kernel host wires it. Reads only: writes go through the pipeline.</summary>
    internal KernelDbContext CreateContext() =>
        Scope().GetRequiredService<IDbContextFactory<KernelDbContext>>().CreateDbContext();

    /// <summary>
    /// Inserts rows directly, bypassing the pipeline, for tests of database constraints that commands would never
    /// reach. Nothing else in the tests writes this way.
    /// </summary>
    internal async Task SeedAsync(params object[] entities)
    {
        await using var context = CreateContext();
        using var writes = context.PermitPipelineWrites();
        context.AddRange(entities);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private IServiceProvider Scope()
    {
        var scope = (_services ?? throw new InvalidOperationException(SkipReason)).CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync().ConfigureAwait(false);
        }

        if (_databaseName is not null && _adminConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync().ConfigureAwait(false);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>An <see cref="ICurrentActor"/> tests can set.</summary>
public sealed class TestCurrentActor : ICurrentActor
{
    public Actor? Actor { get; set; } = Cadence.Kernel.Commands.Actor.Person(KernelPostgresFixture.ActorId);
}

/// <summary>An <see cref="IAuthorizer"/> that permits everything unless <see cref="Policy"/> is set.</summary>
public sealed class TestAuthorizer : IAuthorizer
{
    public Func<Actor, ICommand, AuthorizationDecision>? Policy { get; set; }

    public ValueTask<AuthorizationDecision> AuthorizeAsync(Actor actor, ICommand command, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Policy?.Invoke(actor, command) ?? AuthorizationDecision.Permit());
}

/// <summary>An <see cref="IKernelEventPublisher"/> that records deliveries in order and fails when <see cref="Fail"/> says so.</summary>
public sealed class TestEventPublisher : IKernelEventPublisher
{
    private readonly ConcurrentQueue<EventEnvelope> _delivered = new();

    /// <summary>Return true to make delivery of that envelope throw.</summary>
    public Func<EventEnvelope, bool>? Fail { get; set; }

    /// <summary>Delay applied to every delivery, to hold rows locked while a test runs a second relay.</summary>
    public TimeSpan Delay { get; set; }

    public IReadOnlyList<EventEnvelope> Delivered => [.. _delivered];

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        if (Fail?.Invoke(envelope) == true)
        {
            throw new InvalidOperationException($"Subscriber rejected {envelope.Type}.");
        }

        _delivered.Enqueue(envelope);
    }

    public void Reset()
    {
        _delivered.Clear();
        Fail = null;
        Delay = TimeSpan.Zero;
    }
}
