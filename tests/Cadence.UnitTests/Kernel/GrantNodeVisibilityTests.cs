using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Grant-backed visibility (ADR-2457, #4781): what a reader sees follows the scopes of the grants it holds in effect
/// now, the objects above them on the scope path, the study sites a participant or site reaches, and tenant reference
/// data. <c>KernelGrantVisibilityPostgresTests</c> runs it over the kernel's own scope paths.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GrantNodeVisibilityTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly OrganizationId Sponsor = OrganizationId.New();
    private static readonly OrganizationId Institution = OrganizationId.New();
    private static readonly StudyId StudyA = StudyId.New();
    private static readonly StudyId StudyB = StudyId.New();
    private static readonly StudySiteId SiteA1 = StudySiteId.New();
    private static readonly StudySiteId SiteA2 = StudySiteId.New();
    private static readonly StudySiteId SiteB1 = StudySiteId.New();
    private static readonly Actor Crc = Actor.Person("crc@site.test");
    private static readonly Actor Admin = Actor.Person("admin@sponsor.test");

    private static readonly NodeRef StudyARef = NodeRef.For(StudyA);
    private static readonly NodeRef StudyBRef = NodeRef.For(StudyB);
    private static readonly NodeRef SponsorRef = NodeRef.For(Sponsor);
    private static readonly NodeRef SiteA1Ref = NodeRef.For(SiteA1);
    private static readonly NodeRef SiteA2Ref = NodeRef.For(SiteA2);
    private static readonly NodeRef SiteB1Ref = NodeRef.For(SiteB1);
    private static readonly NodeRef ProtocolA = NodeRef.For(ProtocolVersionId.New());
    private static readonly NodeRef CountryEffectivityA = NodeRef.For(EffectivityId.New());
    private static readonly NodeRef EnrollmentA1 = NodeRef.For(EnrollmentId.New());
    private static readonly NodeRef EnrollmentA2 = NodeRef.For(EnrollmentId.New());
    private static readonly NodeRef ParticipantA1 = NodeRef.For(ParticipantId.New());
    private static readonly NodeRef ParticipantA2 = NodeRef.For(ParticipantId.New());
    private static readonly NodeRef HospitalSite = NodeRef.For(SiteId.New());
    private static readonly NodeRef Observation = NodeRef.For(ObservationId.New());
    private static readonly NodeRef BlindedObservationA1 = NodeRef.For(ObservationId.New());
    private static readonly NodeRef UnblindedObservationA1 = NodeRef.For(ObservationId.New());
    private static readonly NodeRef BlindedObservationA2 = NodeRef.For(ObservationId.New());
    private static readonly NodeRef BlindedFindingA1 = NodeRef.For(ValidationFindingId.New());
    private static readonly NodeRef UnblindedFindingA1 = NodeRef.For(ValidationFindingId.New());
    private static readonly NodeRef Concept = NodeRef.For(ConceptId.New());
    private static readonly NodeRef Colleague = NodeRef.For(PersonId.New());
    private static readonly Actor Pharmacist = Actor.Person("pharmacist@site.test");
    private static readonly Actor McpAgent = Actor.AutomationAgent("mcp");

    private static readonly NodeRef[] Everything =
    [
        StudyARef, StudyBRef, SponsorRef, SiteA1Ref, SiteA2Ref, SiteB1Ref, ProtocolA, CountryEffectivityA, EnrollmentA1, EnrollmentA2,
        ParticipantA1, ParticipantA2, HospitalSite, Observation, Concept, Colleague,
    ];

    /// <summary>
    /// A coordinator granted at study site A1 sees A1 and what is in it, its participant, the study, sponsor and US
    /// effectivity above it, its hospital, and reference data. It does not see site A2, study B, their objects, or an
    /// object the kernel places on no scope path.
    /// </summary>
    [Fact(DisplayName = "Visibility_StudySiteGrant_SeesItsSiteAndWhatIsAboveIt")]
    public async Task VisibilityStudySiteGrantSeesItsSiteAndWhatIsAboveIt()
    {
        var store = new Store(Issue(Crc, AuthorityScope.StudySite(SiteA1)));

        var masked = await MaskAsync(store, Crc);

        masked.Keys.Should().BeEquivalentTo([StudyBRef, SiteA2Ref, SiteB1Ref, EnrollmentA2, ParticipantA2, Observation]);
        masked.Values.Should().AllSatisfy(r => r.Should().Be(MaskReason.NotPermitted));
    }

    /// <summary>A study grant covers the whole study and nothing of another; a tenant grant covers everything.</summary>
    [Fact(DisplayName = "Visibility_StudyAndTenantGrants_CoverTheirScope")]
    public async Task VisibilityStudyAndTenantGrantsCoverTheirScope()
    {
        var study = await MaskAsync(new Store(Issue(Admin, AuthorityScope.Study(StudyA))), Admin);
        study.Keys.Should().BeEquivalentTo([StudyBRef, SiteB1Ref, Observation]);

        var organization = await MaskAsync(new Store(Issue(Admin, AuthorityScope.Organization(Sponsor))), Admin);
        organization.Keys.Should().BeEquivalentTo([Observation]);

        var tenant = await MaskAsync(new Store(Issue(Admin, AuthorityScope.Tenant())), Admin);
        tenant.Should().BeEmpty();
    }

    /// <summary>
    /// Without a grant in effect now nothing is visible, reference data included: a reader with no grant, one whose grant
    /// has not started or was revoked, and one whose delegation hangs under a revoked grant.
    /// </summary>
    [Fact(DisplayName = "Visibility_NoGrantInEffect_MasksEverything")]
    public async Task VisibilityNoGrantInEffectMasksEverything()
    {
        (await MaskAsync(new Store(), Crc)).Keys.Should().BeEquivalentTo(Everything);

        var future = Issue(Crc, AuthorityScope.Tenant(), validFrom: T0.AddDays(1));
        (await MaskAsync(new Store(future), Crc)).Keys.Should().BeEquivalentTo(Everything);

        var revoked = Issue(Crc, AuthorityScope.Tenant());
        revoked.Revoke(T0.AddMinutes(1), T0.AddMinutes(1)).Should().BeNull();
        (await MaskAsync(new Store(revoked), Crc, T0.AddMinutes(2))).Keys.Should().BeEquivalentTo(Everything);

        var parent = Issue(Admin, AuthorityScope.Study(StudyA), withGrantOption: true);
        var delegation = Issue(Crc, AuthorityScope.StudySite(SiteA1), parent: parent.Id);
        (await MaskAsync(new Store(parent, delegation), Crc)).Should().ContainKey(SiteA2Ref).And.NotContainKey(SiteA1Ref);
        parent.Revoke(T0.AddMinutes(1), T0.AddMinutes(1)).Should().BeNull();
        (await MaskAsync(new Store(parent, delegation), Crc, T0.AddMinutes(2))).Keys.Should().BeEquivalentTo(Everything);
    }

    [Fact(DisplayName = "Visibility_IsAtOrAbove_FollowsTheScopePath")]
    public void VisibilityIsAtOrAboveFollowsTheScopePath()
    {
        var site = new ScopePath(Sponsor, StudyA, "US", SiteA1);

        GrantNodeVisibility.IsAtOrAbove(new ScopePath(Sponsor), site).Should().BeTrue();
        GrantNodeVisibility.IsAtOrAbove(new ScopePath(Sponsor, StudyA), site).Should().BeTrue();
        GrantNodeVisibility.IsAtOrAbove(new ScopePath(Sponsor, StudyA, "US"), site).Should().BeTrue();
        GrantNodeVisibility.IsAtOrAbove(site, site).Should().BeTrue();

        GrantNodeVisibility.IsAtOrAbove(ScopePath.TenantWide, site).Should().BeFalse("a path on no scope is not above anything");
        GrantNodeVisibility.IsAtOrAbove(new ScopePath(Sponsor, StudyA, "DE"), site).Should().BeFalse();
        GrantNodeVisibility.IsAtOrAbove(new ScopePath(Sponsor, StudyB), site).Should().BeFalse();
        GrantNodeVisibility.IsAtOrAbove(new ScopePath(Sponsor, StudyA, "US", SiteA2), site).Should().BeFalse();
        GrantNodeVisibility.IsAtOrAbove(site, new ScopePath(Sponsor, StudyA)).Should().BeFalse("what is below is not above");
    }

    /// <summary>
    /// A tenant-wide <c>observation.record</c> grant does not bypass treatment blinding (<c>K-AUTH-4</c>): blinded
    /// observations and validation findings return <see cref="MaskReason.Blinded"/> while non-blinded ones remain visible.
    /// </summary>
    [Fact(DisplayName = "Visibility_TenantObservationGrant_ReturnsBlindedForBlindedEvidenceNodes")]
    public async Task VisibilityTenantObservationGrantReturnsBlindedForBlindedEvidenceNodes()
    {
        var store = new Store(Issue(Crc, AuthorityScope.Tenant()));
        var visibility = new GrantNodeVisibility(store, new FakeClock(T0.AddSeconds(1)));

        NodeRef[] nodes =
        [
            StudyARef,
            EnrollmentA1,
            BlindedObservationA1,
            UnblindedObservationA1,
            BlindedFindingA1,
            UnblindedFindingA1,
        ];

        var masked = await visibility.MaskAsync(Tenant, Crc, nodes, CancellationToken.None);

        masked.Should().HaveCount(2);
        masked[BlindedObservationA1].Should().Be(MaskReason.Blinded);
        masked[BlindedFindingA1].Should().Be(MaskReason.Blinded);
    }

    /// <summary>
    /// Scope precedes blinding: an out-of-scope blinded observation is masked with <see cref="MaskReason.NotPermitted"/>,
    /// an in-scope blinded observation without <c>enrollment.unblind</c> is masked with <see cref="MaskReason.Blinded"/>,
    /// and a standalone <c>enrollment.unblind</c> grant unblinds within its scope only.
    /// </summary>
    [Fact(DisplayName = "Visibility_BlindingFollowsScopePrecedence_AndStandaloneUnblindGrant")]
    public async Task VisibilityBlindingFollowsScopePrecedenceAndStandaloneUnblindGrant()
    {
        var crcSiteGrant = Issue(Crc, AuthorityScope.StudySite(SiteA1));
        var storeWithoutUnblind = new Store(crcSiteGrant);
        var visibilityWithoutUnblind = new GrantNodeVisibility(storeWithoutUnblind, new FakeClock(T0.AddSeconds(1)));

        var crcMasks = await visibilityWithoutUnblind.MaskAsync(
            Tenant,
            Crc,
            [BlindedObservationA1, UnblindedObservationA1, BlindedObservationA2, BlindedFindingA1],
            CancellationToken.None);

        crcMasks[BlindedObservationA1].Should().Be(MaskReason.Blinded);
        crcMasks[BlindedFindingA1].Should().Be(MaskReason.Blinded);
        crcMasks[BlindedObservationA2].Should().Be(MaskReason.NotPermitted);
        crcMasks.Should().NotContainKey(UnblindedObservationA1);

        var pharmacistStudyRead = Issue(Pharmacist, AuthorityScope.Study(StudyA));
        var pharmacistSiteUnblind = IssueUnblind(Pharmacist, AuthorityScope.StudySite(SiteA1));
        var storeWithSiteUnblind = new Store(pharmacistStudyRead, pharmacistSiteUnblind);
        var visibilityWithSiteUnblind = new GrantNodeVisibility(storeWithSiteUnblind, new FakeClock(T0.AddSeconds(1)));

        var pharmacistMasks = await visibilityWithSiteUnblind.MaskAsync(
            Tenant,
            Pharmacist,
            [BlindedObservationA1, BlindedObservationA2, BlindedFindingA1],
            CancellationToken.None);

        pharmacistMasks.Should().ContainSingle();
        pharmacistMasks[BlindedObservationA2].Should().Be(MaskReason.Blinded);
    }

    /// <summary>
    /// Under <c>K-AI-6</c> and <c>K-AUTH-5</c>, an automation agent sees blinded nodes only when both the agent and
    /// <c>RequestedBy</c> person hold active <c>enrollment.unblind</c> grants and satisfy any required qualifications.
    /// </summary>
    [Fact(DisplayName = "Visibility_UnblindingEnforcesAgentRequesterIntersection_AndQualifications")]
    public async Task VisibilityUnblindingEnforcesAgentRequesterIntersectionAndQualifications()
    {
        NodeRef[] nodes = [BlindedObservationA1, BlindedFindingA1, UnblindedObservationA1];

        // Agent holds enrollment.unblind, requester lacks it -> Blinded.
        var agentOnlyUnblind = new Store(
            Issue(McpAgent, AuthorityScope.Tenant()),
            IssueUnblind(McpAgent, AuthorityScope.Tenant()),
            Issue(Crc, AuthorityScope.StudySite(SiteA1)));
        var maskedWhenRequesterBlinded = await new GrantNodeVisibility(agentOnlyUnblind, new FakeClock(T0.AddSeconds(1)))
            .MaskAsync(Tenant, McpAgent.ActingFor(Crc), nodes, CancellationToken.None);
        maskedWhenRequesterBlinded.Keys.Should().BeEquivalentTo([BlindedObservationA1, BlindedFindingA1]);
        maskedWhenRequesterBlinded.Values.Should().AllSatisfy(r => r.Should().Be(MaskReason.Blinded));

        // Requester holds enrollment.unblind, agent lacks it -> Blinded.
        var requesterOnlyUnblind = new Store(
            Issue(McpAgent, AuthorityScope.Tenant()),
            Issue(Pharmacist, AuthorityScope.StudySite(SiteA1)),
            IssueUnblind(Pharmacist, AuthorityScope.StudySite(SiteA1)));
        var maskedWhenAgentBlinded = await new GrantNodeVisibility(requesterOnlyUnblind, new FakeClock(T0.AddSeconds(1)))
            .MaskAsync(Tenant, McpAgent.ActingFor(Pharmacist), nodes, CancellationToken.None);
        maskedWhenAgentBlinded.Keys.Should().BeEquivalentTo([BlindedObservationA1, BlindedFindingA1]);
        maskedWhenAgentBlinded.Values.Should().AllSatisfy(r => r.Should().Be(MaskReason.Blinded));

        // Both hold enrollment.unblind, qualification required: missing or expired qualification -> Blinded; active -> visible.
        var evidenceId = ArtifactVersionId.New();
        var requirement = QualificationRequirement.Define(
            QualificationRequirementId.New(), Tenant, "enrollment.unblind", "unblind_cert", null, AuthorityScope.Study(StudyA), T0, null, Admin, T0).Value!;
        var activeAgentQualification = Qualification.Record(
            QualificationId.New(), Tenant, McpAgent, QualificationKind.Training, "unblind_cert", null, evidenceId, T0, T0.AddDays(30), Admin, T0).Value!;
        var expiredPharmacistQualification = Qualification.Record(
            QualificationId.New(), Tenant, Pharmacist, QualificationKind.Training, "unblind_cert", null, evidenceId, T0.AddDays(-10), T0.AddSeconds(-1), Admin, T0.AddDays(-10)).Value!;

        var storeExpiredQualification = new Store(
            [
                Issue(McpAgent, AuthorityScope.Tenant()),
                IssueUnblind(McpAgent, AuthorityScope.Tenant()),
                Issue(Pharmacist, AuthorityScope.StudySite(SiteA1)),
                IssueUnblind(Pharmacist, AuthorityScope.StudySite(SiteA1)),
            ],
            [activeAgentQualification, expiredPharmacistQualification],
            [requirement]);
        var maskedExpired = await new GrantNodeVisibility(storeExpiredQualification, new FakeClock(T0.AddSeconds(1)))
            .MaskAsync(Tenant, McpAgent.ActingFor(Pharmacist), nodes, CancellationToken.None);
        maskedExpired.Keys.Should().BeEquivalentTo([BlindedObservationA1, BlindedFindingA1]);
        maskedExpired.Values.Should().AllSatisfy(r => r.Should().Be(MaskReason.Blinded));

        var activePharmacistQualification = Qualification.Record(
            QualificationId.New(), Tenant, Pharmacist, QualificationKind.Training, "unblind_cert", null, evidenceId, T0, T0.AddDays(30), Admin, T0).Value!;
        var storeQualified = new Store(
            [
                Issue(McpAgent, AuthorityScope.Tenant()),
                IssueUnblind(McpAgent, AuthorityScope.Tenant()),
                Issue(Pharmacist, AuthorityScope.StudySite(SiteA1)),
                IssueUnblind(Pharmacist, AuthorityScope.StudySite(SiteA1)),
            ],
            [activeAgentQualification, activePharmacistQualification],
            [requirement]);
        var unmasked = await new GrantNodeVisibility(storeQualified, new FakeClock(T0.AddSeconds(1)))
            .MaskAsync(Tenant, McpAgent.ActingFor(Pharmacist), nodes, CancellationToken.None);
        unmasked.Should().BeEmpty();
    }

    private static Task<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(Store store, Actor reader, DateTimeOffset? now = null) =>
        new GrantNodeVisibility(store, new FakeClock(now ?? T0.AddSeconds(1)))
            .MaskAsync(Tenant, reader, [.. Everything, StudyARef], CancellationToken.None).AsTask();

    private static Grant Issue(
        Actor grantee, AuthorityScope scope, DateTimeOffset? validFrom = null, bool withGrantOption = false, GrantId? parent = null) =>
        Grant.Issue(GrantId.New(), Tenant, grantee, "observation.record", null, scope, validFrom ?? T0, null, withGrantOption, parent, Admin, T0).Value!;

    private static Grant IssueUnblind(Actor grantee, AuthorityScope scope) =>
        Grant.Issue(GrantId.New(), Tenant, grantee, "enrollment.unblind", null, scope, T0, null, false, null, Admin, T0).Value!;

    /// <summary>Scope paths of a small tenant: study A with US sites A1 and A2, study B with site B1, one sponsor.</summary>
    private sealed class Store(
        IReadOnlyList<Grant> grants,
        IReadOnlyList<Qualification> qualifications,
        IReadOnlyList<QualificationRequirement> requirements) : IAuthorityStore
    {
        public Store(params Grant[] grants)
            : this(grants, [], [])
        {
        }

        private static readonly ScopePath A1 = new(Sponsor, StudyA, "US", SiteA1);
        private static readonly ScopePath A2 = new(Sponsor, StudyA, "US", SiteA2);
        private static readonly ScopePath B1 = new(Sponsor, StudyB, "DE", SiteB1);

        private static readonly Dictionary<NodeRef, ScopePath> Paths = new()
        {
            [StudyARef] = new(Sponsor, StudyA),
            [StudyBRef] = new(Sponsor, StudyB),
            [SponsorRef] = new(Sponsor),
            [SiteA1Ref] = A1,
            [SiteA2Ref] = A2,
            [SiteB1Ref] = B1,
            [ProtocolA] = new(Sponsor, StudyA),
            [CountryEffectivityA] = new(Sponsor, StudyA, "US"),
            [EnrollmentA1] = A1,
            [EnrollmentA2] = A2,
            [HospitalSite] = new(Institution),
            [BlindedObservationA1] = A1,
            [UnblindedObservationA1] = A1,
            [BlindedObservationA2] = A2,
            [BlindedFindingA1] = A1,
            [UnblindedFindingA1] = A1,
        };

        private static readonly Dictionary<NodeRef, EvidenceNodeClassification> EvidenceClassifications = new()
        {
            [BlindedObservationA1] = new(A1, IsBlinded: true),
            [UnblindedObservationA1] = new(A1, IsBlinded: false),
            [BlindedObservationA2] = new(A2, IsBlinded: true),
            [BlindedFindingA1] = new(A1, IsBlinded: true),
            [UnblindedFindingA1] = new(A1, IsBlinded: false),
        };

        private static readonly Dictionary<NodeRef, ScopePath[]> Reached = new()
        {
            [ParticipantA1] = [A1],
            [ParticipantA2] = [A2],
            [HospitalSite] = [A1, B1],
        };

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([.. grants.Where(g => g.Grantee == actor)]);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult(grants.SingleOrDefault(g => g.Id == grant));

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
        {
            if (resource.Kind == NodeKinds.Grant)
            {
                var scope = grants.Single(g => g.Id.Value == resource.Id).Scope;
                return Task.FromResult(scope.Level switch
                {
                    ScopeLevel.Organization => new ScopePath(Sponsor),
                    ScopeLevel.Study => Paths[NodeRef.For(StudyId.FromGuid(scope.TargetId!.Value))],
                    ScopeLevel.StudyCountry => new ScopePath(Sponsor, StudyId.FromGuid(scope.TargetId!.Value), scope.Country),
                    ScopeLevel.StudySite => Paths[NodeRef.For(StudySiteId.FromGuid(scope.TargetId!.Value))],
                    _ => ScopePath.TenantWide,
                });
            }

            return Task.FromResult(Paths.GetValueOrDefault(resource, ScopePath.TenantWide));
        }

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>(Reached.GetValueOrDefault(node, []));

        public Task<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>> InspectEvidenceNodesAsync(
            TenantId tenant,
            IReadOnlyCollection<NodeRef> nodes,
            CancellationToken cancellationToken)
        {
            var matched = nodes
                .Select(n => new NodeRef(n.Kind, n.Id))
                .Distinct()
                .Where(EvidenceClassifications.ContainsKey)
                .ToDictionary(n => n, n => EvidenceClassifications[n]);
            return Task.FromResult<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>>(matched);
        }

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([.. qualifications.Where(q => q.Holder == actor)]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>(
                [.. requirements.Where(r => string.Equals(r.Capability, capability, StringComparison.Ordinal))]);
    }
}
