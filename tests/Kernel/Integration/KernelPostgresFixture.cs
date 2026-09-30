using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Application.Standards;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Outbox;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Standards;
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
/// to an existing server, as a superuser): the fixture creates a throwaway database, applies the kernel migrations as
/// the owner, creates a login role granted exactly <see cref="KernelDatabase.GrantAppRoleSql"/> (not the owner, no
/// <c>BYPASSRLS</c>), wires the command pipeline for that role exactly as a host does
/// (<see cref="KernelPersistenceServiceCollectionExtensions.AddKernelPersistence"/> plus
/// <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>) with a settable actor and
/// authorizer, and drops the database and role afterwards. Every pipeline test therefore runs under Row-Level
/// Security (ADR-2250).
/// Without it, tests call <see cref="SkipIfUnavailable"/> and report as skipped rather than passing against a
/// stand-in. Once opted in, any startup failure is rethrown.
/// </summary>
public class KernelPostgresFixture : IAsyncLifetime
{
    public const string ActorId = "kernel-it@cadence.test";

    private const string SkipReason = "Kernel PostgreSQL tests need CADENCE_TEST_POSTGRES=<connection string>.";

    private string? _serverConnectionString;
    private string? _databaseName;
    private string? _appRole;
    private readonly ConcurrentBag<IServiceScope> _scopes = [];
    private ServiceProvider? _services;

    /// <summary>The application role's connection string: not the owner, no <c>BYPASSRLS</c>.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>A superuser connection to the test database, which bypasses Row-Level Security.</summary>
    public string? AdminConnectionString { get; private set; }

    /// <summary>The application role's name.</summary>
    public string? AppRole => _appRole;

    public FakeClock Clock { get; } = new();

    /// <summary>The actor the pipeline resolves; set <see cref="TestCurrentActor.Actor"/> to null to simulate none.</summary>
    public TestCurrentActor CurrentActor { get; } = new();

    /// <summary>The authorizer the pipeline consults; permits everything unless a test sets a policy.</summary>
    public TestAuthorizer Authorizer { get; } = new();

    /// <summary>Records what the outbox relay delivers; a test can make it fail.</summary>
    public TestEventPublisher Publisher { get; } = new();

    /// <summary>The attestation verifier governance commands consult; accepts everything unless a test sets a policy.</summary>
    public TestAttestationVerifier Attestations { get; } = new();

    public void SkipIfUnavailable() => Assert.SkipWhen(ConnectionString is null, SkipReason);

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(external))
        {
            return;
        }

        _serverConnectionString = external;
        _databaseName = $"cadence_kernel_it_{Guid.NewGuid():N}";
        _appRole = $"{_databaseName}_app";
        var password = Guid.NewGuid().ToString("N");
        await ExecuteAsync(external, $"CREATE DATABASE \"{_databaseName}\"").ConfigureAwait(false);
        await ExecuteAsync(external, $"CREATE ROLE \"{_appRole}\" LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{password}'").ConfigureAwait(false);

        AdminConnectionString = new NpgsqlConnectionStringBuilder(external) { Database = _databaseName }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(external)
        {
            Database = _databaseName,
            Username = _appRole,
            Password = password,
        }.ConnectionString;

        await KernelDatabase.MigrateAsync(AdminConnectionString).ConfigureAwait(false);
        await OnMigratedAsync(AdminConnectionString).ConfigureAwait(false);
        await ExecuteAsync(AdminConnectionString, KernelDatabase.GrantAppRoleSql(_appRole)).ConfigureAwait(false);

        _services = new ServiceCollection()
            .AddSingleton<IClock>(Clock)
            .AddSingleton<ICurrentActor>(CurrentActor)
            .AddSingleton<IAuthorizer>(Authorizer)
            .AddSingleton<IKernelEventPublisher>(Publisher)
            .AddSingleton<IAttestationVerifier>(Attestations)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<KernelOutboxRelay>()
            .AddKernelPersistence(ConnectionString)
            .AddKernelCommandPipeline()
            .AddKernelStandardsPersistence()
            .AddKernelStandards()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>
    /// Runs once as the superuser after the kernel migrations are applied, before the application role is granted,
    /// for fixtures that add test-only objects.
    /// </summary>
    protected virtual ValueTask OnMigratedAsync(string connectionString) => ValueTask.CompletedTask;

    /// <summary>A command sender resolved from a fresh scope, as a request would get one.</summary>
    public IKernelCommandSender Sender => Scope().GetRequiredService<IKernelCommandSender>();

    /// <summary>The unit-of-work factory, whose <c>FaultInjector</c> tests use to fail a commit at a given stage.</summary>
    internal KernelUnitOfWorkFactory UnitOfWork => Scope().GetRequiredService<KernelUnitOfWorkFactory>();

    /// <summary>The Concept Registry read port, as applications get it.</summary>
    public IConceptRegistry ConceptRegistry => Scope().GetRequiredService<IConceptRegistry>();

    internal KernelAuditChainVerifier AuditChainVerifier => Scope().GetRequiredService<KernelAuditChainVerifier>();

    internal KernelAuditChainSealer AuditChainSealer => Scope().GetRequiredService<KernelAuditChainSealer>();

    internal KernelOutboxRelay OutboxRelay => Scope().GetRequiredService<KernelOutboxRelay>();

    /// <summary>
    /// A context wired as the kernel host wires it, connected as the application role and scoped to
    /// <paramref name="tenant"/> by Row-Level Security. Reads only: writes go through the pipeline.
    /// </summary>
    internal KernelDbContext CreateContext(TenantId tenant) =>
        Scope().GetRequiredService<KernelTenantDbContextFactory>().Create(tenant);

    /// <summary>The Standards Registry read side, as a host resolves it.</summary>
    public IStandardsRegistry Standards => Scope().GetRequiredService<IStandardsRegistry>();

    /// <summary>The tenant-scoped context factory the kernel uses, including the tenant directory.</summary>
    internal KernelTenantDbContextFactory TenantContexts => Scope().GetRequiredService<KernelTenantDbContextFactory>();

    /// <summary>A context from the host's factory that was never bound to a tenant; it must not be able to connect.</summary>
    internal KernelDbContext CreateUnboundContext() =>
        Scope().GetRequiredService<IDbContextFactory<KernelDbContext>>().CreateDbContext();

    /// <summary>
    /// A superuser context over the test database: it bypasses Row-Level Security, so tests use it to inspect every
    /// tenant's rows at once. Reads only, except through <see cref="SeedAsync"/>.
    /// </summary>
    internal KernelDbContext CreateAdminContext()
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>();
        KernelDatabase.UseKernelNpgsql(options, AdminConnectionString ?? throw new InvalidOperationException(SkipReason));
        return new KernelDbContext(options.Options);
    }

    /// <summary>
    /// Inserts rows directly as the superuser, bypassing the pipeline and Row-Level Security (not constraints), for
    /// tests of database constraints that commands would never reach. Nothing else in the tests writes this way.
    /// </summary>
    internal async Task SeedAsync(params object[] entities)
    {
        await using var context = CreateAdminContext();
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

        if (_databaseName is not null && _serverConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(_serverConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)").ConfigureAwait(false);
            await ExecuteAsync(_serverConnectionString, $"DROP ROLE IF EXISTS \"{_appRole}\"").ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
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

/// <summary>
/// An <see cref="IAttestationVerifier"/> that records what it was asked and accepts everything unless
/// <see cref="Policy"/> is set. The Trust slice (#4729) replaces it with the kernel's Attestation store.
/// </summary>
public sealed class TestAttestationVerifier : IAttestationVerifier
{
    private readonly ConcurrentQueue<AttestationRequirement> _requests = new();

    public Func<AttestationRequirement, AttestationCheck>? Policy { get; set; }

    public IReadOnlyList<AttestationRequirement> Requests => [.. _requests];

    public ValueTask<AttestationCheck> VerifyAsync(AttestationRequirement requirement, CancellationToken cancellationToken)
    {
        _requests.Enqueue(requirement);
        return ValueTask.FromResult(Policy?.Invoke(requirement) ?? AttestationCheck.Accept());
    }
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
