using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Trust;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The Schedule of Activities against PostgreSQL (ADR-2475): the reader compiles a stored version's design, and an
/// amendment's impact carries the typed schedule and eligibility directives between it and its parent. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ScheduleOfActivitiesPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>CARDIO-2 with its week 8 visit moved to day 64 and a new exclusion criterion in the population.</summary>
    private static ProtocolDesign Amended(ProtocolDesign design) => design with
    {
        PlannedActivities = [.. design.PlannedActivities.Select(a => a.Id == "visit.week8" ? a with { Timing = a.Timing! with { Day = 64 } } : a)],
        EligibilityCriteria = [.. design.EligibilityCriteria, new EligibilityCriterion("ec.1", CriterionCategory.Exclusion, "Heart failure.", [])],
        Populations = [design.Populations[0] with { CriterionIds = [.. design.Populations[0].CriterionIds, "ec.1"] }],
    };

    /// <summary>A stored version's schedule is the matrix its design compiles to, byte for byte.</summary>
    [Fact(DisplayName = "Reader_CompilesTheStoredVersionsSchedule")]
    public async Task ReaderCompilesTheStoredVersionsSchedule()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var cardio = await CardioAsync(tenant, ct);
        var version = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(tenant, study, version, ct, ProtocolDesignDocument.Write(cardio))).IsSuccess.Should().BeTrue();

        var matrix = await fixture.ProtocolVersions.GetScheduleOfActivitiesAsync(tenant, version, ct);

        ScheduleOfActivities.Write(matrix!).Should().Be(ScheduleOfActivities.Write(ScheduleOfActivities.Compile(cardio)));
        (await fixture.ProtocolVersions.GetScheduleOfActivitiesAsync(tenant, ProtocolVersionId.New(), ct)).Should().BeNull();
        (await fixture.ProtocolVersions.GetScheduleOfActivitiesAsync(TenantId.New(), version, ct)).Should().BeNull("another tenant's version is never read");
    }

    /// <summary>
    /// An amendment that moves a visit and adds an exclusion criterion reports a visit move, with the specifications that
    /// visit collects, and a criterion added, with the population that names it; it changes both the schedule and
    /// eligibility.
    /// </summary>
    [Fact(DisplayName = "AmendmentImpact_CarriesTheScheduleDirectives")]
    public async Task AmendmentImpactCarriesTheScheduleDirectives()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var cardio = await CardioAsync(tenant, ct);
        var parent = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(tenant, study, parent, ct, ProtocolDesignDocument.Write(cardio))).IsSuccess.Should().BeTrue();
        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, parent, KernelProtocolScenario.Key(), "Ready for review"), ct);
        var attestation = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.OkAsync(new ApproveProtocolVersion(tenant, parent, attestation, KernelProtocolScenario.Key(), "Approved protocol"), ct);
        var amendment = ProtocolVersionId.New();
        await _scenario.OkAsync(new ForkProtocolVersion(tenant, parent, amendment, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Protocol amendment"), ct);
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, amendment, ProtocolDesignDocument.Write(Amended(cardio)), null, KernelProtocolScenario.Key(), "Move week 8"), ct);

        var impact = await fixture.AmendmentImpact.ComputeAsync(tenant, fixture.CurrentActor.Actor!, amendment, cancellationToken: ct);

        var schedule = impact!.Schedule!;
        schedule.Directives.Select(d => (d.Kind, d.ElementId)).Should().Equal(
            (SoaDirectiveKind.VisitMoved, "visit.week8"),
            (SoaDirectiveKind.CriterionAdded, "ec.1"));
        schedule.Directives[0].ImpactedElements.Should().Equal("cs.labs", "cs.vitals", "visit.week8");
        schedule.Directives[1].ImpactedElements.Should().Equal("ec.1", "pop.itt");
        schedule.ChangesSchedule.Should().BeTrue();
        schedule.ChangesEligibility.Should().BeTrue();
        schedule.Should().BeEquivalentTo(ScheduleOfActivitiesDiff.Compare(cardio, Amended(cardio)));
    }

    /// <summary>CARDIO-2 over four concepts registered in the tenant, since a design may only name concepts the tenant has.</summary>
    private async Task<ProtocolDesign> CardioAsync(TenantId tenant, CancellationToken ct)
    {
        var concepts = new ConceptRef[4];
        string[] names = ["Systolic blood pressure", "HbA1c", "QT interval", "Informed consent date"];
        for (var i = 0; i < names.Length; i++)
        {
            concepts[i] = await _scenario.OkAsync(
                new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, names[i], $"{names[i]}.", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
                ct);
        }

        return SoaSamples.Cardio(concepts[0], concepts[1], concepts[2], concepts[3]);
    }
}
