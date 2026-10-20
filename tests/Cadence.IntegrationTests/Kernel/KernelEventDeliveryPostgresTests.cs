using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.Background;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Kernel database whose outbox relay delivers to the in-process consumers, as the composition host wires it
/// (ADR-2601): one consumer that registers an organization for every provisioned tenant, and one that can be made to
/// fail.
/// </summary>
public sealed class KernelEventDeliveryPostgresFixture : KernelPostgresFixture
{
    /// <summary>How many more deliveries the flaky consumer fails.</summary>
    public FlakyConsumerState Flaky { get; } = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IKernelEventPublisher>();
        services
            .AddSingleton(Flaky)
            .AddKernelInProcessEvents()
            .AddKernelEventConsumer<SponsorForNewTenant>("sponsor-for-new-tenant")
            .AddKernelEventConsumer<FlakyTenantConsumer>("flaky-tenant-consumer");
    }
}

/// <summary>Counts and fails deliveries of the flaky consumer.</summary>
public sealed class FlakyConsumerState
{
    private int _failuresLeft;
    private int _deliveries;

    public int Deliveries => Volatile.Read(ref _deliveries);

    public void FailNext(int deliveries) => Volatile.Write(ref _failuresLeft, deliveries);

    public void Deliver()
    {
        Interlocked.Increment(ref _deliveries);
        if (Interlocked.Decrement(ref _failuresLeft) >= 0)
        {
            throw new InvalidOperationException("Flaky consumer failed on purpose.");
        }
    }
}

/// <summary>Registers a sponsor organization for every provisioned tenant, keyed by the delivery.</summary>
internal sealed class SponsorForNewTenant(IKernelCommandSender sender) : KernelEventConsumer<TenantProvisioned>
{
    protected override async Task HandleAsync(TenantProvisioned payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        var result = await sender.SendAsync(
            new RegisterOrganization(
                payload.TenantId,
                OrganizationId.FromGuid(delivery.Envelope.EventId),
                $"SP-{payload.Code}",
                $"Sponsor of {payload.Name}",
                OrganizationKind.Sponsor,
                delivery.IdempotencyKey,
                "Every tenant starts with its sponsor"),
            new CommandMetadata(delivery.Envelope.CorrelationId, delivery.Envelope.EventId),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error!.Message);
        }
    }
}

/// <summary>Handles every provisioned tenant and fails when told to.</summary>
internal sealed class FlakyTenantConsumer(FlakyConsumerState state) : KernelEventConsumer<TenantProvisioned>
{
    protected override Task HandleAsync(TenantProvisioned payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        state.Deliver();
        return Task.CompletedTask;
    }
}

/// <summary>
/// After commit, against a real PostgreSQL server as the application role (ADR-2600 §4.6, ADR-2601): the outbox relay
/// delivers a committed event to the in-process consumers, a consumer's command runs as its service account with the
/// delivery's idempotency key, a retried or redelivered event changes nothing twice, and the leader lock lets one
/// replica run a job's pass at a time. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelEventDeliveryPostgresTests(KernelEventDeliveryPostgresFixture fixture)
    : IClassFixture<KernelEventDeliveryPostgresFixture>
{
    private const string Reason = "Event delivery test";

    /// <summary>
    /// Validates that a committed event reaches the consumer, whose command commits as <c>svc:&lt;consumer&gt;</c> with
    /// the idempotency key <c>&lt;consumer&gt;:&lt;event id&gt;</c> and the event as its causation.
    /// </summary>
    [Fact(DisplayName = "CommittedEvent_ReachesConsumer_WhoseCommandRunsAsItsServiceAccount")]
    [KernelRule("K-TX-5")]
    public async Task CommittedEventReachesConsumerWhoseCommandRunsAsItsServiceAccount()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionAsync(ct);

        await RelayAsync(ct);

        await using var db = fixture.CreateAdminContext();
        var provisioned = await db.DomainEvents.AsNoTracking().SingleAsync(e => e.TenantId == tenant && e.Type == "TenantProvisioned", ct);
        var sponsor = await db.Organizations.AsNoTracking().SingleAsync(o => o.TenantId == tenant, ct);
        sponsor.Id.Should().Be(OrganizationId.FromGuid(provisioned.EventId));
        var activity = await db.ProvenanceActivities.AsNoTracking()
            .SingleAsync(a => a.TenantId == tenant && a.CommandType == nameof(RegisterOrganization), ct);
        activity.ActorId.Should().Be("svc:sponsor-for-new-tenant");
        activity.IdempotencyKey.Should().Be($"sponsor-for-new-tenant:{provisioned.EventId:D}");
        activity.CausationId.Should().Be(provisioned.EventId);
    }

    /// <summary>
    /// Validates at-least-once delivery that acts once in effect: when one consumer fails, the relay retries the event
    /// for every consumer, and the consumer that already succeeded replays its stored outcome instead of registering a
    /// second organization. Redelivering a dispatched event replays again.
    /// </summary>
    [Fact(DisplayName = "RetriedAndRedeliveredEvents_ActOnceInEffect")]
    [KernelRule("K-TX-3")]
    public async Task RetriedAndRedeliveredEventsActOnceInEffect()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionAsync(ct);
        await RelayAsync(ct);
        var before = fixture.Flaky.Deliveries;

        await ExecuteAsync($"UPDATE kernel.outbox_messages SET dispatched_at = NULL WHERE tenant_id = '{tenant.Value:D}'", ct);
        fixture.Flaky.FailNext(1);
        var failed = await fixture.OutboxRelay.RelayBatchAsync(100, ct);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var retried = await fixture.OutboxRelay.RelayBatchAsync(100, ct);

        failed.Failed.Should().Be(1);
        retried.Dispatched.Should().BeGreaterThanOrEqualTo(1);
        fixture.Flaky.Deliveries.Should().Be(before + 2);
        await using var db = fixture.CreateAdminContext();
        (await db.Organizations.CountAsync(o => o.TenantId == tenant, ct)).Should().Be(1);
        (await db.ProvenanceActivities.CountAsync(a => a.TenantId == tenant && a.CommandType == nameof(RegisterOrganization), ct)).Should().Be(1);
        (await db.OutboxMessages.CountAsync(o => o.TenantId == tenant && o.DispatchedAt == null, ct)).Should().Be(0);
    }

    /// <summary>
    /// Validates the leader lock: while one replica runs a job's pass, another replica's pass for the same job is
    /// skipped, a different job still runs, and the job runs again once the first pass ends.
    /// </summary>
    [Fact(DisplayName = "LeaderLock_RunsOneReplicasPassAtATime")]
    public async Task LeaderLockRunsOneReplicasPassAtATime()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var first = new KernelLeaderLock(new KernelConnectionString(fixture.ConnectionString!));
        var second = new KernelLeaderLock(new KernelConnectionString(fixture.ConnectionString!));
        var job = $"test-job-{Guid.NewGuid():N}";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRan = false;

        var holding = first.TryRunAsync(job, async _ => { entered.SetResult(); await release.Task; }, ct);
        await entered.Task.WaitAsync(ct);
        var skipped = await second.TryRunAsync(job, _ => { secondRan = true; return Task.CompletedTask; }, ct);
        var otherJob = await second.TryRunAsync(job + "-other", _ => Task.CompletedTask, ct);
        release.SetResult();
        (await holding).Should().BeTrue();
        var after = await second.TryRunAsync(job, _ => { secondRan = true; return Task.CompletedTask; }, ct);

        skipped.Should().BeFalse();
        otherJob.Should().BeTrue();
        after.Should().BeTrue();
        secondRan.Should().BeTrue("the second replica runs the job once the first has finished");
    }

    private async Task<TenantId> ProvisionAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var command = new ProvisionTenant(TenantId.New(), $"E-{suffix}", $"Event tenant {suffix}", $"key-{suffix}", Reason);
        (await fixture.Sender.SendAsync(command, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        return command.TenantId;
    }

    private async Task RelayAsync(CancellationToken ct)
    {
        var result = await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
        result.Failed.Should().Be(0);
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
