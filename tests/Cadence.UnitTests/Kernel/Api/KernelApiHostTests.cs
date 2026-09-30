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
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
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
        $"/api/v1/kernel/protocol-versions/{NodeRef.For(FirstVersion.Id)}",
    ];

    private readonly FakeNavigator _navigator = new();
    private readonly FakeSearch _search = new();
    private readonly HideSome _visibility = new();
    private readonly FakeTenantDirectory _directory = new();
    private readonly FakeProtocolVersions _protocol = new();
    private readonly FakeAuthorityStore _authority = new();
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
    /// path (with the country above it) and the child list mask it, and search leaves it out. Visibility is asked for
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

        using var children = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}/children", ct);
        var child = children.RootElement.EnumerateArray().Should().ContainSingle().Subject;
        child.GetProperty("node").GetProperty("mask").GetString().Should().Be("Blinded");
        child.GetProperty("node").GetProperty("ref").ValueKind.Should().Be(JsonValueKind.Null);
        child.GetProperty("child_count").ValueKind.Should().Be(JsonValueKind.Null);
        child.GetProperty("group").ValueKind.Should().Be(JsonValueKind.Null);

        using var hits = await ReadJsonAsync(client, "/api/v1/kernel/search?q=CAD", ct);
        hits.RootElement.GetArrayLength().Should().Be(0);

        _visibility.Readers.Should().NotBeEmpty().And.OnlyContain(r => r == Actor.Person(TestSecurityContext.DefaultIdentity.UserId));
    }

    /// <summary>Until visibility is decided from the authority model, the host masks every object it reads.</summary>
    [Fact]
    public async Task HostVisibility_MasksEverything_UntilAuthorityShips()
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

        public Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenant);
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
