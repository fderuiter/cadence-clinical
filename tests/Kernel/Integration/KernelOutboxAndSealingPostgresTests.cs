using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Outbox;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// The second half of the command pipeline against a real PostgreSQL server (spec §5.1, §5.3): the outbox relay
/// delivers committed events at least once, in per-tenant order, with retries; the sealer verifies and seals each
/// tenant's audit chain, refuses to seal a broken one, and seals make later truncation detectable. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelOutboxAndSealingPostgresTests(KernelPostgresFixture fixture)
    : IClassFixture<KernelPostgresFixture>, IAsyncLifetime
{
    private const string Reason = "Integration test";

    public async ValueTask InitializeAsync()
    {
        if (fixture.ConnectionString is null)
        {
            return;
        }

        fixture.Publisher.Reset();

        // Each test starts with an empty outbox, so it sees only its own events.
        await fixture.OutboxRelay.RelayBatchAsync(10_000, TestContext.Current.CancellationToken);
        fixture.Publisher.Reset();
    }

    public ValueTask DisposeAsync()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.Publisher.Reset();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Validates that the relay delivers each committed event once, in its §5.2 envelope, marks it dispatched, and
    /// delivers nothing on the next pass.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "OutboxRelay_DeliversCommittedEventsOnce_AndMarksThemDispatched")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task OutboxRelayDeliversCommittedEventsOnceAndMarksThemDispatched()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        var organization = NewOrganization(tenant.TenantId);
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(organization, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var first = await fixture.OutboxRelay.RelayBatchAsync(100, ct);
        var second = await fixture.OutboxRelay.RelayBatchAsync(100, ct);

        first.Should().Be(new OutboxRelayResult(2, 0));
        second.Should().Be(new OutboxRelayResult(0, 0));
        var delivered = fixture.Publisher.Delivered;
        delivered.Select(e => e.Type).Should().Equal("TenantProvisioned", "OrganizationRegistered");
        delivered.Should().OnlyContain(e => e.Tenant == tenant.TenantId && e.Actor == KernelPostgresFixture.ActorId);
        delivered[1].Payload.GetProperty("code").GetString().Should().Be(organization.Code);

        await using var db = fixture.CreateContext();
        var outbox = await db.OutboxMessages.AsNoTracking().Where(o => o.TenantId == tenant.TenantId).ToListAsync(ct);
        outbox.Should().HaveCount(2).And.OnlyContain(o => o.DispatchedAt != null && o.Attempts == 0);
        var eventIds = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == tenant.TenantId).Select(e => e.EventId).ToListAsync(ct);
        delivered.Select(e => e.EventId).Should().BeEquivalentTo(eventIds);
    }

    /// <summary>
    /// Validates retry and per-tenant order: when a delivery fails, that event is retried after its backoff with the
    /// error recorded, the same tenant's later events wait behind it, and other tenants are not held up.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "OutboxRelay_FailedDelivery_RetriesAfterBackoff_AndHoldsBackOnlyThatTenant")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task OutboxRelayFailedDeliveryRetriesAfterBackoffAndHoldsBackOnlyThatTenant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var failing = NewTenant();
        var healthy = NewTenant();
        (await fixture.Sender.SendAsync(failing, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(NewOrganization(failing.TenantId), cancellationToken: ct)).IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(healthy, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        fixture.Publisher.Fail = e => e.Tenant == failing.TenantId && e.Type == "TenantProvisioned";

        var failed = await fixture.OutboxRelay.RelayBatchAsync(100, ct);
        var held = await fixture.OutboxRelay.RelayBatchAsync(100, ct);

        failed.Should().Be(new OutboxRelayResult(1, 1));
        held.Should().Be(new OutboxRelayResult(0, 0), "the failed event is backing off and its tenant's next event waits behind it");
        fixture.Publisher.Delivered.Should().ContainSingle().Which.Tenant.Should().Be(healthy.TenantId);
        await using (var db = fixture.CreateContext())
        {
            var retry = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.TenantId == failing.TenantId && o.Type == "TenantProvisioned", ct);
            retry.Attempts.Should().Be(1);
            retry.DispatchedAt.Should().BeNull();
            retry.NextAttemptAt.Should().Be(retry.EnqueuedAt + TimeSpan.FromSeconds(2));
            retry.LastError.Should().Contain("Subscriber rejected TenantProvisioned");
        }

        fixture.Publisher.Fail = null;
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        var recovered = await fixture.OutboxRelay.RelayBatchAsync(100, ct);

        recovered.Should().Be(new OutboxRelayResult(2, 0));
        fixture.Publisher.Delivered.Where(e => e.Tenant == failing.TenantId).Select(e => e.Type)
            .Should().Equal("TenantProvisioned", "OrganizationRegistered");
    }

    /// <summary>
    /// Validates that relays running at the same time never deliver the same event twice: rows one relay has claimed
    /// are skipped by the other (<c>FOR UPDATE SKIP LOCKED</c>).
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "OutboxRelay_ConcurrentRelays_DeliverEachEventOnce")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task OutboxRelayConcurrentRelaysDeliverEachEventOnce()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 6; i++)
        {
            (await fixture.Sender.SendAsync(NewTenant(), cancellationToken: ct)).IsSuccess.Should().BeTrue();
        }

        fixture.Publisher.Delay = TimeSpan.FromMilliseconds(50);

        var results = await Task.WhenAll(
            fixture.OutboxRelay.RelayBatchAsync(3, ct),
            fixture.OutboxRelay.RelayBatchAsync(3, ct),
            fixture.OutboxRelay.RelayBatchAsync(3, ct));
        await fixture.OutboxRelay.RelayBatchAsync(100, ct);

        results.Sum(r => r.Dispatched).Should().BeGreaterThan(0);
        fixture.Publisher.Delivered.Should().HaveCount(6);
        fixture.Publisher.Delivered.Select(e => e.EventId).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Validates the retry schedule: 2^attempts seconds, capped at one hour.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "OutboxRelay_Backoff_DoublesAndIsCapped")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-053")]
    public void OutboxRelayBackoffDoublesAndIsCapped()
    {
        KernelOutboxRelay.Backoff(1).Should().Be(TimeSpan.FromSeconds(2));
        KernelOutboxRelay.Backoff(5).Should().Be(TimeSpan.FromSeconds(32));
        KernelOutboxRelay.Backoff(12).Should().Be(KernelOutboxRelay.MaxBackoff);
        KernelOutboxRelay.Backoff(int.MaxValue).Should().Be(KernelOutboxRelay.MaxBackoff);
    }

    /// <summary>
    /// Validates root sealing: an intact chain is sealed at its head by the named sealer account, a second pass with
    /// no new links writes nothing, and new links are sealed at the new head.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "AuditSealer_SealsIntactChainAtHead_OnlyWhenItGrows")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task AuditSealerSealsIntactChainAtHeadOnlyWhenItGrows()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var first = await fixture.AuditChainSealer.SealAsync(tenant.TenantId, ct);
        var repeat = await fixture.AuditChainSealer.SealAsync(tenant.TenantId, ct);
        (await fixture.Sender.SendAsync(NewOrganization(tenant.TenantId), cancellationToken: ct)).IsSuccess.Should().BeTrue();
        var grown = await fixture.AuditChainSealer.SealAsync(tenant.TenantId, ct);

        first.Outcome.Should().Be(SealOutcome.Sealed);
        repeat.Outcome.Should().Be(SealOutcome.UpToDate);
        grown.Outcome.Should().Be(SealOutcome.Sealed);

        await using var db = fixture.CreateContext();
        var seals = await db.AuditChainSeals.AsNoTracking().Where(s => s.TenantId == tenant.TenantId).OrderBy(s => s.Sequence).ToListAsync(ct);
        var links = await db.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenant.TenantId).OrderBy(a => a.Sequence).ToListAsync(ct);
        seals.Select(s => s.Sequence).Should().Equal(1, 2);
        seals.Select(s => s.Hash).Should().Equal(links.Select(l => l.Hash));
        seals.Should().OnlyContain(s => s.SealedBy == KernelAuditChainSealer.SealerActorId && s.SealedBy == "svc:audit-sealer");
    }

    /// <summary>
    /// Validates that the sealer never seals a broken chain: after a privileged edit of a link it reports the chain
    /// broken and writes no seal.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "AuditSealer_BrokenChain_IsReported_AndNotSealed")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task AuditSealerBrokenChainIsReportedAndNotSealed()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        await BypassInsertOnlyAsync(
            "audit_entries",
            $"UPDATE kernel.audit_entries SET actor_id = 'someone-else' WHERE tenant_id = '{tenant.TenantId.Value}'",
            ct);

        var result = await fixture.AuditChainSealer.SealAsync(tenant.TenantId, ct);

        result.Outcome.Should().Be(SealOutcome.Broken);
        result.Verification.BrokenAtSequence.Should().Be(1);
        await using var db = fixture.CreateContext();
        (await db.AuditChainSeals.AnyAsync(s => s.TenantId == tenant.TenantId, ct)).Should().BeFalse();
    }

    /// <summary>
    /// Validates what a seal adds: if a privileged user deletes the newest link and rewinds the chain head so the
    /// chain is internally consistent again, the verifier still reports it broken because it ends below its seal.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "AuditVerifier_ChainCutBelowItsSeal_IsBroken")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task AuditVerifierChainCutBelowItsSealIsBroken()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(NewOrganization(tenant.TenantId), cancellationToken: ct)).IsSuccess.Should().BeTrue();
        (await fixture.AuditChainSealer.SealAsync(tenant.TenantId, ct)).Outcome.Should().Be(SealOutcome.Sealed);

        await using (var db = fixture.CreateContext())
        {
            var first = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.TenantId == tenant.TenantId && a.Sequence == 1, ct);
            await BypassInsertOnlyAsync(
                "audit_entries",
                $"""
                DELETE FROM kernel.audit_entries WHERE tenant_id = '{tenant.TenantId.Value}' AND sequence = 2;
                UPDATE kernel.audit_chain_heads SET last_sequence = 1, last_hash = '{first.Hash}' WHERE tenant_id = '{tenant.TenantId.Value}';
                """,
                ct);
        }

        var verification = await fixture.AuditChainVerifier.VerifyAsync(tenant.TenantId, ct);

        verification.IsIntact.Should().BeFalse();
        verification.BrokenAtSequence.Should().Be(2);
        verification.Problem.Should().Contain("sealed at 2");
    }

    /// <summary>
    /// Validates that verification while commands are appending never reports an intact chain as broken: the verifier
    /// checks up to the head it read first.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "AuditVerifier_WhileCommandsAppend_StaysIntact")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task AuditVerifierWhileCommandsAppendStaysIntact()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var writers = Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => fixture.Sender.SendAsync(NewOrganization(tenant.TenantId), cancellationToken: ct)));
        var verifications = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => fixture.AuditChainVerifier.VerifyAsync(tenant.TenantId, ct)));
        await writers;

        verifications.Should().OnlyContain(v => v.IsIntact);
        (await fixture.AuditChainVerifier.VerifyAsync(tenant.TenantId, ct)).EntriesChecked.Should().Be(21);
    }

    private static ProvisionTenant NewTenant()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return new ProvisionTenant(TenantId.New(), $"T-{suffix}", $"Tenant {suffix}", $"key-{suffix}", Reason);
    }

    private static RegisterOrganization NewOrganization(TenantId tenant)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return new RegisterOrganization(
            tenant, OrganizationId.New(), $"ORG-{suffix}", $"Sponsor {suffix}", OrganizationKind.Sponsor, $"key-{suffix}", Reason);
    }

    /// <summary>Runs <paramref name="sql"/> with the table's insert-only trigger disabled, as a privileged user could.</summary>
    private async Task BypassInsertOnlyAsync(string table, string sql, CancellationToken ct)
    {
        var trigger = InsertOnlySql.RowTriggerName(table);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            $"ALTER TABLE kernel.{table} DISABLE TRIGGER {trigger}; {sql}; ALTER TABLE kernel.{table} ENABLE TRIGGER {trigger};",
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
