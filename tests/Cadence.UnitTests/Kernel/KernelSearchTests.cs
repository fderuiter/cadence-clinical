using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Search;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Persistence.Search;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Modules.Intelligence.Tools.Kernel;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

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
    [KernelRule("K-IDN-1")]
    [KernelRule("K-ID-8")]
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

    /// <summary>
    /// Text longer than <see cref="KernelSearch.MaxTextLength"/> is cut before it is compared with codes or split into
    /// words, so a word past the cut never reaches the query.
    /// </summary>
    [Fact(DisplayName = "Search_TruncatesLongText_BeforeCodesAndWords")]
    public async Task SearchTruncatesLongTextBeforeCodesAndWords()
    {
        var store = new RecordingStore();
        var text = new string('a', KernelSearch.MaxTextLength - 1) + " tail";

        await new KernelSearch(store, new Probe(permit: true)).SearchAsync(Tenant, new SearchRequest(text), TestContext.Current.CancellationToken);

        store.Plan!.Text.Should().HaveLength(KernelSearch.MaxTextLength).And.Be(text[..KernelSearch.MaxTextLength]);
        store.Plan.PrefixQuery.Should().Be(new string('a', KernelSearch.MaxTextLength - 1) + ":*", "\"tail\" lies past the cut");
    }

    /// <summary>
    /// When no source is left, because the kinds match none or the only match needs a reveal the actor lacks, the store
    /// is never asked.
    /// </summary>
    [Theory(DisplayName = "Search_WithNoSourceLeft_NeverAsksTheStore")]
    [InlineData(NodeKinds.Enrollment, true)]
    [InlineData(NodeKinds.Person, false)]
    public async Task SearchWithNoSourceLeftNeverAsksTheStore(string kind, bool permitted)
    {
        var store = new RecordingStore();

        var hits = await new KernelSearch(store, new Probe(permitted)).SearchAsync(
            Tenant, new SearchRequest("smith", [kind]), TestContext.Current.CancellationToken);

        hits.Should().BeEmpty();
        store.Plan.Should().BeNull();
    }

    /// <summary>The store refuses a source it does not know and returns nothing for no sources, without querying.</summary>
    [Fact(DisplayName = "Store_RefusesUnknownSources_AndReturnsNothingForNone")]
    [KernelRule("K-ID-8")]
    [KernelRule("K-IDN-1")]
    public async Task StoreRefusesUnknownSourcesAndReturnsNothingForNone()
    {
        var store = new KernelSearchStore(null!);
        var ct = TestContext.Current.CancellationToken;

        await FluentActions.Awaiting(() => store.QueryAsync(TenantId.New(), new SearchPlan("x:*", "x", ["studies", "people"], 10), ct))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*people*");
        (await store.QueryAsync(TenantId.New(), new SearchPlan("x:*", "x", [], 10), ct)).Should().BeEmpty();
    }

    /// <summary>
    /// Built-in kernel sources plus Execution's contributor cover all 18 indexed clinical and governance NodeRef kinds
    /// without ever indexing direct participant identity rows (<c>people</c>).
    /// </summary>
    [Fact(DisplayName = "Sources_CoverAll18IndexedNodeKinds_WithoutPeople")]
    [KernelRule("K-IDN-1")]
    public void SourcesCoverAll18IndexedNodeKindsWithoutPeople()
    {
        var store = new KernelSearchStore(null!);
        var kinds = store.Sources.Select(s => s.Kind)
            .Concat(ExecutionSearchSql.Sources.Select(_ => NodeKinds.FormQuery))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        kinds.Should().BeEquivalentTo(
            NodeKinds.Organization,
            NodeKinds.Site,
            NodeKinds.Study,
            NodeKinds.StudySite,
            NodeKinds.Enrollment,
            NodeKinds.ProtocolVersion,
            NodeKinds.CollectionInstance,
            NodeKinds.Observation,
            NodeKinds.FormQuery,
            NodeKinds.ValidationFinding,
            NodeKinds.Qualification,
            NodeKinds.Artifact,
            NodeKinds.Proposal,
            NodeKinds.Concept,
            NodeKinds.CodeSystem,
            NodeKinds.CodeSystemVersion,
            NodeKinds.StandardVersion,
            NodeKinds.LibraryItem);
        store.Sources.Select(s => s.Kind).Should().NotContain(NodeKinds.Person);
        store.Sources.Select(s => s.Name).Should().NotContain("people");
    }

    /// <summary>
    /// <see cref="KernelSearch"/> fans out to registered <see cref="IKernelSearchContributor"/> implementations when
    /// their kind matches, deduplicates hits by <c>(Kind, Id)</c> keeping the highest-ranked version, and respects <c>Limit</c>.
    /// </summary>
    [Fact(DisplayName = "Search_FansOutToContributors_AndMergesByRank")]
    public async Task SearchFansOutToContributorsAndMergesByRank()
    {
        var ct = TestContext.Current.CancellationToken;
        var queryId = FormQueryId.New();
        var studyId = StudyId.New();
        var store = new CannedStore(
            [new("studies", NodeKinds.Study)],
            [new SearchHit(NodeRef.For(studyId, 1), "CADENCE-101 Oncology", "CADENCE-101", null, false, 0.4)]);
        var contributor = new RecordingContributor(
            [new("execution_form_queries", NodeKinds.FormQuery), new("execution_form_query_messages", NodeKinds.FormQuery)],
            [
                new SearchHit(NodeRef.For(queryId, 1), "Query on vital_sbp (Open)", "QRY-10101010", "CADENCE-101 · SITE-101 · Open", false, 0.3),
                new SearchHit(NodeRef.For(queryId, 2), "Confirm systolic BP reading", "QRY-10101010", "CADENCE-101 · SITE-101 · Open", true, 0.9),
            ]);

        var search = new KernelSearch(store, new Probe(permit: true), [contributor]);
        var allHits = await search.SearchAsync(Tenant, new SearchRequest("QRY-10101010", Limit: 10), ct);
        var queryOnly = await search.SearchAsync(Tenant, new SearchRequest("systolic", [NodeKinds.FormQuery], Limit: 5), ct);

        allHits.Should().HaveCount(2);
        allHits[0].Ref.Should().Be(NodeRef.For(queryId, 2));
        allHits[0].ExactCode.Should().BeTrue();
        allHits[1].Ref.Should().Be(NodeRef.For(studyId, 1));
        queryOnly.Should().ContainSingle().Which.Ref.Kind.Should().Be(NodeKinds.FormQuery);
        contributor.LastQuery!.Sources.Should().Equal("execution_form_queries", "execution_form_query_messages");
    }

    private sealed class CannedStore(IReadOnlyList<SearchSourceInfo> sources, IReadOnlyList<SearchHit> hits) : IKernelSearchStore
    {
        public IReadOnlyList<SearchSourceInfo> Sources { get; } = sources;

        public Task<IReadOnlyList<SearchHit>> QueryAsync(TenantId tenant, SearchPlan plan, CancellationToken cancellationToken) =>
            Task.FromResult(hits);
    }

    private sealed class RecordingContributor(IReadOnlyList<SearchContributorSource> sources, IReadOnlyList<SearchHit> hits) : IKernelSearchContributor
    {
        public IReadOnlyList<SearchContributorSource> Sources { get; } = sources;

        public SearchContributorQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<SearchHit>> QueryAsync(TenantId tenant, SearchContributorQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(hits);
        }
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
