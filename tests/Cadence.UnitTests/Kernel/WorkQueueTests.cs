using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.References;
using Cadence.Kernel.WorkQueues;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Work queues without a database (Canvas D10, ADR-2486): definition rules, and the read keeping only the items the
/// evaluator permits for the definition's capability, masking hidden objects in place, and honouring the limit.
/// </summary>
public sealed class WorkQueueTests
{
    private const string Source = KernelWorkItemSources.ProtocolVersionsInReview;
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Actor Reader = Actor.Person("approver@sponsor.test");
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory(DisplayName = "Define_RefusesInvalidCodeTitleOrCapability")]
    [InlineData("Protocol_Approvals", "Approvals", "protocol.approve", "K.WorkQueue.CodeInvalid")]
    [InlineData("1approvals", "Approvals", "protocol.approve", "K.WorkQueue.CodeInvalid")]
    [InlineData("approvals", " ", "protocol.approve", "K.WorkQueue.TitleInvalid")]
    [InlineData("approvals", "Approvals", "Sponsor Approver", "K.WorkQueue.CapabilityInvalid")]
    [InlineData("approvals", "Approvals", "approve", "K.WorkQueue.CapabilityInvalid")]
    public void DefineRefusesInvalidCodeTitleOrCapability(string code, string title, string capability, string expected)
    {
        var defined = WorkQueueDefinition.Define(WorkQueueDefinitionId.New(), Tenant, code, 1, title, capability, VerbClass.Approve, Source);

        defined.Error!.Code.Should().Be(expected);
    }

    [Fact(DisplayName = "Define_TrimsTheTitle_AndKeepsTheVerbClass")]
    public void DefineTrimsTheTitleAndKeepsTheVerbClass()
    {
        var defined = WorkQueueDefinition.Define(WorkQueueDefinitionId.New(), Tenant, "protocol_approvals", 3, "  Approvals ", "protocol.approve", VerbClass.Approve, Source);

        defined.Value!.Title.Should().Be("Approvals");
        defined.Value.Version.Should().Be(3);
        defined.Value.VerbClass.Should().Be(VerbClass.Approve);
    }

    [Fact(DisplayName = "Read_KeepsOnlyPermittedItems_AskingForTheDefinitionsCapability")]
    public async Task ReadKeepsOnlyPermittedItemsAskingForTheDefinitionsCapability()
    {
        var items = Candidates(3);
        var probe = new Probe(items[0].Subject, items[2].Subject);

        var queue = (await Read(probe, [Definition("protocol_approvals")], [new FixedSource(Source, items)])).Single();

        queue.Items.Select(i => i.Subject).Should().Equal(items[0].Subject, items[2].Subject);
        queue.Items.Should().OnlyContain(i => i.Masked == null && i.Title != null);
        queue.Truncated.Should().BeFalse();
        probe.Asked.Should().HaveCount(3).And.OnlyContain(a => a.Capability == new Capability("protocol.approve", VerbClass.Approve));
    }

    [Fact(DisplayName = "Read_MasksHiddenObjectsInPlace")]
    public async Task ReadMasksHiddenObjectsInPlace()
    {
        var items = Candidates(2);

        var queue = (await Read(
            new Probe(items.Select(i => i.Subject).ToArray()),
            [Definition("protocol_approvals")],
            [new FixedSource(Source, items)],
            new Hide(items[1].Subject, MaskReason.Blinded))).Single();

        queue.Items.Should().HaveCount(2, "a masked item keeps its place so the count is right");
        queue.Items[1].Should().BeEquivalentTo(new WorkItem(null, NodeKinds.ProtocolVersion, null, items[1].Since, MaskReason.Blinded));
    }

    [Fact(DisplayName = "Read_StopsAtTheLimit_AndReportsMore")]
    public async Task ReadStopsAtTheLimitAndReportsMore()
    {
        var items = Candidates(5);
        var probe = new Probe(items.Select(i => i.Subject).ToArray());

        var queue = (await Read(probe, [Definition("protocol_approvals")], [new FixedSource(Source, items)], query: new WorkQueueQuery(Limit: 2))).Single();

        queue.Items.Select(i => i.Subject).Should().Equal(items[0].Subject, items[1].Subject);
        queue.Truncated.Should().BeTrue();
        probe.Asked.Should().HaveCount(2, "no item past the limit is checked");
    }

    [Fact(DisplayName = "Read_FiltersByCode_OrdersByCode_AndLeavesAQueueWithoutASourceEmpty")]
    public async Task ReadFiltersByCodeOrdersByCodeAndLeavesAQueueWithoutASourceEmpty()
    {
        var items = Candidates(1);
        WorkQueueDefinitionSnapshot[] definitions =
        [
            Definition("zeta"),
            Definition("alpha", source: "etmf.documents_to_file"),
            Definition("beta"),
        ];
        var probe = new Probe(items[0].Subject);

        var all = await Read(probe, definitions, [new FixedSource(Source, items)]);
        var filtered = await Read(probe, definitions, [new FixedSource(Source, items)], query: new WorkQueueQuery(Codes: ["zeta"]));

        all.Select(q => q.Code).Should().Equal("alpha", "beta", "zeta");
        all[0].Items.Should().BeEmpty("this host registers no such source");
        all[1].Items.Should().ContainSingle();
        filtered.Select(q => q.Code).Should().Equal("zeta");
    }

    [Fact(DisplayName = "Read_WithoutAnActorOrWithAnInvalidLimit_Fails")]
    public async Task ReadWithoutAnActorOrWithAnInvalidLimitFails()
    {
        var sources = new[] { new FixedSource(Source, Candidates(1)) };
        var anonymous = Service(new Probe(), [Definition("protocol_approvals")], sources, signedIn: false);
        var signedIn = Service(new Probe(), [Definition("protocol_approvals")], sources);

        (await anonymous.ReadAsync(Tenant, new WorkQueueQuery(), TestContext.Current.CancellationToken)).Error.Should().Be(KernelErrors.ActorUnresolved);
        (await signedIn.ReadAsync(Tenant, new WorkQueueQuery(Limit: 0), TestContext.Current.CancellationToken)).Error.Should().Be(WorkQueueErrors.QueryInvalid);
        (await signedIn.ReadAsync(Tenant, new WorkQueueQuery(Limit: WorkQueueQuery.MaxLimit + 1), TestContext.Current.CancellationToken)).Error.Should().Be(WorkQueueErrors.QueryInvalid);
    }

    private static async Task<IReadOnlyList<WorkQueue>> Read(
        Probe probe,
        IReadOnlyList<WorkQueueDefinitionSnapshot> definitions,
        IReadOnlyList<IWorkItemSource> sources,
        INodeVisibility? visibility = null,
        WorkQueueQuery? query = null)
    {
        var result = await Service(probe, definitions, sources, visibility: visibility)
            .ReadAsync(Tenant, query ?? new WorkQueueQuery(), TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private static WorkQueueService Service(
        Probe probe,
        IReadOnlyList<WorkQueueDefinitionSnapshot> definitions,
        IReadOnlyList<IWorkItemSource> sources,
        INodeVisibility? visibility = null,
        bool signedIn = true) =>
        new(new Definitions(definitions), sources, probe, visibility ?? new Hide(null, MaskReason.NotPermitted),
            new FixedActor(signedIn ? Reader : null), NullLogger<WorkQueueService>.Instance);

    private static WorkQueueDefinitionSnapshot Definition(string code, string source = Source) =>
        new(WorkQueueDefinitionId.New(), code, 1, code, "protocol.approve", VerbClass.Approve, source, 1);

    private static List<WorkItemCandidate> Candidates(int count) =>
    [
        .. Enumerable.Range(1, count).Select(n => new WorkItemCandidate(
            NodeRef.For(ProtocolVersionId.New(), 2), $"Protocol version {n}", Start.AddDays(n))),
    ];

    private sealed class Definitions(IReadOnlyList<WorkQueueDefinitionSnapshot> current) : IWorkQueueDefinitions
    {
        public Task<IReadOnlyList<WorkQueueDefinitionSnapshot>> CurrentAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult(current);
    }

    private sealed class FixedSource(string name, IReadOnlyList<WorkItemCandidate> items) : IWorkItemSource
    {
        public string Name { get; } = name;

        public Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkItemCandidate>>([.. items.Take(take)]);
    }

    private sealed class Probe(params NodeRef[] permitted) : IAuthorizationProbe
    {
        public List<(Capability Capability, NodeRef Resource)> Asked { get; } = [];

        public ValueTask<AuthorizationDecision> CanIAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken)
        {
            Asked.Add((capability, resource));
            return ValueTask.FromResult(permitted.Contains(resource) ? AuthorizationDecision.Permit() : AuthorizationDecision.Deny("No grant covers this."));
        }

        public ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Hide(NodeRef? node, MaskReason reason) : INodeVisibility
    {
        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(nodes.Where(n => n == node).ToDictionary(n => n, _ => reason));
    }

    private sealed class FixedActor(Actor? actor) : ICurrentActor
    {
        public Actor? Actor { get; } = actor;
    }
}
