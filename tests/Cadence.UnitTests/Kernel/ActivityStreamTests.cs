using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Activity;
using Cadence.Kernel.Application.Activity;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The activity stream (ADR-2485) without a database: every kernel event type has a family, pages carry a cursor
/// only when more entries exist, and entries about objects the reader may not see are masked, never dropped.
/// </summary>
public sealed class ActivityStreamTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Every event type the kernel declares, and the pipeline's own DecisionDenied, has a family.</summary>
    [Fact]
    public void EveryKernelEventTypeHasAFamily()
    {
        var declared = typeof(EventEnvelope).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<KernelEventAttribute>()?.Type)
            .OfType<string>()
            .Append("DecisionDenied")
            .ToList();

        declared.Should().NotBeEmpty();
        declared.Should().OnlyContain(type => KernelEventFamilies.Of(type) != EventFamily.Other);
        KernelEventFamilies.Types.Should().BeEquivalentTo(declared, "a family for an event the kernel never raises is dead configuration");
    }

    [Fact]
    public void UnknownEventTypesFallIntoOther() =>
        KernelEventFamilies.Of("SomethingNew").Should().Be(EventFamily.Other);

    /// <summary>
    /// A page one entry short of the store's answer carries a cursor at its last entry; the last page carries none.
    /// </summary>
    [Fact]
    public async Task PagesCarryACursorOnlyWhenMoreEntriesExist()
    {
        var entries = Enumerable.Range(0, 5).Select(i => Entry(Start.AddMinutes(-i), NodeRef.For(StudyId.New()))).ToList();
        var stream = new ActivityStream(new ListStore(entries), new MaskNothing(), new FixedActor());

        var first = await stream.ReadAsync(Tenant, new ActivityQuery(Limit: 3), TestContext.Current.CancellationToken);
        var last = await stream.ReadAsync(Tenant, new ActivityQuery(Limit: 5), TestContext.Current.CancellationToken);

        first.Entries.Should().HaveCount(3);
        first.Next.Should().Be(new ActivityCursor(entries[2].Recorded, entries[2].EventId));
        last.Entries.Should().HaveCount(5);
        last.Next.Should().BeNull();
    }

    /// <summary>
    /// An entry whose object the reader may not see keeps its place, time, type and kind, loses its reference, and
    /// says why; the visibility check is asked once per page for the distinct subjects only.
    /// </summary>
    [Fact]
    public async Task EntriesAboutHiddenObjectsAreMaskedNotDropped()
    {
        var hidden = NodeRef.For(ProtocolVersionId.New(), 2);
        var visible = NodeRef.For(StudyId.New(), 1);
        var entries = new List<ActivityEntry>
        {
            Entry(Start, hidden),
            Entry(Start.AddMinutes(-1), visible),
            Entry(Start.AddMinutes(-2), hidden),
            Entry(Start.AddMinutes(-3), subject: null),
        };
        var visibility = new MaskOnly(hidden, MaskReason.Blinded);
        var stream = new ActivityStream(new ListStore(entries), visibility, new FixedActor());

        var page = await stream.ReadAsync(Tenant, new ActivityQuery(), TestContext.Current.CancellationToken);

        page.Entries.Select(e => e.EventId).Should().Equal(entries.Select(e => e.EventId));
        page.Entries[0].Should().Match<ActivityEntry>(e => e.Subject == null && e.Masked == MaskReason.Blinded && e.SubjectKind == NodeKinds.ProtocolVersion);
        page.Entries[1].Should().Match<ActivityEntry>(e => e.Subject == visible && e.Masked == null);
        page.Entries[2].Masked.Should().Be(MaskReason.Blinded);
        page.Entries[3].Should().Match<ActivityEntry>(e => e.Subject == null && e.Masked == null);
        visibility.Asked.Should().ContainSingle().Which.Should().BeEquivalentTo([hidden, visible]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ActivityQuery.MaxLimit + 1)]
    public async Task LimitOutsideTheRangeIsRejected(int limit)
    {
        var stream = new ActivityStream(new ListStore([]), new MaskNothing(), new FixedActor());

        var read = () => stream.ReadAsync(Tenant, new ActivityQuery(Limit: limit), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ActivityIsReadOnlyByAnAccountableActor()
    {
        var stream = new ActivityStream(new ListStore([]), new MaskNothing(), new FixedActor(actor: null));

        var read = () => stream.ReadAsync(Tenant, new ActivityQuery(), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ActivityEntry Entry(DateTimeOffset recorded, NodeRef? subject) =>
        new(Guid.CreateVersion7(), "ProtocolVersionDrafted", EventFamily.Knowledge, recorded, null, "reader@cadence.test",
            subject?.Kind, subject, null, Guid.CreateVersion7());

    private sealed class ListStore(IReadOnlyList<ActivityEntry> entries) : IActivityStore
    {
        public Task<IReadOnlyList<ActivityEntry>> QueryAsync(TenantId tenant, ActivityQuery query, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActivityEntry>>([.. entries.Take(take)]);
    }

    private sealed class MaskNothing : INodeVisibility
    {
        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(new Dictionary<NodeRef, MaskReason>());
    }

    private sealed class MaskOnly(NodeRef node, MaskReason reason) : INodeVisibility
    {
        public List<IReadOnlyCollection<NodeRef>> Asked { get; } = [];

        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            Asked.Add(nodes);
            var masked = nodes.Where(n => n == node).ToDictionary(n => n, _ => reason);
            return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(masked);
        }
    }

    private sealed class FixedActor(Actor? actor) : ICurrentActor
    {
        public FixedActor()
            : this(Actor.Person("reader@cadence.test"))
        {
        }

        public Actor? Actor { get; } = actor;
    }
}
