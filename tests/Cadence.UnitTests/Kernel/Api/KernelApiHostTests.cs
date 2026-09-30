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
using Cadence.Kernel.Commands;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
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
/// replaced by fakes (their tables are covered by the Postgres tests); tenant resolution is replaced where a test needs
/// a tenant. Authorization is the real <see cref="KernelAuthorization"/> over the host's fail-closed evaluator.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelApiHostTests : IDisposable
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly NodeRef Study = NodeRef.For(StudyId.New());
    private static readonly NodeRef StudySite = NodeRef.For(StudySiteId.New());

    private readonly FakeNavigator _navigator = new();
    private readonly FakeSearch _search = new();
    private readonly HideSome _visibility = new();
    private readonly List<WebApplicationFactory<Cadence.Kernel.Host.Program>> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    /// <summary>
    /// Until the kernel can find a caller's kernel tenant, every kernel read answers 503 with problem details and
    /// reaches no kernel port; the status endpoint still answers.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    public async Task EveryRead_Answers503_UntilTenantsCanBeResolved()
    {
        using var client = Client(resolveTenant: false);
        var ct = TestContext.Current.CancellationToken;

        foreach (var path in new[]
                 {
                     $"/api/v1/kernel/nodes/{Study}", $"/api/v1/kernel/nodes/{Study}/path", $"/api/v1/kernel/nodes/{Study}/children",
                     $"/api/v1/kernel/nodes/{Study}/commands", $"/api/v1/kernel/authorization/can-i?capability=x.y&resource={Study}",
                     "/api/v1/kernel/search?q=CAD",
                 })
        {
            var response = await client.GetAsync(path, ct);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, path);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            (await response.Content.ReadAsStringAsync(ct)).Should().Contain("kernel tenant");
        }

        _navigator.Tenants.Should().BeEmpty();
        _search.Requests.Should().BeEmpty();
        (await client.GetAsync("/api/v1/kernel/status", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A request without the gateway's signed identity is refused before any kernel code runs.</summary>
    [Fact]
    public async Task UnsignedRequest_IsUnauthorized()
    {
        var factory = Factory(resolveTenant: true);
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
    public async Task BadRequestsAndMissingObjects_AnswerWithProblems(string path, HttpStatusCode expected)
    {
        using var client = Client();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>
    /// Until the authority slice ships, CanI denies every capability for the signed-in person and explains why: the
    /// unmet condition is the missing evaluator, and the verb class comes from the declaring command.
    /// </summary>
    [Fact]
    public async Task CanI_FailsClosed_AndExplainsWhy()
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
        root.GetProperty("reason").GetString().Should().Be("Permissions can't be evaluated yet, so no kernel action is permitted.");
        var explanation = root.GetProperty("explanation");
        explanation.GetProperty("producer").GetString().Should().Be("kernel.authorization");
        explanation.GetProperty("subject").GetString().Should().Be(resource.ToString());
        var condition = explanation.GetProperty("conditions").EnumerateArray().Should().ContainSingle().Subject;
        condition.GetProperty("code").GetString().Should().Be("authority.evaluator");
        condition.GetProperty("met").GetBoolean().Should().BeFalse();
    }

    /// <summary>A caller the gateway signed without a subject has no actor, so CanI denies with the unresolved-actor condition.</summary>
    [Fact]
    public async Task CanI_WithoutASubject_DeniesForTheUnresolvedActor()
    {
        var entry = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue);
        var resource = new NodeRef(entry.ResourceKind, Guid.CreateVersion7());
        var factory = Factory(resolveTenant: true, withoutSubject: true);
        using var client = factory.CreateSignedClient();

        using var decision = await ReadJsonAsync(
            client, $"/api/v1/kernel/authorization/can-i?capability={entry.Capability.Token}&resource={resource}",
            TestContext.Current.CancellationToken);

        decision.RootElement.GetProperty("permitted").GetBoolean().Should().BeFalse();
        decision.RootElement.GetProperty("explanation").GetProperty("conditions")[0].GetProperty("code").GetString()
            .Should().Be(KernelAuthorization.ActorResolvedCondition);
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
        var factory = Factory(resolveTenant: true, withoutSubject: true);
        using var client = factory.CreateSignedClient();

        using var node = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Study}", TestContext.Current.CancellationToken);

        node.RootElement.GetProperty("mask").GetString().Should().Be("NotPermitted");
        _visibility.Readers.Should().BeEmpty();
    }

    private HttpClient Client(bool resolveTenant = true, bool hostVisibility = false) =>
        Factory(resolveTenant, hostVisibility: hostVisibility).CreateSignedClient();

    private WebApplicationFactory<Cadence.Kernel.Host.Program> Factory(bool resolveTenant, bool withoutSubject = false, bool hostVisibility = false)
    {
        var factory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKernelNavigator>();
                services.AddSingleton<IKernelNavigator>(_navigator);
                services.RemoveAll<IKernelSearch>();
                services.AddSingleton<IKernelSearch>(_search);
                if (resolveTenant)
                {
                    services.RemoveAll<IKernelTenantResolver>();
                    services.AddSingleton<IKernelTenantResolver>(new FixedTenantResolver(Tenant));
                }

                if (!hostVisibility)
                {
                    services.RemoveAll<INodeVisibility>();
                    services.AddSingleton<INodeVisibility>(_visibility);
                }

                if (withoutSubject)
                {
                    services.AddSingleton<Microsoft.AspNetCore.Authentication.IClaimsTransformation, DropSubject>();
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

    private sealed class FixedTenantResolver(TenantId tenant) : IKernelTenantResolver
    {
        public ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken) =>
            ValueTask.FromResult(caller.Identity?.IsAuthenticated == true
                ? KernelTenantResolution.Resolved(tenant)
                : KernelTenantResolution.Unknown("Not signed in."));
    }

    private sealed class DropSubject : Microsoft.AspNetCore.Authentication.IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var identity = new ClaimsIdentity(
                principal.Claims.Where(c => c.Type != CadenceClaimTypes.Subject),
                principal.Identity?.AuthenticationType);
            return Task.FromResult(new ClaimsPrincipal(identity));
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
