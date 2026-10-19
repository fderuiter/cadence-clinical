using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Dry-run authorization and the command catalogue (ADR-2483, Canvas D8): a dry run and a real command ask the same
/// evaluator the same question, dry runs write nothing to the audit chain and are logged instead, and every kernel
/// command declares its capability.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelAuthorizationTests
{
    private const string Reason = "Unit test";

    private static readonly Actor Monitor = Actor.Person("00u-monitor");

    private static readonly TenantId Tenant = TenantId.New();

    private static readonly NodeRef Training = NodeRef.For(AttestationId.New(), 2);

    /// <summary>Capability tokens are lower snake_case <c>resource.action</c> segments.</summary>
    [Theory(DisplayName = "Capability_AcceptsOnlyWellFormedTokens")]
    [InlineData("observation.record", true)]
    [InlineData("identity.reveal.person", true)]
    [InlineData("study.declare_standards", true)]
    [InlineData("observation", false)]
    [InlineData("Observation.Record", false)]
    [InlineData("observation..record", false)]
    [InlineData("observation.record.", false)]
    [InlineData("observation._record", false)]
    [InlineData("observation.record ", false)]
    public void CapabilityAcceptsOnlyWellFormedTokens(string token, bool valid)
    {
        Capability.IsWellFormed(token).Should().Be(valid);
        var create = () => new Capability(token, VerbClass.Execute);
        if (valid)
        {
            create().Token.Should().Be(token);
        }
        else
        {
            create.Should().Throw<ArgumentException>();
        }
    }

    /// <summary>
    /// Every kernel command declares a capability and a resource with a NodeRef kind, and no two commands share a
    /// token, so no command can reach the pipeline undeclared.
    /// </summary>
    [Fact(DisplayName = "Catalog_EveryKernelCommandDeclaresACapability")]
    public void CatalogEveryKernelCommandDeclaresACapability()
    {
        var commandTypes = typeof(ICommand).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICommand).IsAssignableFrom(t))
            .ToList();

        commandTypes.Should().NotBeEmpty();
        commandTypes.Should().OnlyContain(t => KernelCommandCatalog.Kernel.Find(t) != null, "every kernel command declares [KernelCapability]");
        KernelCommandCatalog.Kernel.Entries.Select(e => e.Capability.Token).Should().OnlyHaveUniqueItems();
        KernelCommandCatalog.Kernel.Entries.Should().OnlyContain(e => NodeKinds.IsRegistered(e.ResourceKind));
    }

    /// <summary>The catalogue lists the commands offered on a kind, and leaves out those never offered on an object.</summary>
    [Fact(DisplayName = "Catalog_ListsTheCommandsOfferedOnAKind")]
    public void CatalogListsTheCommandsOfferedOnAKind()
    {
        KernelCommandCatalog.Kernel.ForKind(NodeKinds.Concept).Select(e => e.Command)
            .Should().Equal(nameof(DeprecateConcept), nameof(ReviseConcept));
        KernelCommandCatalog.Kernel.ForKind(NodeKinds.Tenant).Select(e => e.Command)
            .Should().Contain(nameof(RegisterOrganization)).And.NotContain(nameof(ProvisionTenant));
        KernelCommandCatalog.Kernel.Find(typeof(ProvisionTenant))!.Capability.Should().Be(new Capability("tenant.provision", VerbClass.Execute));
    }

    /// <summary>
    /// The two paths cannot diverge: sending a command through the pipeline and asking CanI for the same capability
    /// and resource put an identical request to the one evaluator and get the same decision and explanation. Only the
    /// command opens a transaction and records the denial; the dry run touches no persistence.
    /// </summary>
    [Fact(DisplayName = "DryRun_AndCommand_AskTheEvaluatorTheSameQuestion")]
    public async Task DryRunAndCommandAskTheEvaluatorTheSameQuestion()
    {
        var harness = new Harness();
        var grant = NodeRef.For(AttestationId.New(), 1);
        harness.Evaluator.Policy = _ => AuthorizationDecision.Deny(
            "Concept stewardship training is not current.",
            EvaluatedCondition.Satisfied("grant.present", "Hold concept.revise", grant),
            EvaluatedCondition.Unsatisfied("qualification.current", "Complete concept stewardship training", Training));
        var command = new ReviseConcept(Tenant, ConceptId.New(), "Systolic blood pressure", "Pressure at systole.", "k-1", Reason);
        var entry = KernelCommandCatalog.Kernel.Find(typeof(ReviseConcept))!;

        var sent = await harness.Sender.SendAsync(command, cancellationToken: TestContext.Current.CancellationToken);
        var dryRun = await harness.Probe.CanIAsync(Tenant, entry.Capability, NodeRef.For(command.ConceptId), TestContext.Current.CancellationToken);

        harness.Evaluator.Requests.Should().HaveCount(2);
        harness.Evaluator.Requests[1].Should().Be(harness.Evaluator.Requests[0]);
        harness.Evaluator.Requests[0].Should().Be(new AuthorizationRequest(
            Monitor, Tenant, new Capability("concept.revise", VerbClass.Execute), NodeRef.For(command.ConceptId),
            harness.Clock.UtcNow, harness.Clock.UtcNow));
        sent.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        sent.Error.Explanation.Should().Be(dryRun.Explain(nameof(ReviseConcept)));
        harness.Transactions.Begun.Should().Be(1);
        harness.Transactions.Denials.Should().ContainSingle().Which.Explain(nameof(ReviseConcept)).Should().Be(dryRun.Explain(nameof(ReviseConcept)));
    }

    /// <summary>
    /// A dry run is logged in the authorization access log (event 2483) with the actor, capability, resource, outcome
    /// and unmet condition codes, and never reaches the audit chain.
    /// </summary>
    [Fact(DisplayName = "DryRun_IsLoggedAndNotAudited")]
    public async Task DryRunIsLoggedAndNotAudited()
    {
        var harness = new Harness();
        harness.Evaluator.Policy = _ => AuthorizationDecision.Deny(
            "Training is not current.", EvaluatedCondition.Unsatisfied("qualification.current", "Complete training", Training));
        var concept = NodeRef.For(ConceptId.New());

        var decision = await harness.Probe.CanIAsync(
            Tenant, new Capability("concept.revise", VerbClass.Execute), concept, TestContext.Current.CancellationToken);

        decision.Permitted.Should().BeFalse();
        harness.Transactions.Begun.Should().Be(0);
        var logged = harness.Log.Entries.Should().ContainSingle().Subject;
        logged.EventId.Should().Be(new EventId(2483, "AuthorizationDryRun"));
        logged.Level.Should().Be(LogLevel.Information);
        logged.Message.Should().Contain(Monitor.ToString()).And.Contain("concept.revise").And.Contain(concept.ToString())
            .And.Contain("permitted False").And.Contain("[qualification.current]");
    }

    /// <summary>A dry run answers only for an accountable caller; with none it is denied and logged (K-TX-5).</summary>
    [Fact(DisplayName = "DryRun_WithoutActor_IsDenied")]
    public async Task DryRunWithoutActorIsDenied()
    {
        var harness = new Harness();
        harness.CurrentActor.Actor = null;

        var decision = await harness.Probe.CanIAsync(
            Tenant, new Capability("concept.revise", VerbClass.Execute), NodeRef.For(ConceptId.New()), TestContext.Current.CancellationToken);

        decision.Permitted.Should().BeFalse();
        decision.Conditions.Should().ContainSingle().Which.Code.Should().Be(KernelAuthorization.ActorResolvedCondition);
        harness.Evaluator.Requests.Should().BeEmpty();
        harness.Log.Entries.Should().ContainSingle();
    }

    /// <summary>
    /// The command catalogue for an object lists each command offered on its kind with the actor's dry-run decision,
    /// explained with the object as subject.
    /// </summary>
    [Fact(DisplayName = "Commands_ForAnObject_CarryTheirDryRunDecisions")]
    public async Task CommandsForAnObjectCarryTheirDryRunDecisions()
    {
        var harness = new Harness();
        harness.Evaluator.Policy = request => request.Capability.VerbClass == VerbClass.Approve
            ? AuthorizationDecision.Deny("Only a terminology approver may deprecate.", EvaluatedCondition.Unsatisfied("grant.present", "Hold concept.deprecate"))
            : AuthorizationDecision.Permit("Granted.", EvaluatedCondition.Satisfied("grant.present", "Hold concept.revise", Training));
        var concept = NodeRef.For(ConceptId.New());

        var commands = await harness.Probe.GetCommandsAsync(Tenant, concept, TestContext.Current.CancellationToken);

        commands.Select(c => (c.Command, c.Decision.Permitted)).Should().Equal((nameof(DeprecateConcept), false), (nameof(ReviseConcept), true));
        commands.Should().OnlyContain(c => c.Resource == concept && c.Explanation.Subject == concept);
        commands[0].Explanation.NextActions.Should().Equal(new NextAction("Hold concept.deprecate"));
        harness.Log.Entries.Should().HaveCount(2);
    }

    /// <summary>A command type without a declared capability is denied without asking the evaluator (fail closed).</summary>
    [Fact(DisplayName = "Command_WithoutCapability_IsDenied")]
    public async Task CommandWithoutCapabilityIsDenied()
    {
        var harness = new Harness();

        var decision = await harness.Authorization.AuthorizeAsync(Monitor, new UndeclaredCommand(Tenant), TestContext.Current.CancellationToken);

        decision.Permitted.Should().BeFalse();
        decision.Conditions.Should().ContainSingle().Which.Code.Should().Be(KernelAuthorization.CapabilityDeclaredCondition);
        harness.Evaluator.Requests.Should().BeEmpty();
    }

    /// <summary>A command whose resource identifier is empty is denied without asking the evaluator.</summary>
    [Fact(DisplayName = "Command_WithEmptyResource_IsDenied")]
    public async Task CommandWithEmptyResourceIsDenied()
    {
        var harness = new Harness();

        var decision = await harness.Authorization.AuthorizeAsync(
            Monitor, new ReviseConcept(Tenant, default, "Label", "Definition", "k-2", Reason), TestContext.Current.CancellationToken);

        decision.Permitted.Should().BeFalse();
        decision.Conditions.Should().ContainSingle().Which.Code.Should().Be(KernelAuthorization.ResourceNamedCondition);
        harness.Evaluator.Requests.Should().BeEmpty();
    }

    /// <summary>A denial without a reason gets the kernel's denial message on both paths.</summary>
    [Fact(DisplayName = "Denial_WithoutReason_GetsTheKernelMessage")]
    public async Task DenialWithoutReasonGetsTheKernelMessage()
    {
        var harness = new Harness();
        harness.Evaluator.Policy = _ => new AuthorizationDecision(false);

        var decision = await harness.Probe.CanIAsync(
            Tenant, new Capability("concept.revise", VerbClass.Execute), NodeRef.For(ConceptId.New()), TestContext.Current.CancellationToken);

        decision.Reason.Should().Be(KernelErrors.AuthorizationDenied.Message);
    }

    private sealed record UndeclaredCommand(TenantId TenantId, string IdempotencyKey = "k", string ReasonForChange = Reason) : ICommand<TenantId>;

    private sealed class Harness
    {
        public Harness()
        {
            var provider = new ServiceCollection()
                .AddSingleton<IClock>(Clock)
                .AddSingleton<ICurrentActor>(CurrentActor)
                .AddSingleton<IAuthorizationEvaluator>(Evaluator)
                .AddSingleton<IKernelUnitOfWorkFactory>(Transactions)
                .AddSingleton<ILogger<KernelAuthorization>>(Log)
                .AddKernelAuthorization()
                .AddKernelCommandPipeline()
                .BuildServiceProvider();
            var scope = provider.CreateScope().ServiceProvider;
            Sender = scope.GetRequiredService<IKernelCommandSender>();
            Probe = scope.GetRequiredService<IAuthorizationProbe>();
            Authorization = scope.GetRequiredService<KernelAuthorization>();
        }

        public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

        public SettableActor CurrentActor { get; } = new() { Actor = Monitor };

        public RecordingEvaluator Evaluator { get; } = new();

        public DenialRecorder Transactions { get; } = new();

        public CapturingLogger Log { get; } = new();

        public IKernelCommandSender Sender { get; }

        public IAuthorizationProbe Probe { get; }

        public KernelAuthorization Authorization { get; }
    }

    private sealed class SettableActor : ICurrentActor
    {
        public Actor? Actor { get; set; }
    }

    private sealed class RecordingEvaluator : IAuthorizationEvaluator
    {
        public Func<AuthorizationRequest, AuthorizationDecision> Policy { get; set; } = _ => AuthorizationDecision.Permit();

        public List<AuthorizationRequest> Requests { get; } = [];

        public ValueTask<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(Policy(request));
        }
    }

    /// <summary>Counts transactions and records denials; any other persistence call is a test failure.</summary>
    private sealed class DenialRecorder : IKernelUnitOfWorkFactory
    {
        public int Begun { get; private set; }

        public List<AuthorizationDecision> Denials { get; } = [];

        public IKernelCommandTransaction Begin(CommandExecution execution)
        {
            Begun++;
            return new Transaction(this);
        }

        private sealed class Transaction(DenialRecorder owner) : IKernelCommandTransaction
        {
            public IKernelUnitOfWork Work => throw new InvalidOperationException("A denied command has no unit of work.");

            public void RecordPermit(AuthorizationDecision permit) =>
                throw new InvalidOperationException("A denied command has no permit.");

            public Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken) =>
                throw new InvalidOperationException("A denied command is not replayed.");

            public Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("A denied command is not committed.");

            public Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken)
            {
                owner.Denials.Add(denial);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<KernelAuthorization>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
