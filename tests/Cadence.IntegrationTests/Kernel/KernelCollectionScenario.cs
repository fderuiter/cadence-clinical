using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Builds form scenarios through the production pipeline (ADR-2622): a study whose approved protocol version has a vital
/// signs CollectionSpec and is effective study-wide, an enrollment at one of its sites, and an open form instance.
/// </summary>
internal sealed class KernelCollectionScenario(KernelPostgresFixture fixture)
{
    public const string SpecId = "cs.vitals";
    public const string VisitId = "act.week12";
    public const string Systolic = "item.sbp";
    public const string Diastolic = "item.dbp";
    public const string Comment = "item.comment";
    public const string SaveReason = "Entered from the source document";

    public KernelObservationScenario Observations { get; } = new(fixture);

    public KernelProtocolScenario Protocol => Observations.Protocol;

    /// <summary>A study with an effective protocol version, an enrollment and an open form instance in a new tenant.</summary>
    public async Task<Form> NewFormAsync(CancellationToken ct)
    {
        var (tenant, study) = await Protocol.NewStudyAsync(ct);
        var version = await EffectiveVersionAsync(tenant, study, ct);
        var studySite = await Observations.AddStudySiteAsync(tenant, study, ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, await Observations.ScreenAsync(tenant, studySite, ct));
        var opened = await Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), Tick()), ct);
        return new Form(subject, version, opened.CollectionInstanceId);
    }

    /// <summary>
    /// Approves a version whose design collects systolic and diastolic pressure in mm[Hg] and a free-text comment, and makes
    /// it effective study-wide on an ethics approval. Returns the version.
    /// </summary>
    public async Task<ProtocolVersionId> EffectiveVersionAsync(TenantId tenant, StudyId study, CancellationToken ct, bool requireDiastolic = true)
    {
        var systolic = await DefineAsync(tenant, "Systolic blood pressure", ct);
        var diastolic = await DefineAsync(tenant, "Diastolic blood pressure", ct);
        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity("act.week12", "Week 12 visit", "ep.treatment", [ActivityLocation.Site], [SpecId], new ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs",
                    [
                        new CollectionItem(Systolic, systolic, "mm[Hg]", Required: true),
                        new CollectionItem(Diastolic, diastolic, "mm[Hg]", Required: requireDiastolic),
                        new CollectionItem(Comment, systolic, ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };

        var id = ProtocolVersionId.New();
        var draft = await Protocol.CreateDraftAsync(tenant, study, id, ct, ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Ready"), ct);
        var signature = await Protocol.OkAsync(Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await Protocol.OkAsync(new ApproveProtocolVersion(tenant, id, signature, KernelProtocolScenario.Key(), "Approved"), ct);

        var effectivity = EffectivityId.New();
        await Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, id, EffectivityScope.Study(), Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethics = await EthicsApprovalAsync(tenant, study, ct);
        await Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), [ethics], KernelProtocolScenario.Key(), "Ethics approval received"),
            ct);
        return id;
    }

    public static OpenCollectionInstance Open(
        KernelObservationScenario.Subject subject, CollectionInstanceId id, DateTimeOffset collectedAt, string specId = SpecId, string? visit = VisitId) =>
        new(subject.Tenant, id, subject.Enrollment, specId, visit, collectedAt, KernelProtocolScenario.Key(), "Week 12 visit performed");

    /// <summary>A page save of <paramref name="values"/>.</summary>
    public static RecordCollectedValues Save(Form form, params CollectedValue[] values) =>
        new(form.Subject.Tenant, form.Instance, form.Subject.Enrollment, values, KernelProtocolScenario.Key(), SaveReason);

    public static CollectedValue Value(string item, ObservationValue value, int expectedRevision = 0) => new(item, value, expectedRevision);

    public static QuantityValue Mmhg(decimal value) => KernelObservationScenario.Mmhg(value);

    public DateTimeOffset Tick() => Observations.Tick();

    private async Task<ConceptRef> DefineAsync(TenantId tenant, string label, CancellationToken ct) =>
        await Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, label, $"{label} (definition).", KernelProtocolScenario.Key(), "Defined for the form"),
            ct);

    private async Task<ArtifactVersionId> EthicsApprovalAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var bytes = $"ethics-{Guid.NewGuid():N}";
        var version = ArtifactVersionId.New();
        var content = new ArtifactContent(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), bytes.Length, "application/pdf", $"blob://evidence/{bytes}");
        await Protocol.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), version, ArtifactKind.EthicsApproval, study, "Ethics approval letter", content, KernelProtocolScenario.Key(), "Filed approval"),
            ct);
        return version;
    }

    /// <summary>An open form instance, the enrollment it belongs to and the protocol version that governs it.</summary>
    internal sealed record Form(KernelObservationScenario.Subject Subject, ProtocolVersionId Version, CollectionInstanceId Instance)
    {
        /// <summary>The lock scope of <paramref name="kind"/> covering the form and <paramref name="observation"/>.</summary>
        public NodeRef Scope(string kind, ObservationId observation) =>
            kind == NodeKinds.CollectionInstance ? NodeRef.For(Instance) : Subject.Scope(kind, observation);
    }
}
