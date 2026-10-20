using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.Identity;
using Cadence.Kernel.Application.Standards;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Application.Validation;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Identity;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Outbox;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Indicators;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.Testing.Commands;
using Cadence.Testing.Persistence;
using Cadence.Testing.Time;
using Cadence.TestSupport.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// PostgreSQL fixture for the kernel schema. Opt in with <c>CADENCE_TEST_POSTGRES</c> (an Npgsql connection string
/// to an existing server, as a superuser) or <c>CADENCE_TEST_DOCKER=1</c> (a Testcontainers
/// <see cref="PostgresTestImage.Image"/> container, see <see cref="PostgresTestServer"/>): the fixture creates a throwaway database, applies the kernel migrations as
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

    /// <summary>The residency region the test vault serves.</summary>
    public const string VaultRegion = "EU";

    /// <summary>A throwaway IdentityVault master key, generated per fixture.</summary>
    private static readonly string VaultMasterKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private const string SkipReason = PostgresTestServer.SkipReason;

    private PostgresTestServer? _server;
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

    /// <summary>Decides which lineage nodes a reader sees; shows everything unless a test masks nodes.</summary>
    public TestNodeVisibility Visibility { get; } = new();

    /// <summary>Records what the outbox relay delivers; a test can make it fail.</summary>
    public TestEventPublisher Publisher { get; } = new();

    public void SkipIfUnavailable() => Assert.SkipWhen(ConnectionString is null, SkipReason);

    public async ValueTask InitializeAsync()
    {
        _server = await PostgresTestServer.StartAsync().ConfigureAwait(false);
        if (_server is null)
        {
            return;
        }

        var external = _server.SuperuserConnectionString;
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
        await PostgresTestEvidence.VerifyAndRecordAsync(
            "kernel", _server.Kind, AdminConnectionString, $"{KernelDbContext.Schema}.__ef_migrations_history", _appRole).ConfigureAwait(false);

        var services = new ServiceCollection()
            .AddSingleton<IClock>(Clock)
            .AddSingleton<ICurrentActor>(CurrentActor)
            .AddSingleton<IAuthorizer>(Authorizer)
            .AddSingleton<INodeVisibility>(Visibility)
            .AddSingleton<IKernelEventPublisher>(Publisher)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<KernelOutboxRelay>()
            .AddKernelPersistence(ConnectionString)
            .AddKernelCommandPipeline()
            .AddDeclaredKernelErrorsCheck()
            .AddKernelStandardsPersistence()
            .AddKernelStandards()
            .AddKernelValidationPersistence()
            .AddKernelValidation()
            .AddCadenceRules()
            .AddCadenceStandards()
            .AddKernelIdentityVault()
            .AddKernelIdentityVaultPersistence(VaultMasterKey, VaultRegion);
        ConfigureServices(services);
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Adds test-only services, such as a module's handlers and command store, before the provider is built.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
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

    /// <summary>The IdentityVault port, as a host gets it.</summary>
    public IIdentityVault IdentityVault => Scope().GetRequiredService<IIdentityVault>();

    /// <summary>The vault store the handlers read through, as the application role.</summary>
    internal IIdentityVaultStore IdentityVaultStore => Scope().GetRequiredService<IIdentityVaultStore>();

    /// <summary>The enrollment read port the execution handlers use.</summary>
    public IEnrollmentDirectory Enrollments => Scope().GetRequiredService<IEnrollmentDirectory>();

    /// <summary>The grant and role reads behind the authority evaluator.</summary>
    public IAuthorityStore AuthorityStore => Scope().GetRequiredService<IAuthorityStore>();

    /// <summary>The protocol version read port, as a host gets it.</summary>
    public IProtocolVersionReader ProtocolVersions => Scope().GetRequiredService<IProtocolVersionReader>();

    /// <summary>The effectivity read port, as applications get it.</summary>
    public IEffectivityReader Effectivity => Scope().GetRequiredService<IEffectivityReader>();

    /// <summary>The observation read port, as the kernel host gets it (ADR-2620).</summary>
    public IObservationRecords ObservationRecords => Scope().GetRequiredService<IObservationRecords>();

    /// <summary>The form instance read port (ADR-2622).</summary>
    public ICollectionInstanceRecords CollectionInstanceRecords => Scope().GetRequiredService<ICollectionInstanceRecords>();

    /// <summary>The tenant code directory, as a host gets it.</summary>
    public ITenantDirectory TenantDirectory => Scope().GetRequiredService<ITenantDirectory>();

    /// <summary>The Concept Registry read port, as applications get it.</summary>
    public IConceptRegistry ConceptRegistry => Scope().GetRequiredService<IConceptRegistry>();

    /// <summary>The lineage read port, as applications get it.</summary>
    public IKernelLineage Lineage => Scope().GetRequiredService<IKernelLineage>();

    public IAmendmentImpact AmendmentImpact => Scope().GetRequiredService<IAmendmentImpact>();

    internal KernelLineageOrphanCheck LineageOrphanCheck => Scope().GetRequiredService<KernelLineageOrphanCheck>();

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

    /// <summary>The rule pack loader, as a host resolves it.</summary>
    public RulePackLoader RulePacks => Scope().GetRequiredService<RulePackLoader>();

    /// <summary>The indicator set loader, as a host resolves it (ADR-2488).</summary>
    public IndicatorSetLoader IndicatorSets => Scope().GetRequiredService<IndicatorSetLoader>();

    /// <summary>The indicator evaluator, as a host resolves it (ADR-2488).</summary>
    public IndicatorEvaluator Indicators => Scope().GetRequiredService<IndicatorEvaluator>();

    /// <summary>The study Pulse service, resolved from a fresh scope.</summary>
    public StudyIndicators StudyIndicators => Scope().GetRequiredService<StudyIndicators>();

    /// <summary>The validation runner, resolved from a fresh scope as a request would get one.</summary>
    public ValidationRunner Validation => Scope().GetRequiredService<ValidationRunner>();

    /// <summary>The findings read model, as applications get it.</summary>
    public IValidationFindings Findings => Scope().GetRequiredService<IValidationFindings>();

    /// <summary>The rule evaluator, as a host resolves it.</summary>
    public IRuleEvaluator RuleEvaluator => Scope().GetRequiredService<IRuleEvaluator>();

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

    /// <summary>A fresh scope's provider, as a request would get one; the fixture disposes it.</summary>
    protected IServiceProvider Scope()
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

        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
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

/// <summary>
/// An <see cref="IAuthorizer"/> that permits everything unless <see cref="Policy"/> is set, or hands every decision to
/// <see cref="Inner"/> (for example the production authorization over grants) when that is set.
/// </summary>
public sealed class TestAuthorizer : IAuthorizer
{
    public Func<Actor, ICommand, AuthorizationDecision>? Policy { get; set; }

    public IAuthorizer? Inner { get; set; }

    public ValueTask<AuthorizationDecision> AuthorizeAsync(Actor actor, ICommand command, CancellationToken cancellationToken) =>
        Inner?.AuthorizeAsync(actor, command, cancellationToken)
        ?? ValueTask.FromResult(Policy?.Invoke(actor, command) ?? AuthorizationDecision.Permit());
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

        if (Fail is { } fail && fail(envelope))
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

/// <summary>
/// An <see cref="INodeVisibility"/> tests can set: every node is visible unless it (or its object, for an unversioned
/// entry) is in <see cref="Masked"/>. Records the nodes each call was asked about.
/// </summary>
public sealed class TestNodeVisibility : INodeVisibility
{
    public ConcurrentDictionary<NodeRef, MaskReason> Masked { get; } = new();

    public ConcurrentBag<IReadOnlyCollection<NodeRef>> Calls { get; } = [];

    public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        Calls.Add(nodes);
        var result = new Dictionary<NodeRef, MaskReason>();
        foreach (var node in nodes)
        {
            if (Masked.TryGetValue(node, out var reason) || Masked.TryGetValue(node.Unversioned(), out reason))
            {
                result[node] = reason;
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(result);
    }
}
