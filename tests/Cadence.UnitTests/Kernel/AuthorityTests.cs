using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.References;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The authority model and its decision procedure (spec §9.3, ADR-2455): roles, grants and their bitemporal validity,
/// scopes, delegation chains, agents and platform operators. <c>KernelAuthorityPostgresTests</c> runs the same
/// procedure through the command pipeline against PostgreSQL.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuthorityTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly OrganizationId Sponsor = OrganizationId.New();
    private static readonly StudyId StudyA = StudyId.New();
    private static readonly StudyId StudyB = StudyId.New();
    private static readonly StudySiteId SiteA1 = StudySiteId.New();
    private static readonly Actor Crc = Actor.Person("crc@site.test");
    private static readonly Actor Pi = Actor.Person("pi@site.test");
    private static readonly Actor Admin = Actor.Person("admin@sponsor.test");
    private static readonly Capability Record = new("observation.record", VerbClass.Execute);
    private static readonly NodeRef SiteA1Ref = NodeRef.For(SiteA1);

    /// <summary>Scopes are well formed only with the object their level names, and each level covers what lies below it.</summary>
    [Fact(DisplayName = "AuthorityScope_ValidatesAndCoversThePathBelowIt")]
    public void AuthorityScopeValidatesAndCoversThePathBelowIt()
    {
        var path = new ScopePath(Sponsor, StudyA, "US", SiteA1);

        AuthorityScope.Tenant().Problem().Should().BeNull();
        AuthorityScope.StudyCountry(StudyA, "US").Problem().Should().BeNull();
        new AuthorityScope(ScopeLevel.Tenant, Guid.NewGuid()).Problem().Should().NotBeNull();
        new AuthorityScope(ScopeLevel.Study).Problem().Should().NotBeNull();
        new AuthorityScope(ScopeLevel.Study, Guid.Empty).Problem().Should().NotBeNull();
        AuthorityScope.StudyCountry(StudyA, "us").Problem().Should().NotBeNull();
        new AuthorityScope((ScopeLevel)42, Guid.NewGuid()).Problem().Should().NotBeNull();

        AuthorityScope.Tenant().Covers(path).Should().BeTrue();
        AuthorityScope.Organization(Sponsor).Covers(path).Should().BeTrue();
        AuthorityScope.Study(StudyA).Covers(path).Should().BeTrue();
        AuthorityScope.StudyCountry(StudyA, "US").Covers(path).Should().BeTrue();
        AuthorityScope.StudySite(SiteA1).Covers(path).Should().BeTrue();

        AuthorityScope.Study(StudyB).Covers(path).Should().BeFalse();
        AuthorityScope.StudyCountry(StudyA, "DE").Covers(path).Should().BeFalse();
        AuthorityScope.StudySite(StudySiteId.New()).Covers(path).Should().BeFalse();
        AuthorityScope.StudySite(SiteA1).Covers(new ScopePath(Sponsor, StudyA)).Should().BeFalse("a site grant does not cover the whole study");
        AuthorityScope.Study(StudyA).Covers(ScopePath.TenantWide).Should().BeFalse();
    }

    /// <summary>A role bundles well-formed capabilities, never an unblinding one (K-AUTH-4), and sorts and de-duplicates them.</summary>
    [Fact(DisplayName = "Role_Define_ValidatesAndRefusesUnblinding")]
    public void RoleDefineValidatesAndRefusesUnblinding()
    {
        Role.Define(RoleId.New(), Tenant, "crc", "CRC", ["observation.record"]).Error.Should().Be(AuthorityErrors.RoleCodeInvalid);
        Role.Define(RoleId.New(), Tenant, "CRC", " ", ["observation.record"]).Error.Should().Be(AuthorityErrors.RoleNameInvalid);
        Role.Define(RoleId.New(), Tenant, "CRC", "CRC", []).Error.Should().Be(AuthorityErrors.CapabilityInvalid);
        Role.Define(RoleId.New(), Tenant, "CRC", "CRC", ["Observation.Record"]).Error.Should().Be(AuthorityErrors.CapabilityInvalid);
        Role.Define(RoleId.New(), Tenant, "RTSM", "RTSM", ["randomization.assign", "enrollment.unblind"]).Error.Should().Be(AuthorityErrors.UnblindingNotBundled);
        Capability.IsUnblinding("enrollment.unblind_emergency").Should().BeTrue();
        Capability.IsUnblinding("observation.record").Should().BeFalse();

        var role = Role.Define(RoleId.New(), Tenant, "CRC", " Study coordinator ", ["query.respond", "observation.record", "query.respond"]).Value!;

        role.Name.Should().Be("Study coordinator");
        role.Capabilities.Should().Equal("observation.record", "query.respond");
    }

    /// <summary>A grant names exactly one capability or role, a valid scope and a period that ends after it starts.</summary>
    [Fact(DisplayName = "Grant_Issue_ValidatesTargetScopeAndPeriod")]
    public void GrantIssueValidatesTargetScopeAndPeriod()
    {
        Issue(capability: null).Error.Should().Be(AuthorityErrors.GrantTargetInvalid);
        Issue(role: RoleId.New()).Error.Should().Be(AuthorityErrors.GrantTargetInvalid);
        Issue(capability: "Not A Token").Error.Should().Be(AuthorityErrors.CapabilityInvalid);
        Issue(scope: new AuthorityScope(ScopeLevel.Study)).Error!.Code.Should().Be(AuthorityErrors.ScopeInvalid.Code);
        Issue(validTo: T0).Error.Should().Be(AuthorityErrors.ValidityInvalid);

        var state = Issue(validTo: T0.AddDays(30)).Value!.State();

        state.Should().BeEquivalentTo(new { Grantee = Crc, Capability = "observation.record", ValidFrom = T0, ValidTo = T0.AddDays(30), Revoked = false });
    }

    /// <summary>
    /// Validity is bitemporal: a grant is in effect only where it was valid and already recorded, and a revocation
    /// shortens it from when it is recorded without changing what was known before (K-AUTH-3).
    /// </summary>
    [Fact(DisplayName = "Grant_ValidityIsBitemporal_AndRevocationKeepsHistory")]
    public void GrantValidityIsBitemporalAndRevocationKeepsHistory()
    {
        var recordedAt = T0.AddDays(10);
        var grant = Issue(validFrom: T0, recordedAt: recordedAt).Value!;

        grant.IsInEffect(T0.AddDays(1), T0.AddDays(5)).Should().BeFalse("it was not yet recorded");
        grant.IsInEffect(T0.AddDays(1), recordedAt).Should().BeTrue("a backdated grant is valid from its start once known");
        grant.IsInEffect(T0.AddDays(-1), recordedAt).Should().BeFalse();

        grant.Revoke(T0.AddDays(20), T0.AddDays(21)).Should().BeNull();

        grant.IsInEffect(T0.AddDays(25), T0.AddDays(20)).Should().BeTrue("before the revocation was recorded it was open-ended");
        grant.IsInEffect(T0.AddDays(25), T0.AddDays(22)).Should().BeFalse();
        grant.IsInEffect(T0.AddDays(15), T0.AddDays(22)).Should().BeTrue();
        grant.State().ValidTo.Should().Be(T0.AddDays(20));
        grant.Revoke(T0.AddDays(20), T0.AddDays(23)).Should().Be(AuthorityErrors.RevocationInvalid);

        grant.Revoke(T0.AddDays(-5), T0.AddDays(24)).Should().BeNull();
        grant.State().Revoked.Should().BeTrue();
        grant.IsInEffect(T0.AddDays(15), T0.AddDays(25)).Should().BeFalse();
        grant.Revoke(T0, T0.AddDays(26)).Should().Be(AuthorityErrors.RevocationInvalid);
    }

    /// <summary>With no grant, a denial names the capability, the resource and what to obtain (K-AUTH-6).</summary>
    [Fact(DisplayName = "Evaluate_NoGrant_DeniesNamingTheMissingGrant")]
    public async Task EvaluateNoGrantDeniesNamingTheMissingGrant()
    {
        var decision = await Evaluate(new Store(), Crc, Record, SiteA1Ref);

        decision.Permitted.Should().BeFalse();
        decision.Reason.Should().Contain("observation.record");
        decision.Conditions.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Code = AuthorityEvaluator.GrantCondition,
            Met = false,
            Evidence = new[] { SiteA1Ref },
        });
    }

    /// <summary>A direct grant or a role grant permits when its scope covers the resource, and names itself as evidence.</summary>
    [Fact(DisplayName = "Evaluate_GrantOrRoleCoveringTheResource_Permits")]
    public async Task EvaluateGrantOrRoleCoveringTheResourcePermits()
    {
        var direct = Issue(scope: AuthorityScope.Study(StudyA)).Value!;
        var role = Role.Define(RoleId.New(), Tenant, "CRC", "CRC", ["observation.record"]).Value!;
        var viaRole = Issue(grantee: Pi, capability: null, role: role.Id, scope: AuthorityScope.StudyCountry(StudyA, "US")).Value!;
        var store = new Store(direct, viaRole) { Roles = [role] };

        var permit = await Evaluate(store, Crc, Record, SiteA1Ref);
        var rolePermit = await Evaluate(store, Pi, Record, SiteA1Ref);

        permit.Permitted.Should().BeTrue();
        permit.Conditions.Should().ContainSingle().Which.Evidence.Should().Equal(NodeRef.For(direct.Id));
        rolePermit.Permitted.Should().BeTrue();
        (await Evaluate(store, Pi, new Capability("query.close", VerbClass.Execute), SiteA1Ref)).Permitted.Should().BeFalse();
    }

    /// <summary>A grant for another study, an expired grant or one not yet known does not permit; the denial lists it.</summary>
    [Fact(DisplayName = "Evaluate_GrantOutOfScopeOrTime_Denies")]
    public async Task EvaluateGrantOutOfScopeOrTimeDenies()
    {
        var otherStudy = Issue(scope: AuthorityScope.Study(StudyB)).Value!;
        var expired = Issue(validFrom: T0.AddDays(-30), validTo: T0.AddDays(-1)).Value!;
        var store = new Store(otherStudy, expired);

        var decision = await Evaluate(store, Crc, Record, SiteA1Ref);

        decision.Permitted.Should().BeFalse();
        decision.Conditions.Single().Evidence.Should().Contain([NodeRef.For(otherStudy.Id), NodeRef.For(expired.Id)]);
        (await Evaluate(new Store(Issue(recordedAt: T0.AddHours(1)).Value!), Crc, Record, SiteA1Ref)).Permitted.Should().BeFalse(
            "a grant recorded after the decision time cannot authorize it (K-AUTH-3)");
    }

    /// <summary>A delegation stops authorizing when any grant above it is no longer in effect.</summary>
    [Fact(DisplayName = "Evaluate_Delegation_NeedsEveryParentInEffect")]
    public async Task EvaluateDelegationNeedsEveryParentInEffect()
    {
        var parent = Issue(grantee: Pi, withGrantOption: true).Value!;
        var delegation = Issue(parent: parent.Id).Value!;
        var store = new Store(parent, delegation);

        (await Evaluate(store, Crc, Record, SiteA1Ref, at: T0.AddDays(1))).Permitted.Should().BeTrue();

        parent.Revoke(T0.AddDays(2), T0.AddDays(2)).Should().BeNull();

        (await Evaluate(store, Crc, Record, SiteA1Ref, at: T0.AddDays(3))).Permitted.Should().BeFalse();
        (await Evaluate(store, Crc, Record, SiteA1Ref, at: T0.AddDays(1))).Permitted.Should().BeTrue("the parent was in effect then");
        (await Evaluate(new Store(delegation), Crc, Record, SiteA1Ref, at: T0.AddDays(1))).Permitted.Should().BeFalse("a missing parent is not in effect");
    }

    /// <summary>Automation agents may read and propose with a grant, but never execute or approve (spec §11).</summary>
    [Fact(DisplayName = "Evaluate_AutomationAgent_OnlyReadsAndProposes")]
    public async Task EvaluateAutomationAgentOnlyReadsAndProposes()
    {
        var agent = new Actor(ActorKind.AutomationAgent, "agent:coder");
        var store = new Store(Issue(grantee: agent, capability: "observation.record").Value!, Issue(grantee: agent, capability: "mapping.propose").Value!);

        var execute = await Evaluate(store, agent, Record, SiteA1Ref);

        execute.Permitted.Should().BeFalse();
        execute.Conditions.Single().Code.Should().Be(AuthorityEvaluator.AgentVerbCondition);
        (await Evaluate(store, agent, new Capability("mapping.propose", VerbClass.Propose), SiteA1Ref)).Permitted.Should().BeTrue();
    }

    /// <summary>Platform operators may provision tenants and issue the first grants and roles, and nothing else.</summary>
    [Fact(DisplayName = "Evaluate_PlatformOperator_OnlyBootstraps")]
    public async Task EvaluatePlatformOperatorOnlyBootstraps()
    {
        var operatorAccount = Actor.ServiceAccount("platform");
        var tenantRef = NodeRef.For(Tenant);

        (await Evaluate(new Store(), operatorAccount, new Capability("grant.issue", VerbClass.Approve), tenantRef)).Permitted.Should().BeTrue();
        (await Evaluate(new Store(), operatorAccount, Record, SiteA1Ref)).Permitted.Should().BeFalse();
        (await Evaluate(new Store(), Actor.ServiceAccount("other"), new Capability("grant.issue", VerbClass.Approve), tenantRef)).Permitted.Should().BeFalse();
        var configure = () => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddKernelAuthorityEvaluator(["platform"]);
        configure.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Only the holder of a grant in effect with the grant option may delegate under it, and the delegator may revoke
    /// the delegation it signed.
    /// </summary>
    [Fact(DisplayName = "Evaluate_DelegateAndRevoke_FollowTheGrantHolder")]
    public async Task EvaluateDelegateAndRevokeFollowTheGrantHolder()
    {
        var withOption = Issue(grantee: Pi, withGrantOption: true).Value!;
        var withoutOption = Issue(grantee: Pi).Value!;
        var delegation = Issue(parent: withOption.Id).Value!;
        var store = new Store(withOption, withoutOption, delegation);
        var delegate_ = new Capability("grant.delegate", VerbClass.Execute);
        var revoke = new Capability("grant.revoke", VerbClass.Approve);

        (await Evaluate(store, Pi, delegate_, NodeRef.For(withOption.Id))).Permitted.Should().BeTrue();
        (await Evaluate(store, Pi, delegate_, NodeRef.For(withoutOption.Id))).Permitted.Should().BeFalse();
        (await Evaluate(store, Crc, delegate_, NodeRef.For(withOption.Id))).Permitted.Should().BeFalse();
        (await Evaluate(store, Pi, revoke, NodeRef.For(delegation.Id))).Conditions.Single().Code.Should().Be(AuthorityEvaluator.DelegatorCondition);
        (await Evaluate(store, Admin, revoke, NodeRef.For(delegation.Id))).Permitted.Should().BeFalse();
    }

    private static CommandResult<Grant> Issue(
        Actor? grantee = null,
        string? capability = "observation.record",
        RoleId? role = null,
        AuthorityScope? scope = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null,
        bool withGrantOption = false,
        GrantId? parent = null,
        DateTimeOffset? recordedAt = null) =>
        Grant.Issue(
            GrantId.New(), Tenant, grantee ?? Crc, capability, role, scope ?? AuthorityScope.Study(StudyA),
            validFrom ?? T0, validTo, withGrantOption, parent, Admin, recordedAt ?? T0);

    private static ValueTask<AuthorizationDecision> Evaluate(Store store, Actor actor, Capability capability, NodeRef resource, DateTimeOffset? at = null)
    {
        var evaluator = new AuthorityEvaluator(store, new KernelAuthorityOptions(new HashSet<string>(["svc:platform"])));
        var when = at ?? T0;
        return evaluator.EvaluateAsync(new AuthorizationRequest(actor, Tenant, capability, resource, when, when), CancellationToken.None);
    }

    private sealed class Store(params Grant[] grants) : IAuthorityStore
    {
        public IReadOnlyList<Role> Roles { get; init; } = [];

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([.. grants.Where(g => g.Grantee == actor)]);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult(grants.SingleOrDefault(g => g.Id == grant));

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([.. Roles.Where(r => roles.Contains(r.Id))]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            Task.FromResult(resource.Kind switch
            {
                NodeKinds.StudySite => new ScopePath(Sponsor, StudyA, "US", StudySiteId.FromGuid(resource.Id)),
                NodeKinds.Grant => grants.Single(g => g.Id.Value == resource.Id).Scope.Level == ScopeLevel.Study
                    ? new ScopePath(Sponsor, StudyA)
                    : ScopePath.TenantWide,
                _ => ScopePath.TenantWide,
            });

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([]);
    }
}
