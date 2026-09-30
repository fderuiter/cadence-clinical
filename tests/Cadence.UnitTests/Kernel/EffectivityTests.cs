using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Effectivity in the domain (spec §7.1, #4777): scopes, the lifecycle, the evidence each status accepts, the
/// bitemporal status history and the most-specific-first resolution at a study site. <c>KernelEffectivityPostgresTests</c>
/// covers the handlers, study-site activation and the database guards.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EffectivityTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;

    private static readonly TenantId Tenant = TenantId.New();

    private static readonly StudyId Study = StudyId.New();

    private static readonly ArtifactVersionId Approval = ArtifactVersionId.New();

    [Fact]
    public void Scope_Problem_AcceptsOnlyWellFormedScopes()
    {
        var site = StudySiteId.New();

        EffectivityScope.Study().Problem().Should().BeNull();
        EffectivityScope.InCountry("DE").Problem().Should().BeNull();
        EffectivityScope.At(site).Problem().Should().BeNull();

        new EffectivityScope(EffectivityLevel.Study, "DE").Problem().Should().NotBeNull();
        new EffectivityScope(EffectivityLevel.Study, StudySiteId: site).Problem().Should().NotBeNull();
        EffectivityScope.InCountry("de").Problem().Should().NotBeNull();
        EffectivityScope.InCountry("DEU").Problem().Should().NotBeNull();
        new EffectivityScope(EffectivityLevel.Country).Problem().Should().NotBeNull();
        new EffectivityScope(EffectivityLevel.Country, "DE", site).Problem().Should().NotBeNull();
        new EffectivityScope(EffectivityLevel.StudySite).Problem().Should().NotBeNull();
        new EffectivityScope(EffectivityLevel.StudySite, "DE", site).Problem().Should().NotBeNull();
        new EffectivityScope((EffectivityLevel)9).Problem().Should().NotBeNull();
    }

    [Fact]
    public void Scope_Covers_MatchesStudyCountryOrStudySite()
    {
        var siteA = StudySiteId.New();
        var siteB = StudySiteId.New();

        EffectivityScope.Study().Covers(siteA, "US").Should().BeTrue();
        EffectivityScope.InCountry("US").Covers(siteA, "US").Should().BeTrue();
        EffectivityScope.InCountry("DE").Covers(siteA, "US").Should().BeFalse();
        EffectivityScope.At(siteA).Covers(siteA, "US").Should().BeTrue();
        EffectivityScope.At(siteA).Covers(siteB, "US").Should().BeFalse();
    }

    public static TheoryData<EffectivityStatus, EffectivityStatus, bool> Transitions()
    {
        HashSet<(EffectivityStatus, EffectivityStatus)> allowed =
        [
            (EffectivityStatus.Pending, EffectivityStatus.RegulatoryApproved),
            (EffectivityStatus.Pending, EffectivityStatus.EthicsApproved),
            (EffectivityStatus.Pending, EffectivityStatus.Effective),
            (EffectivityStatus.Pending, EffectivityStatus.Withdrawn),
            (EffectivityStatus.RegulatoryApproved, EffectivityStatus.EthicsApproved),
            (EffectivityStatus.RegulatoryApproved, EffectivityStatus.Effective),
            (EffectivityStatus.RegulatoryApproved, EffectivityStatus.Withdrawn),
            (EffectivityStatus.EthicsApproved, EffectivityStatus.RegulatoryApproved),
            (EffectivityStatus.EthicsApproved, EffectivityStatus.Effective),
            (EffectivityStatus.EthicsApproved, EffectivityStatus.Withdrawn),
            (EffectivityStatus.Effective, EffectivityStatus.Withdrawn),
        ];
        var data = new TheoryData<EffectivityStatus, EffectivityStatus, bool>();
        foreach (var from in Enum.GetValues<EffectivityStatus>())
        {
            foreach (var to in Enum.GetValues<EffectivityStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Transitions_FollowTheLifecycle(EffectivityStatus from, EffectivityStatus to, bool expected) =>
        EffectivityTransitions.IsAllowed(from, to).Should().Be(expected);

    [Fact]
    public void EvidenceKinds_AreApprovalsOnly()
    {
        EffectivityTransitions.EvidenceKinds(EffectivityStatus.Effective).Should().BeEquivalentTo(
            [ArtifactKind.EthicsApproval, ArtifactKind.RegulatoryApproval, ArtifactKind.CtaAcknowledgement]);
        EffectivityTransitions.EvidenceKinds(EffectivityStatus.EthicsApproved).Should().BeEquivalentTo([ArtifactKind.EthicsApproval]);
        EffectivityTransitions.EvidenceKinds(EffectivityStatus.RegulatoryApproved).Should().BeEquivalentTo(
            [ArtifactKind.RegulatoryApproval, ArtifactKind.CtaAcknowledgement]);
        EffectivityTransitions.EvidenceKinds(EffectivityStatus.Pending).Should().BeEmpty();
        EffectivityTransitions.EvidenceKinds(EffectivityStatus.Withdrawn).Should().BeEmpty();
    }

    [Fact]
    public void Declare_StartsPendingAndRefusesAFutureDeclaration()
    {
        var effectivity = Declare(EffectivityScope.InCountry("DE"));

        effectivity.Status.Should().Be(EffectivityStatus.Pending);
        effectivity.StatusSince.Should().Be(T0);
        effectivity.Scope.Should().Be(EffectivityScope.InCountry("DE"));
        effectivity.CurrentEvidence().Should().BeEmpty();
        effectivity.Periods.Should().ContainSingle();

        Effectivity.Declare(EffectivityId.New(), Tenant, Study, ProtocolVersionId.New(), EffectivityScope.Study(), T0.AddSeconds(1), T0)
            .Error.Should().Be(EffectivityErrors.TransitionTimeInvalid);
        Effectivity.Declare(EffectivityId.New(), Tenant, Study, ProtocolVersionId.New(), EffectivityScope.InCountry("x"), T0, T0)
            .Error!.Code.Should().Be(EffectivityErrors.ScopeInvalid.Code);
    }

    [Fact]
    public void ChangeStatus_Effective_NeedsEvidence()
    {
        var effectivity = Declare(EffectivityScope.Study());

        effectivity.ChangeStatus(EffectivityStatus.Effective, [], T0.AddSeconds(1), T0.AddSeconds(1)).Should().Be(EffectivityErrors.EvidenceRequired);
        effectivity.ChangeStatus(EffectivityStatus.Effective, [Approval, Approval], T0.AddSeconds(1), T0.AddSeconds(1))!.Code
            .Should().Be(EffectivityErrors.EvidenceInvalid.Code);

        effectivity.ChangeStatus(EffectivityStatus.Effective, [Approval], T0.AddSeconds(1), T0.AddSeconds(1)).Should().BeNull();
        effectivity.Status.Should().Be(EffectivityStatus.Effective);
        effectivity.CurrentEvidence().Should().Equal(Approval);
    }

    [Fact]
    public void ChangeStatus_RefusesBadTimesAndTerminalMoves()
    {
        var effectivity = Declare(EffectivityScope.Study());

        effectivity.CheckTransition(EffectivityStatus.EthicsApproved, T0, T0.AddSeconds(1)).Should().Be(EffectivityErrors.TransitionTimeInvalid);
        effectivity.CheckTransition(EffectivityStatus.EthicsApproved, T0.AddSeconds(2), T0.AddSeconds(1)).Should().Be(EffectivityErrors.TransitionTimeInvalid);
        effectivity.CheckTransition(EffectivityStatus.EthicsApproved, T0, T0).Should().Be(EffectivityErrors.TransitionTimeInvalid);
        effectivity.CheckTransition((EffectivityStatus)42, T0.AddSeconds(1), T0.AddSeconds(1))!.Code.Should().Be(EffectivityErrors.InvalidTransition.Code);

        effectivity.ChangeStatus(EffectivityStatus.Withdrawn, [], T0.AddSeconds(1), T0.AddSeconds(1)).Should().BeNull();
        effectivity.CheckTransition(EffectivityStatus.Effective, T0.AddSeconds(2), T0.AddSeconds(2))!.Code
            .Should().Be(EffectivityErrors.InvalidTransition.Code);
    }

    [Fact]
    public void ChangeStatus_TwiceAtOneRecordedInstant_IsAConcurrencyConflict()
    {
        var effectivity = Declare(EffectivityScope.Study());
        effectivity.ChangeStatus(EffectivityStatus.EthicsApproved, [Approval], T0.AddSeconds(1), T0.AddSeconds(2)).Should().BeNull();

        effectivity.CheckTransition(EffectivityStatus.Effective, T0.AddSeconds(2), T0.AddSeconds(2)).Should().Be(KernelErrors.ConcurrencyConflict);
    }

    /// <summary>
    /// A status change recorded later than it took effect keeps both views: what was true at each valid instant, and
    /// what was known at each recorded instant (spec §4.1).
    /// </summary>
    [Fact]
    public void StatusAsOf_ReadsBothTimes()
    {
        var effectivity = Declare(EffectivityScope.Study());
        effectivity.ChangeStatus(EffectivityStatus.Effective, [Approval], T0.AddMinutes(10), T0.AddMinutes(20)).Should().BeNull();

        effectivity.StatusAsOf(T0.AddMinutes(-1), T0.AddMinutes(30)).Should().BeNull();
        effectivity.StatusAsOf(T0.AddMinutes(5), T0.AddMinutes(30)).Should().Be(EffectivityStatus.Pending);
        effectivity.StatusAsOf(T0.AddMinutes(15), T0.AddMinutes(30)).Should().Be(EffectivityStatus.Effective);
        effectivity.StatusAsOf(T0.AddMinutes(15), T0.AddMinutes(15)).Should().Be(EffectivityStatus.Pending);
        effectivity.Periods.Should().HaveCount(3);
        effectivity.Periods.Count(p => p.Period.Recorded.IsOpen).Should().Be(2);
    }

    /// <summary>
    /// The spec §7.1 example: version 2 is effective study-wide, but a site-level withdrawal decides at site B, so it is
    /// in force at site A and not at site B.
    /// </summary>
    [Fact]
    public void Resolution_MostSpecificScopeDecides()
    {
        var siteA = StudySiteId.New();
        var siteB = StudySiteId.New();
        var version = ProtocolVersionId.New();
        var studyWide = Effective(Declare(EffectivityScope.Study(), version));
        var atB = Declare(EffectivityScope.At(siteB), version);
        atB.ChangeStatus(EffectivityStatus.Withdrawn, [], T0.AddSeconds(1), T0.AddSeconds(1)).Should().BeNull();

        EffectivityResolution.EffectiveAt([studyWide, atB], siteA, "US", Later, Later).Should().Equal(studyWide);
        EffectivityResolution.EffectiveAt([studyWide, atB], siteB, "US", Later, Later).Should().BeEmpty();
    }

    [Fact]
    public void Resolution_CountryOverridesStudyAndSiteOverridesCountry()
    {
        var site = StudySiteId.New();
        var version = ProtocolVersionId.New();
        var studyWide = Effective(Declare(EffectivityScope.Study(), version));
        var germany = Declare(EffectivityScope.InCountry("DE"), version);

        EffectivityResolution.EffectiveAt([studyWide, germany], site, "DE", Later, Later).Should().BeEmpty();
        EffectivityResolution.EffectiveAt([studyWide, germany], site, "FR", Later, Later).Should().Equal(studyWide);

        var atSite = Effective(Declare(EffectivityScope.At(site), version));
        EffectivityResolution.EffectiveAt([studyWide, germany, atSite], site, "DE", Later, Later).Should().Equal(atSite);
    }

    [Fact]
    public void Resolution_IgnoresEffectivitiesNotYetDeclaredAndResolvesEachVersion()
    {
        var site = StudySiteId.New();
        var v1 = Effective(Declare(EffectivityScope.Study()));
        var v2 = Effective(Declare(EffectivityScope.Study()));

        EffectivityResolution.EffectiveAt([v1, v2], site, "US", Later, Later).Should().BeEquivalentTo([v1, v2]);
        EffectivityResolution.EffectiveAt([v1, v2], site, "US", T0.AddSeconds(-1), Later).Should().BeEmpty();

        // A site-level effectivity declared later does not decide before it existed, in either time.
        var siteOnly = Declare(EffectivityScope.At(site), v1.ProtocolVersionId, declaredAt: T0.AddMinutes(1), recordedAt: T0.AddMinutes(1));
        EffectivityResolution.EffectiveAt([v1, siteOnly], site, "US", T0.AddSeconds(30), Later).Should().Equal(v1);
        EffectivityResolution.EffectiveAt([v1, siteOnly], site, "US", Later, Later).Should().BeEmpty();
    }

    private static DateTimeOffset Later => T0.AddHours(1);

    private static Effectivity Declare(
        EffectivityScope scope, ProtocolVersionId? version = null, DateTimeOffset? declaredAt = null, DateTimeOffset? recordedAt = null) =>
        Effectivity.Declare(
            EffectivityId.New(), Tenant, Study, version ?? ProtocolVersionId.New(), scope, declaredAt ?? T0, recordedAt ?? T0).Value!;

    private static Effectivity Effective(Effectivity effectivity)
    {
        var at = effectivity.StatusSince.AddSeconds(1);
        effectivity.ChangeStatus(EffectivityStatus.Effective, [Approval], at, at).Should().BeNull();
        return effectivity;
    }
}
