using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Persistence.Simulation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Protocol design elements as lineage nodes against PostgreSQL (ADR-2474): approving a version writes its elements and
/// their references in the approval's transaction, an amendment writes only what it changes, and amendment impact
/// follows only the changed elements. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProtocolElementLineagePostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>Two objectives, each with a primary endpoint; the first endpoint has an estimand over arm A.</summary>
    private static readonly ProtocolDesign Design = ProtocolDesignSamples.MinimalDesign() with
    {
        Objectives =
        [
            new Objective("obj.1", ObjectiveLevel.Primary, "Show that the drug lowers systolic blood pressure."),
            new Objective("obj.2", ObjectiveLevel.Secondary, "Describe the drug's safety."),
        ],
        Endpoints =
        [
            new Endpoint("ep.1", "obj.1", ObjectiveLevel.Primary, "Change in systolic blood pressure at week 12."),
            new Endpoint("ep.2", "obj.2", ObjectiveLevel.Secondary, "Adverse events to week 12."),
        ],
        Populations = [new Population("pop.itt", "Intention to treat", [])],
        Estimands =
        [
            new Estimand("est.1", "ep.1", "pop.itt", ["arm.A"], "Change in SBP at week 12", [], "Difference in means"),
        ],
    };

    private static readonly string[] ElementIds = ["arm.A", "arm.B", "ep.1", "ep.2", "est.1", "obj.1", "obj.2", "pop.itt"];

    /// <summary>
    /// Approving the first version writes each design element as a node at version 1, governed by the approved version,
    /// with the scientific chain between them, so a forward trace from an objective reaches its endpoint and estimand.
    /// </summary>
    [Fact(DisplayName = "ApprovingAVersion_WritesItsElementsAsLineageNodes")]
    [KernelRule("K-LIN-2")]
    public async Task ApprovingAVersionWritesItsElementsAsLineageNodes()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);

        var version = await ApproveFirstAsync(tenant, study, ct);

        await using (var db = fixture.CreateContext(tenant))
        {
            var nodes = await db.LineageNodes.AsNoTracking().Where(n => n.Kind == NodeKinds.ProtocolElement).ToListAsync(ct);
            nodes.Select(n => (n.NodeId, n.Version)).Should().BeEquivalentTo(ElementIds.Select(id => (Element(study, id).Id, 1L)));
            var approval = await db.LineageNodes.AsNoTracking()
                .Where(n => n.Kind == NodeKinds.ProtocolVersion && n.NodeId == version.Value)
                .OrderByDescending(n => n.Version)
                .FirstAsync(ct);
            nodes.Should().OnlyContain(n => n.ActivityId == approval.ActivityId, "the elements are written in the approval's transaction (K-LIN-2)");
            var governed = await db.LineageEdges.AsNoTracking()
                .Where(e => e.FromId == version.Value && e.ToKind == NodeKinds.ProtocolElement)
                .ToListAsync(ct);
            governed.Should().HaveCount(ElementIds.Length).And.OnlyContain(e => e.Kind == LineageEdgeKind.GovernedBy && e.FromVersion == approval.Version);
        }

        var trace = await fixture.Lineage.TraceAsync(
            tenant, fixture.CurrentActor.Actor!, new LineageQuery(Element(study, "obj.1").AtVersion(1), LineageDirection.Forward), ct);
        trace.Nodes.Select(n => n.Ref).Should().BeEquivalentTo(
            [Element(study, "obj.1").AtVersion(1), Element(study, "ep.1").AtVersion(1), Element(study, "est.1").AtVersion(1)]);
    }

    /// <summary>
    /// Approving an amendment that rewords one objective writes that objective at the amendment's number, superseding
    /// its earlier version and linked to the unchanged endpoint; the approved amendment governs every element.
    /// </summary>
    [Fact(DisplayName = "ApprovingAnAmendment_WritesOnlyTheElementsItChanges")]
    public async Task ApprovingAnAmendmentWritesOnlyTheElementsItChanges()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var first = await ApproveFirstAsync(tenant, study, ct);

        var amendment = await ForkAsync(tenant, first, Reworded(), ct);
        await ApproveAsync(tenant, amendment, ct);

        await using var db = fixture.CreateContext(tenant);
        var nodes = await db.LineageNodes.AsNoTracking().Where(n => n.Kind == NodeKinds.ProtocolElement).ToListAsync(ct);
        nodes.Where(n => n.Version == 2).Select(n => n.NodeId).Should().Equal(Element(study, "obj.1").Id);
        nodes.Should().HaveCount(ElementIds.Length + 1);
        var objective = Element(study, "obj.1");
        var endpoint = Element(study, "ep.1");
        (await db.LineageEdges.AsNoTracking().Where(e => e.ToId == objective.Id && e.ToVersion == 2).Select(e => new { e.FromId, e.FromVersion, e.Kind }).ToListAsync(ct))
            .Should().BeEquivalentTo(
            [
                new { FromId = objective.Id, FromVersion = 1L, Kind = LineageEdgeKind.Supersedes },
                new { FromId = amendment.Value, FromVersion = await ApprovedVersionIndexAsync(db, amendment, ct), Kind = LineageEdgeKind.GovernedBy },
            ]);
        (await db.LineageEdges.AsNoTracking().CountAsync(e => e.FromId == objective.Id && e.FromVersion == 2 && e.ToId == endpoint.Id && e.ToVersion == 1, ct))
            .Should().Be(1);
        (await db.LineageEdges.AsNoTracking().CountAsync(e => e.FromId == amendment.Value && e.ToKind == NodeKinds.ProtocolElement, ct))
            .Should().Be(ElementIds.Length);
    }

    /// <summary>
    /// K-PROT-2 by element: an amendment that rewords the first objective impacts that objective, the endpoint and
    /// estimand that depend on it, and what depends on the parent version as a whole, but not the second objective, its
    /// endpoint or what was collected for that endpoint. The report lists the elements actually changed.
    /// </summary>
    [Fact(DisplayName = "AmendmentImpact_FollowsOnlyTheChangedElements")]
    [KernelRule("K-PROT-2")]
    public async Task AmendmentImpactFollowsOnlyTheChangedElements()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var parent = await ApproveFirstAsync(tenant, study, ct);
        var governed = NodeRef.For(ObservationId.New(), 1);
        var safetyData = NodeRef.For(ObservationId.New(), 1);
        long approvedAt;
        await using (var db = fixture.CreateContext(tenant))
        {
            approvedAt = await ApprovedVersionIndexAsync(db, parent, ct);
        }

        await fixture.SeedAsync(
            Edge(tenant, NodeRef.For(parent, approvedAt), governed, LineageEdgeKind.GovernedBy),
            Edge(tenant, Element(study, "ep.2").AtVersion(1), safetyData, LineageEdgeKind.CollectedBy));
        var amendment = await ForkAsync(tenant, parent, Reworded(), ct);

        var impact = await fixture.AmendmentImpact.ComputeAsync(tenant, fixture.CurrentActor.Actor!, amendment, cancellationToken: ct);

        impact!.ChangedElements.Should().BeEquivalentTo([new ElementChange("obj.1", null, ElementChangeKind.Changed, ["/text"])]);
        impact.Dependents.Groups.SelectMany(g => g.Objects).Select(o => o.Ref).Should().BeEquivalentTo(
            [
                governed,
                Element(study, "obj.1").AtVersion(1),
                Element(study, "ep.1").AtVersion(1),
                Element(study, "est.1").AtVersion(1),
            ]);
    }

    /// <summary>
    /// An element belongs to its study: the authority evaluator scopes it to the study's path, and it zooms under the
    /// protocol version that last governed it, at its latest version and labelled with its element id.
    /// </summary>
    [Fact(DisplayName = "ProtocolElement_IsScopedAndZoomedUnderItsStudy")]
    public async Task ProtocolElementIsScopedAndZoomedUnderItsStudy()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var amendment = await ForkAsync(tenant, await ApproveFirstAsync(tenant, study, ct), Reworded(), ct);
        await ApproveAsync(tenant, amendment, ct);
        var objective = Element(study, "obj.1");

        (await fixture.AuthorityStore.PathOfAsync(tenant, objective, ct)).Should().Be(await fixture.AuthorityStore.PathOfAsync(tenant, NodeRef.For(study), ct));
        (await fixture.AuthorityStore.PathOfAsync(tenant, NodeRef.For(ProtocolElementId.For(StudyId.New(), "obj.1")), ct)).Should().Be(ScopePath.TenantWide);

        var navigator = new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts));
        (await navigator.ResolveAsync(tenant, objective, ct)).Should().Be(new NodeSummary(objective.AtVersion(2), "Protocol element obj.1", "obj.1"));
        var path = await navigator.GetZoomPathAsync(tenant, objective, ct);
        path!.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Study, NodeKinds.ProtocolVersion, NodeKinds.ProtocolElement);
        path![2].Ref!.Unversioned().Should().Be(NodeRef.For(amendment));
    }

    /// <summary>
    /// End-to-end clinical evidence lineage (ADR-2474, spec §8.1, §9.6): forms, saved and unasked observations,
    /// signatures and assertions hang off their governing <c>protocol_element</c> nodes, so amending an unrelated
    /// objective flags zero clinical records, amending one <see cref="CollectionItem"/> flags only that item's
    /// observations, assertions and form signature while sibling items and unscheduled forms stay untouched, and
    /// bidirectional traces link <c>Objective → … → Observation → Assertion / Attestation</c>.
    /// </summary>
    [Fact(DisplayName = "AmendmentImpact_ScopesFormsObservationsSignaturesAndAssertions_ToChangedElements")]
    [KernelRule("K-PROT-2")]
    public async Task AmendmentImpactScopesFormsObservationsSignaturesAndAssertionsToChangedElements()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var forms = new KernelCollectionScenario(fixture);
        var (tenant, study) = await forms.Protocol.NewStudyAsync(ct);
        var sbpConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), Cadence.Kernel.Knowledge.ConceptOrigin.Sponsor, null, Cadence.Kernel.Knowledge.ConceptKind.BiomedicalConcept, "Systolic BP", "SBP definition.", KernelProtocolScenario.Key(), "Define SBP"),
            ct);
        var dbpConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), Cadence.Kernel.Knowledge.ConceptOrigin.Sponsor, null, Cadence.Kernel.Knowledge.ConceptKind.BiomedicalConcept, "Diastolic BP", "DBP definition.", KernelProtocolScenario.Key(), "Define DBP"),
            ct);
        var noteConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), Cadence.Kernel.Knowledge.ConceptOrigin.Sponsor, null, Cadence.Kernel.Knowledge.ConceptKind.BiomedicalConcept, "Clinical note", "Note definition.", KernelProtocolScenario.Key(), "Define note"),
            ct);

        var clinicalDesign = Design with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity("act.week12", "Week 12 visit", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital signs",
                    [
                        new CollectionItem("item.sbp", sbpConcept, "mm[Hg]", Required: true, Purpose: new DataPurpose { EndpointIds = ["ep.1"] }),
                        new CollectionItem("item.dbp", dbpConcept, "mm[Hg]", Required: true),
                    ]),
                new CollectionSpec(
                    "cs.notes",
                    "Unscheduled notes",
                    [
                        new CollectionItem("item.note", noteConcept, ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };

        var baseVersion = ProtocolVersionId.New();
        (await forms.Protocol.CreateDraftAsync(tenant, study, baseVersion, ct, ProtocolDesignDocument.Write(clinicalDesign))).IsSuccess.Should().BeTrue();
        await ApproveAsync(tenant, baseVersion, ct);
        var effectivity = EffectivityId.New();
        await forms.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, baseVersion, EffectivityScope.Study(), forms.Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = System.Text.Encoding.UTF8.GetBytes($"ethics-{Guid.NewGuid():N}");
        var ethicsArtifact = ArtifactVersionId.New();
        await forms.Protocol.OkAsync(
            new RegisterArtifact(
                tenant, ArtifactId.New(), ethicsArtifact, ArtifactKind.EthicsApproval, study, "Ethics approval",
                new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://ethics"),
                KernelProtocolScenario.Key(), "Filed approval"),
            ct);
        await forms.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, forms.Tick(), [ethicsArtifact], KernelProtocolScenario.Key(), "Effective"),
            ct);

        var studySite = await forms.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await forms.Observations.ScreenAsync(tenant, studySite, ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);

        // Scheduled vitals form with SBP + DBP, assertions on both, and a form signature.
        var vitalsInstance = CollectionInstanceId.New();
        await forms.Protocol.OkAsync(KernelCollectionScenario.Open(subject, vitalsInstance, forms.Tick(), "cs.vitals", "act.week12"), ct);
        var vitalsForm = new KernelCollectionScenario.Form(subject, baseVersion, vitalsInstance);
        var savedVitals = await forms.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                vitalsForm,
                KernelCollectionScenario.Value("item.sbp", KernelCollectionScenario.Mmhg(134)),
                KernelCollectionScenario.Value("item.dbp", KernelCollectionScenario.Mmhg(84))),
            ct);
        var sbpObs = savedVitals.Values.Single(v => v.ItemId == "item.sbp").ObservationId;
        var dbpObs = savedVitals.Values.Single(v => v.ItemId == "item.dbp").ObservationId;

        var sbpVerified = await forms.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollment, sbpObs, 1, KernelProtocolScenario.Key(), "SDV SBP"), ct);
        var dbpVerified = await forms.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollment, dbpObs, 1, KernelProtocolScenario.Key(), "SDV DBP"), ct);
        var sbpQuery = await forms.Protocol.OkAsync(
            new RaiseQuery(tenant, AssertionId.New(), enrollment, sbpObs, "Confirm cuff size.", KernelProtocolScenario.Key(), "Query SBP"), ct);
        var sbpAnswer = await forms.Protocol.OkAsync(
            new AnswerQuery(tenant, AssertionId.New(), enrollment, sbpQuery.AssertionId, "Standard cuff used.", KernelProtocolScenario.Key(), "Answer SBP"), ct);

        var vitalsHash = CollectionInstance.ContentHashOf(
        [
            new CollectionContentEntry(sbpObs, 1, KernelCollectionScenario.Mmhg(134)),
            new CollectionContentEntry(dbpObs, 1, KernelCollectionScenario.Mmhg(84)),
        ]);
        var vitalsSignature = await forms.Protocol.OkAsync(
            new SignCollectionInstance(
                tenant, AttestationId.New(), vitalsInstance, enrollment, AttestationMeaning.Approved, vitalsHash,
                forms.Tick(), "oidc+webauthn", KernelProtocolScenario.Key(), "Investigator sign-off"),
            ct);

        // Unscheduled form with an unasked item and its own signature.
        var notesInstance = CollectionInstanceId.New();
        await forms.Protocol.OkAsync(KernelCollectionScenario.Open(subject, notesInstance, forms.Tick(), "cs.notes", visit: null), ct);
        var unaskedNote = await forms.Protocol.OkAsync(
            new RecordUnaskedCollectionItem(tenant, notesInstance, enrollment, "item.note", null, KernelProtocolScenario.Key(), "Missing note query"),
            ct);
        var notesHash = CollectionInstance.ContentHashOf([new CollectionContentEntry(unaskedNote.ObservationId, 1, unaskedNote.Value)]);
        var notesSignature = await forms.Protocol.OkAsync(
            new SignCollectionInstance(
                tenant, AttestationId.New(), notesInstance, enrollment, AttestationMeaning.Responsibility, notesHash,
                forms.Tick(), "oidc+webauthn", KernelProtocolScenario.Key(), "Sign unscheduled notes"),
            ct);

        var simulationInputs = new KernelSimulationInputs(
            fixture.TenantContexts,
            new KernelLineageStore(fixture.TenantContexts, fixture.Visibility, fixture.Clock));

        // 1. Rewording the unrelated secondary objective obj.2 flags zero forms, observations, assertions, or signatures
        // in both /impact and /simulation/simulate.
        var unrelatedAmendment = await ForkAsync(
            tenant,
            baseVersion,
            ProtocolDesignDocument.Write(clinicalDesign with
            {
                Objectives = [clinicalDesign.Objectives[0], clinicalDesign.Objectives[1] with { Text = "Describe safety through week 12." }],
            }),
            ct);
        var unrelatedImpact = await fixture.AmendmentImpact.ComputeAsync(tenant, fixture.CurrentActor.Actor!, unrelatedAmendment, cancellationToken: ct);
        unrelatedImpact!.Dependents.Groups.SelectMany(g => g.Objects).Select(o => o.Ref).Should().BeEquivalentTo(
            [Element(study, "obj.2").AtVersion(1), Element(study, "ep.2").AtVersion(1)]);
        var simulatedUnrelated = await simulationInputs.DependentsAsync(
            tenant,
            fixture.CurrentActor.Actor!,
            baseVersion,
            unrelatedAmendment,
            new System.Collections.Generic.HashSet<NodeRef> { Element(study, "obj.2") },
            new SimulationAsOf(fixture.Clock.UtcNow, fixture.Clock.UtcNow),
            ct);
        simulatedUnrelated.Groups.SelectMany(g => g.Objects).Select(o => o.Ref).Should().BeEquivalentTo(
            [Element(study, "obj.2").AtVersion(1), Element(study, "ep.2").AtVersion(1)]);

        // 2. Editing only item.sbp flags item.sbp and its observation, assertions, and vitals form signature (plus the
        // sibling draft forked from baseVersion via whole-version DerivedFrom), while sibling item.dbp, the form instance
        // itself, and the unscheduled notes form stay untouched.
        var vitalsSpec = clinicalDesign.CollectionSpecs[0];
        var itemAmendment = await ForkAsync(
            tenant,
            baseVersion,
            ProtocolDesignDocument.Write(clinicalDesign with
            {
                CollectionSpecs =
                [
                    vitalsSpec with { Items = [vitalsSpec.Items[0] with { Required = false }, vitalsSpec.Items[1]] },
                    clinicalDesign.CollectionSpecs[1],
                ],
            }),
            ct);
        var itemImpact = await fixture.AmendmentImpact.ComputeAsync(tenant, fixture.CurrentActor.Actor!, itemAmendment, cancellationToken: ct);
        itemImpact!.ChangedElements.Select(c => c.ElementId).Should().Equal("item.sbp");
        itemImpact.Dependents.Groups.SelectMany(g => g.Objects).Select(o => o.Ref).Should().BeEquivalentTo(
            [
                NodeRef.For(unrelatedAmendment, 2),
                Element(study, "item.sbp").AtVersion(1),
                NodeRef.For(sbpObs, 1),
                NodeRef.For(sbpVerified.AssertionId, 1),
                NodeRef.For(sbpQuery.AssertionId, 1),
                NodeRef.For(sbpAnswer.AssertionId, 1),
                NodeRef.For(vitalsSignature, 1),
            ]);
        itemImpact.Dependents.Groups.SelectMany(g => g.Objects).Select(o => o.Ref).Should().NotContain(
            [
                NodeRef.For(vitalsInstance, 1),
                NodeRef.For(vitalsInstance, 2),
                NodeRef.For(dbpObs, 1),
                NodeRef.For(dbpVerified.AssertionId, 1),
                NodeRef.For(notesInstance, 1),
                NodeRef.For(notesInstance, 2),
                NodeRef.For(unaskedNote.ObservationId, 1),
                NodeRef.For(notesSignature, 1),
            ]);

        // 3. Bidirectional traceability: forward Objective -> Observation -> Assertion/Signature, and backward Answer -> Objective.
        var forwardFromObjective = await fixture.Lineage.TraceAsync(
            tenant, fixture.CurrentActor.Actor!, new LineageQuery(Element(study, "obj.1").AtVersion(1), LineageDirection.Forward, Depth: 10), ct);
        forwardFromObjective.Nodes.Select(n => n.Ref).Should().Contain(
            [
                Element(study, "ep.1").AtVersion(1),
                Element(study, "item.sbp").AtVersion(1),
                NodeRef.For(sbpObs, 1),
                NodeRef.For(sbpVerified.AssertionId, 1),
                NodeRef.For(sbpQuery.AssertionId, 1),
                NodeRef.For(sbpAnswer.AssertionId, 1),
                NodeRef.For(vitalsSignature, 1),
            ]);

        var backwardFromAnswer = await fixture.Lineage.TraceAsync(
            tenant, fixture.CurrentActor.Actor!, new LineageQuery(NodeRef.For(sbpAnswer.AssertionId, 1), LineageDirection.Backward, Depth: 10), ct);
        backwardFromAnswer.Nodes.Select(n => n.Ref).Should().Contain(
            [
                NodeRef.For(sbpQuery.AssertionId, 1),
                NodeRef.For(sbpObs, 1),
                Element(study, "item.sbp").AtVersion(1),
                Element(study, "cs.vitals").AtVersion(1),
                Element(study, "act.week12").AtVersion(1),
                Element(study, "ep.1").AtVersion(1),
                Element(study, "obj.1").AtVersion(1),
                NodeRef.For(sbpConcept.ConceptId, sbpConcept.Version),
            ]);
    }

    private static string Reworded() => ProtocolDesignDocument.Write(Design with
    {
        Objectives = [Design.Objectives[0] with { Text = "Show that the drug lowers blood pressure." }, Design.Objectives[1]],
    });

    private static NodeRef Element(StudyId study, string element) => NodeRef.For(ProtocolElementId.For(study, element));

    private async Task<ProtocolVersionId> ApproveFirstAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(tenant, study, id, ct, ProtocolDesignDocument.Write(Design))).IsSuccess.Should().BeTrue();
        await ApproveAsync(tenant, id, ct);
        return id;
    }

    private async Task<ProtocolVersionId> ForkAsync(TenantId tenant, ProtocolVersionId parent, string content, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        await _scenario.OkAsync(new ForkProtocolVersion(tenant, parent, id, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Amendment"), ct);
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, id, content, null, KernelProtocolScenario.Key(), "Reword the objective"), ct);
        return id;
    }

    private async Task ApproveAsync(TenantId tenant, ProtocolVersionId id, CancellationToken ct)
    {
        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Ready"), ct);
        var attestation = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.OkAsync(new ApproveProtocolVersion(tenant, id, attestation, KernelProtocolScenario.Key(), "Approved"), ct);
    }

    private static Task<long> ApprovedVersionIndexAsync(KernelDbContext db, ProtocolVersionId id, CancellationToken ct) =>
        db.ProtocolVersions.AsNoTracking().Where(v => v.Id == id).Select(v => v.VersionIndex).SingleAsync(ct);

    private LineageEdgeRecord Edge(TenantId tenant, NodeRef from, NodeRef to, LineageEdgeKind kind) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant,
        FromKind = from.Kind,
        FromId = from.Id,
        FromVersion = from.Version!.Value,
        ToKind = to.Kind,
        ToId = to.Id,
        ToVersion = to.Version!.Value,
        Kind = kind,
        ActivityId = Guid.CreateVersion7(),
        RecordedAt = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow),
    };
}
