using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Search;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Kernel search planning (ADR-2484): what the user types becomes a parameterised prefix query of letters and
/// digits, sources holding identifying content are searched only with their reveal capability, and kinds filter the
/// sources. The SQL itself is covered by <c>KernelSearchPostgresTests</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelSearchTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private static readonly Capability RevealPerson = new("identity.reveal.person", VerbClass.Read);

    /// <summary>Operators and punctuation never reach the query; words become lower-case prefixes joined with AND.</summary>
    [Fact(DisplayName = "Search_ReducesTextToPrefixWords")]
    public async Task SearchReducesTextToPrefixWords()
    {
        var store = new RecordingStore();

        await new KernelSearch(store, new Probe(permit: true)).SearchAsync(
            Tenant, new SearchRequest("  Blood & Pressure | !sys:* 'x'); DROP  "), TestContext.Current.CancellationToken);

        store.Plan!.PrefixQuery.Should().Be("blood:* & pressure:* & sys:* & x:* & drop:*");
        store.Plan.Text.Should().Be("Blood & Pressure | !sys:* 'x'); DROP");
        KernelSearch.Terms("a b c d e f g h i j").Should().HaveCount(KernelSearch.MaxTerms);
        KernelSearch.Terms("Blutdruck systolisch 収縮期").Should().Equal("blutdruck", "systolisch", "収縮期");
    }

    /// <summary>Text with no letters or digits searches nothing.</summary>
    [Fact(DisplayName = "Search_WithoutWords_ReturnsNothing")]
    public async Task SearchWithoutWordsReturnsNothing()
    {
        var store = new RecordingStore();

        var hits = await new KernelSearch(store, new Probe(permit: true)).SearchAsync(Tenant, new SearchRequest(" &|!:* "), TestContext.Current.CancellationToken);

        hits.Should().BeEmpty();
        store.Plan.Should().BeNull();
    }

    /// <summary>
    /// K-IDN-1: a source that holds identifying content is searched only when the actor holds its reveal capability,
    /// asked through the same dry-run authorization as every other control.
    /// </summary>
    [Theory(DisplayName = "IdentitySource_IsSearchedOnlyWithTheRevealCapability")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdentitySourceIsSearchedOnlyWithTheRevealCapability(bool permitted)
    {
        var store = new RecordingStore();
        var probe = new Probe(permitted);

        await new KernelSearch(store, probe).SearchAsync(Tenant, new SearchRequest("smith"), TestContext.Current.CancellationToken);

        string[] expected = permitted ? ["studies", "people"] : ["studies"];
        store.Plan!.Sources.Should().Equal(expected);
        probe.Asked.Should().Equal((RevealPerson, NodeRef.For(Tenant)));
    }

    /// <summary>Kinds limit the sources searched.</summary>
    [Fact(DisplayName = "Search_FiltersSourcesByKind")]
    public async Task SearchFiltersSourcesByKind()
    {
        var store = new RecordingStore();
        var probe = new Probe(permit: true);

        await new KernelSearch(store, probe).SearchAsync(Tenant, new SearchRequest("smith", [NodeKinds.Study]), TestContext.Current.CancellationToken);

        store.Plan!.Sources.Should().Equal("studies");
        probe.Asked.Should().BeEmpty();
    }

    /// <summary>Limits outside 1..50 and an empty tenant are programming errors.</summary>
    [Fact(DisplayName = "Search_RejectsBadLimitsAndTenants")]
    public async Task SearchRejectsBadLimitsAndTenants()
    {
        var search = new KernelSearch(new RecordingStore(), new Probe(permit: true));
        var ct = TestContext.Current.CancellationToken;

        await FluentActions.Awaiting(() => search.SearchAsync(Tenant, new SearchRequest("x", Limit: 0), ct)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => search.SearchAsync(Tenant, new SearchRequest("x", Limit: 51), ct)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => search.SearchAsync(default, new SearchRequest("x"), ct)).Should().ThrowAsync<ArgumentException>();
    }

    private sealed class RecordingStore : IKernelSearchStore
    {
        public IReadOnlyList<SearchSourceInfo> Sources { get; } =
        [
            new("studies", NodeKinds.Study),
            new("people", NodeKinds.Person, RevealPerson),
        ];

        public SearchPlan? Plan { get; private set; }

        public Task<IReadOnlyList<SearchHit>> QueryAsync(TenantId tenant, SearchPlan plan, CancellationToken cancellationToken)
        {
            Plan = plan;
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);
        }
    }

    private sealed class Probe(bool permit) : IAuthorizationProbe
    {
        public List<(Capability Capability, NodeRef Resource)> Asked { get; } = [];

        public ValueTask<AuthorizationDecision> CanIAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken)
        {
            Asked.Add((capability, resource));
            return ValueTask.FromResult(permit
                ? AuthorizationDecision.Permit()
                : AuthorizationDecision.Deny("No reveal grant.", EvaluatedCondition.Unsatisfied("grant.present", "Hold identity.reveal.person")));
        }

        public ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
