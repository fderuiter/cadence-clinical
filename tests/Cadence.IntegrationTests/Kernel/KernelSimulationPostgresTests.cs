using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Simulation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.Standards.Mapping;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;
using Cadence.TestSupport.Kernel.Standards;

using FluentAssertions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Simulation against PostgreSQL (ADR-2480): a forked and revised protocol draft is compared with its approved parent
/// through kernel persistence, with the parent's dependents counted exactly as amendment impact counts them, the run adds or changes no row in any kernel table (K-SIM-1), and another tenant's
/// versions cannot be simulated. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelSimulationPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private static readonly string Revised = ProtocolDesignDocument.Write(ProtocolDesignSamples.MinimalDesign() with
    {
        Arms = [ProtocolDesignSamples.MinimalDesign().Arms[0], new Arm("arm.C", "High dose", ArmType.Experimental, [])],
    });

    /// <summary>
    /// A draft forked from the approved version and revised to replace an arm is reported element by element, its
    /// dependents are those of the removed arm and of the parent as a whole, and running the simulation leaves every
    /// kernel table of the tenant exactly as it was.
    /// </summary>
    [Fact(DisplayName = "Simulate_ForkedDraftAgainstApprovedParent_WritesNothing")]
    [KernelRule("K-SIM-1")]
    public async Task SimulateForkedDraftAgainstApprovedParentWritesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, study) = await scenario.NewStudyAsync(ct);
        var parent = await ApproveAsync(scenario, tenant, study, ProtocolDesignSamples.Minimal, ct);
        var draft = ProtocolVersionId.New();
        await scenario.OkAsync(new ForkProtocolVersion(tenant, parent, draft, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new ReviseProtocolDraft(tenant, draft, Revised, null, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var other = ProtocolVersionId.New();
        await scenario.OkAsync(new ForkProtocolVersion(tenant, parent, other, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var before = await RowCountsAsync(tenant, ct);

        var result = await Engine().SimulateAsync(tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(draft)), ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.Scope.Should().Be(NodeRef.For(study));
        result.Value.StructuralDiff.Should().BeEquivalentTo(
            [
                new ElementChange(ProtocolStructuralDiff.RootId, null, ElementChangeKind.Changed, ["/arms"]),
                new ElementChange("arm.B", null, ElementChangeKind.Removed, []),
                new ElementChange("arm.C", null, ElementChangeKind.Added, []),
            ],
            options => options.WithStrictOrdering());
        result.Value.Candidate.BaseVersion!.Unversioned().Should().Be(NodeRef.For(parent));
        result.Value.EngineVersions.Should().Contain(SimulationEngine.DependentsEngine);
        result.Value.Dependents!.Start.Should().Be(NodeRef.For(parent).ToString());
        result.Value.Dependents.Groups.SelectMany(g => g.Objects).Select(o => o.Ref).Should().BeEquivalentTo(
            [NodeRef.For(other, 1), NodeRef.For(ProtocolElementId.For(study, "arm.B"), 1)],
            "the other open amendment depends on the parent, and so does the removed arm (ADR-2474); the simulated draft "
            + "itself and the unchanged arm do not count");
        var impact = await fixture.AmendmentImpact.ComputeAsync(
            tenant, fixture.CurrentActor.Actor!, draft, validAt: result.Value.AsOf.ValidAt, knownAt: result.Value.AsOf.KnownAt, cancellationToken: ct);
        impact!.Dependents.Should().BeEquivalentTo(result.Value.Dependents, "simulation and amendment impact count through the same read (Canvas D2)");
        (await RowCountsAsync(tenant, ct)).Should().Equal(before, "a simulation writes no clinical state (K-SIM-1)");
    }

    /// <summary>A version of another tenant is unknown to the simulation, as to every other read.</summary>
    [Fact(DisplayName = "Simulate_CannotReachAnotherTenantsVersions")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task SimulateCannotReachAnotherTenantsVersions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (owner, study) = await scenario.NewStudyAsync(ct);
        var (stranger, _) = await scenario.NewStudyAsync(ct);
        var version = await ApproveAsync(scenario, owner, study, KernelProtocolScenario.Content, ct);

        var own = await Engine().SimulateAsync(owner, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(version, Revised)), ct);
        var foreign = await Engine().SimulateAsync(stranger, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(version, Revised)), ct);

        own.IsSuccess.Should().BeTrue(own.Error?.Message);
        foreign.Error!.Code.Should().Be(SimulationErrors.VersionUnknown.Code);
    }

    /// <summary>
    /// Validates bitemporal (valid_at, known_at) simulation across ProtocolVersion, MappingSpecification, and RulePack
    /// candidates on PostgreSQL, including historical vs current site/participant operational counts, dataset projection
    /// changes, rule validation deltas, reader-scoped site masking (ADR-2590), and zero database writes (K-SIM-1).
    /// </summary>
    [Fact(DisplayName = "Simulate_BitemporalMultiKindOperationalProjectionsValidationAndMasking_WritesNothing")]
    [KernelRule("K-SIM-1")]
    [KernelRule("K-SIM-2")]
    [KernelRule("K-SIM-3")]
    [KernelRule("K-SIM-4")]
    [KernelRule("K-PROT-2")]
    public async Task SimulateBitemporalMultiKindOperationalProjectionsValidationAndMaskingWritesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var tenant = seed.Tenant;
        var (_, study) = await scenario.NewStudyAsync(tenant, ct);

        var sbp = await scenario.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                "Systolic blood pressure",
                "Brachial systolic blood pressure",
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);

        // Register & approve SDTMIG 3.4, a base MappingSpecification, and a base RulePack, then declare StudyStandardsProfile.
        var sdtmigPackage = StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package;
        var sdtmigId = await scenario.OkAsync(
            new RegisterStandardVersion(tenant, StandardVersionId.New(), sdtmigPackage, null, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        var sdtmigHash = (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmigId, ct))!.ContentHash;
        var sdtmigApproval = await scenario.OkAsync(scenario.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.StandardVersion, sdtmigId.Value, sdtmigHash)), ct);
        await scenario.OkAsync(new ApproveStandardVersion(tenant, sdtmigId, sdtmigApproval, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var baseMapSpec = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp));
        var baseMapDraft = await scenario.OkAsync(
            new DraftLibraryItem(
                tenant,
                LibraryItemId.New(),
                LibraryItemKind.MappingSpecification,
                "sdtm-vs-sim",
                baseMapSpec.Title,
                MappingSpecificationJson.ToJson(baseMapSpec),
                [sdtmigId],
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);
        var baseMapApproval = await scenario.OkAsync(
            scenario.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, baseMapDraft.Id.Value, baseMapDraft.ContentHash)),
            ct);
        await scenario.OkAsync(new ApproveLibraryItem(tenant, baseMapDraft.Id, baseMapApproval, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var baseRulePack = BuildVsRulePack("R-SBP-CHECK", 1, "<", 100m);
        var baseRuleDraft = await scenario.OkAsync(
            new DraftLibraryItem(
                tenant,
                LibraryItemId.New(),
                LibraryItemKind.RulePack,
                "rules-vs-sim",
                baseRulePack.Title,
                RulePackJson.ToJson(baseRulePack),
                [],
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);
        var baseRuleApproval = await scenario.OkAsync(
            scenario.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, baseRuleDraft.Id.Value, baseRuleDraft.ContentHash)),
            ct);
        await scenario.OkAsync(new ApproveLibraryItem(tenant, baseRuleDraft.Id, baseRuleApproval, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        await scenario.OkAsync(
            new DeclareStudyStandards(
                tenant,
                study,
                StudyStandardsProfileId.New(),
                [sdtmigId],
                [SampleMappingSpecifications.SdtmCt],
                [baseMapDraft.Id, baseRuleDraft.Id],
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);

        // Approve Protocol v1 with Screening (Day 1) and Week 4 (Day 28) visits collecting SBP.
        var baseDesign = new ProtocolDesign
        {
            Title = "Bitemporal Simulation Study",
            Epochs = [new Epoch("ep.screening", "Screening", 1), new Epoch("ep.treatment", "Treatment", 2)],
            PlannedActivities =
            [
                new PlannedActivity("visit.screening", "Screening", "ep.screening", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(1, 0, 0)),
                new PlannedActivity("visit.week4", "Week 4", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(28, 2, 2)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec("cs.vitals", "Vital Signs", [new CollectionItem("item.sbp", sbp, "kPa", Required: true)]),
            ],
        };
        var v1 = await ApproveAsync(scenario, tenant, study, ProtocolDesignDocument.Write(baseDesign), ct);

        // Register central IRB artifact and Effective study-wide effectivity for v1.
        var irb = await scenario.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ArtifactVersionId.New(),
                ArtifactKind.EthicsApproval,
                study,
                "IRB v1",
                new ArtifactContent(ContentHash.OfBytes("irb-v1"u8), 128, "application/pdf", "s3://etmf/irb-v1.pdf"),
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);
        var effV1State = await scenario.OkAsync(
            new DeclareEffectivity(tenant, EffectivityId.New(), v1, EffectivityScope.Study(), fixture.Clock.UtcNow, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await scenario.OkAsync(
            new ChangeEffectivityStatus(tenant, effV1State.EffectivityId, EffectivityStatus.Effective, fixture.Clock.UtcNow, [irb.LatestVersionId], KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);

        // Register 2 sites: activate SITE-A before tCut; leave SITE-B Pending (Selected) before tCut.
        var instId = OrganizationId.New();
        await scenario.OkAsync(
            new RegisterOrganization(tenant, instId, "INST-SIM", "Sim Medical Center", OrganizationKind.SiteInstitution, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        var siteA = await scenario.OkAsync(new RegisterSite(tenant, SiteId.New(), instId, "SITE-A", "Site Alpha", "US", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var siteB = await scenario.OkAsync(new RegisterSite(tenant, SiteId.New(), instId, "SITE-B", "Site Beta", "DE", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var studySiteA = await scenario.OkAsync(new LinkSiteToStudy(tenant, StudySiteId.New(), study, siteA, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var studySiteB = await scenario.OkAsync(new LinkSiteToStudy(tenant, StudySiteId.New(), study, siteB, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new ActivateStudySite(tenant, studySiteA, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        // Enroll Participant 1 at SITE-A and record SBP = 12 kPa (90 mmHg -> triggers R-SBP-CHECK < 100) before tCut.
        var part1 = await scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var enr1 = EnrollmentId.New();
        var tScreen1 = fixture.Clock.UtcNow;
        await scenario.OkAsync(new ScreenParticipant(tenant, enr1, part1, studySiteA, SubjectCode.From("SUBJ-A-001"), tScreen1, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var tEnroll1 = fixture.Clock.UtcNow;
        await scenario.OkAsync(new ChangeEnrollmentStatus(tenant, enr1, EnrollmentStatus.Enrolled, tEnroll1, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var col1 = CollectionInstanceId.New();
        await scenario.OkAsync(new OpenCollectionInstance(tenant, col1, enr1, "cs.vitals", "visit.screening", tEnroll1, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(
            new RecordCollectedValues(tenant, col1, enr1, [new CollectedValue("item.sbp", new QuantityValue(12m, "kPa"), 0, ObservationId.New(), tEnroll1)], KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);

        // Fork Protocol v2 shifting Week 4 from Day 28 to Day 30 with TransitionPolicy.RequiresReconsent before tCut.
        var amendedDesign = baseDesign with
        {
            PlannedActivities =
            [
                baseDesign.PlannedActivities[0],
                new PlannedActivity("visit.week4", "Week 4", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(30, 2, 2)),
            ],
        };
        var v2Amendment = new ProtocolAmendment(
            "Shift Week 4 visit",
            "Align Week 4 window",
            AmendmentClassification.Substantial,
            "Moves Week 4 from Day 28 to Day 30",
            ["visit.week4"],
            TransitionPolicy.RequiresReconsent);
        var v2Draft = ProtocolVersionId.New();
        await scenario.OkAsync(new ForkProtocolVersion(tenant, v1, v2Draft, v2Amendment, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new ReviseProtocolDraft(tenant, v2Draft, ProtocolDesignDocument.Write(amendedDesign), v2Amendment, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var tCut = fixture.Clock.UtcNow;

        // Mutate state AFTER tCut: activate SITE-B, enroll Participant 2 at SITE-B, and record SBP = 18 kPa (135 mmHg).
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var tPost = fixture.Clock.UtcNow;
        await scenario.OkAsync(new ActivateStudySite(tenant, studySiteB, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var part2 = await scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var enr2 = EnrollmentId.New();
        await scenario.OkAsync(new ScreenParticipant(tenant, enr2, part2, studySiteB, SubjectCode.From("SUBJ-B-001"), tPost, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var tEnroll2 = fixture.Clock.UtcNow;
        await scenario.OkAsync(new ChangeEnrollmentStatus(tenant, enr2, EnrollmentStatus.Enrolled, tEnroll2, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var col2 = CollectionInstanceId.New();
        await scenario.OkAsync(new OpenCollectionInstance(tenant, col2, enr2, "cs.vitals", "visit.screening", tEnroll2, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(
            new RecordCollectedValues(tenant, col2, enr2, [new CollectedValue("item.sbp", new QuantityValue(18m, "kPa"), 0, ObservationId.New(), tEnroll2)], KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);

        var tNow = fixture.Clock.UtcNow;
        var beforeCounts = await RowCountsAsync(tenant, ct);
        var engine = Engine();

        // 1. Historical (tCut, tCut) vs current (tNow, tNow) ProtocolVersion simulation and AmendmentImpact.
        var protAtCut = (await engine.SimulateAsync(tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(v2Draft), tCut, tCut), ct)).Value!;
        var protAtCutRepeat = (await engine.SimulateAsync(tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(v2Draft), tCut, tCut), ct)).Value!;
        var protAtNow = (await engine.SimulateAsync(tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(v2Draft), tNow, tNow), ct)).Value!;

        protAtCutRepeat.InputHash.Should().Be(protAtCut.InputHash);
        protAtCutRepeat.ResultHash.Should().Be(protAtCut.ResultHash);
        protAtCut.OperationalImpact!.ActiveSiteCount.Should().Be(1, "at tCut only SITE-A was active");
        protAtCut.OperationalImpact.PendingSiteCount.Should().Be(1, "at tCut SITE-B was still Selected (pending)");
        protAtCut.OperationalImpact.TotalParticipantCount.Should().Be(1);
        protAtCut.OperationalImpact.AffectedParticipantCount.Should().Be(1);
        protAtCut.OperationalImpact.ReconsentParticipantCount.Should().Be(1);

        protAtNow.OperationalImpact!.ActiveSiteCount.Should().Be(2, "after tCut both SITE-A and SITE-B are active");
        protAtNow.OperationalImpact.PendingSiteCount.Should().Be(0);
        protAtNow.OperationalImpact.TotalParticipantCount.Should().Be(2);
        protAtNow.OperationalImpact.AffectedParticipantCount.Should().Be(2);
        protAtNow.OperationalImpact.ReconsentParticipantCount.Should().Be(2);

        var impactAtCut = await fixture.AmendmentImpact.ComputeAsync(tenant, fixture.CurrentActor.Actor!, v2Draft, validAt: tCut, knownAt: tCut, cancellationToken: ct);
        var impactAtNow = await fixture.AmendmentImpact.ComputeAsync(tenant, fixture.CurrentActor.Actor!, v2Draft, validAt: tNow, knownAt: tNow, cancellationToken: ct);
        impactAtCut!.ActiveSiteCount.Should().Be(1);
        impactAtCut.PendingSiteCount.Should().Be(1);
        impactAtCut.TotalParticipantCount.Should().Be(1);
        impactAtNow!.ActiveSiteCount.Should().Be(2);
        impactAtNow.TotalParticipantCount.Should().Be(2);

        // 2. Multi-kind MappingSpecification & RulePack bitemporal simulations at (tCut, tCut) vs (tNow, tNow).
        var candidateMapJson = MappingSpecificationJson.ToJson(
            SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp) with { Decimals = 1 })).GetRawText();
        var mapAtCut = (await engine.SimulateAsync(
            tenant,
            new SimulationRequest(SimulationCandidate.UnsavedMappingSpecification(study, candidateMapJson, baseMapDraft.Id, sdtmigId), tCut, tCut),
            ct)).Value!;
        var mapAtNow = (await engine.SimulateAsync(
            tenant,
            new SimulationRequest(SimulationCandidate.UnsavedMappingSpecification(study, candidateMapJson, baseMapDraft.Id, sdtmigId), tNow, tNow),
            ct)).Value!;
        mapAtCut.ProjectionChanges.Should().ContainSingle(d => d.Dataset == "VS").Which.ChangedCount.Should().Be(1, "only 1 observation existed at tCut");
        mapAtNow.ProjectionChanges.Should().ContainSingle(d => d.Dataset == "VS").Which.ChangedCount.Should().Be(2, "2 observations exist at tNow");

        var candidateRuleJson = RulePackJson.ToJson(BuildVsRulePack("R-SBP-CHECK", 2, ">", 130m)).GetRawText();
        var ruleAtCut = (await engine.SimulateAsync(
            tenant,
            new SimulationRequest(SimulationCandidate.UnsavedRulePack(study, candidateRuleJson, baseRuleDraft.Id), tCut, tCut),
            ct)).Value!;
        var ruleAtNow = (await engine.SimulateAsync(
            tenant,
            new SimulationRequest(SimulationCandidate.UnsavedRulePack(study, candidateRuleJson, baseRuleDraft.Id), tNow, tNow),
            ct)).Value!;
        ruleAtCut.ValidationDelta!.Resolving.Should().HaveCount(1, "90 mmHg resolves at tCut");
        ruleAtCut.ValidationDelta.Opening.Should().BeEmpty("135 mmHg did not exist yet at tCut");
        ruleAtNow.ValidationDelta!.Resolving.Should().HaveCount(1);
        ruleAtNow.ValidationDelta.Opening.Should().HaveCount(1, "135 mmHg opens at tNow");

        // 3. Reader-scoped site masking hides SITE-B and surfaces Hidden.MoreExist + MaskedPart entries.
        fixture.Visibility.Masked[NodeRef.For(studySiteB)] = MaskReason.NotPermitted;
        try
        {
            var maskedNow = (await engine.SimulateAsync(tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(v2Draft), tNow, tNow), ct)).Value!;
            maskedNow.OperationalImpact!.ActiveSiteCount.Should().Be(1);
            maskedNow.OperationalImpact.TotalParticipantCount.Should().Be(1);
            maskedNow.OperationalImpact.Hidden.MoreExist.Should().BeTrue();
            maskedNow.OperationalImpact.Hidden.Reasons.Should().Contain(MaskReason.NotPermitted);
            maskedNow.Masked.Should().Contain(new MaskedPart(SimulationSnapshotMasking.SitesPart, MaskReason.NotPermitted));
        }
        finally
        {
            fixture.Visibility.Masked.Clear();
        }

        // 4. Every simulation and amendment impact read left all kernel tables untouched (K-SIM-1).
        (await RowCountsAsync(tenant, ct)).Should().Equal(beforeCounts, "multi-kind bitemporal simulations write zero database state (K-SIM-1)");
    }

    private SimulationEngine Engine() =>
        new(
            new KernelSimulationInputs(fixture.TenantContexts, (KernelLineageStore)fixture.Lineage),
            new PermitAll(),
            fixture.Visibility,
            fixture.CurrentActor,
            fixture.Clock,
            new MappingEngine(),
            fixture.RuleEvaluator);

    private static RulePack BuildVsRulePack(string id, int version, string op, decimal threshold)
    {
        var guidanceUri = new UriBuilder(Uri.UriSchemeHttps, "example.org") { Path = "vs-guidance" }.Uri.AbsoluteUri;
        using var exprDoc = System.Text.Json.JsonDocument.Parse(
            $$"""{"op":"{{op}}","args":[{"var":"VSSTRESN"},{{threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}}]}""");
        return new RulePack(
            RuleAuthority.Sponsor,
            "VS standardization checks",
            [
                new Rule(
                    id,
                    version,
                    RuleAuthority.Sponsor,
                    new SourceCitation("Sponsor VS Edit Checks", "1.0", guidanceUri),
                    new RuleApplicability(ValidationStage.Standardization, "SDTMIG", "3.4", null, "VS"),
                    ValidationSeverity.Warning,
                    RuleExpressionJson.Parse(exprDoc.RootElement),
                    $"Rule {id} triggered.",
                    "Confirm vital sign measurement.",
                    EffectivePeriod.Always),
            ]);
    }

    private static async Task<ProtocolVersionId> ApproveAsync(KernelProtocolScenario scenario, TenantId tenant, StudyId study, string content, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        (await scenario.CreateDraftAsync(tenant, study, id, ct, content)).IsSuccess.Should().BeTrue();
        var inReview = await scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var attestation = await scenario.OkAsync(scenario.AttestCommand(tenant, Cadence.Kernel.Trust.AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await scenario.OkAsync(new ApproveProtocolVersion(tenant, id, attestation, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        return id;
    }

    /// <summary>Every kernel table with a tenant column, with its row count and a digest of its rows for the tenant.</summary>
    private async Task<List<string>> RowCountsAsync(TenantId tenant, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.columns WHERE table_schema = 'kernel' AND column_name = 'tenant_id' ORDER BY table_name",
            connection))
        await using (var reader = await list.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        tables.Should().Contain(["protocol_versions", "audit_entries", "outbox_messages"]);
        var counts = new List<string>();
        foreach (var table in tables)
        {
            await using var count = new NpgsqlCommand(
                $"SELECT count(*) || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM kernel.\"{table}\" t WHERE tenant_id = @tenant",
                connection);
            count.Parameters.AddWithValue("tenant", tenant.Value);
            counts.Add($"{table}={await count.ExecuteScalarAsync(ct)}");
        }

        return counts;
    }

    private sealed class PermitAll : IAuthorizationProbe
    {
        public ValueTask<AuthorizationDecision> CanIAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AuthorizationDecision.Permit());

        public ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
