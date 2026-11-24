using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The Canvas review screen's proposal reads and rejection through the real kernel host on PostgreSQL (ADR-2635): an
/// automation agent's proposal is listed and read by a reviewer granted in its study, with its model, plan and content
/// hash; the reviewer rejects it with the signed reason, which the kernel stores as the decision reason; a caller with no
/// grant neither sees nor rejects it. The host runs on the fixture's clock, so expiry is derived as the pipeline derives
/// it. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelProposalHostPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Reviewer = "reviewer@proposal-host.test";
    private const string Stranger = "stranger@proposal-host.test";
    private const string Model = "claude-opus-5";
    private const string RejectionReason = "The plan closes queries the site has not answered";

    private const string Requester = "data-manager@sponsor.test";
    private static readonly Actor Agent = Actor.AutomationAgent("data-reviewer").ActingFor(Actor.Person(Requester));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly KernelProtocolScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose()
    {
        _host?.Dispose();
        if (fixture.ConnectionString is not null)
        {
            fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    /// <summary>
    /// A reviewer lists the study's open proposals and reads the agent's proposal with its model, plan, affected objects
    /// and content hash; a rejection without a reason is refused with the kernel's code, a rejection with one is stored
    /// as the decision reason by the reviewer, and the proposal then lists as rejected and no longer as open. A caller
    /// with no grant is refused the list, the proposal and the rejection.
    /// </summary>
    [Fact(DisplayName = "HostProposal_IsListedReadAndRejected_ByAGrantedReviewer_AndHiddenFromOthers")]
    public async Task HostProposalIsListedReadAndRejectedByAGrantedReviewerAndHiddenFromOthers()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, study, proposal) = await SeedAsync(ct);
        using var reviewer = Client(code, Reviewer);
        using var stranger = Client(code, Stranger);
        var proposalRef = NodeRef.For(proposal).ToString();

        var open = await SendAsync<List<KernelProposalResponse>>(reviewer, HttpMethod.Get, $"studies/{NodeRef.For(study)}/proposals?status=Open", null, HttpStatusCode.OK, ct);
        var listed = open.Should().ContainSingle().Subject;
        listed.Ref.Should().Be(proposalRef);
        listed.ModelId.Should().Be(Model);
        listed.ProposedBy.Should().Be(Agent.Id);
        listed.ProposedByKind.Should().Be(nameof(ActorKind.AutomationAgent));
        listed.RequestedBy.Should().Be(Requester);
        listed.Plan!.Title.Should().Be("Proposed plan and reasoning");
        listed.Affected!.Select(a => (a.Ref, a.Mask)).Should().Equal((NodeRef.For(study).ToString(), (string?)null));
        listed.RequestedCapabilities.Should().Equal("protocol.approve");

        var read = await SendAsync<KernelProposalResponse>(reviewer, HttpMethod.Get, $"proposals/{proposalRef}", null, HttpStatusCode.OK, ct);
        read.Should().BeEquivalentTo(listed);

        (await StatusAsync(stranger, HttpMethod.Get, $"studies/{NodeRef.For(study)}/proposals", null, ct)).Should().Be((HttpStatusCode.Forbidden, null));
        (await StatusAsync(stranger, HttpMethod.Get, $"proposals/{proposalRef}", null, ct)).Should().Be((HttpStatusCode.Forbidden, null));
        (await StatusAsync(stranger, HttpMethod.Post, $"proposals/{proposalRef}/rejection", RejectionReason, ct))
            .Should().Be((HttpStatusCode.Forbidden, KernelErrors.AuthorizationDenied.Code));
        (await StatusAsync(reviewer, HttpMethod.Post, $"proposals/{proposalRef}/rejection", null, ct))
            .Should().Be(((HttpStatusCode)422, KernelErrors.ReasonForChangeRequired.Code));

        var rejected = await SendAsync<KernelProposalStateResponse>(reviewer, HttpMethod.Post, $"proposals/{proposalRef}/rejection", RejectionReason, HttpStatusCode.OK, ct);
        rejected.Status.Should().Be(nameof(ProposalStatus.Rejected));
        rejected.DecidedBy.Should().Be(Reviewer);
        (await StatusAsync(reviewer, HttpMethod.Post, $"proposals/{proposalRef}/rejection", RejectionReason, ct))
            .Should().Be((HttpStatusCode.Conflict, ProposalErrors.NotOpen.Code));

        (await SendAsync<List<KernelProposalResponse>>(reviewer, HttpMethod.Get, $"studies/{NodeRef.For(study)}/proposals?status=Open", null, HttpStatusCode.OK, ct))
            .Should().BeEmpty();
        var decided = await SendAsync<List<KernelProposalResponse>>(reviewer, HttpMethod.Get, $"studies/{NodeRef.For(study)}/proposals?status=rejected", null, HttpStatusCode.OK, ct);
        decided.Should().ContainSingle().Which.DecisionReason.Should().Be(RejectionReason);

        await using var db = fixture.CreateContext(tenant);
        var stored = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposal, ct);
        stored.Status.Should().Be(ProposalStatus.Rejected);
        stored.DecidedById.Should().Be(Reviewer);
        stored.DecisionReason.Should().Be(RejectionReason);
    }

    /// <summary>An open proposal past its expiry lists as expired, never as open, and can no longer be rejected.</summary>
    [Fact(DisplayName = "HostProposal_PastItsExpiry_ListsAsExpired_AndIsNotRejected")]
    public async Task HostProposalPastItsExpiryListsAsExpiredAndIsNotRejected()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, _, study, proposal) = await SeedAsync(ct);
        using var reviewer = Client(code, Reviewer);
        fixture.Clock.Advance(TimeSpan.FromDays(8));

        (await SendAsync<List<KernelProposalResponse>>(reviewer, HttpMethod.Get, $"studies/{NodeRef.For(study)}/proposals?status=Open", null, HttpStatusCode.OK, ct))
            .Should().BeEmpty();
        (await SendAsync<List<KernelProposalResponse>>(reviewer, HttpMethod.Get, $"studies/{NodeRef.For(study)}/proposals?status=Expired", null, HttpStatusCode.OK, ct))
            .Should().ContainSingle().Which.Ref.Should().Be(NodeRef.For(proposal).ToString());
        (await StatusAsync(reviewer, HttpMethod.Post, $"proposals/{NodeRef.For(proposal)}/rejection", RejectionReason, ct))
            .Should().Be((HttpStatusCode.Conflict, ProposalErrors.Expired.Code));
    }

    /// <summary>A tenant with a study, a plan artifact and an agent's open proposal; the reviewer granted proposal.reject in the study.</summary>
    private async Task<(string Code, TenantId Tenant, StudyId Study, ProposalId Proposal)> SeedAsync(CancellationToken ct)
    {
        var code = $"T-PRP-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, code, "Proposal host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.NewStudyAsync(tenant, ct);
        var bytes = $"plan-{Guid.NewGuid():N}";
        var plan = ArtifactVersionId.New();
        await _scenario.OkAsync(
            new RegisterArtifact(
                tenant, ArtifactId.New(), plan, ArtifactKind.Other, study, "Proposed plan and reasoning",
                new ArtifactContent(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), bytes.Length, "application/json", $"blob://plans/{bytes}"),
                KernelProtocolScenario.Key(), "Plan filed"),
            ct);
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(Reviewer), "proposal.reject", null, AuthorityScope.Study(study), Tick(), null, false,
                KernelProtocolScenario.Key(), "Appointed"),
            ct);

        var proposal = ProposalId.New();
        fixture.CurrentActor.Actor = Agent;
        try
        {
            await _scenario.OkAsync(
                new Propose(
                    tenant, proposal, study, "Close the stale queries at site 101", plan, [NodeRef.For(study)], null, ["protocol.approve"],
                    fixture.Clock.UtcNow.AddDays(7), Model, KernelProtocolScenario.Key(), "Data review suggestion"),
                ct);
        }
        finally
        {
            fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }

        Tick();
        return (code, tenant, study, proposal);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                // Expiry is derived from the clock the fixture's commands were recorded with.
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(fixture.Clock);

                // Every error the rejection returns must be one its command declares (ADR-2600 §4.5).
                services.AddDeclaredKernelErrorsCheck();
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, string? reason, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static async Task<(HttpStatusCode Status, string? Code)> StatusAsync(HttpClient client, HttpMethod method, string path, string? reason, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        using var problem = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason)
    {
        var request = new HttpRequestMessage(method, new Uri($"/api/v1/kernel/{path}", UriKind.Relative));
        request.Headers.Add(KernelObservationEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { });
        }

        return request;
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));
}
