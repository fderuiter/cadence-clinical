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
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Testing;
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
    [KernelRule("K-AUTH-4")]
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
    [KernelRule("K-AUTH-3")]
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
    [KernelRule("K-AUTH-6")]
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
    [KernelRule("K-AUTH-3")]
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
    [GxPRequirement("PRD-SYS-051")]
    public async Task EvaluateAutomationAgentOnlyReadsAndProposes()
    {
        var identity = new Actor(ActorKind.AutomationAgent, "agent:coder");
        var agent = identity.ActingFor(Crc);
        var propose = new Capability("mapping.propose", VerbClass.Propose);
        var store = new Store(
            Issue(grantee: identity, capability: "observation.record").Value!,
            Issue(grantee: identity, capability: "mapping.propose").Value!,
            Issue(grantee: Crc, capability: "observation.record").Value!,
            Issue(grantee: Crc, capability: "mapping.propose").Value!);

        var execute = await Evaluate(store, agent, Record, SiteA1Ref);

        execute.Permitted.Should().BeFalse("a person holding the capability does not let the agent execute");
        execute.Conditions.Single().Code.Should().Be(AuthorityEvaluator.AgentVerbCondition);
        (await Evaluate(store, agent, propose, SiteA1Ref)).Permitted.Should().BeTrue();
    }

    /// <summary>
    /// An agent acts for exactly one requesting person and may do only what both it and that person may do (K-AI-6,
    /// ADR-2632): acting for nobody it may do nothing, and a person without the capability cannot lend it.
    /// </summary>
    [Fact(DisplayName = "Evaluate_AutomationAgent_IsBoundedByTheRequestingPerson")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task EvaluateAutomationAgentIsBoundedByTheRequestingPerson()
    {
        var identity = new Actor(ActorKind.AutomationAgent, "agent:coder");
        var propose = new Capability("mapping.propose", VerbClass.Propose);
        var otherPerson = Actor.Person("monitor@sponsor.test");
        var store = new Store(
            Issue(grantee: identity, capability: "mapping.propose").Value!,
            Issue(grantee: Crc, capability: "mapping.propose").Value!,
            Issue(grantee: otherPerson, capability: "observation.record").Value!);

        var alone = await Evaluate(store, identity, propose, SiteA1Ref);
        var forCrc = await Evaluate(store, identity.ActingFor(Crc), propose, SiteA1Ref);
        var forOther = await Evaluate(store, identity.ActingFor(otherPerson), propose, SiteA1Ref);
        var unGranted = await Evaluate(new Store(Issue(grantee: Crc, capability: "mapping.propose").Value!), identity.ActingFor(Crc), propose, SiteA1Ref);

        alone.Permitted.Should().BeFalse();
        alone.Conditions.Single().Code.Should().Be(AuthorityEvaluator.RequesterCondition);
        forCrc.Permitted.Should().BeTrue();
        forCrc.Conditions.Should().Contain(c => c.Code == AuthorityEvaluator.RequesterCondition && c.Met);
        forOther.Permitted.Should().BeFalse("the person it acts for does not hold the capability");
        forOther.Conditions.Should().Contain(c => c.Code == AuthorityEvaluator.RequesterCondition && !c.Met);
        unGranted.Permitted.Should().BeFalse("the agent's own grants still bound it");
        FluentActions.Invoking(() => Crc with { RequestedBy = otherPerson }).Should().Throw<ArgumentException>("only an agent acts for someone");
        FluentActions.Invoking(() => identity.ActingFor(identity)).Should().Throw<ArgumentException>("an agent acts for a person");
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

    /// <summary>
    /// A qualification has a lower snake_case code; protocol training names its protocol version and nothing else does;
    /// its validity ends after it starts.
    /// </summary>
    [Fact(DisplayName = "Qualification_Record_ValidatesCodeProtocolVersionAndPeriod")]
    public void QualificationRecordValidatesCodeProtocolVersionAndPeriod()
    {
        var version = ProtocolVersionId.New();

        Qualify(code: "GCP").Error.Should().Be(QualificationErrors.CodeInvalid);
        Qualify(code: "gcp__refresher").Error.Should().Be(QualificationErrors.CodeInvalid);
        Qualify(code: new string('a', Qualification.MaxCodeLength + 1)).Error.Should().Be(QualificationErrors.CodeInvalid);
        Qualify(kind: (QualificationKind)42).Error.Should().Be(QualificationErrors.CodeInvalid);
        Qualify(kind: QualificationKind.ProtocolTraining).Error.Should().Be(QualificationErrors.ProtocolVersionMismatch);
        Qualify(version: version).Error.Should().Be(QualificationErrors.ProtocolVersionMismatch);
        Qualify(validFrom: T0, validTo: T0).Error.Should().Be(QualificationErrors.ValidityInvalid);

        var recorded = Qualify(kind: QualificationKind.ProtocolTraining, code: "protocol_training", version: version, validTo: T0.AddYears(1)).Value!;
        recorded.State().Should().Be(new QualificationState(
            recorded.Id, Crc, QualificationKind.ProtocolTraining, "protocol_training", version, recorded.EvidenceArtifactVersionId, T0, T0.AddYears(1), Revoked: false));
    }

    /// <summary>
    /// A qualification's validity is bitemporal: revoking it keeps what was known before, and <c>LapsedBy</c> names the
    /// end of a validity known at the time.
    /// </summary>
    [Fact(DisplayName = "Qualification_Revoke_KeepsHistoryAndNamesTheLapse")]
    public void QualificationRevokeKeepsHistoryAndNamesTheLapse()
    {
        var gcp = Qualify(validTo: T0.AddDays(30)).Value!;

        gcp.LapsedBy(T0.AddDays(10), T0).Should().BeNull();
        gcp.LapsedBy(T0.AddDays(30), T0).Should().Be(T0.AddDays(30));
        gcp.Revoke(T0.AddDays(10), T0.AddDays(5)).Should().BeNull();

        gcp.IsInEffect(T0.AddDays(20), T0.AddDays(1)).Should().BeTrue("the revocation was not known yet");
        gcp.IsInEffect(T0.AddDays(20), T0.AddDays(6)).Should().BeFalse();
        gcp.IsInEffect(T0.AddDays(9), T0.AddDays(6)).Should().BeTrue();
        gcp.LapsedBy(T0.AddDays(20), T0.AddDays(6)).Should().Be(T0.AddDays(10));
        gcp.Revoke(T0.AddDays(10), T0.AddDays(7)).Should().Be(QualificationErrors.RevocationInvalid);
        gcp.Revoke(T0, T0.AddDays(8)).Should().BeNull();
        gcp.State().Revoked.Should().BeTrue();
        gcp.Revoke(T0, T0.AddDays(9)).Should().Be(QualificationErrors.RevocationInvalid);
    }

    /// <summary>A requirement names a well-formed capability and qualification code, a valid scope and a valid period.</summary>
    [Fact(DisplayName = "QualificationRequirement_Define_Validates")]
    public void QualificationRequirementDefineValidates()
    {
        Require(capability: "Observation.Record").Error.Should().Be(AuthorityErrors.CapabilityInvalid);
        Require(code: "GCP").Error.Should().Be(QualificationErrors.CodeInvalid);
        Require(scope: new AuthorityScope(ScopeLevel.Study)).Error!.Code.Should().Be(AuthorityErrors.ScopeInvalid.Code);
        Require(validTo: T0.AddDays(-1)).Error.Should().Be(QualificationErrors.ValidityInvalid);

        var version = ProtocolVersionId.New();
        var requirement = Require(code: "protocol_training", version: version).Value!;
        requirement.IsMetBy(Qualify(kind: QualificationKind.ProtocolTraining, code: "protocol_training", version: version).Value!).Should().BeTrue();
        requirement.IsMetBy(Qualify(kind: QualificationKind.ProtocolTraining, code: "protocol_training", version: ProtocolVersionId.New()).Value!).Should().BeFalse();
        requirement.IsMetBy(Qualify().Value!).Should().BeFalse();
        requirement.Retire(T0.AddDays(1), T0.AddDays(1)).Should().BeNull();
        requirement.IsInEffect(T0.AddDays(2), T0.AddDays(2)).Should().BeFalse();
        requirement.IsInEffect(T0, T0.AddDays(2)).Should().BeTrue();
    }

    /// <summary>
    /// With a requirement in effect over the resource, a grant alone is not enough: the evaluator denies and names the
    /// qualification to obtain, and permits with the qualification and its evidence once one is current (K-AUTH-5,
    /// K-AUTH-6).
    /// </summary>
    [Fact(DisplayName = "Evaluate_RequiredQualification_DeniesUntilHeldAndNamesIt")]
    [KernelRule("K-AUTH-5")]
    [KernelRule("K-AUTH-6")]
    public async Task EvaluateRequiredQualificationDeniesUntilHeldAndNamesIt()
    {
        var grant = Issue().Value!;
        var requirement = Require().Value!;
        var gcp = Qualify().Value!;

        var denied = await Evaluate(new Store(grant) { Requirements = [requirement] }, Crc, Record, SiteA1Ref);
        denied.Permitted.Should().BeFalse();
        denied.Reason.Should().Be("crc@site.test is not qualified to observation.record: Obtain gcp.");
        denied.Conditions.Should().Equal(
            EvaluatedCondition.Satisfied(AuthorityEvaluator.GrantCondition, $"Holds observation.record for {grant.Scope}", NodeRef.For(grant.Id)),
            EvaluatedCondition.Unsatisfied(AuthorityEvaluator.QualificationCondition, "Obtain gcp", NodeRef.For(requirement.Id)));

        var permitted = await Evaluate(new Store(grant) { Requirements = [requirement], Qualifications = [gcp] }, Crc, Record, SiteA1Ref);
        permitted.Permitted.Should().BeTrue();
        permitted.Conditions.Should().HaveCount(2).And.Contain(EvaluatedCondition.Satisfied(
            AuthorityEvaluator.QualificationCondition, "Holds current gcp", NodeRef.For(requirement.Id), NodeRef.For(gcp.Id), NodeRef.For(gcp.EvidenceArtifactVersionId)));

        var otherHolder = await Evaluate(new Store(grant, Issue(grantee: Pi).Value!) { Requirements = [requirement], Qualifications = [gcp] }, Pi, Record, SiteA1Ref);
        otherHolder.Permitted.Should().BeFalse("the qualification is the CRC's");
    }

    /// <summary>An expired qualification is named with the day it expired, so the denial says what to renew (K-AUTH-6).</summary>
    [Fact(DisplayName = "Evaluate_ExpiredQualification_DeniesNamingTheExpiry")]
    [KernelRule("K-AUTH-6")]
    public async Task EvaluateExpiredQualificationDeniesNamingTheExpiry()
    {
        var grant = Issue(validFrom: T0.AddYears(-2)).Value!;
        var requirement = Require(validFrom: T0.AddYears(-2)).Value!;
        var older = Qualify(validFrom: T0.AddYears(-2), validTo: T0.AddYears(-1)).Value!;
        var lapsed = Qualify(validFrom: T0.AddYears(-1), validTo: T0.AddDays(-3)).Value!;

        var decision = await Evaluate(new Store(grant) { Requirements = [requirement], Qualifications = [older, lapsed] }, Crc, Record, SiteA1Ref);

        decision.Permitted.Should().BeFalse();
        decision.Reason.Should().Be($"crc@site.test is not qualified to observation.record: Renew gcp; it expired on {T0.AddDays(-3):yyyy-MM-dd}.");
        decision.Conditions[^1].Evidence.Should().Equal(NodeRef.For(requirement.Id), NodeRef.For(lapsed.Id));
        var evaluator = new AuthorityEvaluator(new Store(grant) { Requirements = [requirement], Qualifications = [older, lapsed] }, new KernelAuthorityOptions(new HashSet<string>()));
        (await evaluator.EvaluateAsync(new AuthorizationRequest(Crc, Tenant, Record, SiteA1Ref, T0.AddDays(-4), T0), CancellationToken.None))
            .Permitted.Should().BeTrue("the qualification was current then");
    }

    /// <summary>
    /// Spec §14 scenario 12 in memory: a requirement for training on one protocol version is met only by training on that
    /// version, and the denial names the version.
    /// </summary>
    [Fact(DisplayName = "Evaluate_ProtocolTrainingRequirement_NeedsTheNamedVersion")]
    public async Task EvaluateProtocolTrainingRequirementNeedsTheNamedVersion()
    {
        var v1 = ProtocolVersionId.New();
        var v2 = ProtocolVersionId.New();
        var grant = Issue().Value!;
        var requirement = Require(code: "protocol_training", version: v2).Value!;
        var trainedOnV1 = Qualify(kind: QualificationKind.ProtocolTraining, code: "protocol_training", version: v1).Value!;
        var trainedOnV2 = Qualify(kind: QualificationKind.ProtocolTraining, code: "protocol_training", version: v2).Value!;

        var denied = await Evaluate(
            new Store(grant)
            {
                Requirements = [requirement],
                Qualifications = [trainedOnV1],
                VersionNumbers = new System.Collections.Generic.Dictionary<ProtocolVersionId, int> { [v2] = 2 },
            },
            Crc,
            Record,
            SiteA1Ref);
        denied.Reason.Should().EndWith(": Obtain protocol_training for protocol version 2.");
        denied.Reason.Should().NotContain(v2.Value.ToString("D"));
        denied.Conditions.Single(c => !c.Met).Evidence.Should().Contain(NodeRef.For(v2));
        (await Evaluate(new Store(grant) { Requirements = [requirement], Qualifications = [trainedOnV1, trainedOnV2] }, Crc, Record, SiteA1Ref))
            .Permitted.Should().BeTrue();
    }

    /// <summary>
    /// When Actor.Id is a UUID, denial reasons use a human-readable actor phrase rather than leaking the raw UUID and
    /// attach the corresponding Person NodeRef as condition evidence.
    /// </summary>
    [Fact(DisplayName = "Evaluate_UuidActorDenial_UsesHumanReadableLabelAndPersonNodeRefEvidence")]
    [KernelRule("K-AUTH-6")]
    public async Task EvaluateUuidActorDenialUsesHumanReadableLabelAndPersonNodeRefEvidence()
    {
        var personId = PersonId.New();
        var uuidActor = new Actor(ActorKind.Person, personId.Value.ToString("D"));

        var noGrant = await Evaluate(new Store(), uuidActor, Record, SiteA1Ref);

        noGrant.Permitted.Should().BeFalse();
        noGrant.Reason.Should().NotContain(personId.Value.ToString("D"));
        noGrant.Reason.Should().StartWith("The signed-in user holds no grant");
        noGrant.Conditions.Should().ContainSingle()
            .Which.Evidence.Should().Equal(NodeRef.For(personId));
    }

    /// <summary>
    /// Requirements apply only to their capability, within their scope and while in effect; a qualification recorded
    /// late cannot validate an action evaluated as of before it was known (K-AUTH-3).
    /// </summary>
    [Fact(DisplayName = "Evaluate_Qualification_RespectsScopeCapabilityAndKnownTime")]
    [KernelRule("K-AUTH-3")]
    public async Task EvaluateQualificationRespectsScopeCapabilityAndKnownTime()
    {
        var grant = Issue().Value!;
        var elsewhere = Require(scope: AuthorityScope.Study(StudyB)).Value!;
        var otherCapability = Require(capability: "observation.sign").Value!;
        var later = Require(validFrom: T0.AddDays(1)).Value!;
        (await Evaluate(new Store(grant) { Requirements = [elsewhere, otherCapability, later] }, Crc, Record, SiteA1Ref))
            .Conditions.Should().ContainSingle("no requirement applies here and now");

        var requirement = Require().Value!;
        var recordedLate = Qualify(validFrom: T0.AddDays(-1), recordedAt: T0.AddHours(1)).Value!;
        var evaluator = new AuthorityEvaluator(new Store(grant) { Requirements = [requirement], Qualifications = [recordedLate] }, new KernelAuthorityOptions(new HashSet<string>()));

        (await evaluator.EvaluateAsync(new AuthorizationRequest(Crc, Tenant, Record, SiteA1Ref, T0, T0), CancellationToken.None))
            .Permitted.Should().BeFalse("the training was not known when the action was decided");
        (await evaluator.EvaluateAsync(new AuthorizationRequest(Crc, Tenant, Record, SiteA1Ref, T0, T0.AddHours(2)), CancellationToken.None))
            .Permitted.Should().BeTrue();
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

    private static CommandResult<Qualification> Qualify(
        QualificationKind kind = QualificationKind.Training,
        string code = "gcp",
        ProtocolVersionId? version = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null,
        DateTimeOffset? recordedAt = null) =>
        Qualification.Record(
            QualificationId.New(), Tenant, Crc, kind, code, version, ArtifactVersionId.New(), validFrom ?? T0, validTo, Admin, recordedAt ?? T0);

    private static CommandResult<QualificationRequirement> Require(
        string capability = "observation.record",
        string code = "gcp",
        ProtocolVersionId? version = null,
        AuthorityScope? scope = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null) =>
        QualificationRequirement.Define(
            QualificationRequirementId.New(), Tenant, capability, code, version, scope ?? AuthorityScope.Study(StudyA),
            validFrom ?? T0, validTo, Admin, T0);

    private static ValueTask<AuthorizationDecision> Evaluate(Store store, Actor actor, Capability capability, NodeRef resource, DateTimeOffset? at = null)
    {
        var evaluator = new AuthorityEvaluator(store, new KernelAuthorityOptions(new HashSet<string>(["svc:platform"])));
        var when = at ?? T0;
        return evaluator.EvaluateAsync(new AuthorizationRequest(actor, Tenant, capability, resource, when, when), CancellationToken.None);
    }

    private sealed class Store(params Grant[] grants) : IAuthorityStore
    {
        public IReadOnlyList<Role> Roles { get; init; } = [];

        public IReadOnlyList<Qualification> Qualifications { get; init; } = [];

        public IReadOnlyList<QualificationRequirement> Requirements { get; init; } = [];

        public System.Collections.Generic.IReadOnlyDictionary<ProtocolVersionId, int> VersionNumbers { get; init; } =
            new System.Collections.Generic.Dictionary<ProtocolVersionId, int>();

        public Task<System.Collections.Generic.IReadOnlyDictionary<ProtocolVersionId, int>> ProtocolVersionNumbersAsync(
            TenantId tenant,
            IReadOnlyCollection<ProtocolVersionId> versions,
            CancellationToken cancellationToken) =>
            Task.FromResult<System.Collections.Generic.IReadOnlyDictionary<ProtocolVersionId, int>>(
                VersionNumbers.Where(kvp => versions.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

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

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([.. Qualifications.Where(q => q.Holder == actor)]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([.. Requirements.Where(r => r.Capability == capability)]);
    }
}
