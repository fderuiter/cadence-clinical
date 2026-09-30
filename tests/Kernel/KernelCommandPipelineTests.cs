using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Postgres-free checks of the kernel command contracts and pipeline ordering (spec §5.1, §5.4). The transactional
/// behaviour is covered against a real database by <c>KernelCommandPipelinePostgresTests</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed partial class KernelCommandPipelineTests
{
    private const string Reason = "Unit test";

    /// <summary>
    /// Validates K-TX-5 at construction: generic or anonymous ids are refused, and service accounts and agents must
    /// carry their prefix while people may not.
    /// </summary>
    [Theory(DisplayName = "Actor_RejectsUnaccountableIds")]
    [InlineData(ActorKind.Person, "system")]
    [InlineData(ActorKind.Person, "Anonymous")]
    [InlineData(ActorKind.Person, " ")]
    [InlineData(ActorKind.Person, "svc:relay")]
    [InlineData(ActorKind.Person, "agent:coder")]
    [InlineData(ActorKind.ServiceAccount, "relay")]
    [InlineData(ActorKind.ServiceAccount, "svc:")]
    [InlineData(ActorKind.AutomationAgent, "coder")]
    public void ActorRejectsUnaccountableIds(ActorKind kind, string id)
    {
        var act = () => new Actor(kind, id);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Validates the accountable actor forms: an OIDC subject for a person and <c>svc:</c>-prefixed service accounts.
    /// </summary>
    [Fact(DisplayName = "Actor_AcceptsPersonsAndNamedServiceAccounts")]
    public void ActorAcceptsPersonsAndNamedServiceAccounts()
    {
        Actor.Person("00u1abcd").Should().Be(new Actor(ActorKind.Person, "00u1abcd"));
        Actor.ServiceAccount("outbox-relay").Id.Should().Be("svc:outbox-relay");
        new Actor(ActorKind.AutomationAgent, "agent:mapper").Kind.Should().Be(ActorKind.AutomationAgent);
    }

    /// <summary>
    /// Validates K-ERR-1: every registered kernel error has a unique <c>K.&lt;Area&gt;.&lt;Name&gt;</c> code and an HTTP
    /// status in the 4xx/5xx range.
    /// </summary>
    [Fact(DisplayName = "KernelErrors_Registry_HasUniqueWellFormedCodes")]
    public void KernelErrorsRegistryHasUniqueWellFormedCodes()
    {
        var errors = KernelErrors.All;

        errors.Should().Contain(KernelErrors.AuthorizationDenied).And.Contain(IdentityErrors.TenantUnknown);
        errors.Select(e => e.Code).Should().OnlyHaveUniqueItems();
        errors.Should().OnlyContain(e => ErrorCode().IsMatch(e.Code) && e.HttpStatus >= 400 && e.HttpStatus < 600);
        KernelErrors.AuthorizationDenied.HttpStatus.Should().Be(403);
        KernelErrors.NotFound.HttpStatus.Should().Be(404);
        KernelErrors.IdempotencyConflict.HttpStatus.Should().Be(409);
        KernelErrors.ConcurrencyConflict.HttpStatus.Should().Be(409);
    }

    /// <summary>
    /// Validates that the request hash is stable for equal payloads and changes with any field, which is what lets
    /// K-TX-3 tell a replay from a conflicting reuse of a key.
    /// </summary>
    [Fact(DisplayName = "RequestHash_IsStable_AndCoversEveryField")]
    public void RequestHashIsStableAndCoversEveryField()
    {
        var command = NewTenant();

        var hash = KernelCommandPipeline.ComputeRequestHash(command, typeof(ProvisionTenant));

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        KernelCommandPipeline.ComputeRequestHash(command with { }, typeof(ProvisionTenant)).Should().Be(hash);
        KernelCommandPipeline.ComputeRequestHash(command with { Name = "Other" }, typeof(ProvisionTenant)).Should().NotBe(hash);
        KernelCommandPipeline.ComputeRequestHash(command with { IdempotencyKey = "key-2" }, typeof(ProvisionTenant)).Should().NotBe(hash);
    }

    /// <summary>
    /// Validates that recorded time is truncated to PostgreSQL's microsecond precision, so audit hashes computed before
    /// the write still match after a round-trip.
    /// </summary>
    [Fact(DisplayName = "RecordedTime_IsTruncatedToMicroseconds")]
    public void RecordedTimeIsTruncatedToMicroseconds()
    {
        var value = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(2)).AddTicks(1_234_567);

        var truncated = KernelTimestamps.ToStoredPrecision(value);

        truncated.Offset.Should().Be(TimeSpan.Zero);
        truncated.UtcTicks.Should().Be(value.UtcTicks - 7);
    }

    /// <summary>
    /// Validates the pipeline's order of refusals: payload validation, then actor resolution (K-TX-5), then handler
    /// lookup, each before any unit of work is opened.
    /// </summary>
    [Fact(DisplayName = "Pipeline_RefusesInvalidCommands_BeforeOpeningAUnitOfWork")]
    public async Task PipelineRefusesInvalidCommandsBeforeOpeningAUnitOfWork()
    {
        var ct = TestContext.Current.CancellationToken;
        var actor = new SettableActor { Actor = Actor.Person("user-1") };
        var unitOfWork = new ThrowingUnitOfWorkFactory();
        await using var services = new ServiceCollection()
            .AddSingleton<IClock>(new FakeClock())
            .AddSingleton<ICurrentActor>(actor)
            .AddSingleton<IAuthorizer>(new PermitAll())
            .AddSingleton<IKernelUnitOfWorkFactory>(unitOfWork)
            .AddKernelCommandPipeline()
            .BuildServiceProvider();
        var sender = services.GetRequiredService<IKernelCommandSender>();

        (await sender.SendAsync(NewTenant() with { TenantId = default }, cancellationToken: ct)).Error.Should().Be(KernelErrors.TenantRequired);
        (await sender.SendAsync(NewTenant() with { IdempotencyKey = " " }, cancellationToken: ct)).Error.Should().Be(KernelErrors.IdempotencyKeyRequired);
        (await sender.SendAsync(NewTenant() with { ReasonForChange = "" }, cancellationToken: ct)).Error.Should().Be(KernelErrors.ReasonForChangeRequired);
        (await sender.SendAsync(new UnhandledCommand(TenantId.New(), "key", Reason), cancellationToken: ct)).Error!.Code
            .Should().Be(KernelErrors.HandlerMissing.Code);

        actor.Actor = null;
        (await sender.SendAsync(NewTenant(), cancellationToken: ct)).Error.Should().Be(KernelErrors.ActorUnresolved);

        unitOfWork.Begun.Should().BeFalse();
    }

    /// <summary>
    /// Validates K-TX-3 under a race, with the interleaving forced: a concurrent send with the same key commits after
    /// this send's replay check, so the handler sees the tenant and refuses with AlreadyExists. The pipeline must look
    /// for the key again and return the winner's result as a replay.
    /// </summary>
    [Fact(DisplayName = "Pipeline_HandlerRefusal_AfterSameKeyCommit_ReplaysTheWinner")]
    public async Task PipelineHandlerRefusalAfterSameKeyCommitReplaysTheWinner()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = NewTenant();
        var transaction = new RacingTransaction(WinnerOutcome(command)) { TenantExists = true };
        await using var services = BuildServices(transaction);
        var sender = services.GetRequiredService<IKernelCommandSender>();

        var result = await sender.SendAsync(command, cancellationToken: ct);

        result.IsSuccess.Should().BeTrue();
        result.Replayed.Should().BeTrue();
        result.Value.Should().Be(command.TenantId);
        transaction.Lookups.Should().Be(2);
        transaction.Commits.Should().Be(0);
    }

    /// <summary>
    /// Validates K-TX-3 when the winner commits after this send's handler ran: the commit reports a concurrency
    /// conflict, and the pipeline returns the winner's stored result instead.
    /// </summary>
    [Fact(DisplayName = "Pipeline_ConcurrencyConflict_AfterSameKeyCommit_ReplaysTheWinner")]
    public async Task PipelineConcurrencyConflictAfterSameKeyCommitReplaysTheWinner()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = NewTenant();
        var transaction = new RacingTransaction(WinnerOutcome(command)) { CommitOutcome = CommitOutcome.ConcurrencyConflict };
        await using var services = BuildServices(transaction);
        var sender = services.GetRequiredService<IKernelCommandSender>();

        var result = await sender.SendAsync(command, cancellationToken: ct);

        result.Replayed.Should().BeTrue();
        result.Value.Should().Be(command.TenantId);
        transaction.Lookups.Should().Be(2);
        transaction.Commits.Should().Be(1);
    }

    /// <summary>
    /// Validates that the re-check only replays a stored outcome: with nothing stored under the key, the handler's
    /// refusal and the commit's concurrency conflict reach the caller unchanged, and a key stored for a different
    /// payload is an idempotency conflict.
    /// </summary>
    [Fact(DisplayName = "Pipeline_WithNoStoredOutcome_KeepsItsOwnError")]
    public async Task PipelineWithNoStoredOutcomeKeepsItsOwnError()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = NewTenant();

        var refused = new RacingTransaction(winner: null) { TenantExists = true };
        await using (var services = BuildServices(refused))
        {
            (await services.GetRequiredService<IKernelCommandSender>().SendAsync(command, cancellationToken: ct))
                .Error.Should().Be(KernelErrors.AlreadyExists);
        }

        var conflicted = new RacingTransaction(winner: null) { CommitOutcome = CommitOutcome.ConcurrencyConflict };
        await using (var services = BuildServices(conflicted))
        {
            (await services.GetRequiredService<IKernelCommandSender>().SendAsync(command, cancellationToken: ct))
                .Error.Should().Be(KernelErrors.ConcurrencyConflict);
        }

        var otherPayload = new RacingTransaction(WinnerOutcome(command with { Name = "Other" })) { TenantExists = true };
        await using (var services = BuildServices(otherPayload))
        {
            (await services.GetRequiredService<IKernelCommandSender>().SendAsync(command, cancellationToken: ct))
                .Error.Should().Be(KernelErrors.IdempotencyConflict);
        }
    }

    private static ServiceProvider BuildServices(RacingTransaction transaction) =>
        new ServiceCollection()
            .AddSingleton<IClock>(new FakeClock())
            .AddSingleton<ICurrentActor>(new SettableActor { Actor = Actor.Person("user-1") })
            .AddSingleton<IAuthorizer>(new PermitAll())
            .AddSingleton<IKernelUnitOfWorkFactory>(new SingleTransactionFactory(transaction))
            .AddKernelCommandPipeline()
            .BuildServiceProvider();

    private static IdempotentOutcome WinnerOutcome(ProvisionTenant winner) =>
        new(
            KernelCommandPipeline.ComputeRequestHash(winner, typeof(ProvisionTenant)),
            JsonSerializer.Serialize(winner.TenantId, KernelCommandPipeline.JsonOptions));

    private static ProvisionTenant NewTenant() =>
        new(TenantId.New(), "T-001", "Tenant", "key-1", Reason);

    [GeneratedRegex(@"^K\.[A-Z][A-Za-z]+\.[A-Z][A-Za-z]+$")]
    private static partial Regex ErrorCode();

    private sealed record UnhandledCommand(TenantId TenantId, string IdempotencyKey, string ReasonForChange) : ICommand<bool>;

    private sealed class SettableActor : ICurrentActor
    {
        public Actor? Actor { get; set; }
    }

    private sealed class PermitAll : IAuthorizer
    {
        public ValueTask<AuthorizationDecision> AuthorizeAsync(Actor actor, ICommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AuthorizationDecision.Permit());
    }

    private sealed class ThrowingUnitOfWorkFactory : IKernelUnitOfWorkFactory
    {
        public bool Begun { get; private set; }

        public IKernelCommandTransaction Begin(CommandExecution execution)
        {
            Begun = true;
            throw new InvalidOperationException("The pipeline should have refused the command before opening a unit of work.");
        }
    }

    private sealed class SingleTransactionFactory(IKernelCommandTransaction transaction) : IKernelUnitOfWorkFactory
    {
        public IKernelCommandTransaction Begin(CommandExecution execution) => transaction;
    }

    /// <summary>
    /// A transaction in which a concurrent send with the same key commits right after the first idempotency lookup:
    /// the first lookup finds nothing and every later one finds <paramref name="winner"/>.
    /// </summary>
    private sealed class RacingTransaction(IdempotentOutcome? winner) : IKernelCommandTransaction, IKernelUnitOfWork
    {
        public bool TenantExists { get; init; }

        public CommitOutcome CommitOutcome { get; init; } = CommitOutcome.Committed;

        public int Lookups { get; private set; }

        public int Commits { get; private set; }

        public IKernelUnitOfWork Work => this;

        public Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Lookups++ == 0 ? null : winner);

        public Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken)
        {
            Commits++;
            return Task.FromResult(CommitOutcome);
        }

        public Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The command was permitted.");

        public Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.FromResult<TAggregate?>(null);

        public Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.FromResult(TenantExists);

        public void Add(AggregateRootBase aggregate)
        {
        }

        public void Raise(object domainEvent, DateTimeOffset? occurredValid = null)
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
