using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

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
    public const string Position = "item.position";
    public const string AdverseTerm = "item.ae_llt";
    public const string PositionCodelist = "C71148";
    public const string SaveReason = "Entered from the source document";

    public KernelObservationScenario Observations { get; } = new(fixture);

    public KernelProtocolScenario Protocol => Observations.Protocol;

    /// <summary>A study with an imported code-system version, coded collection items, and an open form instance.</summary>
    public async Task<(Form Form, CodeSystemVersionId CodeSystemVersion)> NewCodedFormAsync(CancellationToken ct)
    {
        var (tenant, study) = await Protocol.NewStudyAsync(ct);
        var codeSystemId = await Protocol.OkAsync(
            new RegisterCodeSystem(
                tenant,
                CodeSystemId.New(),
                "https://terminology.sponsor.test/vitals",
                "Sponsor Vitals Terminology",
                "Sponsor",
                LicenseClass.Open,
                KernelProtocolScenario.Key(),
                "Register terminology"),
            ct);
        var csVersion = await Protocol.OkAsync(
            new ImportCodeSystemVersion(
                tenant,
                CodeSystemVersionId.New(),
                codeSystemId,
                "2026-03",
                new DateOnly(2026, 3, 1),
                new TerminologyPackage(new string('a', 64), "vitals-ct.json", "https://terminology.sponsor.test/vitals/2026-03.json", 512, "json"),
                [
                    new CodeSystemEntryData("SUPINE", "Supine", PositionCodelist, "SUPINE"),
                    new CodeSystemEntryData("SITTING", "Sitting", PositionCodelist, "SITTING"),
                    new CodeSystemEntryData("Y", "Yes", "C66742", "Y"),
                    new CodeSystemEntryData("LLT_HEADACHE", "Headache"),
                ],
                KernelProtocolScenario.Key(),
                "Import terminology version"),
            ct);

        var systolic = await DefineAsync(tenant, "Systolic blood pressure", ct);
        var position = await DefineAsync(tenant, "Body position", ct);
        var aeTerm = await DefineAsync(tenant, "Adverse event term", ct);
        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(VisitId, "Week 12 visit", "ep.treatment", [ActivityLocation.Site], [SpecId], new ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs",
                    [
                        new CollectionItem(Systolic, systolic, "mm[Hg]", Required: true),
                        new CollectionItem(Position, position, Terminology: new TerminologyBinding(csVersion, PositionCodelist), ValueKind: ObservationValueKind.Coded, Required: true),
                        new CollectionItem(AdverseTerm, aeTerm, Terminology: new TerminologyBinding(csVersion), ValueKind: ObservationValueKind.Coded),
                    ]),
            ],
        };

        var version = ProtocolVersionId.New();
        var draft = await Protocol.CreateDraftAsync(tenant, study, version, ct, ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, version, KernelProtocolScenario.Key(), "Ready"), ct);
        var signature = await Protocol.OkAsync(Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await Protocol.OkAsync(new ApproveProtocolVersion(tenant, version, signature, KernelProtocolScenario.Key(), "Approved"), ct);

        var effectivity = EffectivityId.New();
        await Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, version, EffectivityScope.Study(), Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethics = await EthicsApprovalAsync(tenant, study, ct);
        await Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), [ethics], KernelProtocolScenario.Key(), "Ethics approval received"),
            ct);

        var studySite = await Observations.AddStudySiteAsync(tenant, study, ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, await Observations.ScreenAsync(tenant, studySite, ct));
        var opened = await Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), Tick()), ct);
        return (new Form(subject, version, opened.CollectionInstanceId), csVersion);
    }

    /// <summary>A study with an effective protocol version, an enrollment and an open form instance in a new tenant.</summary>
    public async Task<Form> NewFormAsync(CancellationToken ct, bool repeatingSpec = false)
    {
        var (tenant, study) = await Protocol.NewStudyAsync(ct);
        var version = await EffectiveVersionAsync(tenant, study, ct, repeatingSpec: repeatingSpec);
        var studySite = await Observations.AddStudySiteAsync(tenant, study, ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, await Observations.ScreenAsync(tenant, studySite, ct));
        var opened = await Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), Tick()), ct);
        return new Form(subject, version, opened.CollectionInstanceId);
    }

    /// <summary>
    /// Approves a version whose design collects systolic and diastolic pressure in mm[Hg] and a free-text comment, and makes
    /// it effective study-wide on an ethics approval. Returns the version.
    /// </summary>
    public async Task<ProtocolVersionId> EffectiveVersionAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken ct,
        bool requireDiastolic = true,
        bool repeatingSpec = false)
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
                    ],
                    Repeating: repeatingSpec),
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

    /// <summary>
    /// Source-verifies every recorded member observation on <paramref name="form"/> at its current revision (if not
    /// already verified) and signs the form's current content hash with <see cref="AttestationMeaning.Approved"/>.
    /// </summary>
    public async Task VerifyAndSignAsync(Form form, CancellationToken ct)
    {
        var tenant = form.Subject.Tenant;
        var enrollment = form.Subject.Enrollment;
        System.Collections.Generic.List<Observation> memberObservations;
        System.Collections.Generic.HashSet<(ObservationId, int)> alreadyVerified;

        await using (var db = fixture.CreateContext(tenant))
        {
            var memberIds = await db.CollectionInstances.AsNoTracking()
                .Where(c => c.Id == form.Instance)
                .SelectMany(c => c.Members.Select(m => m.ObservationId))
                .ToListAsync(ct);
            memberObservations = await db.Observations.AsNoTracking()
                .Where(o => memberIds.Contains(o.Id))
                .ToListAsync(ct);
            var obsGuids = memberObservations.Select(o => o.Id.Value).ToList();
            var verifiedRows = await db.Assertions.AsNoTracking()
                .Where(a => a.Kind == AssertionKind.Verified && a.RetractsId == null && a.TargetKind == NodeKinds.Observation && obsGuids.Contains(a.TargetId))
                .ToListAsync(ct);
            var verifiedIds = verifiedRows.Select(a => a.Id).ToList();
            var retractedIds = verifiedIds.Count == 0
                ? []
                : await db.Assertions.AsNoTracking()
                    .Where(a => a.RetractsId != null && verifiedIds.Contains(a.RetractsId.Value))
                    .Select(a => a.RetractsId!.Value)
                    .ToListAsync(ct);
            var retractedSet = retractedIds.ToHashSet();
            alreadyVerified = verifiedRows
                .Where(a => !retractedSet.Contains(a.Id) && a.TargetRevision is not null)
                .Select(a => (ObservationId.FromGuid(a.TargetId), a.TargetRevision!.Value))
                .ToHashSet();
        }

        foreach (var obs in memberObservations.Where(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked }))
        {
            if (!alreadyVerified.Contains((obs.Id, obs.Revision)))
            {
                await Protocol.OkAsync(
                    new VerifyObservation(tenant, AssertionId.New(), enrollment, obs.Id, obs.Revision, KernelProtocolScenario.Key(), "Verified against source chart"),
                    ct);
            }
        }

        if (memberObservations.Any(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked }))
        {
            var hash = CollectionInstance.ContentHashOf(
                memberObservations.Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value)));
            await Protocol.OkAsync(
                new SignCollectionInstance(
                    tenant,
                    AttestationId.New(),
                    form.Instance,
                    enrollment,
                    AttestationMeaning.Approved,
                    hash,
                    Tick(),
                    "pwd+otp",
                    KernelProtocolScenario.Key(),
                    "Investigator approved form"),
                ct);
        }
    }

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
