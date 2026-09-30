using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Activity;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Activity;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Kernel.Api;

/// <summary>
/// The kernel API host's read endpoints (ADR-2540), booted from the real <c>Program.cs</c>. Navigation and search are
/// replaced by fakes (their tables are covered by the Postgres tests), as are the tenant directory, the protocol version
/// reader and the authority store. Authorization is the real <see cref="KernelAuthorization"/> over the kernel's
/// <see cref="AuthorityEvaluator"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelApiHostTests : IDisposable
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Actor Reader = Actor.Person(TestSecurityContext.DefaultIdentity.UserId);
    private static readonly DateTimeOffset GrantedFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly NodeRef Study = NodeRef.For(StudyId.New());
    private static readonly NodeRef StudySite = NodeRef.For(StudySiteId.New());
    private static readonly ProtocolVersionSummary FirstVersion = new(
        ProtocolVersionId.New(), StudyId.FromGuid(Study.Id), 1, null, ProtocolVersionStatus.Approved, "sha256:abc", null,
        AttestationId.New(), null);
    private static readonly ProtocolVersionSummary Amendment = new(
        ProtocolVersionId.New(), StudyId.FromGuid(Study.Id), 2, FirstVersion.Id, ProtocolVersionStatus.Draft, null,
        "Add a week 12 visit", null, null);

    private static readonly string[] ReadPaths =
    [
        $"/api/v1/kernel/nodes/{Study}", $"/api/v1/kernel/nodes/{Study}/path", $"/api/v1/kernel/nodes/{Study}/children",
        $"/api/v1/kernel/nodes/{Study}/commands", $"/api/v1/kernel/authorization/can-i?capability=x.y&resource={Study}",
        "/api/v1/kernel/search?q=CAD", $"/api/v1/kernel/studies/{Study}/protocol-versions",
        $"/api/v1/kernel/protocol-versions/{NodeRef.For(FirstVersion.Id)}", $"/api/v1/kernel/activity?scope={Study}",
    ];

    private readonly FakeNavigator _navigator = new();
    private readonly FakeSearch _search = new();
    private readonly HideSome _visibility = new();
    private readonly FakeTenantDirectory _directory = new();
    private readonly FakeProtocolVersions _protocol = new();
    private readonly FakeAuthorityStore _authority = new();
    private readonly FakeLineage _lineage = new();
    private readonly FakeActivityStore _activity = new();
    private readonly List<WebApplicationFactory<Cadence.Kernel.Host.Program>> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    /// <summary>
    /// A caller whose signed tenant code names no active kernel tenant is refused with 403 problem details on every
    /// kernel read, which reaches no kernel port; the status endpoint still answers.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    public async Task EveryRead_IsRefused_WhenTheSignedTenantIsNotAKernelTenant()
    {
        using var client = Client(knownTenant: false);
        var ct = TestContext.Current.CancellationToken;

        foreach (var path in ReadPaths)
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            (await response.Content.ReadAsStringAsync(ct)).Should().Contain("not an active kernel tenant");
        }

        _directory.Codes.Should().OnlyContain(c => c == GatewayIdentity.DefaultTenantId);
        _navigator.Tenants.Should().BeEmpty();
        _search.Requests.Should().BeEmpty();
        _protocol.Tenants.Should().BeEmpty();
        _activity.Queries.Should().BeEmpty();
        (await client.GetAsync(new Uri("/api/v1/kernel/status", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A signed identity without a tenant code is refused without asking the tenant directory.</summary>
    [Fact]
    public async Task ReadWithoutATenantCode_IsRefused()
    {
        var factory = Factory(dropClaim: CadenceClaimTypes.TenantId);
        using var client = factory.CreateSignedClient();
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync(new Uri($"/api/v1/kernel/nodes/{Study}", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("names no tenant");
        _directory.Codes.Should().BeEmpty();
        _navigator.Tenants.Should().BeEmpty();
    }

    /// <summary>A request without the gateway's signed identity is refused before any kernel code runs.</summary>
    [Fact]
    public async Task UnsignedRequest_IsUnauthorized()
    {
        var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/v1/kernel/nodes/{Study}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _navigator.Tenants.Should().BeEmpty();
    }

    /// <summary>
    /// Navigation reads in the tenant resolved from the caller's claims and answers in snake_case with NodeRef strings:
    /// the summary, the zoom path with its country level, and the children with counts.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    public async Task Navigation_ReadsInTheCallersTenant()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var node = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}", ct);
        node.RootElement.GetProperty("ref").GetString().Should().Be(Study.AtVersion(3).ToString());
        node.RootElement.GetProperty("kind").GetString().Should().Be(NodeKinds.Study);
        node.RootElement.GetProperty("title").GetString().Should().Be("Cadence 101");
        node.RootElement.GetProperty("code").GetString().Should().Be("CAD-101");
        node.RootElement.GetProperty("status").GetString().Should().Be("Active");

        using var path = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{StudySite}/path", ct);
        path.RootElement.EnumerateArray().Select(s => (s.GetProperty("level").GetString(), s.GetProperty("label").GetString()))
            .Should().Equal((NodeKinds.Study, "CAD-101"), (ZoomLevels.Country, "DE"), (NodeKinds.StudySite, "017"));
        path.RootElement[1].GetProperty("ref").ValueKind.Should().Be(JsonValueKind.Null);

        using var children = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}/children", ct);
        var child = children.RootElement.EnumerateArray().Should().ContainSingle().Subject;
        child.GetProperty("node").GetProperty("code").GetString().Should().Be("017");
        child.GetProperty("child_count").GetInt32().Should().Be(4);
        child.GetProperty("group").GetString().Should().Be("DE");

        _navigator.Tenants.Should().NotBeEmpty().And.OnlyContain(t => t == Tenant);
        _directory.Codes.Should().NotBeEmpty().And.OnlyContain(c => c == GatewayIdentity.DefaultTenantId);
    }

    /// <summary>A malformed NodeRef is a 400 and an object the tenant does not hold is a 404, both as problem details.</summary>
    [Theory]
    [InlineData("/api/v1/kernel/nodes/subject:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192F3C4-5E6A-7B8C-9D0E-1F2A3B4C5D6E/path", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/nodes/site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/path", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/nodes/site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/children", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/nodes/nonsense/commands", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/authorization/can-i?capability=not.declared&resource=study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/search?q=CAD&limit=51", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/search?q=CAD&limit=0", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/studies/site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/protocol-versions", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/studies/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/protocol-versions", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/protocol-versions/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/protocol-versions/protocol_version:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/nodes/nonsense/lineage", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/lineage?direction=sideways", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/lineage?direction=1", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/lineage?depth=0", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/dependents?depth=51", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/dependents?edge_kind=Nope", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/dependents?edge_kind=3", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/protocol-versions/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/impact", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/protocol-versions/protocol_version:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/impact", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/activity?scope=nonsense", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/activity?family=Nope", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/activity?family=2", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/activity?limit=0", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/activity?limit=201", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/activity?before=not-a-cursor", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/activity?before=AAAA", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/activity?scope=site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", HttpStatusCode.NotFound)]
    public async Task BadRequestsAndMissingObjects_AnswerWithProblems(string path, HttpStatusCode expected)
    {
        using var client = Client();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>
    /// CanI is decided by the kernel's authority evaluator for the signed-in person: without a grant of the capability
    /// it is a denial whose unmet condition is the missing grant, with the resource as evidence, and the verb class
    /// comes from the declaring command.
    /// </summary>
    [Fact]
    public async Task CanI_WithoutAGrant_Denies_AndExplainsWhy()
    {
        var entry = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue);
        var resource = new NodeRef(entry.ResourceKind, Guid.CreateVersion7());
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var decision = await ReadJsonAsync(
            client, $"/api/v1/kernel/authorization/can-i?capability={entry.Capability.Token}&resource={resource}", ct);

        var root = decision.RootElement;
        root.GetProperty("capability").GetString().Should().Be(entry.Capability.Token);
        root.GetProperty("verb_class").GetString().Should().Be(entry.Capability.VerbClass.ToString());
        root.GetProperty("resource").GetString().Should().Be(resource.ToString());
        root.GetProperty("permitted").GetBoolean().Should().BeFalse();
        root.GetProperty("reason").GetString().Should().Be($"{Reader.Id} holds no grant of {entry.Capability.Token}.");
        var explanation = root.GetProperty("explanation");
        explanation.GetProperty("producer").GetString().Should().Be("kernel.authorization");
        explanation.GetProperty("subject").GetString().Should().Be(resource.ToString());
        var condition = explanation.GetProperty("conditions").EnumerateArray().Should().ContainSingle().Subject;
        condition.GetProperty("code").GetString().Should().Be(AuthorityEvaluator.GrantCondition);
        condition.GetProperty("met").GetBoolean().Should().BeFalse();
        condition.GetProperty("evidence").EnumerateArray().Select(e => e.GetString()).Should().Contain(resource.ToString());
        _authority.Readers.Should().NotBeEmpty().And.OnlyContain(r => r == Reader);
    }

    /// <summary>
    /// A grant of the capability held by the signed-in person, in effect and covering the resource, permits the dry
    /// run, and the satisfied condition names the grant.
    /// </summary>
    [Fact]
    public async Task CanI_WithAGrantInEffect_Permits()
    {
        var entry = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue);
        var resource = new NodeRef(entry.ResourceKind, Guid.CreateVersion7());
        var grant = Grant.Issue(
            GrantId.New(), Tenant, Reader, entry.Capability.Token, null, AuthorityScope.Tenant(), GrantedFrom, null,
            withGrantOption: false, parentGrantId: null, Actor.Person("study-admin"), GrantedFrom).Value!;
        _authority.Grants.Add(grant);
        using var client = Client();

        using var decision = await ReadJsonAsync(
            client, $"/api/v1/kernel/authorization/can-i?capability={entry.Capability.Token}&resource={resource}",
            TestContext.Current.CancellationToken);

        decision.RootElement.GetProperty("permitted").GetBoolean().Should().BeTrue();
        var condition = decision.RootElement.GetProperty("explanation").GetProperty("conditions").EnumerateArray().Should().ContainSingle().Subject;
        condition.GetProperty("code").GetString().Should().Be(AuthorityEvaluator.GrantCondition);
        condition.GetProperty("met").GetBoolean().Should().BeTrue();
        condition.GetProperty("evidence").EnumerateArray().Select(e => e.GetString()).Should().Equal(NodeRef.For(grant.Id).ToString());
    }

    /// <summary>A caller the gateway signed without a subject has no actor, so CanI denies with the unresolved-actor condition.</summary>
    [Fact]
    public async Task CanI_WithoutASubject_DeniesForTheUnresolvedActor()
    {
        var entry = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue);
        var resource = new NodeRef(entry.ResourceKind, Guid.CreateVersion7());
        var factory = Factory(dropClaim: CadenceClaimTypes.Subject);
        using var client = factory.CreateSignedClient();

        using var decision = await ReadJsonAsync(
            client, $"/api/v1/kernel/authorization/can-i?capability={entry.Capability.Token}&resource={resource}",
            TestContext.Current.CancellationToken);

        decision.RootElement.GetProperty("permitted").GetBoolean().Should().BeFalse();
        decision.RootElement.GetProperty("explanation").GetProperty("conditions")[0].GetProperty("code").GetString()
            .Should().Be(KernelAuthorization.ActorResolvedCondition);
    }

    /// <summary>
    /// A signed subject that names no accountable person (a reserved id, or a service account's) gives the request no
    /// actor: CanI denies with the unresolved-actor condition instead of failing.
    /// </summary>
    [Theory]
    [InlineData("system")]
    [InlineData("svc:etl")]
    public async Task CanI_ForASubjectThatIsNoAccountablePerson_DeniesForTheUnresolvedActor(string subject)
    {
        var entry = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue);
        var resource = new NodeRef(entry.ResourceKind, Guid.CreateVersion7());
        using var client = Factory().CreateSignedClient(TestSecurityContext.Identity(subject, "Data Manager"));

        using var decision = await ReadJsonAsync(
            client, $"/api/v1/kernel/authorization/can-i?capability={entry.Capability.Token}&resource={resource}",
            TestContext.Current.CancellationToken);

        decision.RootElement.GetProperty("permitted").GetBoolean().Should().BeFalse();
        decision.RootElement.GetProperty("explanation").GetProperty("conditions")[0].GetProperty("code").GetString()
            .Should().Be(KernelAuthorization.ActorResolvedCondition);
        _authority.Readers.Should().BeEmpty();
    }

    /// <summary>The command catalogue for an object lists every kernel command offered on its kind, each denied and explained.</summary>
    [Fact]
    public async Task Commands_ListTheCatalogueForTheKind_EachDenied()
    {
        var kind = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue).ResourceKind;
        var expected = KernelCommandCatalog.Kernel.ForKind(kind).Select(e => e.Command).ToList();
        var resource = new NodeRef(kind, Guid.CreateVersion7());
        using var client = Client();

        using var commands = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{resource}/commands", TestContext.Current.CancellationToken);

        var rows = commands.RootElement.EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("command").GetString()).Should().Equal(expected);
        rows.Should().OnlyContain(r => !r.GetProperty("decision").GetProperty("permitted").GetBoolean());
        rows.Should().OnlyContain(r => r.GetProperty("decision").GetProperty("explanation").GetProperty("claim").GetString()!.Length > 0);
    }

    /// <summary>Search passes the text, kinds and limit to kernel search in the caller's tenant and returns its hits.</summary>
    [Fact]
    public async Task Search_PassesTheQueryAndReturnsHits()
    {
        using var client = Client();

        using var hits = await ReadJsonAsync(
            client, "/api/v1/kernel/search?q=CAD%20101&kind=study&kind=site&limit=5", TestContext.Current.CancellationToken);

        var (tenant, request) = _search.Requests.Should().ContainSingle().Subject;
        tenant.Should().Be(Tenant);
        request.Text.Should().Be("CAD 101");
        request.Kinds.Should().Equal("study", "site");
        request.Limit.Should().Be(5);
        var hit = hits.RootElement.EnumerateArray().Should().ContainSingle().Subject;
        hit.GetProperty("ref").GetString().Should().Be(Study.AtVersion(3).ToString());
        hit.GetProperty("kind").GetString().Should().Be(NodeKinds.Study);
        hit.GetProperty("exact_code").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// An object the caller may not see keeps its place and kind but nothing that identifies it: the node read, the
    /// path (with the country above it) mask it, and search leaves it out. Visibility is asked for
    /// the signed-in person.
    /// </summary>
    [Fact]
    public async Task HiddenObjects_AreMaskedInPlace_AndLeftOutOfSearch()
    {
        _visibility.Hidden[StudySite.AtVersion(1)] = MaskReason.Blinded;
        _visibility.Hidden[Study.AtVersion(3)] = MaskReason.NotPermitted;
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var node = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{StudySite}", ct);
        node.RootElement.GetProperty("kind").GetString().Should().Be(NodeKinds.StudySite);
        node.RootElement.GetProperty("mask").GetString().Should().Be("Blinded");
        foreach (var hidden in new[] { "ref", "title", "code", "status" })
        {
            node.RootElement.GetProperty(hidden).ValueKind.Should().Be(JsonValueKind.Null, hidden);
        }

        using var path = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{StudySite}/path", ct);
        path.RootElement.EnumerateArray()
            .Select(s => (s.GetProperty("level").GetString(), s.GetProperty("label").GetString(), s.GetProperty("mask").GetString()))
            .Should().Equal(
                (NodeKinds.Study, null, "NotPermitted"),
                (ZoomLevels.Country, null, "Blinded"),
                (NodeKinds.StudySite, null, "Blinded"));

        using var hits = await ReadJsonAsync(client, "/api/v1/kernel/search?q=CAD", ct);
        hits.RootElement.GetArrayLength().Should().Be(0);

        _visibility.Readers.Should().NotBeEmpty().And.OnlyContain(r => r == Actor.Person(TestSecurityContext.DefaultIdentity.UserId));
    }

    /// <summary>
    /// A hidden child of a visible node keeps its place in the list with only its kind and mask; its child count and
    /// group are withheld.
    /// </summary>
    [Fact]
    public async Task Children_MaskAHiddenChildInPlace()
    {
        _visibility.Hidden[StudySite.AtVersion(1)] = MaskReason.Blinded;
        using var client = Client();

        using var children = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}/children", TestContext.Current.CancellationToken);

        var child = children.RootElement.EnumerateArray().Should().ContainSingle().Subject;
        child.GetProperty("node").GetProperty("mask").GetString().Should().Be("Blinded");
        child.GetProperty("node").GetProperty("ref").ValueKind.Should().Be(JsonValueKind.Null);
        child.GetProperty("child_count").ValueKind.Should().Be(JsonValueKind.Null);
        child.GetProperty("group").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// The children of a node the caller may not see are refused, not listed masked: the list would still say how many
    /// there are, such as the participants another site has enrolled (#5033). Nothing about the children is read.
    /// </summary>
    [Fact]
    public async Task Children_OfAHiddenNode_AreRefused()
    {
        _visibility.Hidden[Study.AtVersion(3)] = MaskReason.NotPermitted;
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync(new Uri($"/api/v1/kernel/nodes/{Study}/children", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(ct);
        body.Should().NotContain("Hamburg").And.NotContain(StudySite.Id.ToString());
        _navigator.ChildReads.Should().Be(0);
    }

    /// <summary>
    /// The activity stream (ADR-2485) reads the scope with the caller's filters and answers newest first in snake_case:
    /// an entry about an object the caller may not see keeps its place with its kind and mask reason and no reference,
    /// and the next cursor, passed back as it came, continues after the page's last entry.
    /// </summary>
    [Fact]
    public async Task Activity_ReadsTheScope_MasksHiddenSubjects_AndPages()
    {
        _visibility.Hidden[FakeActivityStore.HiddenSubject] = MaskReason.NotPermitted;
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        using var page = await ReadJsonAsync(
            client, $"/api/v1/kernel/activity?scope={Study}&family=execution&family=Knowledge&actor=crc@site.test&from={Uri.EscapeDataString(from.ToString("O"))}&limit=2", ct);

        var query = _activity.Queries.Should().ContainSingle().Subject;
        query.Tenant.Should().Be(Tenant);
        query.Query.Scope.Should().Be(Study);
        query.Query.Families.Should().BeEquivalentTo([EventFamily.Execution, EventFamily.Knowledge]);
        query.Query.ActorId.Should().Be("crc@site.test");
        query.Query.From.Should().Be(from);
        query.Query.Limit.Should().Be(2);
        query.Take.Should().Be(3, "one extra entry says whether there is a next page");

        var entries = page.RootElement.GetProperty("entries").EnumerateArray().ToList();
        entries.Select(e => e.GetProperty("type").GetString()).Should().Equal("ParticipantScreened", "EnrollmentStatusChanged");
        entries[0].GetProperty("subject").GetString().Should().Be(FakeActivityStore.VisibleSubject.ToString());
        entries[0].GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);
        entries[0].GetProperty("family").GetString().Should().Be("Execution");
        entries[0].GetProperty("actor_id").GetString().Should().Be("crc@site.test");
        entries[1].GetProperty("subject").ValueKind.Should().Be(JsonValueKind.Null);
        entries[1].GetProperty("subject_kind").GetString().Should().Be(NodeKinds.Enrollment);
        entries[1].GetProperty("mask").GetString().Should().Be("NotPermitted");
        var next = page.RootElement.GetProperty("next").GetString();
        next.Should().NotBeNullOrEmpty();
        _visibility.Readers.Should().OnlyContain(r => r == Reader);

        using var last = await ReadJsonAsync(client, $"/api/v1/kernel/activity?scope={Study}&before={next}", ct);

        _activity.Queries[^1].Query.Before.Should().Be(new ActivityCursor(FakeActivityStore.Entries[1].Recorded, FakeActivityStore.Entries[1].EventId));
        last.RootElement.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// The activity of a scope the caller may not see is refused, not listed masked: its entries would describe it.
    /// Without a scope the gate is the tenant, which a caller with no grant in effect does not see.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Activity_OfAHiddenScope_IsRefused(bool scoped)
    {
        _visibility.Hidden[scoped ? Study.AtVersion(3) : NodeRef.For(Tenant)] = MaskReason.NotPermitted;
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync(new Uri(scoped ? $"/api/v1/kernel/activity?scope={Study}" : "/api/v1/kernel/activity", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        _activity.Queries.Should().BeEmpty();
    }

    /// <summary>
    /// The host decides visibility from the reader's grants (ADR-2457): with no grant in effect every object it reads is
    /// masked in place, and search leaves every hit out.
    /// </summary>
    [Fact]
    public async Task HostVisibility_WithoutAGrant_MasksEverything()
    {
        using var client = Client(hostVisibility: true);
        var ct = TestContext.Current.CancellationToken;

        using var node = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}", ct);
        node.RootElement.GetProperty("mask").GetString().Should().Be("NotPermitted");
        node.RootElement.GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);

        using var path = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{StudySite}/path", ct);
        path.RootElement.EnumerateArray().Should().OnlyContain(s => s.GetProperty("mask").GetString() == "NotPermitted");

        using var hits = await ReadJsonAsync(client, "/api/v1/kernel/search?q=CAD", ct);
        hits.RootElement.GetArrayLength().Should().Be(0);
        _authority.Readers.Should().OnlyContain(r => r == Reader);
    }

    /// <summary>A tenant-wide grant in effect lets the reader see the objects the host reads, and find them in search.</summary>
    [Fact]
    public async Task HostVisibility_WithATenantWideGrant_ShowsWhatTheGrantCovers()
    {
        var capability = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue).Capability.Token;
        _authority.Grants.Add(Grant.Issue(
            GrantId.New(), Tenant, Reader, capability, null, AuthorityScope.Tenant(), GrantedFrom, null,
            withGrantOption: false, parentGrantId: null, Actor.Person("study-admin"), GrantedFrom).Value!);
        using var client = Client(hostVisibility: true);
        var ct = TestContext.Current.CancellationToken;

        using var node = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}", ct);
        node.RootElement.GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);
        node.RootElement.GetProperty("title").GetString().Should().NotBeNullOrEmpty();

        using var hits = await ReadJsonAsync(client, "/api/v1/kernel/search?q=CAD", ct);
        hits.RootElement.GetArrayLength().Should().BePositive();
    }

    /// <summary>A caller without a verified subject is no accountable reader, so every object is masked for them.</summary>
    [Fact]
    public async Task ReaderWithoutASubject_SeesEverythingMasked()
    {
        var factory = Factory(dropClaim: CadenceClaimTypes.Subject);
        using var client = factory.CreateSignedClient();

        using var node = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}", TestContext.Current.CancellationToken);

        node.RootElement.GetProperty("mask").GetString().Should().Be("NotPermitted");
        _visibility.Readers.Should().BeEmpty();
    }

    /// <summary>
    /// A study's protocol versions come newest first, in the caller's tenant, without content: number, parent,
    /// status, content hash, amendment intent and approving attestation, each object as a NodeRef.
    /// </summary>
    [Fact]
    public async Task ProtocolVersions_ListNewestFirst_InTheCallersTenant()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var list = await ReadJsonAsync(client, $"/api/v1/kernel/studies/{Study}/protocol-versions", ct);

        var rows = list.RootElement.EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("number").GetInt32()).Should().Equal(2, 1);
        var amendment = rows[0];
        amendment.GetProperty("ref").GetString().Should().Be(NodeRef.For(Amendment.Id).ToString());
        amendment.GetProperty("study").GetString().Should().Be(Study.ToString());
        amendment.GetProperty("parent_version").GetString().Should().Be(NodeRef.For(FirstVersion.Id).ToString());
        amendment.GetProperty("status").GetString().Should().Be("Draft");
        amendment.GetProperty("amendment_intent").GetString().Should().Be("Add a week 12 visit");
        amendment.GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);
        var first = rows[1];
        first.GetProperty("status").GetString().Should().Be("Approved");
        first.GetProperty("content_hash").GetString().Should().Be("sha256:abc");
        first.GetProperty("approval_attestation").GetString().Should().Be(NodeRef.For(FirstVersion.ApprovalAttestationId!.Value).ToString());

        using var one = await ReadJsonAsync(client, $"/api/v1/kernel/protocol-versions/{NodeRef.For(Amendment.Id)}", ct);
        one.RootElement.GetProperty("number").GetInt32().Should().Be(2);

        _protocol.Tenants.Should().NotBeEmpty().And.OnlyContain(t => t == Tenant);
    }

    /// <summary>
    /// A protocol version the caller may not see keeps its place in the list with only its mask; the single read masks
    /// it the same way; and a study the caller may not see has its versions refused, not listed.
    /// </summary>
    [Fact]
    public async Task ProtocolVersions_MaskHiddenVersions_AndRefuseAHiddenStudy()
    {
        _visibility.Hidden[NodeRef.For(FirstVersion.Id)] = MaskReason.NotPermitted;
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var list = await ReadJsonAsync(client, $"/api/v1/kernel/studies/{Study}/protocol-versions", ct);
        var hidden = list.RootElement[1];
        hidden.GetProperty("mask").GetString().Should().Be("NotPermitted");
        foreach (var field in new[] { "ref", "study", "number", "parent_version", "status", "content_hash", "approval_attestation" })
        {
            hidden.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null, field);
        }

        using var one = await ReadJsonAsync(client, $"/api/v1/kernel/protocol-versions/{NodeRef.For(FirstVersion.Id)}", ct);
        one.RootElement.GetProperty("mask").GetString().Should().Be("NotPermitted");
        one.RootElement.GetProperty("number").ValueKind.Should().Be(JsonValueKind.Null);

        _visibility.Hidden[Study.AtVersion(3)] = MaskReason.Blinded;
        var refused = await client.GetAsync(new Uri($"/api/v1/kernel/studies/{Study}/protocol-versions", UriKind.Relative), ct);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>
    /// Trace walks the lineage in the caller's tenant as the signed-in reader, with the direction, depth, edge kinds and
    /// times asked for, and returns masked objects in place with their handles.
    /// </summary>
    [Fact]
    public async Task Trace_WalksTheLineageAsTheReader_WithMaskedObjectsInPlace()
    {
        using var client = Client();

        using var graph = await ReadJsonAsync(
            client,
            $"/api/v1/kernel/nodes/{Study}/lineage?direction=forward&depth=3&edge_kind=DerivedFrom&edge_kind=governedby&valid_at=2026-06-01T00:00:00Z",
            TestContext.Current.CancellationToken);

        var (tenant, reader, query) = _lineage.Traces.Should().ContainSingle().Subject;
        tenant.Should().Be(Tenant);
        reader.Should().Be(Reader);
        query.Node.Should().Be(Study);
        query.Direction.Should().Be(LineageDirection.Forward);
        query.Depth.Should().Be(3);
        query.EdgeKinds.Should().BeEquivalentTo([LineageEdgeKind.DerivedFrom, LineageEdgeKind.GovernedBy]);
        query.ValidAt.Should().Be(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        query.KnownAt.Should().BeNull();

        var root = graph.RootElement;
        root.GetProperty("direction").GetString().Should().Be("Forward");
        root.GetProperty("truncated").GetBoolean().Should().BeTrue();
        var masked = root.GetProperty("nodes")[1];
        masked.GetProperty("handle").GetString().Should().Be("masked:1");
        masked.GetProperty("kind").GetString().Should().Be(NodeKinds.Participant);
        masked.GetProperty("ref").ValueKind.Should().Be(JsonValueKind.Null);
        masked.GetProperty("mask").GetString().Should().Be("Blinded");
        var edge = root.GetProperty("edges").EnumerateArray().Should().ContainSingle().Subject;
        edge.GetProperty("to").GetString().Should().Be("masked:1");
        edge.GetProperty("kind").GetString().Should().Be("DerivedFrom");
        edge.GetProperty("activity").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Trace defaults to a backward walk of the kernel's default depth over every edge kind.</summary>
    [Fact]
    public async Task Trace_DefaultsToABackwardWalk()
    {
        using var client = Client();

        using var graph = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}/lineage", TestContext.Current.CancellationToken);

        var (_, _, query) = _lineage.Traces.Should().ContainSingle().Subject;
        query.Direction.Should().Be(LineageDirection.Backward);
        query.Depth.Should().Be(LineageQuery.DefaultDepth);
        query.EdgeKinds.Should().BeNull();
    }

    /// <summary>Dependents come grouped by kind with counts that include masked objects, and their total.</summary>
    [Fact]
    public async Task Dependents_AreGroupedByKind_CountingMaskedObjects()
    {
        using var client = Client();

        using var report = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}/dependents?depth=2", TestContext.Current.CancellationToken);

        var (tenant, reader, query) = _lineage.Dependents.Should().ContainSingle().Subject;
        (tenant, reader, query.Node, query.Depth).Should().Be((Tenant, Reader, Study, 2));
        var root = report.RootElement;
        root.GetProperty("total").GetInt32().Should().Be(2);
        var group = root.GetProperty("groups").EnumerateArray().Should().ContainSingle().Subject;
        group.GetProperty("kind").GetString().Should().Be(NodeKinds.Participant);
        group.GetProperty("count").GetInt32().Should().Be(2);
        group.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("mask").GetString()).Should().Equal(null, "Blinded");
    }

    /// <summary>
    /// An amendment's impact names the amended and parent versions, the elements it declares and the elements it
    /// actually changes, with the dependents counted the Impact way; a first version amends nothing.
    /// </summary>
    [Fact]
    public async Task AmendmentImpact_CountsWhatDependsOnTheAmendedVersion()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var impact = await ReadJsonAsync(client, $"/api/v1/kernel/protocol-versions/{NodeRef.For(Amendment.Id)}/impact", ct);

        var root = impact.RootElement;
        root.GetProperty("amended").GetString().Should().Be(NodeRef.For(Amendment.Id).ToString());
        root.GetProperty("parent").GetString().Should().Be(NodeRef.For(FirstVersion.Id).ToString());
        root.GetProperty("impacted_elements").EnumerateArray().Select(e => e.GetString()).Should().Equal("schedule_of_activities");
        root.GetProperty("dependents").GetProperty("total").GetInt32().Should().Be(2);
        var changed = root.GetProperty("changed_elements").EnumerateArray().Should().ContainSingle().Subject;
        changed.GetProperty("element_id").GetString().Should().Be("act.week12");
        changed.GetProperty("change").GetString().Should().Be("Changed");
        changed.GetProperty("properties").EnumerateArray().Select(p => p.GetString()).Should().Equal("/timing/day");
        _lineage.Impacts.Should().ContainSingle().Which.Should().Be((Tenant, Reader, Amendment.Id));

        var first = await client.GetAsync(new Uri($"/api/v1/kernel/protocol-versions/{NodeRef.For(FirstVersion.Id)}/impact", UriKind.Relative), ct);
        first.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await first.Content.ReadAsStringAsync(ct)).Should().Contain("amends nothing");
    }

    /// <summary>Lineage reads name their reader, so a caller with no accountable person is refused before any walk.</summary>
    [Theory]
    [InlineData("lineage")]
    [InlineData("dependents")]
    public async Task LineageReads_WithoutAnAccountableReader_AreRefused(string read)
    {
        using var client = Factory().CreateSignedClient(TestSecurityContext.Identity("system", "Data Manager"));

        var response = await client.GetAsync(new Uri($"/api/v1/kernel/nodes/{Study}/{read}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _lineage.Traces.Should().BeEmpty();
        _lineage.Dependents.Should().BeEmpty();
    }

    private HttpClient Client(bool knownTenant = true, bool hostVisibility = false) =>
        Factory(knownTenant, hostVisibility: hostVisibility).CreateSignedClient();

    private WebApplicationFactory<Cadence.Kernel.Host.Program> Factory(
        bool knownTenant = true, string? dropClaim = null, bool hostVisibility = false)
    {
        if (knownTenant)
        {
            _directory.Tenants[GatewayIdentity.DefaultTenantId] = Tenant;
        }

        var factory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKernelNavigator>();
                services.AddSingleton<IKernelNavigator>(_navigator);
                services.RemoveAll<IKernelSearch>();
                services.AddSingleton<IKernelSearch>(_search);
                services.RemoveAll<ITenantDirectory>();
                services.AddSingleton<ITenantDirectory>(_directory);
                services.RemoveAll<IProtocolVersionReader>();
                services.AddSingleton<IProtocolVersionReader>(_protocol);
                services.RemoveAll<IAuthorityStore>();
                services.AddSingleton<IAuthorityStore>(_authority);
                services.RemoveAll<IKernelLineage>();
                services.AddSingleton<IKernelLineage>(_lineage);
                services.RemoveAll<IAmendmentImpact>();
                services.AddSingleton<IAmendmentImpact>(_lineage);
                services.RemoveAll<IActivityStore>();
                services.AddSingleton<IActivityStore>(_activity);

                // The activity projector reads the database the host is configured with, which these tests never open.
                var projector = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "KernelActivityProjectionService");
                services.Remove(projector);

                if (!hostVisibility)
                {
                    services.RemoveAll<INodeVisibility>();
                    services.AddSingleton<INodeVisibility>(_visibility);
                }

                if (dropClaim is not null)
                {
                    services.AddSingleton<Microsoft.AspNetCore.Authentication.IClaimsTransformation>(new DropClaim(dropClaim));
                }
            });
        });
        _factories.Add(factory);
        return factory;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private sealed class FakeTenantDirectory : ITenantDirectory
    {
        public Dictionary<string, TenantId> Tenants { get; } = [];

        public List<string> Codes { get; } = [];

        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken)
        {
            Codes.Add(code);
            return Task.FromResult(Tenants.TryGetValue(code, out var tenant) ? tenant : (TenantId?)null);
        }
    }

    /// <summary>The grants a test issues; every resource sits at tenant scope, which only tenant-wide grants cover.</summary>
    private sealed class FakeAuthorityStore : IAuthorityStore
    {
        public List<Grant> Grants { get; } = [];

        public List<Actor> Readers { get; } = [];

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken)
        {
            tenant.Should().Be(Tenant);
            Readers.Add(actor);
            return Task.FromResult<IReadOnlyList<Grant>>([.. Grants.Where(g => g.Grantee == actor)]);
        }

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult(Grants.SingleOrDefault(g => g.Id == grant));

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            Task.FromResult(ScopePath.TenantWide);

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([]);
    }

    /// <summary>A lineage with one visible start and one blinded participant downstream of it.</summary>
    private sealed class FakeLineage : IKernelLineage, IAmendmentImpact
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        private static readonly NodeRef Visible = NodeRef.For(ParticipantId.New(), 1);

        public List<(TenantId, Actor, LineageQuery)> Traces { get; } = [];

        public List<(TenantId, Actor, DependentsQuery)> Dependents { get; } = [];

        public List<(TenantId, Actor, ProtocolVersionId)> Impacts { get; } = [];

        public Task<LineageGraph> TraceAsync(TenantId tenant, Actor reader, LineageQuery query, CancellationToken cancellationToken = default)
        {
            Traces.Add((tenant, reader, query));
            var start = query.Node.ToString();
            return Task.FromResult(new LineageGraph(
                start, query.Direction, query.Depth, Now, Now,
                [new LineageNode(start, query.Node.Kind, query.Node, null, 0), new LineageNode("masked:1", NodeKinds.Participant, null, MaskReason.Blinded, 1)],
                [new LineageEdge(start, "masked:1", LineageEdgeKind.DerivedFrom, null, Now, null)],
                Truncated: true));
        }

        public Task<LineageDossier> DossierAsync(
            TenantId tenant, Actor reader, NodeRef node, int depth = LineageQuery.DefaultDepth, DateTimeOffset? validAt = null,
            DateTimeOffset? knownAt = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The kernel host serves no dossier read.");

        public Task<DependentsReport> DependentsAsync(TenantId tenant, Actor reader, DependentsQuery query, CancellationToken cancellationToken = default)
        {
            Dependents.Add((tenant, reader, query));
            return Task.FromResult(Report(query.Node, query.Depth));
        }

        public Task<AmendmentImpact?> ComputeAsync(
            TenantId tenant, Actor reader, ProtocolVersionId amended, int depth = LineageQuery.DefaultDepth, DateTimeOffset? validAt = null,
            DateTimeOffset? knownAt = null, CancellationToken cancellationToken = default)
        {
            Impacts.Add((tenant, reader, amended));
            return Task.FromResult<AmendmentImpact?>(amended == Amendment.Id
                ? new AmendmentImpact(
                    Amendment.Id,
                    FirstVersion.Id,
                    ["schedule_of_activities"],
                    Report(NodeRef.For(FirstVersion.Id), depth),
                    [new ElementChange("act.week12", "PlannedActivity", ElementChangeKind.Changed, ["/timing/day"])])
                : null);
        }

        private static DependentsReport Report(NodeRef start, int depth) =>
            new(
                start.ToString(), depth, Now, Now,
                [new DependentGroup(NodeKinds.Participant, 2, [
                    new LineageNode(Visible.ToString(), NodeKinds.Participant, Visible, null, 1),
                    new LineageNode("masked:1", NodeKinds.Participant, null, MaskReason.Blinded, 2),
                ])],
                Total: 2,
                Truncated: false);
    }

    private sealed class DropClaim(string type) : Microsoft.AspNetCore.Authentication.IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var identity = new ClaimsIdentity(principal.Claims.Where(c => c.Type != type), principal.Identity?.AuthenticationType);
            return Task.FromResult(new ClaimsPrincipal(identity));
        }
    }

    /// <summary>Two protocol versions of <see cref="Study"/>: an approved first version and a draft amendment of it.</summary>
    private sealed class FakeProtocolVersions : IProtocolVersionReader
    {
        public List<TenantId> Tenants { get; } = [];

        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken)
        {
            Tenants.Add(tenant);
            IReadOnlyList<ProtocolVersionSummary> versions = NodeRef.For(study) == Study ? [Amendment, FirstVersion] : [];
            return Task.FromResult(versions);
        }

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
        {
            Tenants.Add(tenant);
            return Task.FromResult(new[] { Amendment, FirstVersion }.FirstOrDefault(v => v.Id == version));
        }
    }

    /// <summary>Three entries newest first: a visible screening, a status change a test may hide, and an older one.</summary>
    private sealed class FakeActivityStore : IActivityStore
    {
        public static readonly NodeRef VisibleSubject = NodeRef.For(EnrollmentId.New(), 1);
        public static readonly NodeRef HiddenSubject = NodeRef.For(EnrollmentId.New(), 2);
        private static readonly DateTimeOffset At = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

        public static readonly ActivityEntry[] Entries =
        [
            new(Guid.CreateVersion7(), "ParticipantScreened", EventFamily.Execution, At, At, "crc@site.test", NodeKinds.Enrollment, VisibleSubject, null, Guid.CreateVersion7()),
            new(Guid.CreateVersion7(), "EnrollmentStatusChanged", EventFamily.Execution, At.AddSeconds(-1), null, "crc@site.test", NodeKinds.Enrollment, HiddenSubject, null, Guid.CreateVersion7()),
            new(Guid.CreateVersion7(), "ProtocolVersionApproved", EventFamily.Knowledge, At.AddSeconds(-2), null, "crc@site.test", NodeKinds.ProtocolVersion, NodeRef.For(FirstVersion.Id, 3), null, Guid.CreateVersion7()),
        ];

        public List<(TenantId Tenant, ActivityQuery Query, int Take)> Queries { get; } = [];

        public Task<IReadOnlyList<ActivityEntry>> QueryAsync(TenantId tenant, ActivityQuery query, int take, CancellationToken cancellationToken)
        {
            Queries.Add((tenant, query, take));
            var rows = Entries.Where(e => query.Before is not { } before
                || e.Recorded < before.Recorded || (e.Recorded == before.Recorded && e.EventId.CompareTo(before.EventId) < 0));
            return Task.FromResult<IReadOnlyList<ActivityEntry>>([.. rows.Take(take)]);
        }
    }

    /// <summary>Every object is visible except the ones a test hides.</summary>
    private sealed class HideSome : INodeVisibility
    {
        public Dictionary<NodeRef, MaskReason> Hidden { get; } = [];

        public List<Actor> Readers { get; } = [];

        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            tenant.Should().Be(Tenant);
            Readers.Add(reader);
            return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(
                nodes.Where(Hidden.ContainsKey).ToDictionary(n => n, n => Hidden[n]));
        }
    }

    private sealed class FakeNavigator : IKernelNavigator
    {
        private static readonly NodeSummary StudySummary = new(Study.AtVersion(3), "Cadence 101", "CAD-101", "Active");
        private static readonly NodeSummary SiteSummary = new(StudySite.AtVersion(1), "Hamburg Clinic", "017", "Active");

        public List<TenantId> Tenants { get; } = [];

        public Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenant);
            return Task.FromResult(Find(node));
        }

        public Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenant);
            IReadOnlyList<ZoomStep>? path = Find(node) is null
                ? null
                : [
                    new ZoomStep(NodeKinds.Study, "CAD-101", StudySummary.Ref),
                    new ZoomStep(ZoomLevels.Country, "DE", null),
                    new ZoomStep(NodeKinds.StudySite, "017", SiteSummary.Ref),
                ];
            return Task.FromResult(path);
        }

        public int ChildReads { get; private set; }

        public Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenant);
            ChildReads++;
            IReadOnlyList<ScopeChild>? children = Find(node) is null ? null : [new ScopeChild(SiteSummary, 4, "DE")];
            return Task.FromResult(children);
        }

        private static NodeSummary? Find(NodeRef node)
        {
            if (node.Unversioned() == Study)
            {
                return StudySummary;
            }

            return node.Unversioned() == StudySite ? SiteSummary : null;
        }
    }

    private sealed class FakeSearch : IKernelSearch
    {
        public List<(TenantId Tenant, SearchRequest Request)> Requests { get; } = [];

        public Task<IReadOnlyList<SearchHit>> SearchAsync(TenantId tenant, SearchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add((tenant, request));
            return Task.FromResult<IReadOnlyList<SearchHit>>(
                [new SearchHit(Study.AtVersion(3), "Cadence 101", "CAD-101", null, ExactCode: true, Rank: 1)]);
        }
    }
}
