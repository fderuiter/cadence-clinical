using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Verifies canonical Kernel delegation and training consolidation:
/// 1. All 8 authority and qualification commands + bitemporal read routes are mounted on <c>MapKernelTrust()</c>.
/// 2. All 6 capture capabilities deny untrained staff with <c>K-AUTH-6</c> and unlock once an artifact-backed
///    <c>Qualification</c> (<c>protocol_training</c>) is recorded.
/// 3. Capability denial <c>ProblemExplanation</c> payloads redact unauthorized <see cref="NodeRef"/>s on the wire
///    while preserving unredacted <see cref="AuthorizationDecision"/> entries in the GxP audit log.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelTrustAndTrainingConsolidationTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly OrganizationId Sponsor = OrganizationId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly StudySiteId Site = StudySiteId.New();
    private static readonly Actor Pi = Actor.Person("pi@site.test");
    private static readonly Actor Crc = Actor.Person("crc@site.test");

    private static readonly string[] SixCaptureCapabilities =
    [
        "participant.screen",
        "observation.record",
        "observation.correct",
        "collection.open",
        "collection.record",
        "collection.sign",
    ];

    [Fact(DisplayName = "MapKernelTrust_MountsAllEightAuthorityAndQualificationCommandRoutesAndBitemporalReads")]
    [GxPRequirement("PRD-TRN-004")]
    public void MapKernelTrustMountsAllEightAuthorityAndQualificationCommandRoutesAndBitemporalReads()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<Cadence.Kernel.Time.IClock>(new FakeClock(T0));
        builder.Services.AddSingleton<ICurrentActor>(new FixedCurrentActor(Pi));
        builder.Services.AddSingleton<IKernelChangeReason>(new StubChangeReason());
        builder.Services.AddSingleton<INodeVisibility>(new StubNodeVisibility());
        builder.Services.AddSingleton<IKernelCommandSender>(new StubCommandSender());
        using var app = builder.Build();

        var kernel = app.MapGroup("/api/v1/kernel");
        kernel.MapKernelTrust();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .ToHashSet(StringComparer.Ordinal);

        string[] expectedRoutes =
        [
            "GET /api/v1/kernel/roles",
            "POST /api/v1/kernel/roles",
            "GET /api/v1/kernel/grants",
            "POST /api/v1/kernel/grants",
            "POST /api/v1/kernel/grants/{grantRef}/delegate",
            "POST /api/v1/kernel/grants/{grantRef}/revoke",
            "GET /api/v1/kernel/qualifications",
            "POST /api/v1/kernel/qualifications",
            "POST /api/v1/kernel/qualifications/{qualificationRef}/revoke",
            "GET /api/v1/kernel/qualification-requirements",
            "POST /api/v1/kernel/qualification-requirements",
            "POST /api/v1/kernel/qualification-requirements/{requirementRef}/retire",
        ];

        routes.Should().Contain(expectedRoutes);
    }

    [Fact(DisplayName = "AllSixCaptureCapabilities_DenyUntrainedStaffWithKAuth6_AndUnlockAfterTrainingRecordQualification")]
    [GxPRequirement("PRD-TRN-002")]
    [GxPRequirement("PRD-TRN-004")]
    [KernelRule("K-AUTH-6")]
    public async Task AllSixCaptureCapabilitiesDenyUntrainedStaffWithKAuth6AndUnlockAfterTrainingRecordQualification()
    {
        var ct = TestContext.Current.CancellationToken;

        // 1. Principal Investigator holds site grant withGrantOption: true and delegates capture role to CRC.
        var crcRole = Role.Define(
            RoleId.New(),
            Tenant,
            "SITE_CRC",
            "Clinical Research Coordinator",
            SixCaptureCapabilities).Value!;

        var piGrant = Grant.Issue(
            GrantId.New(),
            Tenant,
            Pi,
            capability: null,
            roleId: crcRole.Id,
            scope: AuthorityScope.StudySite(Site),
            validFrom: T0,
            validTo: null,
            withGrantOption: true,
            parentGrantId: null,
            issuedBy: Actor.Person("sponsor@trial.test"),
            recordedAt: T0).Value!;

        var delegatedCrcGrant = Grant.Issue(
            GrantId.New(),
            Tenant,
            Crc,
            capability: null,
            roleId: crcRole.Id,
            scope: AuthorityScope.StudySite(Site),
            validFrom: T0,
            validTo: null,
            withGrantOption: false,
            parentGrantId: piGrant.Id,
            issuedBy: Pi,
            recordedAt: T0).Value!;

        var protocolVersion = ProtocolVersionId.New();
        var requirements = SixCaptureCapabilities
            .Select(cap => QualificationRequirement.Define(
                QualificationRequirementId.New(),
                Tenant,
                cap,
                "protocol_training",
                protocolVersion,
                AuthorityScope.Study(Study),
                validFrom: T0,
                validTo: null,
                definedBy: Pi,
                recordedAt: T0).Value!)
            .ToList();

        var untrainedStore = new InMemoryCaptureAuthorityStore([piGrant, delegatedCrcGrant], [crcRole], requirements, []);
        var evaluator = new AuthorityEvaluator(untrainedStore, new KernelAuthorityOptions(new HashSet<string>(["svc:platform"])));
        var siteRef = NodeRef.For(Site);

        foreach (string capToken in SixCaptureCapabilities)
        {
            var decision = await evaluator.EvaluateAsync(
                new AuthorizationRequest(Crc, Tenant, new Capability(capToken, VerbClass.Execute), siteRef, T0, T0),
                ct);

            decision.Permitted.Should().BeFalse($"untrained staff must be denied {capToken}");
            decision.Conditions.Should().Contain(
                c => !c.Met && c.Code == AuthorityEvaluator.QualificationCondition,
                $"denial on {capToken} must surface K-AUTH-6 qualification condition");
        }

        // 2. Register TrainingRecord artifact (ADR-2459) and record protocol_training Qualification -> all 6 capabilities unlock!
        var trainingArtifact = Artifact.Register(
            ArtifactId.New(),
            Tenant,
            ArtifactKind.TrainingRecord,
            Study,
            "TMF 05.02.04 Certificate - Protocol V1.0",
            ArtifactVersionId.New(),
            new ArtifactContent(
                "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                4096,
                "application/pdf",
                "etmf://05.02.04/DOC-101"),
            recordedAt: T0).Value!;

        var qualification = Qualification.Record(
            QualificationId.New(),
            Tenant,
            Crc,
            QualificationKind.ProtocolTraining,
            "protocol_training",
            protocolVersion,
            trainingArtifact.LatestVersion.Id,
            validFrom: T0,
            validTo: null,
            recordedBy: Pi,
            recordedAt: T0).Value!;

        var trainedStore = new InMemoryCaptureAuthorityStore([piGrant, delegatedCrcGrant], [crcRole], requirements, [qualification]);
        var trainedEvaluator = new AuthorityEvaluator(trainedStore, new KernelAuthorityOptions(new HashSet<string>(["svc:platform"])));

        foreach (string capToken in SixCaptureCapabilities)
        {
            var decision = await trainedEvaluator.EvaluateAsync(
                new AuthorizationRequest(Crc, Tenant, new Capability(capToken, VerbClass.Execute), siteRef, T0, T0),
                ct);

            decision.Permitted.Should().BeTrue($"trained staff holding TrainingRecord qualification must exercise {capToken}");
        }
    }

    [Fact(DisplayName = "CapabilityDenial_MasksUnauthorizedNodeRefsOnWire_WhilePreservingUnredactedAuditDenial")]
    [GxPRequirement("PRD-TRN-002")]
    public async Task CapabilityDenialMasksUnauthorizedNodeRefsOnWireWhilePreservingUnredactedAuditDenial()
    {
        var ct = TestContext.Current.CancellationToken;
        var unauthorizedEnrollment = EnrollmentId.New();
        var unauthorizedEnrollmentRef = NodeRef.For(unauthorizedEnrollment);
        var hiddenRequirementRef = NodeRef.For(QualificationRequirementId.New());

        var store = new InMemoryCaptureAuthorityStore([], [], [], []);
        var evaluator = new AuthorityEvaluator(store, new KernelAuthorityOptions(new HashSet<string>(["svc:platform"])));
        var authorizer = new KernelAuthorization(
            evaluator,
            new FixedCurrentActor(Crc),
            KernelCommandCatalog.Kernel,
            new FakeClock(T0),
            NullLogger<KernelAuthorization>.Instance,
            store);

        var uow = new CapturingDenialUnitOfWorkFactory();
        await using var sp = new ServiceCollection()
            .AddSingleton<Cadence.Kernel.Time.IClock>(new FakeClock(T0))
            .AddSingleton<ICurrentActor>(new FixedCurrentActor(Crc))
            .AddSingleton<IAuthorizer>(authorizer)
            .AddSingleton<IKernelUnitOfWorkFactory>(uow)
            .AddKernelCommandPipeline()
            .BuildServiceProvider();

        var sender = sp.GetRequiredService<IKernelCommandSender>();
        var command = new RecordObservation(
            Tenant,
            ObservationId.New(),
            unauthorizedEnrollment,
            ConceptRef.New(),
            new QuantityValue(120m, "mm[Hg]"),
            T0,
            "idem-unauth-obs",
            "Attempt observation on unauthorized enrollment");

        var outcome = await sender.SendAsync(command, cancellationToken: ct);
        outcome.IsSuccess.Should().BeFalse();
        outcome.Error!.HttpStatus.Should().Be(403);
        outcome.Error.Explanation.Should().NotBeNull();

        // 1. GxP audit record preserves unredacted target/decision.
        uow.RecordedDenials.Should().ContainSingle();
        uow.RecordedDenials[0].Permitted.Should().BeFalse();
        uow.RecordedDenials[0].Explain(nameof(RecordObservation), unauthorizedEnrollmentRef).Subject.Should().Be(unauthorizedEnrollmentRef);
        uow.RecordedExecutionTargets.Should().ContainSingle().Which.Should().Contain(unauthorizedEnrollment.Value.ToString());
        outcome.Error.Explanation!.Subject.Should().Be(unauthorizedEnrollmentRef);

        // 2. Wire ProblemExplanation masks both structured Subject/Evidence and inline NodeRef UUIDs in free text.
        var richExplanation = new Explanation(
            unauthorizedEnrollmentRef,
            $"Denied observation.record on {unauthorizedEnrollmentRef} due to unmet {hiddenRequirementRef}.",
            "kernel.authorization",
            [new ExplanationStep($"Checked scope of {unauthorizedEnrollmentRef}.", [unauthorizedEnrollmentRef])],
            [new EvaluatedCondition("K-AUTH-6", $"Missing qualification for {hiddenRequirementRef}.", false, [hiddenRequirementRef])],
            [new NextAction($"Complete training for {unauthorizedEnrollmentRef}.", "qualification.record", unauthorizedEnrollmentRef)]);

        var masks = new Dictionary<NodeRef, MaskReason>
        {
            [unauthorizedEnrollmentRef] = MaskReason.NotPermitted,
            [hiddenRequirementRef] = MaskReason.NotPermitted,
        };

        var wire = ProblemExplanation.From(richExplanation, masks);
        wire.Subject.Should().Be("masked:enrollment:NotPermitted");
        wire.Claim.Should().NotContain(unauthorizedEnrollment.Value.ToString())
            .And.NotContain(hiddenRequirementRef.Id.ToString())
            .And.Contain("masked:enrollment:NotPermitted");
        wire.Conditions[0].Evidence.Should().Equal("masked:qualification_requirement:NotPermitted");
        wire.NextActions[0].Target.Should().Be("masked:enrollment:NotPermitted");
    }

    private sealed class FixedCurrentActor(Actor actor) : ICurrentActor
    {
        public Actor? Actor => actor;
    }

    private sealed class StubChangeReason : IKernelChangeReason
    {
        public string? Read(Microsoft.AspNetCore.Http.HttpContext context) => "Test reason";
    }

    private sealed class StubNodeVisibility : INodeVisibility
    {
        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant,
            Actor reader,
            IReadOnlyCollection<NodeRef> nodes,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(new Dictionary<NodeRef, MaskReason>());
    }

    private sealed class StubCommandSender : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingDenialUnitOfWorkFactory : IKernelUnitOfWorkFactory, IKernelCommandTransaction
    {
        public List<AuthorizationDecision> RecordedDenials { get; } = [];
        public List<string> RecordedExecutionTargets { get; } = [];

        public IKernelUnitOfWork Work => throw new NotSupportedException();

        public IKernelCommandTransaction Begin(CommandExecution execution)
        {
            RecordedExecutionTargets.Add(System.Text.Json.JsonSerializer.Serialize(execution.Command, execution.Command.GetType()));
            return this;
        }

        public Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IdempotentOutcome?>(null);

        public void RecordPermit(AuthorizationDecision permit)
        {
        }

        public void RecordProposalExecution(NodeRef proposal)
        {
        }

        public Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken)
        {
            RecordedDenials.Add(denial);
            return Task.CompletedTask;
        }

        public Task RecordRefusalAsync(KernelError refusal, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken) =>
            Task.FromResult(CommitOutcome.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemoryCaptureAuthorityStore(
        IReadOnlyList<Grant> grants,
        IReadOnlyList<Role> definedRoles,
        IReadOnlyList<QualificationRequirement> requirements,
        IReadOnlyList<Qualification> qualifications) : IAuthorityStore
    {
        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([.. grants.Where(g => g.Grantee == actor)]);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult(grants.SingleOrDefault(g => g.Id == grant));

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([.. definedRoles.Where(r => roles.Contains(r.Id))]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            Task.FromResult(resource.Kind switch
            {
                NodeKinds.Study => new ScopePath(Sponsor, Study),
                NodeKinds.StudySite => new ScopePath(Sponsor, Study, "US", Site),
                NodeKinds.Enrollment => new ScopePath(Sponsor, Study, "US", Site),
                _ => ScopePath.TenantWide,
            });

        public Task<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>> InspectEvidenceNodesAsync(
            TenantId tenant,
            IReadOnlyCollection<NodeRef> nodes,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>>(new Dictionary<NodeRef, EvidenceNodeClassification>());

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([new ScopePath(Sponsor, Study, "US", Site)]);

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([.. qualifications.Where(q => q.Holder == actor)]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([.. requirements.Where(r => r.Capability == capability)]);

        public Task<IReadOnlyDictionary<ProtocolVersionId, int>> ProtocolVersionNumbersAsync(
            TenantId tenant,
            IReadOnlyCollection<ProtocolVersionId> versions,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<ProtocolVersionId, int>>(versions.ToDictionary(v => v, _ => 1));
    }
}
