using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Activity;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application;
using Cadence.Kernel.Application.Activity;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Intelligence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Kernel.WorkQueues;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Kernel.Protocol;
using Cadence.UnitTests.TestSupport;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
    private static readonly NodeRef Enrollment = NodeRef.For(EnrollmentId.New());
    private static readonly NodeRef Concept = NodeRef.For(ConceptId.New());
    private static readonly NodeRef FormInstance = NodeRef.For(CollectionInstanceId.New());
    private static readonly NodeRef RecordedObservation = NodeRef.For(ObservationId.New());
    private static readonly ProtocolVersionSummary FirstVersion = new(
        ProtocolVersionId.New(), StudyId.FromGuid(Study.Id), 1, null, ProtocolVersionStatus.Approved, "sha256:abc", null,
        AttestationId.New(), null);
    private static readonly ProtocolVersionSummary Amendment = new(
        ProtocolVersionId.New(), StudyId.FromGuid(Study.Id), 2, FirstVersion.Id, ProtocolVersionStatus.Draft, null,
        "Add a week 12 visit", null, null);

    private static readonly NodeRef HiddenAffected = NodeRef.For(EnrollmentId.New(), 2);
    private static readonly ProposalSummary OpenProposal = new(
        ProposalId.New(), StudyId.FromGuid(Study.Id), "Close the stale queries at site 017",
        new ProposalPlan(ArtifactVersionId.New(), ArtifactId.New(), "Proposed plan and reasoning", 1, "application/json", "sha256:plan"),
        [NodeRef.For(FirstVersion.Id, 3).ToString(), HiddenAffected.ToString()], null, ["protocol.approve"],
        new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero),
        "agent:deviation-triage", ActorKind.AutomationAgent, "data-manager@sponsor.test", "claude-opus-5", 0.9, "sha256:proposal", ProposalStatus.Open,
        null, null, null, null, false, null, null);

    private static readonly ProposalSummary HiddenProposal = OpenProposal with { Id = ProposalId.New(), Intent = "Hidden" };

    private static readonly string[] ReadPaths =
    [
        $"/api/v1/kernel/nodes/{Study}", $"/api/v1/kernel/nodes/{Study}/path", $"/api/v1/kernel/nodes/{Study}/children",
        $"/api/v1/kernel/nodes/{Study}/commands", $"/api/v1/kernel/authorization/can-i?capability=x.y&resource={Study}",
        "/api/v1/kernel/search?q=CAD", $"/api/v1/kernel/studies/{Study}/protocol-versions",
        $"/api/v1/kernel/protocol-versions/{NodeRef.For(FirstVersion.Id)}", $"/api/v1/kernel/activity?scope={Study}",
        $"/api/v1/kernel/protocol-versions/{NodeRef.For(Amendment.Id)}/schedule-of-activities",
        $"/api/v1/kernel/studies/{Study}/proposals", $"/api/v1/kernel/proposals/{NodeRef.For(OpenProposal.Id)}",
        "/api/v1/kernel/enrollments/lookup?study_code=CAD-101&subject_code=017-0001",
        "/api/v1/kernel/locks/in-force?study_code=CAD-101",
    ];

    /// <summary>The amendment's design: CARDIO-2 with its week 12 visit a week later.</summary>
    private static readonly ProtocolDesign AmendedDesign = SoaSamples.Cardio() with
    {
        PlannedActivities =
        [
            .. SoaSamples.Cardio().PlannedActivities.Select(a => a.Id == "visit.week12" ? a with { Timing = a.Timing! with { Day = 92 } } : a),
        ],
    };

    private static readonly SoaAmendmentDiff ScheduleDiff = ScheduleOfActivitiesDiff.Compare(SoaSamples.Cardio(), AmendedDesign);

    private readonly FakeNavigator _navigator = new();
    private readonly FakeSearch _search = new();
    private readonly HideSome _visibility = new();
    private readonly FakeTenantDirectory _directory = new();
    private readonly FakeProtocolVersions _protocol = new();
    private readonly FakeProposals _proposals = new();
    private readonly FakeAuthorityStore _authority = new();
    private readonly FakeLineage _lineage = new();
    private readonly FakeActivityStore _activity = new();
    private readonly FakeEnrollmentDirectory _enrollments = new();
    private readonly FakeStudyDirectory _studies = new();
    private readonly FakeValidationFindings _findings = new();
    private readonly FakeSimulationInputs _simulationInputs = new();
    private readonly FakeWorkQueues _workQueues = new();
    private readonly FakeWorkItemTargetResolver _workItemTargets = new();
    private readonly FakeLockRecords _locks = new();
    private readonly FakeStudySdvAlerts _sdvAlerts = new();
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

        _directory.Codes.Should().OnlyContain(c => c == TestSecurityContext.TenantCode);
        _navigator.Tenants.Should().BeEmpty();
        _search.Requests.Should().BeEmpty();
        _protocol.Tenants.Should().BeEmpty();
        _activity.Queries.Should().BeEmpty();
        _enrollments.Lookups.Should().BeEmpty();
        _locks.Checks.Should().BeEmpty();
        (await client.GetAsync(new Uri("/api/v1/kernel/status", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A caller whose sign-in names no tenant is signed with no tenant and refused without asking the tenant directory:
    /// there is no fallback tenant (#5038).
    /// </summary>
    [Fact]
    public async Task ReadWithoutATenantCode_IsRefused()
    {
        var factory = Factory();
        using var client = factory.CreateSignedClient(TestSecurityContext.DefaultIdentity with { TenantId = null });
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
        _directory.Codes.Should().NotBeEmpty().And.OnlyContain(c => c == TestSecurityContext.TenantCode);
    }

    /// <summary>
    /// Concept, collection_instance, and observation NodeRefs resolve their human summaries, and direct node visibility
    /// masks unauthorized collection_instance nodes and blinded observation nodes in place across node and path reads.
    /// </summary>
    [Fact]
    public async Task ConceptCollectionInstanceAndObservationNodes_ResolveAndInheritEnrollmentMasking()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var conceptDoc = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{Concept.AtVersion(1)}", ct);
        conceptDoc.RootElement.GetProperty("title").GetString().Should().Be("Systolic Blood Pressure");
        conceptDoc.RootElement.GetProperty("code").GetString().Should().Be("C25298");
        conceptDoc.RootElement.GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);

        using var formDoc = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{FormInstance}", ct);
        formDoc.RootElement.GetProperty("title").GetString().Should().Be("Vital Signs (Screening)");
        formDoc.RootElement.GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);

        using var obsDoc = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{RecordedObservation}", ct);
        obsDoc.RootElement.GetProperty("title").GetString().Should().Be("Systolic Blood Pressure: 120 mm[Hg]");
        obsDoc.RootElement.GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);

        _visibility.Hidden[FormInstance.AtVersion(1)] = MaskReason.NotPermitted;
        _visibility.Hidden[RecordedObservation.AtVersion(1)] = MaskReason.Blinded;

        using var maskedFormDoc = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{FormInstance}", ct);
        maskedFormDoc.RootElement.GetProperty("mask").GetString().Should().Be("NotPermitted");
        maskedFormDoc.RootElement.GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);

        using var maskedObsDoc = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{RecordedObservation}", ct);
        maskedObsDoc.RootElement.GetProperty("mask").GetString().Should().Be("Blinded");
        maskedObsDoc.RootElement.GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);

        using var maskedObsPath = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{RecordedObservation}/path", ct);
        maskedObsPath.RootElement[4].GetProperty("mask").GetString().Should().Be("NotPermitted");
        maskedObsPath.RootElement[5].GetProperty("mask").GetString().Should().Be("Blinded");
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
    [InlineData("/api/v1/kernel/protocol-versions/nonsense/schedule-of-activities", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/protocol-versions/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/schedule-of-activities", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/protocol-versions/protocol_version:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/schedule-of-activities", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/nodes/nonsense/lineage", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/lineage?direction=sideways", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/lineage?direction=1", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/lineage?depth=0", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/dependents?depth=51", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/dependents?edge_kind=Nope", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/nodes/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/dependents?edge_kind=3", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/protocol-versions/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/impact", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/protocol-versions/protocol_version:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/impact", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/studies/site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/proposals", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/studies/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/proposals", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/proposals/study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/proposals/proposal:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/kernel/activity?scope=nonsense:bad", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/kernel/activity?scope=nonsense", HttpStatusCode.NotFound)]
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
        var resource = entry.ResourceKind == NodeKinds.ValidationFinding
            ? FakeValidationFindings.OpenFinding
            : new NodeRef(entry.ResourceKind, Guid.CreateVersion7());
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
        var resource = entry.ResourceKind == NodeKinds.ValidationFinding
            ? FakeValidationFindings.OpenFinding
            : new NodeRef(entry.ResourceKind, Guid.CreateVersion7());
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

    /// <summary>CanI resolves read-only <c>simulation.run</c> capabilities instead of rejecting them as unknown.</summary>
    [Fact]
    public async Task CanI_ResolvesSimulationRunReadCapability()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var denied = await ReadJsonAsync(
            client, $"/api/v1/kernel/authorization/can-i?capability={SimulationCapabilities.Run.Token}&resource={Study}", ct);
        denied.RootElement.GetProperty("capability").GetString().Should().Be(SimulationCapabilities.Run.Token);
        denied.RootElement.GetProperty("verb_class").GetString().Should().Be("Read");
        denied.RootElement.GetProperty("permitted").GetBoolean().Should().BeFalse();

        var grant = Grant.Issue(
            GrantId.New(), Tenant, Reader, SimulationCapabilities.Run.Token, null, AuthorityScope.Tenant(), GrantedFrom, null,
            withGrantOption: false, parentGrantId: null, Actor.Person("study-admin"), GrantedFrom).Value!;
        _authority.Grants.Add(grant);

        using var permitted = await ReadJsonAsync(
            client, $"/api/v1/kernel/authorization/can-i?capability={SimulationCapabilities.Run.Token}&resource={Study}", ct);
        permitted.RootElement.GetProperty("permitted").GetBoolean().Should().BeTrue();
    }

    /// <summary>Simulation and matrix-gap endpoints serialize enums as strings and enforce @version pins with 409.</summary>
    [Fact]
    public async Task SimulationAndMatrixGapEndpoints_ReturnStringEnums_AndEnforceVersionPins()
    {
        var grant = Grant.Issue(
            GrantId.New(), Tenant, Reader, SimulationCapabilities.Run.Token, null, AuthorityScope.Tenant(), GrantedFrom, null,
            withGrantOption: false, parentGrantId: null, Actor.Person("study-admin"), GrantedFrom).Value!;
        _authority.Grants.Add(grant);
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        var simResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/kernel/simulation/simulate", UriKind.Relative),
            new { stored_version_ref = NodeRef.For(Amendment.Id, 2).ToString() },
            ct);
        simResponse.StatusCode.Should().Be(HttpStatusCode.OK, await simResponse.Content.ReadAsStringAsync(ct));
        using var simDoc = JsonDocument.Parse(await simResponse.Content.ReadAsStringAsync(ct));
        var simRoot = simDoc.RootElement;
        simRoot.GetProperty("candidate").GetProperty("kind").GetString().Should().Be("ProtocolVersion");
        simRoot.GetProperty("candidate").GetProperty("version").GetString().Should().Be(NodeRef.For(Amendment.Id, 2).ToString());
        simRoot.GetProperty("structural_diff")[0].GetProperty("change").GetString().Should().Be("Changed");
        simRoot.GetProperty("schedule_changes")[0].GetProperty("change").GetString().Should().Be("Changed");
        simRoot.GetProperty("matrix_gaps")[0].GetProperty("kind").GetString().Should().Be("IchM11Violation");
        simRoot.GetProperty("matrix_gaps")[0].GetProperty("severity").GetString().Should().Be("Error");
        simRoot.GetProperty("matrix_gaps").EnumerateArray().Select(g => g.GetProperty("severity").GetString()).Should().Contain("Warning");

        var staleResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/kernel/simulation/simulate", UriKind.Relative),
            new { stored_version_ref = NodeRef.For(Amendment.Id, 99).ToString() },
            ct);
        staleResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var staleDoc = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync(ct));
        staleDoc.RootElement.GetProperty("code").GetString().Should().Be(SimulationErrors.VersionStale.Code);

        var gapResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/kernel/protocols/matrix-gap-check", UriKind.Relative),
            new { protocol_content = ProtocolDesignSamples.Minimal },
            ct);
        gapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var gapDoc = JsonDocument.Parse(await gapResponse.Content.ReadAsStringAsync(ct));
        gapDoc.RootElement[0].GetProperty("kind").GetString().Should().Be("IchM11Violation");
        gapDoc.RootElement[0].GetProperty("severity").GetString().Should().Be("Error");
    }

    /// <summary>
    /// A forked InReview amendment simulated on the kernel API host emits governed protocol_element NodeRefs without
    /// altering canonical SimulationResultHashing, saves a SimulationReport artifact, signs a Part 11 Reviewed
    /// attestation via X-Sig-Token, and passes ProtocolApprovalSimulationGate with reviewAttestationRef.
    /// </summary>
    [Fact]
    [KernelRule("K-PROT-2")]
    [KernelRule("K-SIM-5")]
    public async Task ForkedInReviewAmendment_SavesSimulationReport_SignsReviewedViaSigToken_AndPassesApprovalGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var t0 = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(t0);
        var replayCache = new ReplayCache(clock);
        var govTenant = TenantId.New();
        var store = new KernelProtocolGovernanceEndpointsTests.InMemoryGovernanceStore(govTenant, clock);
        var reviewer = TestSecurityContext.Identity("sponsor.reviewer", "Sponsor Reviewer");
        store.GrantRole(
            Actor.Person(reviewer.UserId),
            "SPONSOR_REVIEWER",
            [
                "protocol.draft",
                "protocol.amend",
                "protocol.revise_draft",
                "protocol.submit_for_review",
                "protocol.approve",
                "simulation.run",
                "simulation.save_report",
                "artifact.register",
                "artifact.add_version",
                "attestation.sign",
            ],
            AuthorityScope.Tenant());

        var sbpConcept = store.SeedSbpConcept();
        var v1Design = new ProtocolDesign
        {
            Title = "CARDIO-2",
            Epochs = [new Epoch("ep.screening", "Screening", 1), new Epoch("ep.treatment", "Treatment", 2)],
            PlannedActivities =
            [
                new PlannedActivity("visit.screening", "Screening", "ep.screening", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(1, 0, 0)),
                new PlannedActivity("visit.week12", "Week 12", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(85, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec("cs.vitals", "Vital Signs", [new CollectionItem("item.sbp", sbpConcept, "mm[Hg]", Required: true)]),
            ],
        };
        var v2Design = v1Design with
        {
            PlannedActivities =
            [
                .. v1Design.PlannedActivities.Select(a => a.Id == "visit.week12" ? a with { Timing = a.Timing! with { Day = 92 } } : a),
            ],
        };
        var v2Diff = ScheduleOfActivitiesDiff.Compare(v1Design, v2Design);
        var v1Id = store.SeedDraftProtocolVersion(ProtocolDesignDocument.Write(v1Design));
        var studyRef = NodeRef.For(store.StudyId).ToString();
        var v1Ref = NodeRef.For(v1Id).ToString();

        var hostFactory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestBiometricLivenessKey().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(replayCache);
                services.RemoveAll<ITenantDirectory>();
                services.AddSingleton<ITenantDirectory>(store);
                services.RemoveAll<IAuthorityStore>();
                services.AddSingleton<IAuthorityStore>(store);
                services.RemoveAll<IStudyDirectory>();
                services.AddSingleton<IStudyDirectory>(store);
                services.RemoveAll<IProtocolVersionReader>();
                services.AddSingleton<IProtocolVersionReader>(store);
                services.RemoveAll<IStudyGovernanceReader>();
                services.AddSingleton<IStudyGovernanceReader>(store);
                services.RemoveAll<IAttestationVerifier>();
                services.AddSingleton<IAttestationVerifier>(store);
                services.RemoveAll<ITenantEntitlementReader>();
                services.AddSingleton<ITenantEntitlementReader>(store);
                services.RemoveAll<ITenantResidencyReader>();
                services.AddSingleton<ITenantResidencyReader>(store);
                services.RemoveAll<IKernelUnitOfWorkFactory>();
                services.AddSingleton<IKernelUnitOfWorkFactory>(store);
                services.RemoveAll<ISimulationInputs>();
                services.AddSingleton<ISimulationInputs>(store);
                services.RemoveAll<Cadence.Kernel.Knowledge.IConceptRegistry>();
                services.AddSingleton<Cadence.Kernel.Knowledge.IConceptRegistry>(store);
                services.RemoveAll<IProtocolSubmissionCheck>();
                services.RemoveBackgroundHostedServices();
            });
        });
        _factories.Add(hostFactory);
        using var client = hostFactory.CreateSignedClient(reviewer);
        var jsonOptions = hostFactory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        string v1Hash;
        using (var submitV1 = await client.SendAsync(
            KernelProtocolGovernanceEndpointsTests.BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/submit-for-review", "host-v1-submit", "Freeze v1", null),
            ct))
        {
            submitV1.StatusCode.Should().Be(HttpStatusCode.OK, await submitV1.Content.ReadAsStringAsync(ct));
            v1Hash = JsonDocument.Parse(await submitV1.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("content_hash").GetString()!;
        }

        var v1ApprovePath = $"/api/v1/kernel/protocol-versions/{v1Ref}/approve";
        var v1Sig = TestSecurityContext.CreateSigToken(
            reviewer.UserId,
            v1ApprovePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: clock.UtcNow);
        using (var approveV1 = await client.SendAsync(
            KernelProtocolGovernanceEndpointsTests.BuildPost(v1ApprovePath, "host-v1-approve", "Approve v1", v1Sig, new { meaning = "Approved", expected_content_hash = v1Hash }),
            ct))
        {
            approveV1.StatusCode.Should().Be(HttpStatusCode.OK, await approveV1.Content.ReadAsStringAsync(ct));
        }

        var v2Id = ProtocolVersionId.New();
        using (var forkV2 = await client.SendAsync(
            KernelProtocolGovernanceEndpointsTests.BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/fork", "host-v2-fork", "Fork v2", null, new
            {
                intent = "Move Week 12 visit to Day 92",
                rationale = "Align with quarterly clinic window",
                classification = "NonSubstantial",
                summary_of_changes = "Moves visit.week12 from Day 85 to Day 92",
                impacted_elements = v2Diff.ImpactedElements,
                transition_policy = "Automatic",
                protocol_version_id = v2Id.Value,
            }),
            ct))
        {
            forkV2.StatusCode.Should().Be(HttpStatusCode.Created, await forkV2.Content.ReadAsStringAsync(ct));
        }

        await using (var scope = hostFactory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = Actor.Person(reviewer.UserId);
            var sender = scope.ServiceProvider.GetRequiredService<IKernelCommandSender>();
            var amendment = new ProtocolAmendment(
                "Move Week 12 visit to Day 92",
                "Align with quarterly clinic window",
                AmendmentClassification.NonSubstantial,
                "Moves visit.week12 from Day 85 to Day 92",
                v2Diff.ImpactedElements,
                TransitionPolicy.Automatic);
            var revised = await sender.SendAsync(
                new ReviseProtocolDraft(govTenant, v2Id, ProtocolDesignDocument.Write(v2Design), amendment, "host-v2-revise", "Apply Week 12 shift"),
                cancellationToken: ct);
            revised.IsSuccess.Should().BeTrue(revised.Error?.Message);
        }

        var v2Ref = NodeRef.For(v2Id).ToString();
        string v2Hash;
        using (var submitV2 = await client.SendAsync(
            KernelProtocolGovernanceEndpointsTests.BuildPost($"/api/v1/kernel/protocol-versions/{v2Ref}/submit-for-review", "host-v2-submit", "Submit v2 for review", null),
            ct))
        {
            submitV2.StatusCode.Should().Be(HttpStatusCode.OK, await submitV2.Content.ReadAsStringAsync(ct));
            v2Hash = JsonDocument.Parse(await submitV2.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("content_hash").GetString()!;
        }

        KernelSimulationResponse simResult;
        using (var simRes = await client.PostAsJsonAsync(
            new Uri("/api/v1/kernel/simulation/simulate", UriKind.Relative),
            new { stored_version_ref = v2Ref, valid_at = t0, known_at = t0 },
            ct))
        {
            simRes.StatusCode.Should().Be(HttpStatusCode.OK, await simRes.Content.ReadAsStringAsync(ct));
            simResult = (await simRes.Content.ReadFromJsonAsync<KernelSimulationResponse>(jsonOptions, ct))!;
            simResult.ScheduleChanges.Should().ContainSingle(c => c.ActivityId == "visit.week12")
                .Which.ProtocolElement.Should().StartWith("protocol_element:").And.EndWith("@1");
        }

        string reportArtifactVersionRef;
        string reportContentHash;
        using (var saveRes = await client.SendAsync(
            KernelProtocolGovernanceEndpointsTests.BuildPost($"/api/v1/kernel/studies/{studyRef}/simulation-reports", "host-v2-save-sim", "Save v2 simulation report", null, new
            {
                title = "CARDIO-2 v2 Simulation Report",
                result = simResult,
            }, jsonOptions),
            ct))
        {
            saveRes.StatusCode.Should().Be(HttpStatusCode.Created, await saveRes.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await saveRes.Content.ReadAsStringAsync(ct)).RootElement;
            reportArtifactVersionRef = doc.GetProperty("artifact_version_ref").GetString()!;
            reportContentHash = doc.GetProperty("content_hash").GetString()!;
        }

        var reviewPath = KernelSimulationEndpoints.ReviewArtifactVersionAction(reportArtifactVersionRef);
        var reviewSig = TestSecurityContext.CreateSigToken(
            reviewer.UserId,
            reviewPath,
            KernelSimulationEndpoints.ReviewArtifactVersionSemanticAction,
            issuedAt: clock.UtcNow);
        string reviewAttestationRef;
        using (var reviewRes = await client.SendAsync(
            KernelProtocolGovernanceEndpointsTests.BuildPost(reviewPath, "host-v2-review", "Sign Reviewed attestation", reviewSig, new
            {
                meaning = "Reviewed",
                expected_content_hash = reportContentHash,
            }),
            ct))
        {
            reviewRes.StatusCode.Should().Be(HttpStatusCode.OK, await reviewRes.Content.ReadAsStringAsync(ct));
            reviewAttestationRef = JsonDocument.Parse(await reviewRes.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("attestation_ref").GetString()!;
        }

        var v2ApprovePath = $"/api/v1/kernel/protocol-versions/{v2Ref}/approve";
        var v2ApproveSig = TestSecurityContext.CreateSigToken(
            reviewer.UserId,
            v2ApprovePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: clock.UtcNow);
        using var approveV2 = await client.SendAsync(
            KernelProtocolGovernanceEndpointsTests.BuildPost(v2ApprovePath, "host-v2-approve", "Approve v2 with reviewAttestationRef", v2ApproveSig, new
            {
                meaning = "Approved",
                expected_content_hash = v2Hash,
                review_attestation_ref = reviewAttestationRef,
            }),
            ct);
        approveV2.StatusCode.Should().Be(HttpStatusCode.OK, await approveV2.Content.ReadAsStringAsync(ct));
        JsonDocument.Parse(await approveV2.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("status").GetString().Should().Be("Approved");
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

    /// <summary>
    /// K-EXP-2: CanI and GetCommands apply INodeVisibility masking to Explanation evidence, Subject, and NextAction
    /// targets so hidden or blinded NodeRefs are formatted as masked:&lt;kind&gt;:&lt;Reason&gt; without leaking UUIDs or dropping
    /// conditions.
    /// </summary>
    [Fact]
    [KernelRule("K-EXP-2")]
    public async Task CanI_And_Commands_MaskRestrictedExplanationEvidenceWithoutLeakingUuids()
    {
        var entry = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue);
        var hiddenResource = new NodeRef(entry.ResourceKind, Guid.CreateVersion7());
        var hiddenGrant = Grant.Issue(
            GrantId.New(), Tenant, Reader, entry.Capability.Token, null, AuthorityScope.Tenant(), GrantedFrom, null,
            withGrantOption: false, parentGrantId: null, Actor.Person("study-admin"), GrantedFrom).Value!;
        _authority.Grants.Add(hiddenGrant);
        _visibility.Hidden[hiddenResource] = MaskReason.NotPermitted;
        _visibility.Hidden[NodeRef.For(hiddenGrant.Id)] = MaskReason.Blinded;

        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var canI = await ReadJsonAsync(
            client, $"/api/v1/kernel/authorization/can-i?capability={entry.Capability.Token}&resource={hiddenResource}", ct);
        string canIRaw = canI.RootElement.GetProperty("explanation").GetRawText();
        canIRaw.Should().NotContain(hiddenResource.Id.ToString("D"));
        canIRaw.Should().NotContain(hiddenGrant.Id.Value.ToString("D"));
        canI.RootElement.GetProperty("explanation").GetProperty("subject").GetString()
            .Should().Be($"masked:{entry.ResourceKind}:NotPermitted");
        canI.RootElement.GetProperty("explanation").GetProperty("conditions")[0].GetProperty("evidence")
            .EnumerateArray().Select(e => e.GetString())
            .Should().Equal($"masked:{entry.ResourceKind}:NotPermitted", $"masked:{NodeKinds.Grant}:Blinded");

        using var commands = await ReadJsonAsync(client, $"/api/v1/kernel/nodes/{hiddenResource}/commands", ct);
        string commandsRaw = commands.RootElement.GetRawText();
        commandsRaw.Should().NotContain(hiddenGrant.Id.Value.ToString("D"));
        commands.RootElement[0].GetProperty("decision").GetProperty("explanation").GetProperty("subject").GetString()
            .Should().Be($"masked:{entry.ResourceKind}:NotPermitted");
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

        var schedule = root.GetProperty("schedule");
        var moved = schedule.GetProperty("directives").EnumerateArray().Should().ContainSingle().Subject;
        moved.GetProperty("kind").GetString().Should().Be("VisitMoved");
        moved.GetProperty("element_id").GetString().Should().Be("visit.week12");
        moved.GetProperty("before").GetProperty("study_day").GetInt32().Should().Be(85);
        moved.GetProperty("after").GetProperty("study_day").GetInt32().Should().Be(92);
        schedule.GetProperty("undeclared").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(ScheduleDiff.ImpactedElements, "the amendment declares none of the elements the move touches");
        schedule.GetProperty("impacted_concepts").EnumerateArray().Select(e => e.GetString())
            .Should().Contain(NodeRef.For(SoaSamples.Sbp.ConceptId, SoaSamples.Sbp.Version).ToString());
        schedule.GetProperty("changes_schedule").GetBoolean().Should().BeTrue();
        schedule.GetProperty("changes_eligibility").GetBoolean().Should().BeFalse();

        var first = await client.GetAsync(new Uri($"/api/v1/kernel/protocol-versions/{NodeRef.For(FirstVersion.Id)}/impact", UriKind.Relative), ct);
        first.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await first.Content.ReadAsStringAsync(ct)).Should().Contain("amends nothing");
    }

    /// <summary>
    /// The impact report describes the amendment's content, so an amendment the caller may not see is refused before
    /// anything is computed.
    /// </summary>
    [Fact]
    public async Task AmendmentImpact_OfAHiddenAmendment_IsRefused()
    {
        _visibility.Hidden[NodeRef.For(Amendment.Id)] = MaskReason.NotPermitted;
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        var refused = await client.GetAsync(new Uri($"/api/v1/kernel/protocol-versions/{NodeRef.For(Amendment.Id)}/impact", UriKind.Relative), ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        _lineage.Impacts.Should().BeEmpty();
    }

    /// <summary>
    /// A version's Schedule of Activities is compiled from its design: epochs over their columns, visits by study day,
    /// rows with the columns that collect them and their concept versions, footnotes, and the matrix hash. Content that
    /// is not a typed design has none, and a version the caller may not see is refused.
    /// </summary>
    [Fact]
    public async Task ScheduleOfActivities_IsCompiledFromTheDesign_AndRefusedWhenHidden()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;
        var path = $"/api/v1/kernel/protocol-versions/{NodeRef.For(Amendment.Id)}/schedule-of-activities";

        using var soa = await ReadJsonAsync(client, path, ct);

        var root = soa.RootElement;
        root.GetProperty("version").GetString().Should().Be(NodeRef.For(Amendment.Id).ToString());
        root.GetProperty("hash").GetString().Should().Be(ScheduleOfActivities.Hash(ScheduleOfActivities.Compile(AmendedDesign)));
        root.GetProperty("title").GetString().Should().Be("CARDIO-2");
        root.GetProperty("epochs").EnumerateArray().Select(e => e.GetProperty("epoch_id").GetString())
            .Should().Equal("epoch.screening", "epoch.treatment");
        root.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("activity_id").GetString())
            .Should().Equal("visit.screening", "visit.baseline", "visit.week4", "visit.week8", "visit.week12", "visit.et");
        var vitals = root.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("collection_spec_id").GetString() == "cs.vitals");
        vitals.GetProperty("frequency").GetInt32().Should().Be(6);
        vitals.GetProperty("concepts").EnumerateArray().Select(c => c.GetString())
            .Should().Equal(NodeRef.For(SoaSamples.Sbp.ConceptId, SoaSamples.Sbp.Version).ToString());
        root.GetProperty("footnotes").EnumerateArray().Select(f => f.GetProperty("kind").GetString()).Should().Contain("EventDriven");

        var untyped = await client.GetAsync(new Uri($"/api/v1/kernel/protocol-versions/{NodeRef.For(FirstVersion.Id)}/schedule-of-activities", UriKind.Relative), ct);
        untyped.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await untyped.Content.ReadAsStringAsync(ct)).Should().Contain("not a typed protocol design");

        _visibility.Hidden[NodeRef.For(Amendment.Id)] = MaskReason.Blinded;
        var refused = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
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

    /// <summary>
    /// The development tenant bootstrap runs only in Development (ADR-2542): with its setting in any other environment
    /// the host refuses to start, so no deployed host carries a platform operator.
    /// </summary>
    [Fact]
    public void DevelopmentTenantSetting_OutsideDevelopment_StopsTheHost()
    {
        using var factory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseTestPostgresConnectionString()
            .UseSetting("Kernel:DevelopmentTenant:Code", "cadence-local"));

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*only in Development (ADR-2542)*");
    }

    private HttpClient Client(bool knownTenant = true, bool hostVisibility = false) =>
        Factory(knownTenant, hostVisibility: hostVisibility).CreateSignedClient();

    /// <summary>
    /// A study's proposals are listed for the Canvas with the filter and the host's clock passed to the reader: each
    /// keeps its intent, plan, model and content hash; an affected object the reader may not see keeps its place masked,
    /// and a proposal the reader may not see is masked whole. Reading a hidden proposal by its reference is refused.
    /// </summary>
    [Fact]
    public async Task Proposals_AreListedAndRead_WithHiddenObjectsMaskedInPlace()
    {
        var ct = TestContext.Current.CancellationToken;
        _visibility.Hidden[HiddenAffected] = MaskReason.Blinded;
        _visibility.Hidden[NodeRef.For(HiddenProposal.Id)] = MaskReason.NotPermitted;
        using var client = Client();

        using var list = await ReadJsonAsync(client, $"/api/v1/kernel/studies/{Study}/proposals?status=open&limit=10", ct);
        var items = list.RootElement.EnumerateArray().ToList();
        items.Should().HaveCount(2);
        var open = items[0];
        open.GetProperty("ref").GetString().Should().Be(NodeRef.For(OpenProposal.Id).ToString());
        open.GetProperty("intent").GetString().Should().Be(OpenProposal.Intent);
        open.GetProperty("model_id").GetString().Should().Be("claude-opus-5");
        open.GetProperty("proposed_by_kind").GetString().Should().Be("AutomationAgent");
        open.GetProperty("requested_by").GetString().Should().Be("data-manager@sponsor.test");
        open.GetProperty("content_hash").GetString().Should().Be("sha256:proposal");
        open.GetProperty("status").GetString().Should().Be("Open");
        open.GetProperty("plan").GetProperty("title").GetString().Should().Be("Proposed plan and reasoning");
        var affected = open.GetProperty("affected").EnumerateArray().ToList();
        affected.Select(a => a.GetProperty("ref").GetString()).Should().Equal(OpenProposal.Affected);
        affected[0].GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);
        affected[1].GetProperty("mask").GetString().Should().Be("Blinded");
        items[1].GetProperty("ref").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("mask").GetString().Should().Be("NotPermitted");
        _proposals.Lists.Should().ContainSingle().Which.Should().Be((Tenant, ProposalStatus.Open, 10));

        using var read = await ReadJsonAsync(client, $"/api/v1/kernel/proposals/{NodeRef.For(OpenProposal.Id)}", ct);
        read.RootElement.GetProperty("requested_capabilities").EnumerateArray().Select(c => c.GetString()).Should().Equal("protocol.approve");

        var hidden = await client.GetAsync(new Uri($"/api/v1/kernel/proposals/{NodeRef.For(HiddenProposal.Id)}", UriKind.Relative), ct);
        hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>A status the kernel does not define and a limit out of range are refused before the reader runs.</summary>
    [Theory]
    [InlineData("status=pending")]
    [InlineData("status=5")]
    [InlineData("limit=0")]
    [InlineData("limit=201")]
    public async Task ProposalList_WithAnInvalidFilter_IsRefused(string query)
    {
        using var client = Client();

        var response = await client.GetAsync(new Uri($"/api/v1/kernel/studies/{Study}/proposals?{query}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _proposals.Lists.Should().BeEmpty();
    }

    /// <summary>A study the reader may not see has its proposals refused, not masked, because a list would describe it.</summary>
    [Fact]
    public async Task ProposalList_OfAHiddenStudy_IsRefused()
    {
        _visibility.Hidden[Study.AtVersion(3)] = MaskReason.NotPermitted;
        using var client = Client();

        var response = await client.GetAsync(new Uri($"/api/v1/kernel/studies/{Study}/proposals", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _proposals.Lists.Should().BeEmpty();
    }

    /// <summary>
    /// Direct enrollment lookup resolves study, linked study site, and enrollment codes to pinned versioned NodeRefs
    /// in the caller's tenant, with or without an explicit site code.
    /// </summary>
    [Fact]
    public async Task EnrollmentLookup_ResolvesPinnedRefs_WithAndWithoutSiteCode()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var withoutSite = await ReadJsonAsync(
            client,
            "/api/v1/kernel/enrollments/lookup?study_code=CAD-101&subject_code=017-0001",
            ct);
        var root = withoutSite.RootElement;
        root.GetProperty("study_ref").GetString().Should().Be(Study.AtVersion(3).ToString());
        root.GetProperty("study_code").GetString().Should().Be("CAD-101");
        root.GetProperty("study_title").GetString().Should().Be("Cadence 101");
        root.GetProperty("study_site_ref").GetString().Should().Be(StudySite.AtVersion(1).ToString());
        root.GetProperty("site_code").GetString().Should().Be("017");
        root.GetProperty("site_title").GetString().Should().Be("Hamburg Clinic");
        root.GetProperty("enrollment_ref").GetString().Should().Be(FakeEnrollmentDirectory.LookupEnrollment.AtVersion(2).ToString());
        root.GetProperty("subject_code").GetString().Should().Be("017-0001");
        root.GetProperty("status").GetString().Should().Be("Enrolled");

        using var withSite = await ReadJsonAsync(
            client,
            "/api/v1/kernel/enrollments/lookup?study_code=CAD-101&site_code=017&subject_code=017-0001",
            ct);
        withSite.RootElement.GetProperty("enrollment_ref").GetString().Should().Be(FakeEnrollmentDirectory.LookupEnrollment.AtVersion(2).ToString());
        _enrollments.Lookups.Should().HaveCount(2).And.OnlyContain(l => l.Tenant == Tenant);
    }

    /// <summary>
    /// Absent codes, mismatched site codes, and masked studies, study sites, or enrollments all return the same
    /// non-confirming 404 problem details so code lookups never confirm hidden objects (ADR-2471).
    /// </summary>
    [Fact]
    public async Task EnrollmentLookup_ReturnsIdenticalNonConfirming404_WhenAbsentMismatchedOrMasked()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        var absent = await client.GetAsync(
            new Uri("/api/v1/kernel/enrollments/lookup?study_code=CAD-101&subject_code=017-9999", UriKind.Relative), ct);
        absent.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var absentDoc = JsonDocument.Parse(await absent.Content.ReadAsStringAsync(ct));
        var expectedTitle = absentDoc.RootElement.GetProperty("title").GetString();
        var expectedDetail = absentDoc.RootElement.GetProperty("detail").GetString();

        var wrongSite = await client.GetAsync(
            new Uri("/api/v1/kernel/enrollments/lookup?study_code=CAD-101&site_code=999&subject_code=017-0001", UriKind.Relative), ct);
        wrongSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var wrongSiteDoc = JsonDocument.Parse(await wrongSite.Content.ReadAsStringAsync(ct));
        wrongSiteDoc.RootElement.GetProperty("title").GetString().Should().Be(expectedTitle);
        wrongSiteDoc.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);

        foreach (var (hiddenNode, reason) in new (NodeRef Node, MaskReason Reason)[]
        {
            (Study.AtVersion(3), MaskReason.NotPermitted),
            (StudySite.AtVersion(1), MaskReason.NotPermitted),
            (FakeEnrollmentDirectory.LookupEnrollment.AtVersion(2), MaskReason.Blinded),
            (FakeEnrollmentDirectory.LookupEnrollment.AtVersion(2), MaskReason.IdentityVault),
        })
        {
            _visibility.Hidden.Clear();
            _visibility.Hidden[hiddenNode] = reason;

            var masked = await client.GetAsync(
                new Uri("/api/v1/kernel/enrollments/lookup?study_code=CAD-101&subject_code=017-0001", UriKind.Relative), ct);
            masked.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var body = await masked.Content.ReadAsStringAsync(ct);
            body.Should().NotContain(reason.ToString()).And.NotContain(hiddenNode.Id.ToString());
            using var maskedDoc = JsonDocument.Parse(body);
            maskedDoc.RootElement.GetProperty("title").GetString().Should().Be(expectedTitle);
            maskedDoc.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);
        }
    }

    /// <summary>Missing or malformed lookup parameters are rejected with 400 before reaching the directory.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("?study_code=CAD-101")]
    [InlineData("?subject_code=017-0001")]
    [InlineData("?study_code=&subject_code=017-0001")]
    [InlineData("?study_code=CAD-101&subject_code=%20%20")]
    public async Task EnrollmentLookup_WithInvalidQuery_IsBadRequest(string query)
    {
        using var client = Client();

        var response = await client.GetAsync(new Uri($"/api/v1/kernel/enrollments/lookup{query}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _enrollments.Lookups.Should().BeEmpty();
    }

    /// <summary>
    /// Batch node resolution resolves up to 100 NodeRefs in one request, masks hidden objects in place keyed by the
    /// requested ref, omits non-existent objects, and rejects batches exceeding 100 refs with 400.
    /// </summary>
    [Fact]
    public async Task BatchNodes_ResolvesKnownNodes_MasksHiddenInPlace_OmitsUnknown_AndCapsAt100()
    {
        _visibility.Hidden[Enrollment.AtVersion(1)] = MaskReason.Blinded;
        var missing = NodeRef.For(SiteId.New());
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var batch = await ReadJsonAsync(
            client,
            $"/api/v1/kernel/nodes?ref={Study}&ref={Enrollment}&ref={missing}",
            ct);
        var nodes = batch.RootElement.GetProperty("nodes");
        nodes.TryGetProperty(Study.ToString(), out var studyNode).Should().BeTrue();
        studyNode.GetProperty("ref").GetString().Should().Be(Study.AtVersion(3).ToString());
        studyNode.GetProperty("code").GetString().Should().Be("CAD-101");
        studyNode.GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);

        nodes.TryGetProperty(Enrollment.ToString(), out var maskedNode).Should().BeTrue();
        maskedNode.GetProperty("ref").ValueKind.Should().Be(JsonValueKind.Null);
        maskedNode.GetProperty("kind").GetString().Should().Be(NodeKinds.Enrollment);
        maskedNode.GetProperty("mask").GetString().Should().Be("Blinded");

        nodes.TryGetProperty(missing.ToString(), out _).Should().BeFalse();

        var tooManyQuery = string.Join("&", Enumerable.Range(0, 101).Select(_ => $"ref={Study}"));
        var tooMany = await client.GetAsync(new Uri($"/api/v1/kernel/nodes?{tooManyQuery}", UriKind.Relative), ct);
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Study-scoped read endpoints accept a study code directly without a prior search round-trip, repeatable status
    /// filters narrow validation findings, unified attention returns open proposals plus Open/Investigating findings,
    /// and activity include_nodes embeds unmasked subject summaries while keeping masked subjects hidden.
    /// </summary>
    [Fact]
    public async Task StudyCodeReads_SupportProposalsRepeatableFindingStatusesAttentionAndActivityNodes()
    {
        _visibility.Hidden[FakeActivityStore.HiddenSubject] = MaskReason.Blinded;
        _visibility.Hidden[HiddenAffected] = MaskReason.Blinded;
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;

        using var proposalsByCode = await ReadJsonAsync(client, "/api/v1/kernel/studies/CAD-101/proposals?status=Open&limit=5", ct);
        proposalsByCode.RootElement.EnumerateArray().Should().HaveCount(2);

        using var findingsByStatus = await ReadJsonAsync(
            client,
            "/api/v1/kernel/studies/CAD-101/validation/findings?status=Open&status=Investigating&limit=10",
            ct);
        findingsByStatus.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        findingsByStatus.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("status").GetString())
            .Should().Equal("Open", "Investigating");

        using var attention = await ReadJsonAsync(client, "/api/v1/kernel/studies/CAD-101/attention?limit=1", ct);
        attention.RootElement.GetProperty("study_ref").GetString().Should().Be(Study.AtVersion(3).ToString());
        attention.RootElement.GetProperty("proposals").EnumerateArray().Should().HaveCount(2);
        attention.RootElement.GetProperty("findings").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("status").GetString().Should().Be("Open");
        var attentionAlert = attention.RootElement.GetProperty("sdv_alerts").EnumerateArray().Should().ContainSingle().Subject;
        attentionAlert.GetProperty("study_code").GetString().Should().Be("CAD-101");
        attentionAlert.GetProperty("item_id").GetString().Should().Be("sbp");
        attentionAlert.GetProperty("superseded_revision").GetInt32().Should().Be(1);
        attentionAlert.GetProperty("current_revision").GetInt32().Should().Be(2);

        using var activityPage = await ReadJsonAsync(client, "/api/v1/kernel/activity?scope=CAD-101&include_nodes=true", ct);
        var entries = activityPage.RootElement.GetProperty("entries").EnumerateArray().ToList();
        entries.Should().HaveCount(3);
        entries[1].GetProperty("subject").ValueKind.Should().Be(JsonValueKind.Null);
        entries[1].GetProperty("mask").GetString().Should().Be("Blinded");

        var activityNodes = activityPage.RootElement.GetProperty("nodes");
        activityNodes.TryGetProperty(FakeActivityStore.VisibleSubject.ToString(), out var visNode).Should().BeTrue();
        visNode.GetProperty("code").GetString().Should().Be("017-010");
        activityNodes.TryGetProperty(FakeActivityStore.HiddenSubject.ToString(), out _).Should().BeFalse();
        activityNodes.TryGetProperty(NodeRef.For(FirstVersion.Id, 3).ToString(), out var pvNode).Should().BeTrue();
        pvNode.GetProperty("code").GetString().Should().Be("v1");

        using var workQueuesByCode = await ReadJsonAsync(client, "/api/v1/kernel/work-queues?within=CAD-101", ct);
        workQueuesByCode.RootElement.GetProperty("queues").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("code").GetString().Should().Be("ecrf.verify");
        _workQueues.Queries.Should().ContainSingle().Which.Within.Should().Be(Study);

        using var locksByCode = await ReadJsonAsync(client, "/api/v1/kernel/studies/CAD-101/locks", ct);
        locksByCode.RootElement.GetProperty("study_ref").GetString().Should().Be(Study.ToString());
        locksByCode.RootElement.GetProperty("study_code").GetString().Should().Be("CAD-101");

        using var readinessByCode = await ReadJsonAsync(client, "/api/v1/kernel/studies/CAD-101/locks/readiness", ct);
        readinessByCode.RootElement.GetProperty("study_ref").GetString().Should().Be(Study.ToString());
        readinessByCode.RootElement.GetProperty("ready_for_lock").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Over a real Kestrel listener with <see cref="GrantNodeVisibility"/>, zero-grant and cross-site callers probing
    /// <c>GET /api/v1/kernel/locks/in-force</c> receive non-confirming 404 problem responses on hidden studies and
    /// explicit out-of-scope sites, and cross-site subject probes omitting <c>site_code</c> match absent subjects
    /// (<c>locked == false</c>) without disclosing cross-site locks (ADR-2471).
    /// </summary>
    [Fact]
    public async Task LocksInForce_OverKestrel_EnforcesNonConfirmingNodeVisibilityForZeroGrantAndCrossSiteCallers()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = SeedLockVisibilityScenario();
        using var host = CreateKestrelHost(hostVisibility: true);
        using var zeroGrantClient = host.CreateKestrelSignedClient(
            TestSecurityContext.Identity("stranger@sponsor.test"));
        using var site01Client = host.CreateKestrelSignedClient(
            TestSecurityContext.Identity(fixture.Site01Coordinator.Id, "CRC"));

        // 1. Zero-grant caller: confidential locked study and nonexistent study return identical non-confirming 404 problem details.
        var confidentialRes = await zeroGrantClient.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-CONFIDENTIAL-MARKER", UriKind.Relative), ct);
        var nonexistentRes = await zeroGrantClient.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-NONEXISTENT-MARKER", UriKind.Relative), ct);

        confidentialRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        nonexistentRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        confidentialRes.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        nonexistentRes.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var confidentialBody = await confidentialRes.Content.ReadAsStringAsync(ct);
        var nonexistentBody = await nonexistentRes.Content.ReadAsStringAsync(ct);
        confidentialBody.Should().NotContain(fixture.ConfidentialStudyId.Value.ToString("D"))
            .And.NotContain(nameof(MaskReason.NotPermitted))
            .And.NotContain("Locked");

        using var confidentialDoc = JsonDocument.Parse(confidentialBody);
        using var nonexistentDoc = JsonDocument.Parse(nonexistentBody);
        var expectedTitle = nonexistentDoc.RootElement.GetProperty("title").GetString();
        var expectedDetail = nonexistentDoc.RootElement.GetProperty("detail").GetString();
        confidentialDoc.RootElement.GetProperty("title").GetString().Should().Be(expectedTitle);
        confidentialDoc.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);

        // 2. Cross-site coordinator (granted only at SITE-01): explicit out-of-scope SITE-03 returns the same 404 as an absent site.
        var hiddenSiteRes = await site01Client.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&site_code=SITE-03", UriKind.Relative), ct);
        var absentSiteRes = await site01Client.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&site_code=SITE-NONEXISTENT", UriKind.Relative), ct);

        hiddenSiteRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        absentSiteRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var hiddenSiteBody = await hiddenSiteRes.Content.ReadAsStringAsync(ct);
        hiddenSiteBody.Should().NotContain(fixture.Site03Id.Value.ToString("D"))
            .And.NotContain(nameof(MaskReason.NotPermitted));
        using var hiddenSiteDoc = JsonDocument.Parse(hiddenSiteBody);
        using var absentSiteDoc = JsonDocument.Parse(await absentSiteRes.Content.ReadAsStringAsync(ct));
        hiddenSiteDoc.RootElement.GetProperty("title").GetString().Should().Be(expectedTitle);
        hiddenSiteDoc.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);

        // 3. Cross-site subject probe omitting site_code ignores masked Enrollment + inferred StudySite so it matches an absent subject.
        using var crossSiteSubjectDoc = await ReadJsonAsync(
            site01Client,
            "/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&subject_code=SUBJ-SITE02-MARKER",
            ct);
        using var absentSubjectDoc = await ReadJsonAsync(
            site01Client,
            "/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&subject_code=SUBJ-ABSENT-MARKER",
            ct);
        crossSiteSubjectDoc.RootElement.GetProperty("study_exists").GetBoolean().Should().BeTrue();
        crossSiteSubjectDoc.RootElement.GetProperty("locked").GetBoolean().Should().BeFalse();
        crossSiteSubjectDoc.RootElement.GetProperty("lock_kind").ValueKind.Should().Be(JsonValueKind.Null);
        crossSiteSubjectDoc.RootElement.GetProperty("scope_kind").ValueKind.Should().Be(JsonValueKind.Null);
        crossSiteSubjectDoc.RootElement.GetRawText().Should().Be(absentSubjectDoc.RootElement.GetRawText());

        // Probing by enrollment_id without site_code likewise ignores the masked cross-site enrollment.
        using var crossSiteEnrollmentDoc = await ReadJsonAsync(
            site01Client,
            $"/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&enrollment_id={fixture.SubjSite02Id.Value:D}",
            ct);
        crossSiteEnrollmentDoc.RootElement.GetProperty("locked").GetBoolean().Should().BeFalse();

        // When the visible study itself is Frozen while SUBJ-SITE02-MARKER is Locked, only the visible study Freeze is reported.
        _locks.ActiveLocks.Add((fixture.VisibleStudyId, new ScopeWriteLockCandidate(LockKind.Frozen, NodeRef.For(fixture.VisibleStudyId))));
        using var studyFrozenCrossSiteDoc = await ReadJsonAsync(
            site01Client,
            "/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&subject_code=SUBJ-SITE02-MARKER",
            ct);
        studyFrozenCrossSiteDoc.RootElement.GetProperty("locked").GetBoolean().Should().BeTrue();
        studyFrozenCrossSiteDoc.RootElement.GetProperty("lock_kind").GetString().Should().Be("Frozen");
        studyFrozenCrossSiteDoc.RootElement.GetProperty("scope_kind").GetString().Should().Be(NodeKinds.Study);
    }

    /// <summary>
    /// Over Kestrel, an authorized site coordinator receives 200 OK when checking a new <c>subject_code</c> before
    /// enrollment creation and sees locks covering their own site, automation agents enforce <see cref="INodeVisibility"/>,
    /// and verified <c>svc:</c> <c>SYSTEM_SERVICE</c> background consumers (<c>svc:econsent-consumer</c>,
    /// <c>svc:execution-db-seeder</c>) evaluate tenant-wide covering locks while spoofed or non-system callers cannot.
    /// </summary>
    [Fact]
    public async Task LocksInForce_OverKestrel_PreservesAuthorizedCoordinatorPreEnrollmentChecks_EnforcesAgents_AndAllowsVerifiedSystemServices()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = SeedLockVisibilityScenario();
        var scopedAgentHolder = new MutableCurrentActor();
        using var host = CreateKestrelHost(hostVisibility: true, overrideActor: scopedAgentHolder);
        using var site01Client = host.CreateKestrelSignedClient(
            TestSecurityContext.Identity(fixture.Site01Coordinator.Id, "CRC"));

        // 1. Authorized SITE-01 coordinator checking a brand-new subject_code before enrollment creation gets 200 OK (unlocked).
        using (var preEnrollUnlocked = await ReadJsonAsync(
            site01Client,
            "/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&site_code=SITE-01&subject_code=SUBJ-NEW-MARKER",
            ct))
        {
            preEnrollUnlocked.RootElement.GetProperty("study_exists").GetBoolean().Should().BeTrue();
            preEnrollUnlocked.RootElement.GetProperty("locked").GetBoolean().Should().BeFalse();
        }

        // Freeze SITE-01 -> pre-enrollment check and own-site subject check (without site_code) both report Frozen on study_site.
        _locks.ActiveLocks.Add((fixture.VisibleStudyId, new ScopeWriteLockCandidate(LockKind.Frozen, NodeRef.For(fixture.Site01Id))));
        using (var preEnrollFrozen = await ReadJsonAsync(
            site01Client,
            "/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&site_code=SITE-01&subject_code=SUBJ-NEW-MARKER",
            ct))
        {
            preEnrollFrozen.RootElement.GetProperty("locked").GetBoolean().Should().BeTrue();
            preEnrollFrozen.RootElement.GetProperty("lock_kind").GetString().Should().Be("Frozen");
            preEnrollFrozen.RootElement.GetProperty("scope_kind").GetString().Should().Be(NodeKinds.StudySite);
        }

        using (var ownSubjectFrozen = await ReadJsonAsync(
            site01Client,
            "/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&subject_code=SUBJ-SITE01-MARKER",
            ct))
        {
            ownSubjectFrozen.RootElement.GetProperty("locked").GetBoolean().Should().BeTrue();
            ownSubjectFrozen.RootElement.GetProperty("lock_kind").GetString().Should().Be("Frozen");
            ownSubjectFrozen.RootElement.GetProperty("scope_kind").GetString().Should().Be(NodeKinds.StudySite);
        }

        // 2. Automation agent callers: external agent: subject (even claiming SYSTEM_SERVICE) cannot bypass INodeVisibility,
        // and an internal AutomationAgent acting for SITE-01 coordinator is constrained to SITE-01 visibility (K-AI-6).
        using var externalAgentClient = host.CreateKestrelSignedClient(
            TestSecurityContext.Identity("agent:triage-bot", "SYSTEM_SERVICE"));
        (await externalAgentClient.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-CONFIDENTIAL-MARKER", UriKind.Relative), ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await externalAgentClient.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER", UriKind.Relative), ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var agentIdentity = Actor.AutomationAgent("triage-bot");
        var capability = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue).Capability.Token;
        _authority.Grants.Add(Grant.Issue(
            GrantId.New(), Tenant, agentIdentity, capability, null, AuthorityScope.StudySite(fixture.Site01Id),
            GrantedFrom, null, withGrantOption: false, parentGrantId: null, Actor.Person("study-admin"), GrantedFrom).Value!);
        scopedAgentHolder.Override = agentIdentity.ActingFor(fixture.Site01Coordinator);
        try
        {
            (await externalAgentClient.GetAsync(
                new Uri("/api/v1/kernel/locks/in-force?study_code=STD-CONFIDENTIAL-MARKER", UriKind.Relative), ct))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await externalAgentClient.GetAsync(
                new Uri("/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&site_code=SITE-03", UriKind.Relative), ct))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var agentCrossSiteSubject = await ReadJsonAsync(
                externalAgentClient,
                "/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&subject_code=SUBJ-SITE02-MARKER",
                ct);
            agentCrossSiteSubject.RootElement.GetProperty("locked").GetBoolean().Should().BeFalse();

            using var agentOwnSite = await ReadJsonAsync(
                externalAgentClient,
                "/api/v1/kernel/locks/in-force?study_code=STD-VISIBLE-MARKER&site_code=SITE-01",
                ct);
            agentOwnSite.RootElement.GetProperty("locked").GetBoolean().Should().BeTrue();
        }
        finally
        {
            scopedAgentHolder.Override = null;
        }

        // 3. Spoofing defense: svc: without SYSTEM_SERVICE role and a person claiming SYSTEM_SERVICE both receive 404.
        using var unprivilegedSvcClient = host.CreateKestrelSignedClient(
            TestSecurityContext.Identity("svc:etl", "Service"));
        (await unprivilegedSvcClient.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-CONFIDENTIAL-MARKER", UriKind.Relative), ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var spoofedPersonClient = host.CreateKestrelSignedClient(
            TestSecurityContext.Identity("stranger@sponsor.test", "SYSTEM_SERVICE"));
        (await spoofedPersonClient.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-CONFIDENTIAL-MARKER", UriKind.Relative), ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. Verified svc:econsent-consumer and svc:execution-db-seeder in role SYSTEM_SERVICE evaluate tenant-wide locks via HttpKernelLockGuard.
        using var rawKestrelClient = host.CreateUnsignedKestrelClient();
        var guard = new HttpKernelLockGuard(
            rawKestrelClient,
            new HttpContextAccessor(),
            new StaticGatewayOptionsMonitor(TestSecurityContext.GatewaySecret),
            host.Services.GetRequiredService<Cadence.Kernel.Time.IClock>());

        foreach (var serviceAccount in new[] { ServiceAccounts.EConsentConsumer, ServiceAccounts.DbSeeder })
        {
            var confidentialGuard = await guard.EnsureUnlockedForServiceAsync(
                serviceAccount,
                TestSecurityContext.TenantCode,
                "STD-CONFIDENTIAL-MARKER",
                cancellationToken: ct);
            confidentialGuard.IsFailure.Should().BeTrue(serviceAccount.UserId);
            confidentialGuard.Error!.Code.Should().Be(HttpKernelLockGuard.ObservationLockedCode);

            var site03Guard = await guard.EnsureUnlockedForServiceAsync(
                serviceAccount,
                TestSecurityContext.TenantCode,
                "STD-VISIBLE-MARKER",
                siteCode: "SITE-03",
                cancellationToken: ct);
            site03Guard.IsFailure.Should().BeTrue(serviceAccount.UserId);
            site03Guard.Error!.Code.Should().Be(HttpKernelLockGuard.ObservationLockedCode);

            var subj02Guard = await guard.EnsureUnlockedForServiceAsync(
                serviceAccount,
                TestSecurityContext.TenantCode,
                "STD-VISIBLE-MARKER",
                subjectCode: "SUBJ-SITE02-MARKER",
                cancellationToken: ct);
            subj02Guard.IsFailure.Should().BeTrue(serviceAccount.UserId);
            subj02Guard.Error!.Code.Should().Be(HttpKernelLockGuard.ObservationLockedCode);

            var missingStudyGuard = await guard.EnsureUnlockedForServiceAsync(
                serviceAccount,
                TestSecurityContext.TenantCode,
                "STD-NONEXISTENT-MARKER",
                cancellationToken: ct);
            missingStudyGuard.IsSuccess.Should().BeTrue(serviceAccount.UserId);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BatchInForceLockEndpoint_EnforcesNonConfirming404OnHiddenSitesAndEvaluatesBatchLocksForSystemService()
    {
        var ct = TestContext.Current.CancellationToken;
        _ = SeedLockVisibilityScenario();
        var host = CreateKestrelHost(hostVisibility: true);

        using var coordinatorClient = host.CreateKestrelSignedClient(
            TestSecurityContext.Identity("crc.site01@site.test", "Coordinator"));

        // Coordinator can batch-check their own visible unlocked site (SITE-01).
        using var ownSiteBatch = await coordinatorClient.PostAsJsonAsync(
            "/api/v1/kernel/locks/in-force/batch",
            new { study_code = "STD-VISIBLE-MARKER", site_codes = new[] { "SITE-01" } },
            ct);
        ownSiteBatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var ownDoc = JsonDocument.Parse(await ownSiteBatch.Content.ReadAsStringAsync(ct));
        ownDoc.RootElement.GetProperty("locked").GetBoolean().Should().BeFalse();

        // Including an unassigned site (SITE-03) in the batch yields a non-confirming 404 matching a nonexistent site.
        using var hiddenSiteBatch = await coordinatorClient.PostAsJsonAsync(
            "/api/v1/kernel/locks/in-force/batch",
            new { study_code = "STD-VISIBLE-MARKER", site_codes = new[] { "SITE-01", "SITE-03" } },
            ct);
        using var nonexistentSiteBatch = await coordinatorClient.PostAsJsonAsync(
            "/api/v1/kernel/locks/in-force/batch",
            new { study_code = "STD-VISIBLE-MARKER", site_codes = new[] { "SITE-01", "SITE-NONEXISTENT" } },
            ct);
        hiddenSiteBatch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        nonexistentSiteBatch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        string hiddenBody = await hiddenSiteBatch.Content.ReadAsStringAsync(ct);
        string nonexistentBody = await nonexistentSiteBatch.Content.ReadAsStringAsync(ct);
        hiddenBody.Should().NotContain("SITE-03").And.NotContain(nameof(MaskReason.NotPermitted));
        using var hiddenDoc = JsonDocument.Parse(hiddenBody);
        using var nonexistentDoc = JsonDocument.Parse(nonexistentBody);
        hiddenDoc.RootElement.GetProperty("title").GetString()
            .Should().Be(nonexistentDoc.RootElement.GetProperty("title").GetString());
        hiddenDoc.RootElement.GetProperty("detail").GetString()
            .Should().Be(nonexistentDoc.RootElement.GetProperty("detail").GetString());

        // Verified SYSTEM_SERVICE caller (svc:etmf) evaluates multi-site locks in one batch call.
        using var systemServiceClient = host.CreateKestrelSignedClient(
            TestSecurityContext.Identity("svc:etmf", Cadence.Security.Rbac.PermissionMatrix.EtmfServiceRole, "SYSTEM_SERVICE"));

        using var unlockedServiceBatch = await systemServiceClient.PostAsJsonAsync(
            "/api/v1/kernel/locks/in-force/batch",
            new { study_code = "STD-VISIBLE-MARKER", site_codes = new[] { "SITE-01" } },
            ct);
        unlockedServiceBatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var unlockedDoc = JsonDocument.Parse(await unlockedServiceBatch.Content.ReadAsStringAsync(ct));
        unlockedDoc.RootElement.GetProperty("locked").GetBoolean().Should().BeFalse();

        using var lockedServiceBatch = await systemServiceClient.PostAsJsonAsync(
            "/api/v1/kernel/locks/in-force/batch",
            new { study_code = "STD-VISIBLE-MARKER", site_codes = new[] { "SITE-01", "SITE-02" } },
            ct);
        lockedServiceBatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var lockedDoc = JsonDocument.Parse(await lockedServiceBatch.Content.ReadAsStringAsync(ct));
        lockedDoc.RootElement.GetProperty("locked").GetBoolean().Should().BeTrue();
        lockedDoc.RootElement.GetProperty("lock_kind").GetString().Should().Be("Locked");
        lockedDoc.RootElement.GetProperty("scope_kind").GetString().Should().Be("study_site");
    }

    private LockVisibilityFixture SeedLockVisibilityScenario()
    {
        var confidentialStudyId = StudyId.New();
        var visibleStudyId = StudyId.New();
        var site01Id = StudySiteId.New();
        var site02Id = StudySiteId.New();
        var site03Id = StudySiteId.New();
        var subjSite01Id = EnrollmentId.New();
        var subjSite02Id = EnrollmentId.New();
        var site01Coordinator = Actor.Person("crc.site01@site.test");

        _locks.StudiesByCode["STD-CONFIDENTIAL-MARKER"] = confidentialStudyId;
        _locks.StudiesByCode["STD-VISIBLE-MARKER"] = visibleStudyId;
        _locks.SitesByCode[(visibleStudyId, "SITE-01")] = site01Id;
        _locks.SitesByCode[(visibleStudyId, "SITE-02")] = site02Id;
        _locks.SitesByCode[(visibleStudyId, "SITE-03")] = site03Id;
        _locks.EnrollmentsBySubjectCode[(visibleStudyId, "SUBJ-SITE01-MARKER")] = (subjSite01Id, site01Id);
        _locks.EnrollmentsBySubjectCode[(visibleStudyId, "SUBJ-SITE02-MARKER")] = (subjSite02Id, site02Id);
        _locks.EnrollmentsById[(visibleStudyId, subjSite01Id.Value)] = (subjSite01Id, site01Id);
        _locks.EnrollmentsById[(visibleStudyId, subjSite02Id.Value)] = (subjSite02Id, site02Id);

        _locks.ActiveLocks.Add((confidentialStudyId, new ScopeWriteLockCandidate(LockKind.Locked, NodeRef.For(confidentialStudyId))));
        _locks.ActiveLocks.Add((visibleStudyId, new ScopeWriteLockCandidate(LockKind.Locked, NodeRef.For(site02Id))));
        _locks.ActiveLocks.Add((visibleStudyId, new ScopeWriteLockCandidate(LockKind.Locked, NodeRef.For(site03Id))));
        _locks.ActiveLocks.Add((visibleStudyId, new ScopeWriteLockCandidate(LockKind.Locked, NodeRef.For(subjSite02Id))));

        var sponsorOrgId = OrganizationId.New();
        _authority.Paths[NodeRef.For(confidentialStudyId)] = new ScopePath(Organization: sponsorOrgId, Study: confidentialStudyId);
        _authority.Paths[NodeRef.For(visibleStudyId)] = new ScopePath(Organization: sponsorOrgId, Study: visibleStudyId);
        _authority.Paths[NodeRef.For(site01Id)] = new ScopePath(Organization: sponsorOrgId, Study: visibleStudyId, StudySite: site01Id);
        _authority.Paths[NodeRef.For(site02Id)] = new ScopePath(Organization: sponsorOrgId, Study: visibleStudyId, StudySite: site02Id);
        _authority.Paths[NodeRef.For(site03Id)] = new ScopePath(Organization: sponsorOrgId, Study: visibleStudyId, StudySite: site03Id);
        _authority.Paths[NodeRef.For(subjSite01Id)] = new ScopePath(Organization: sponsorOrgId, Study: visibleStudyId, StudySite: site01Id);
        _authority.Paths[NodeRef.For(subjSite02Id)] = new ScopePath(Organization: sponsorOrgId, Study: visibleStudyId, StudySite: site02Id);

        var capability = KernelCommandCatalog.Kernel.Entries.First(e => e.InCatalogue).Capability.Token;
        _authority.Grants.Add(Grant.Issue(
            GrantId.New(), Tenant, site01Coordinator, capability, null, AuthorityScope.StudySite(site01Id),
            GrantedFrom, null, withGrantOption: false, parentGrantId: null, Actor.Person("study-admin"), GrantedFrom).Value!);

        return new LockVisibilityFixture(
            confidentialStudyId,
            visibleStudyId,
            site01Id,
            site02Id,
            site03Id,
            subjSite01Id,
            subjSite02Id,
            site01Coordinator);
    }

    private KestrelLockHostFactory CreateKestrelHost(bool hostVisibility, MutableCurrentActor? overrideActor = null)
    {
        _directory.Tenants[TestSecurityContext.TenantCode] = Tenant;
        var factory = new KestrelLockHostFactory(this, hostVisibility, overrideActor);
        _factories.Add(factory);
        return factory;
    }

    private sealed record LockVisibilityFixture(
        StudyId ConfidentialStudyId,
        StudyId VisibleStudyId,
        StudySiteId Site01Id,
        StudySiteId Site02Id,
        StudySiteId Site03Id,
        EnrollmentId SubjSite01Id,
        EnrollmentId SubjSite02Id,
        Actor Site01Coordinator);

    private sealed class MutableCurrentActor : ICurrentActor
    {
        public Actor? Override { get; set; }

        public Actor? Actor => Override;
    }

    private sealed class DelegatingCurrentActor(ICurrentActor inner, MutableCurrentActor overrideHolder) : ICurrentActor
    {
        public Actor? Actor => overrideHolder.Actor ?? inner.Actor;
    }

    private sealed class StaticGatewayOptionsMonitor(string gatewaySecret) : IOptionsMonitor<GatewayAuthenticationOptions>
    {
        public GatewayAuthenticationOptions CurrentValue { get; } = new() { GatewaySecret = gatewaySecret };

        public GatewayAuthenticationOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<GatewayAuthenticationOptions, string?> listener) => null;
    }

    private sealed class KestrelLockHostFactory(
        KernelApiHostTests owner,
        bool hostVisibility,
        MutableCurrentActor? overrideActor) : WebApplicationFactory<Cadence.Kernel.Host.Program>
    {
        private bool _started;

        public HttpClient CreateKestrelSignedClient(GatewayIdentity identity)
        {
            EnsureStarted();
            var handler = new TestSecurityContext.GatewaySigningHandler(identity)
            {
                InnerHandler = new SocketsHttpHandler(),
            };
            return new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = ClientOptions.BaseAddress,
            };
        }

        public HttpClient CreateUnsignedKestrelClient()
        {
            EnsureStarted();
            return new HttpClient(new SocketsHttpHandler(), disposeHandler: true)
            {
                BaseAddress = ClientOptions.BaseAddress,
            };
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestBiometricLivenessKey().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                owner.ConfigureFakeServices(services, hostVisibility, dropClaim: null);
                services.RemoveBackgroundHostedServices();

                if (overrideActor is not null)
                {
                    var innerDescriptor = services.Single(d => d.ServiceType == typeof(ICurrentActor));
                    services.Remove(innerDescriptor);
                    services.AddScoped<ICurrentActor>(sp =>
                    {
                        var inner = (ICurrentActor)ActivatorUtilities.CreateInstance(sp, innerDescriptor.ImplementationType!);
                        return new DelegatingCurrentActor(inner, overrideActor);
                    });
                }
            });
        }

        private void EnsureStarted()
        {
            if (_started)
            {
                return;
            }

            UseKestrel(0);
            StartServer();
            _started = true;
        }
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> Factory(
        bool knownTenant = true, string? dropClaim = null, bool hostVisibility = false)
    {
        if (knownTenant)
        {
            _directory.Tenants[TestSecurityContext.TenantCode] = Tenant;
        }

        var factory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestBiometricLivenessKey().UseTestPostgresConnectionString();
            builder.ConfigureServices(services => ConfigureFakeServices(services, hostVisibility, dropClaim));
        });
        _factories.Add(factory);
        return factory;
    }

    private void ConfigureFakeServices(IServiceCollection services, bool hostVisibility, string? dropClaim)
    {
        services.RemoveAll<IKernelNavigator>();
        services.AddSingleton<IKernelNavigator>(_navigator);
        services.RemoveAll<IKernelSearch>();
        services.AddSingleton<IKernelSearch>(_search);
        services.RemoveAll<ITenantDirectory>();
        services.AddSingleton<ITenantDirectory>(_directory);
        services.RemoveAll<IProtocolVersionReader>();
        services.AddSingleton<IProtocolVersionReader>(_protocol);
        services.RemoveAll<IProposalReader>();
        services.AddSingleton<IProposalReader>(_proposals);
        services.RemoveAll<IAuthorityStore>();
        services.AddSingleton<IAuthorityStore>(_authority);
        services.RemoveAll<IKernelLineage>();
        services.AddSingleton<IKernelLineage>(_lineage);
        services.RemoveAll<IAmendmentImpact>();
        services.AddSingleton<IAmendmentImpact>(_lineage);
        services.RemoveAll<IActivityStore>();
        services.AddSingleton<IActivityStore>(_activity);
        services.RemoveAll<IEnrollmentDirectory>();
        services.AddSingleton<IEnrollmentDirectory>(_enrollments);
        services.RemoveAll<IStudyDirectory>();
        services.AddSingleton<IStudyDirectory>(_studies);
        services.RemoveAll<IValidationFindings>();
        services.AddSingleton<IValidationFindings>(_findings);
        services.RemoveAll<ISimulationInputs>();
        services.AddSingleton<ISimulationInputs>(_simulationInputs);
        services.RemoveAll<IWorkQueues>();
        services.AddSingleton<IWorkQueues>(_workQueues);
        services.RemoveAll<IWorkItemTargetResolver>();
        services.AddSingleton<IWorkItemTargetResolver>(_workItemTargets);
        services.RemoveAll<ILockRecords>();
        services.AddSingleton<ILockRecords>(_locks);
        services.RemoveAll<IStudySdvAlertReader>();
        services.AddSingleton<IStudySdvAlertReader>(_sdvAlerts);

        // Background jobs read the database the host is configured with, which these tests never open.
        services.RemoveBackgroundHostedServices();

        if (!hostVisibility)
        {
            services.RemoveAll<INodeVisibility>();
            services.AddSingleton<INodeVisibility>(_visibility);
        }

        if (dropClaim is not null)
        {
            services.AddSingleton<Microsoft.AspNetCore.Authentication.IClaimsTransformation>(new DropClaim(dropClaim));
        }
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

        public Dictionary<NodeRef, ScopePath> Paths { get; } = [];

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

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
        {
            var unversioned = resource.Unversioned();
            if (Paths.TryGetValue(unversioned, out var explicitPath))
            {
                return Task.FromResult(explicitPath);
            }

            if (unversioned.Kind == NodeKinds.Grant
                && Grants.SingleOrDefault(g => g.Id == GrantId.FromGuid(unversioned.Id)) is { Scope.TargetId: { } targetGuid } grant)
            {
                if (grant.Scope.Level == ScopeLevel.StudySite
                    && Paths.TryGetValue(NodeRef.For(StudySiteId.FromGuid(targetGuid)), out var sitePath))
                {
                    return Task.FromResult(sitePath);
                }

                if (grant.Scope.Level is ScopeLevel.Study or ScopeLevel.StudyCountry
                    && Paths.TryGetValue(NodeRef.For(StudyId.FromGuid(targetGuid)), out var studyPath))
                {
                    return Task.FromResult(studyPath);
                }
            }

            return Task.FromResult(ScopePath.TenantWide);
        }

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([]);

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([]);
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
                    [new ElementChange("act.week12", "PlannedActivity", ElementChangeKind.Changed, ["/timing/day"])],
                    ScheduleDiff)
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

        /// <summary>The amendment holds a typed design; the first version's content is not one.</summary>
        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
        {
            Tenants.Add(tenant);
            return Task.FromResult(version == Amendment.Id ? ScheduleOfActivities.Compile(AmendedDesign) : null);
        }
    }

    private sealed class FakeProposals : IProposalReader
    {
        public List<(TenantId Tenant, ProposalStatus? Status, int Limit)> Lists { get; } = [];

        public Task<IReadOnlyList<ProposalSummary>> ListForStudyAsync(
            TenantId tenant, StudyId study, ProposalStatus? status, DateTimeOffset now, int limit, CancellationToken cancellationToken)
        {
            Lists.Add((tenant, status, limit));
            IReadOnlyList<ProposalSummary> proposals = NodeRef.For(study) == Study ? [OpenProposal, HiddenProposal] : [];
            return Task.FromResult(proposals);
        }

        public Task<ProposalSummary?> GetAsync(TenantId tenant, ProposalId proposal, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { OpenProposal, HiddenProposal }.FirstOrDefault(p => p.Id == proposal));
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
        private static readonly NodeSummary EnrollmentSummary = new(Enrollment.AtVersion(1), "017-001", "017-001", "Enrolled");
        private static readonly NodeSummary ConceptSummary = new(Concept.AtVersion(1), "Systolic Blood Pressure", "C25298", "Active");
        private static readonly NodeSummary FormSummary = new(FormInstance.AtVersion(1), "Vital Signs (Screening)");
        private static readonly NodeSummary ObservationSummary = new(RecordedObservation.AtVersion(1), "Systolic Blood Pressure: 120 mm[Hg]");
        private static readonly NodeSummary ActivityEnrollmentSummary = new(FakeActivityStore.VisibleSubject, "017-010", "017-010", "Enrolled");
        private static readonly NodeSummary FirstVersionSummary = new(NodeRef.For(FirstVersion.Id, 3), "Protocol version 1", "v1", "Approved");

        public List<TenantId> Tenants { get; } = [];

        public Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenant);
            return Task.FromResult(Find(node));
        }

        public Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenant);
            if (Find(node) is not { } found)
            {
                return Task.FromResult<IReadOnlyList<ZoomStep>?>(null);
            }

            IReadOnlyList<ZoomStep> path = found.Ref.Kind switch
            {
                NodeKinds.Concept => [new ZoomStep(NodeKinds.Concept, "C25298", ConceptSummary.Ref)],
                NodeKinds.CollectionInstance =>
                [
                    new ZoomStep(NodeKinds.Study, "CAD-101", StudySummary.Ref),
                    new ZoomStep(ZoomLevels.Country, "DE", null),
                    new ZoomStep(NodeKinds.StudySite, "017", SiteSummary.Ref),
                    new ZoomStep(NodeKinds.Enrollment, "017-001", EnrollmentSummary.Ref),
                    new ZoomStep(NodeKinds.CollectionInstance, FormSummary.Title, FormSummary.Ref),
                ],
                NodeKinds.Observation =>
                [
                    new ZoomStep(NodeKinds.Study, "CAD-101", StudySummary.Ref),
                    new ZoomStep(ZoomLevels.Country, "DE", null),
                    new ZoomStep(NodeKinds.StudySite, "017", SiteSummary.Ref),
                    new ZoomStep(NodeKinds.Enrollment, "017-001", EnrollmentSummary.Ref),
                    new ZoomStep(NodeKinds.CollectionInstance, FormSummary.Title, FormSummary.Ref),
                    new ZoomStep(NodeKinds.Observation, ObservationSummary.Title, ObservationSummary.Ref),
                ],
                _ =>
                [
                    new ZoomStep(NodeKinds.Study, "CAD-101", StudySummary.Ref),
                    new ZoomStep(ZoomLevels.Country, "DE", null),
                    new ZoomStep(NodeKinds.StudySite, "017", SiteSummary.Ref),
                ],
            };
            return Task.FromResult<IReadOnlyList<ZoomStep>?>(path);
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
            var unversioned = node.Unversioned();
            if (unversioned == Study)
            {
                return StudySummary;
            }

            if (unversioned == StudySite)
            {
                return SiteSummary;
            }

            if (unversioned == Enrollment)
            {
                return EnrollmentSummary;
            }

            if (unversioned == Concept)
            {
                return ConceptSummary;
            }

            if (unversioned == FormInstance)
            {
                return FormSummary;
            }

            if (unversioned == FakeActivityStore.VisibleSubject.Unversioned())
            {
                return ActivityEnrollmentSummary;
            }

            if (unversioned == NodeRef.For(FirstVersion.Id))
            {
                return FirstVersionSummary;
            }

            return unversioned == RecordedObservation ? ObservationSummary : null;
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

    private sealed class FakeEnrollmentDirectory : IEnrollmentDirectory
    {
        public static readonly NodeRef LookupEnrollment = NodeRef.For(EnrollmentId.New());

        public List<(TenantId Tenant, string StudyCode, SubjectCode SubjectCode, string? SiteCode)> Lookups { get; } = [];

        public Task<EnrollmentCodeLookupResult?> LookupByCodeAsync(
            TenantId tenant,
            string studyCode,
            SubjectCode subjectCode,
            string? siteCode,
            CancellationToken cancellationToken = default)
        {
            Lookups.Add((tenant, studyCode, subjectCode, siteCode));
            if (!string.Equals(studyCode, "CAD-101", StringComparison.OrdinalIgnoreCase)
                || subjectCode != SubjectCode.From("017-0001")
                || (siteCode is not null && !string.Equals(siteCode, "017", StringComparison.Ordinal)))
            {
                return Task.FromResult<EnrollmentCodeLookupResult?>(null);
            }

            return Task.FromResult<EnrollmentCodeLookupResult?>(new EnrollmentCodeLookupResult(
                StudyId.FromGuid(Study.Id),
                3,
                "CAD-101",
                "Cadence 101",
                StudySiteId.FromGuid(StudySite.Id),
                1,
                "017",
                "Hamburg Clinic",
                EnrollmentId.FromGuid(LookupEnrollment.Id),
                2,
                subjectCode,
                EnrollmentStatus.Enrolled));
        }

        public Task<EnrollmentState?> ResolveParticipantAsync(TenantId tenant, StudyId study, SubjectCode code, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EnrollmentState?> FindAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EnrollmentStatus?> StatusAsOfAsync(
            TenantId tenant, EnrollmentId enrollment, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<(StudyId StudyId, StudySiteId StudySiteId)?> FindStudySiteAsync(
            TenantId tenant, string studyCode, string siteCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> NextEnrollmentNumberAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSimulationInputs : ISimulationInputs
    {
        private static readonly string BaseContent = ProtocolDesignDocument.Write(SoaSamples.Cardio());
        private static readonly string AmendedContent = ProtocolDesignDocument.Write(AmendedDesign);

        public Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken)
        {
            if (id == FirstVersion.Id)
            {
                return Task.FromResult<ProtocolVersionSnapshot?>(new ProtocolVersionSnapshot(
                    FirstVersion.Id, StudyId.FromGuid(Study.Id), 1, ProtocolVersionStatus.Approved, null, BaseContent, ContentHash.Compute(BaseContent), 3));
            }

            if (id == Amendment.Id)
            {
                return Task.FromResult<ProtocolVersionSnapshot?>(new ProtocolVersionSnapshot(
                    Amendment.Id, StudyId.FromGuid(Study.Id), 2, ProtocolVersionStatus.Draft, FirstVersion.Id, AmendedContent, null, 2));
            }

            return Task.FromResult<ProtocolVersionSnapshot?>(null);
        }

        public Task<DependentsReport> DependentsAsync(
            TenantId tenant,
            Actor reader,
            ProtocolVersionId baseVersion,
            ProtocolVersionId? candidate,
            IReadOnlySet<NodeRef> impactedElements,
            SimulationAsOf asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DependentsReport(
                NodeRef.For(baseVersion).ToString(), LineageQuery.DefaultDepth, asOf.ValidAt, asOf.KnownAt, [], 0, false));
    }

    private sealed class FakeStudyDirectory : IStudyDirectory
    {
        private static readonly StudyDirectoryEntry KnownStudy = new(
            StudyId.FromGuid(Study.Id),
            3,
            "CAD-101",
            "Cadence 101",
            "Phase II",
            OrganizationId.New(),
            1,
            "SPONSOR-1",
            "Cadence Sponsor",
            1);

        public Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyDirectoryEntry>>([KnownStudy]);

        public Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(studyId == KnownStudy.StudyId ? KnownStudy : null);

        public Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(studyCode, KnownStudy.StudyCode, StringComparison.OrdinalIgnoreCase) ? KnownStudy : null);

        public Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationDirectoryEntry>>([]);

        public Task<OrganizationDirectoryEntry?> FindOrganizationAsync(TenantId tenant, OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationDirectoryEntry?>(null);

        public Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(TenantId tenant, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationDirectoryEntry?>(null);

        public Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SiteDirectoryEntry>>([]);

        public Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SiteDirectoryEntry?>(null);

        public Task<SiteDirectoryEntry?> FindSiteByCodeAsync(TenantId tenant, string siteCode, OrganizationId? institutionId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<SiteDirectoryEntry?>(null);

        public Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteDirectoryEntry>>([]);

        public Task<StudySiteDirectoryEntry?> FindStudySiteAsync(TenantId tenant, StudySiteId studySiteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudySiteDirectoryEntry?>(null);

        public Task<StudySiteDirectoryEntry?> FindStudySiteBySiteAsync(TenantId tenant, StudyId studyId, SiteId siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudySiteDirectoryEntry?>(null);
    }

    private sealed class FakeValidationFindings : IValidationFindings
    {
        private static readonly ValidationRunId Run = ValidationRunId.New();
        private static readonly LibraryItemId Pack = LibraryItemId.New();
        private static readonly ValidationFindingView[] AllFindings =
        [
            MakeFinding(ValidationSeverity.Error, FindingStatus.Open, "VS-001"),
            MakeFinding(ValidationSeverity.Warning, FindingStatus.Investigating, "VS-002"),
            MakeFinding(ValidationSeverity.Notice, FindingStatus.Resolved, "VS-003"),
        ];

        public static NodeRef OpenFinding => NodeRef.For(AllFindings[0].Id);

        public Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken) =>
            Task.FromResult(AllFindings.FirstOrDefault(f => f.Id == finding));

        public Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken)
        {
            if (query.Study != StudyId.FromGuid(Study.Id))
            {
                return Task.FromResult(new FindingPage([], 0));
            }

            var matching = AllFindings
                .Where(f => query.Statuses is null || query.Statuses.Contains(f.Status))
                .ToList();
            return Task.FromResult(new FindingPage([.. matching.Take(query.Take)], matching.Count));
        }

        public Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken) =>
            Task.FromResult<ValidationRunSummary?>(null);

        private static ValidationFindingView MakeFinding(ValidationSeverity severity, FindingStatus status, string ruleId) =>
            new(
                ValidationFindingId.New(),
                StudyId.FromGuid(Study.Id),
                ValidationStage.Collection,
                "core-vitals",
                Pack,
                ruleId,
                1,
                severity,
                new FindingTarget(FindingTargetKind.DesignElement, "protocol", NodeRef.For(FirstVersion.Id).ToString()),
                $"{ruleId} finding",
                null,
                [],
                null,
                null,
                null,
                status,
                Run,
                Run,
                status == FindingStatus.Resolved ? Run : null,
                null,
                null);
    }

    private sealed class FakeWorkQueues : IWorkQueues
    {
        private static readonly NodeRef Definition = NodeRef.For(WorkQueueDefinitionId.New(), 1);
        private static readonly DateTimeOffset DueSince = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

        public List<WorkQueueQuery> Queries { get; } = [];

        public Task<CommandResult<IReadOnlyList<WorkQueue>>> ReadAsync(TenantId tenant, WorkQueueQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            IReadOnlyList<WorkQueue> queues =
            [
                new WorkQueue(
                    Definition,
                    "ecrf.verify",
                    1,
                    "Source Data Verification",
                    "observation.verify",
                    [new WorkItem(RecordedObservation.AtVersion(1), NodeKinds.Observation, "Vital Signs SDV", DueSince, null)],
                    Truncated: false),
            ];
            return Task.FromResult(CommandResult<IReadOnlyList<WorkQueue>>.Success(queues));
        }
    }

    private sealed class FakeWorkItemTargetResolver : IWorkItemTargetResolver
    {
        public Task<IReadOnlyDictionary<NodeRef, WorkItemTargetContext>> ResolveAsync(
            TenantId tenant,
            IReadOnlyCollection<NodeRef> subjects,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<NodeRef, WorkItemTargetContext>>(
                new Dictionary<NodeRef, WorkItemTargetContext>());
    }

    private sealed class FakeLockRecords : ILockRecords
    {
        public Dictionary<string, StudyId> StudiesByCode { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CAD-101"] = StudyId.FromGuid(Study.Id),
        };

        public Dictionary<(StudyId Study, string SiteCode), StudySiteId> SitesByCode { get; } = [];

        public Dictionary<(StudyId Study, string SubjectCode), (EnrollmentId EnrollmentId, StudySiteId StudySiteId)> EnrollmentsBySubjectCode { get; } = [];

        public Dictionary<(StudyId Study, Guid EnrollmentGuid), (EnrollmentId EnrollmentId, StudySiteId StudySiteId)> EnrollmentsById { get; } = [];

        public List<(StudyId Study, ScopeWriteLockCandidate Candidate)> ActiveLocks { get; } = [];

        public List<(TenantId Tenant, string StudyCodeOrId, string? SiteCodeOrId, string? SubjectCode, Guid? EnrollmentId)> Checks { get; } = [];

        public Task<StudyLockEntry?> FindAsync(TenantId tenant, AssertionId lockId, CancellationToken cancellationToken) =>
            Task.FromResult<StudyLockEntry?>(null);

        public Task<ScopeWriteLockCheck> CheckScopeWriteLockAsync(
            TenantId tenant,
            string studyCodeOrId,
            string? siteCodeOrId = null,
            string? subjectCode = null,
            Guid? enrollmentId = null,
            CancellationToken cancellationToken = default)
        {
            Checks.Add((tenant, studyCodeOrId, siteCodeOrId, subjectCode, enrollmentId));
            if (string.IsNullOrWhiteSpace(studyCodeOrId))
            {
                return Task.FromResult(new ScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null));
            }

            var trimmedStudy = studyCodeOrId.Trim();
            StudyId? resolvedStudy = null;
            if (NodeRef.TryParse(trimmedStudy, out var studyNode) && studyNode.Kind == NodeKinds.Study)
            {
                var candidate = StudyId.FromGuid(studyNode.Id);
                if (StudiesByCode.Values.Contains(candidate))
                {
                    resolvedStudy = candidate;
                }
            }
            else if (StudiesByCode.TryGetValue(trimmedStudy, out var byCode))
            {
                resolvedStudy = byCode;
            }

            if (resolvedStudy is not { } foundStudy)
            {
                return Task.FromResult(new ScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null));
            }

            var hasExplicitSite = !string.IsNullOrWhiteSpace(siteCodeOrId);
            StudySiteId? resolvedSiteId = null;
            if (hasExplicitSite && SitesByCode.TryGetValue((foundStudy, siteCodeOrId!.Trim()), out var siteId))
            {
                resolvedSiteId = siteId;
            }

            EnrollmentId? resolvedEnrollmentId = null;
            StudySiteId? enrollmentStudySiteId = null;
            if (enrollmentId is { } givenGuid
                && givenGuid != Guid.Empty
                && EnrollmentsById.TryGetValue((foundStudy, givenGuid), out var byGuid))
            {
                resolvedEnrollmentId = byGuid.EnrollmentId;
                enrollmentStudySiteId = byGuid.StudySiteId;
                if (!hasExplicitSite)
                {
                    resolvedSiteId = byGuid.StudySiteId;
                }
            }
            else if (!string.IsNullOrWhiteSpace(subjectCode)
                && EnrollmentsBySubjectCode.TryGetValue((foundStudy, subjectCode.Trim()), out var bySubject))
            {
                resolvedEnrollmentId = bySubject.EnrollmentId;
                enrollmentStudySiteId = bySubject.StudySiteId;
                if (!hasExplicitSite)
                {
                    resolvedSiteId = bySubject.StudySiteId;
                }
            }

            var coveringScopes = new HashSet<NodeRef> { NodeRef.For(foundStudy) };
            if (resolvedSiteId is { } ssid)
            {
                coveringScopes.Add(NodeRef.For(ssid));
            }

            if (enrollmentStudySiteId is { } esid)
            {
                coveringScopes.Add(NodeRef.For(esid));
            }

            if (resolvedEnrollmentId is { } eid)
            {
                coveringScopes.Add(NodeRef.For(eid));
            }

            var activeCandidates = ActiveLocks
                .Where(l => l.Study == foundStudy && coveringScopes.Contains(l.Candidate.Scope.Unversioned()))
                .Select(l => l.Candidate)
                .OrderByDescending(c => c.Kind == LockKind.Locked)
                .ThenBy(c => LockScopes.OrderOf(c.Scope.Kind))
                .ToList();
            var top = activeCandidates.FirstOrDefault();

            return Task.FromResult(new ScopeWriteLockCheck(
                StudyExists: true,
                Locked: top is not null,
                LockKind: top?.Kind,
                ScopeKind: top?.Scope.Kind,
                StudyId: foundStudy,
                StudySiteId: resolvedSiteId,
                EnrollmentId: resolvedEnrollmentId,
                CoveringLocks: activeCandidates,
                EnrollmentStudySiteId: enrollmentStudySiteId));
        }

        public Task<StudyLockList?> ListByStudyAsync(
            TenantId tenant,
            StudyId study,
            DateTimeOffset validAt,
            DateTimeOffset knownAt,
            bool activeOnly,
            CancellationToken cancellationToken)
        {
            if (study != StudyId.FromGuid(Study.Id))
            {
                return Task.FromResult<StudyLockList?>(null);
            }

            return Task.FromResult<StudyLockList?>(new StudyLockList(study, "CAD-101", validAt, knownAt, []));
        }

        public Task<LockReadinessSnapshot?> ReadReadinessAsync(
            TenantId tenant,
            StudyId study,
            NodeRef scope,
            CancellationToken cancellationToken)
        {
            if (study != StudyId.FromGuid(Study.Id))
            {
                return Task.FromResult<LockReadinessSnapshot?>(null);
            }

            var explanation = new Explanation(
                scope,
                "Scope is ready for lock.",
                "kernel.locks",
                [new ExplanationStep("Zero open blockers.", [scope])],
                [EvaluatedCondition.Satisfied("lock.ready", "No open queries, missing items, or error findings.", scope)],
                []);
            return Task.FromResult<LockReadinessSnapshot?>(new LockReadinessSnapshot(
                study,
                "CAD-101",
                scope,
                ReadyForLock: true,
                CanFreeze: true,
                OpenQueryCount: 0,
                MissingRequiredItemCount: 0,
                Blockers: [],
                Explanation: explanation,
                OpenErrorFindingCount: 0));
        }
    }

    private sealed class FakeStudySdvAlerts : IStudySdvAlertReader
    {
        private static readonly Guid SampleAlertId = Guid.Parse("77777777-8888-4999-aaaa-bbbbbbbbbbbb");

        public Task<IReadOnlyList<StudySdvAlertView>> ListForStudyAsync(
            TenantId tenant,
            StudyId study,
            string? knownStudyCode = null,
            CancellationToken cancellationToken = default)
        {
            if (study != StudyId.FromGuid(Study.Id))
            {
                return Task.FromResult<IReadOnlyList<StudySdvAlertView>>([]);
            }

            return Task.FromResult<IReadOnlyList<StudySdvAlertView>>(
            [
                new StudySdvAlertView(
                    SampleAlertId,
                    study,
                    StudySiteId.FromGuid(StudySite.Id),
                    EnrollmentId.FromGuid(Enrollment.Id),
                    CollectionInstanceId.FromGuid(FormInstance.Id),
                    ObservationId.FromGuid(RecordedObservation.Id),
                    knownStudyCode ?? "CAD-101",
                    "SITE-017",
                    "017-010",
                    "form.vitals",
                    "visit.screening",
                    "sbp",
                    1,
                    2,
                    "cra@sponsor.test",
                    GrantedFrom.AddDays(2),
                    "crc@site017.test",
                    GrantedFrom.AddDays(3),
                    "Corrected transcription error against source flowsheet",
                    "Previously verified field modified on Subject 017-010 - Visit visit.screening (sbp)"),
            ]);
        }
    }
}
