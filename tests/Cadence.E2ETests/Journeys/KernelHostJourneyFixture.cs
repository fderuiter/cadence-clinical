using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Xunit;

namespace Cadence.E2ETests.Journeys;

/// <summary>
/// The single host (ADR-2601, ADR-2610: <c>apps/kernel-api</c>, where the kernel and its modules run in one process) on
/// a throwaway PostgreSQL database, for the cross-module journeys (#5471). The database follows the opt-ins of every
/// database fixture (<c>CADENCE_TEST_POSTGRES</c>, then <c>CADENCE_TEST_DOCKER=1</c>); without either the journeys call
/// <see cref="SkipUnlessPostgres"/> and report as skipped, never passed. The kernel migrations are applied as the
/// owner and the host connects as a login role granted exactly the application role's rights (no <c>BYPASSRLS</c>), as
/// in production, so every journey write runs under Row-Level Security.
/// <para>
/// The study a journey needs (tenant, sponsor, site, the approved and effective protocol version, concepts and the
/// journey callers' grants) is arranged through the kernel command pipeline as <see cref="SetupActor"/>, on its own
/// service provider, with every command permitted: arranging the world is not what the journeys test. Every journey
/// step then goes through the host as a gateway-signed caller, over the host's HTTP routes where they exist and
/// through <see cref="KernelHostCommandRelay"/> where the kernel command exists but its route does not yet.
/// </para>
/// </summary>
public sealed class KernelHostJourneyFixture : IAsyncLifetime
{
    /// <summary>The person who arranges each journey's study; never a journey caller.</summary>
    public const string SetupActor = "study-setup@journeys.test";

    /// <summary>The wire format of the kernel routes: snake_case, typed values with a leading or trailing type member.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelHostCommandRelay _relay = new();
    private PostgresTestServer? _server;
    private string? _databaseName;
    private string? _appRole;
    private string? _adminConnectionString;
    private string? _appConnectionString;
    private ServiceProvider? _setup;
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    /// <summary>Skips the calling journey unless a PostgreSQL server was opted in.</summary>
    public void SkipUnlessPostgres() => Assert.SkipWhen(_appConnectionString is null, PostgresTestServer.SkipReason);

    public async ValueTask InitializeAsync()
    {
        _server = await PostgresTestServer.StartAsync().ConfigureAwait(false);
        if (_server is null)
        {
            return;
        }

        var superuser = _server.SuperuserConnectionString;
        _databaseName = $"cadence_e2e_{Guid.NewGuid():N}";
        _appRole = $"{_databaseName}_app";
        var password = Guid.NewGuid().ToString("N");
        await ExecuteAsync(superuser, $"CREATE DATABASE \"{_databaseName}\"").ConfigureAwait(false);
        await ExecuteAsync(superuser, $"CREATE ROLE \"{_appRole}\" LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{password}'").ConfigureAwait(false);
        _adminConnectionString = new NpgsqlConnectionStringBuilder(superuser) { Database = _databaseName }.ConnectionString;
        _appConnectionString = new NpgsqlConnectionStringBuilder(superuser) { Database = _databaseName, Username = _appRole, Password = password }.ConnectionString;

        await KernelDatabase.MigrateAsync(_adminConnectionString).ConfigureAwait(false);
        await ExecuteAsync(_adminConnectionString, KernelDatabase.GrantAppRoleSql(_appRole)).ConfigureAwait(false);

        // The host connects as the application role, so it cannot migrate itself. Its modules (Execution's eCRF forms,
        // #5576, among them) are migrated as the owner exactly as the host registers them, then granted to the role, as a
        // deployment job does (KernelMigrationHostedService, ADR-2601).
        _host = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", _appConnectionString)
            .ConfigureTestServices(services => services
                // Every error a command returns must be one it declares (ADR-2600 §4.5).
                .AddDeclaredKernelErrorsCheck()
                .AddSingleton(_relay)
                .AddSingleton<IStartupFilter>(new KernelHostCommandRelay.StartupFilter(_relay))));
        foreach (var module in _host.Services.GetServices<ModuleMigrationRegistration>())
        {
            await module.MigrateAsync(_adminConnectionString, CancellationToken.None).ConfigureAwait(false);
            await ExecuteAsync(_adminConnectionString, module.GrantAppRoleSql(_appRole)).ConfigureAwait(false);
        }

        // The host reads the real clock (an attestation is accepted only within five minutes of signing in), so the
        // arranged study is dated on the same clock.
        _setup = new ServiceCollection()
            .AddSingleton<IClock>(SystemClock.Instance)
            .AddSingleton<ICurrentActor>(new FixedActor(Actor.Person(SetupActor)))
            .AddSingleton<IAuthorizer>(new PermitArrangement())
            .AddSingleton<IKernelEventPublisher>(new NoSubscribers())
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddKernelPersistence(_appConnectionString)
            .AddKernelCommandPipeline()
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }

        if (_setup is not null)
        {
            await _setup.DisposeAsync().ConfigureAwait(false);
        }

        if (_server is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(_server.SuperuserConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)").ConfigureAwait(false);
            await ExecuteAsync(_server.SuperuserConnectionString, $"DROP ROLE IF EXISTS \"{_appRole}\"").ConfigureAwait(false);
            await _server.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Arranges part of a journey's world as <see cref="SetupActor"/>; a refusal fails the journey.</summary>
    public async Task<TResult> ArrangeAsync<TResult>(ICommand<TResult> command, CancellationToken ct)
    {
        await using var scope = (_setup ?? throw new InvalidOperationException(PostgresTestServer.SkipReason)).CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IKernelCommandSender>().SendAsync(command, cancellationToken: ct);
        result.IsSuccess.Should().BeTrue($"arranging {command.GetType().Name} must succeed: {result.Error?.Code} {result.Error?.Message}");
        return result.Value!;
    }

    /// <summary>A client of the single host that signs <paramref name="userId"/>'s identity in <paramref name="tenantCode"/>.</summary>
    public HttpClient Caller(string tenantCode, string userId)
    {
        return (_host ?? throw new InvalidOperationException(PostgresTestServer.SkipReason)).CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    /// <summary>
    /// Sends a kernel command into the host as <paramref name="caller"/> through <see cref="KernelHostCommandRelay"/>,
    /// with <paramref name="reason"/> in the signed reason header and a fresh idempotency key. Returns the host's result.
    /// </summary>
    public async Task<CommandResult<TResult>> SendThroughHostAsync<TResult>(
        HttpClient caller, string reason, Func<RelayedWrite, ICommand<TResult>> build, CancellationToken ct)
    {
        var id = _relay.Register(build);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{KernelHostCommandRelay.PathPrefix}{id}", UriKind.Relative));
        request.Headers.Add(KernelObservationEndpoints.IdempotencyKeyHeader, $"journey-{Guid.NewGuid():N}");
        request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        using var response = await caller.SendAsync(request, ct);
        return _relay.Take<TResult>(id);
    }

    /// <summary><see cref="SendThroughHostAsync{TResult}"/> that must succeed; returns its value.</summary>
    public async Task<TResult> OkThroughHostAsync<TResult>(HttpClient caller, string reason, Func<RelayedWrite, ICommand<TResult>> build, CancellationToken ct)
    {
        var result = await SendThroughHostAsync(caller, reason, build, ct);
        result.IsSuccess.Should().BeTrue($"the host must accept the step: {result.Error?.Code} {result.Error?.Message}");
        return result.Value!;
    }

    /// <summary>Calls a host HTTP route with an idempotency key and, for a write, the signed reason header.</summary>
    public static async Task<(HttpResponseMessage Response, string Body)> CallAsync(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, CancellationToken ct)
    {
        // A path starting with "/" is any host route (a module's); otherwise one under the kernel's prefix.
        using var request = new HttpRequestMessage(method, new Uri(path.StartsWith('/') ? path : $"/api/v1/kernel/{path}", UriKind.Relative));
        request.Headers.Add(KernelObservationEndpoints.IdempotencyKeyHeader, $"journey-{Guid.NewGuid():N}");
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request, ct);
        return (response, await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>The tenant's kernel audit chain, oldest first, read as the owner (it bypasses Row-Level Security).</summary>
    public async Task<IReadOnlyList<AuditRow>> AuditAsync(TenantId tenant, CancellationToken ct)
    {
        var rows = new List<AuditRow>();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT actor_id, action, aggregate_type, aggregate_id, reason_for_change FROM kernel.audit_entries WHERE tenant_id = @tenant ORDER BY sequence",
            connection);
        command.Parameters.AddWithValue("tenant", tenant.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.GetString(1),
                await reader.IsDBNullAsync(2, ct) ? null : reader.GetString(2),
                await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3),
                reader.GetString(4)));
        }

        return rows;
    }

    /// <summary>
    /// Waits until the tenant's audit chain has an entry about <paramref name="aggregateId"/> by <paramref name="actorId"/>,
    /// for work the host does after commit (an event consumer); fails after thirty seconds.
    /// </summary>
    public async Task<AuditRow> AwaitAuditAsync(TenantId tenant, string aggregateId, string actorId, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            if ((await AuditAsync(tenant, ct)).FirstOrDefault(a => a.AggregateId == aggregateId && a.ActorId == actorId) is { } row)
            {
                return row;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"No audit entry about {aggregateId} by {actorId} within thirty seconds.");
            }
        }
    }

    /// <summary>The <c>participant_id</c> column of one kernel row, read as the owner.</summary>
    public async Task<Guid> ParticipantOfAsync(string table, Guid id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"SELECT participant_id FROM kernel.{table} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (Guid)(await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException($"No {table} row {id}."));
    }

    /// <summary>The payloads of the tenant's committed events of <paramref name="type"/>, oldest first: the integration contract.</summary>
    public async Task<IReadOnlyList<JsonDocument>> EventsAsync(TenantId tenant, string type, CancellationToken ct)
    {
        var events = new List<JsonDocument>();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT payload::text FROM kernel.domain_events WHERE tenant_id = @tenant AND type = @type ORDER BY recorded",
            connection);
        command.Parameters.AddWithValue("tenant", tenant.Value);
        command.Parameters.AddWithValue("type", type);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(JsonDocument.Parse(reader.GetString(0)));
        }

        return events;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>One kernel audit-chain entry.</summary>
    public sealed record AuditRow(string ActorId, string Action, string? AggregateType, string? AggregateId, string ReasonForChange);

    private sealed class FixedActor(Actor actor) : ICurrentActor
    {
        public Actor? Actor { get; } = actor;
    }

    /// <summary>Permits the arrangement commands; the journey steps are authorized by the host's own evaluator.</summary>
    private sealed class PermitArrangement : IAuthorizer
    {
        public ValueTask<AuthorizationDecision> AuthorizeAsync(Actor actor, ICommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AuthorizationDecision.Permit("Arranging the journey's study"));
    }

    /// <summary>The arrangement provider runs no relay; the host's own relay delivers every committed event.</summary>
    private sealed class NoSubscribers : IKernelEventPublisher
    {
        public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
