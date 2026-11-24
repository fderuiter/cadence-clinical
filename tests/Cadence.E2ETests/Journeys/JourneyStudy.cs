using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

namespace Cadence.E2ETests.Journeys;

/// <summary>
/// The world a journey runs in, arranged as <see cref="KernelHostJourneyFixture.SetupActor"/>: a tenant with its own
/// code, a sponsor, one study at one site, an approved protocol version effective study-wide whose design collects vital
/// signs at week 12 and adverse events on a log form, and grants for the four journey callers. Each journey arranges its
/// own study, so the journeys share nothing but the host.
/// </summary>
internal sealed record JourneyStudy(
    string TenantCode,
    TenantId Tenant,
    StudyId Study,
    StudySiteId StudySite,
    ProtocolVersionId ProtocolVersion,
    ConceptRef SystolicBloodPressure,
    ConceptRef AdverseEventTerm,
    ConceptRef AdverseEventSerious)
{
    /// <summary>The site's coordinator: registers, screens and enrolls participants, enters and corrects data, answers queries.</summary>
    public const string Coordinator = "crc@journeys.test";

    /// <summary>The site's investigator: signs and withdraws participants.</summary>
    public const string Investigator = "investigator@journeys.test";

    /// <summary>The sponsor's monitor at the site: raises and closes queries and verifies values against source (SDV).</summary>
    public const string Monitor = "cra@journeys.test";

    /// <summary>The sponsor's data manager on the whole study: signs and applies the database lock.</summary>
    public const string DataManager = "dm@journeys.test";

    public const string VitalsSpec = "cs.vitals";
    public const string Week12 = "act.week12";
    public const string Systolic = "item.sbp";
    public const string AdverseEventSpec = "cs.ae";
    public const string AeTerm = "item.aeterm";
    public const string AeSerious = "item.aeser";

    /// <summary>The Execution module's form consumer (<c>OpenFormOnCollectionOpened</c>).</summary>
    public const string ExecutionFormsAccount = "execution-forms";

    private const string Reason = "Arranged for the journey";

    /// <summary>Arranges a new study in its own tenant.</summary>
    public static async Task<JourneyStudy> ArrangeAsync(KernelHostJourneyFixture fixture, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var code = $"T-J-{suffix}";
        var tenant = await fixture.ArrangeAsync(new ProvisionTenant(TenantId.New(), code, $"Journey tenant {suffix}", Key(), Reason), ct);
        var sponsor = await fixture.ArrangeAsync(
            new RegisterOrganization(tenant, OrganizationId.New(), $"SPONSOR-{suffix}", "Journey sponsor", OrganizationKind.Sponsor, Key(), Reason), ct);
        var study = await fixture.ArrangeAsync(
            new RegisterStudy(tenant, StudyId.New(), sponsor, $"STUDY-{suffix}", "Cardiovascular outcomes", "Phase 3", Key(), Reason), ct);
        var institution = await fixture.ArrangeAsync(
            new RegisterOrganization(tenant, OrganizationId.New(), $"INST-{suffix}", "Memorial Hospital", OrganizationKind.SiteInstitution, Key(), Reason), ct);
        var site = await fixture.ArrangeAsync(new RegisterSite(tenant, SiteId.New(), institution, $"SITE-{suffix}", "Memorial Hospital", "US", Key(), Reason), ct);
        var studySite = await fixture.ArrangeAsync(new LinkSiteToStudy(tenant, StudySiteId.New(), study, site, Key(), Reason), ct);

        var sbp = await DefineAsync(fixture, tenant, "Systolic blood pressure", ConceptKind.BiomedicalConcept, ct);
        var aeTerm = await DefineAsync(fixture, tenant, "Adverse event term", ConceptKind.BiomedicalConcept, ct);
        var aeSerious = await DefineAsync(fixture, tenant, "Adverse event seriousness", ConceptKind.BiomedicalConcept, ct);
        var version = await EffectiveVersionAsync(fixture, tenant, study, sbp, aeTerm, aeSerious, ct);

        var from = SystemClock.Instance.UtcNow.AddHours(-2);
        await GrantAsync(fixture, tenant, Coordinator, AuthorityScope.Tenant(), from, ct, "participant.register");
        await GrantAsync(fixture, tenant, Coordinator, AuthorityScope.StudySite(studySite), from, ct,
            "participant.screen", "enrollment.change_status", "collection.open", "collection.record", "observation.record", "observation.correct", "observation.query.answer");
        await GrantAsync(fixture, tenant, Investigator, AuthorityScope.Tenant(), from, ct, "attestation.sign");
        await GrantAsync(fixture, tenant, Investigator, AuthorityScope.StudySite(studySite), from, ct, "participant.withdraw", "observation.correct", "collection.sign");
        await GrantAsync(fixture, tenant, Monitor, AuthorityScope.StudySite(studySite), from, ct, "observation.query", "observation.verify", "assertion.retract");
        await GrantAsync(fixture, tenant, DataManager, AuthorityScope.Tenant(), from, ct, "attestation.sign");
        await GrantAsync(fixture, tenant, DataManager, AuthorityScope.Study(study), from, ct, "lock.apply", "study.export", "enrollment.export");

        // The Execution module's consumer opens its form when the kernel opens one, as svc:execution-forms; a tenant that
        // uses eCRF forms grants it tenant-wide, as its administrator would (apps/execution/module/README.md).
        await fixture.ArrangeAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.ServiceAccount(ExecutionFormsAccount), "execution.form.open", null, AuthorityScope.Tenant(), from, null, false,
                Key(), "Tenant uses eCRF forms"), ct);

        return new JourneyStudy(code, tenant, study, studySite, version, sbp, aeTerm, aeSerious);
    }

    public static string Key() => $"journey-arrange-{Guid.NewGuid():N}";

    private static Task<ConceptRef> DefineAsync(KernelHostJourneyFixture fixture, TenantId tenant, string label, ConceptKind kind, CancellationToken ct) =>
        fixture.ArrangeAsync(new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, kind, label, $"{label} (definition).", Key(), Reason), ct);

    /// <summary>
    /// Approves a version whose design collects systolic pressure at week 12 and adverse events on a log form, and makes it
    /// effective study-wide on an ethics approval, from two hours ago in valid time.
    /// </summary>
    private static async Task<ProtocolVersionId> EffectiveVersionAsync(
        KernelHostJourneyFixture fixture, TenantId tenant, StudyId study, ConceptRef sbp, ConceptRef aeTerm, ConceptRef aeSerious, CancellationToken ct)
    {
        var design = new ProtocolDesign
        {
            Title = "JOURNEY-1",
            Arms =
            [
                new Arm("arm.A", "Active", ArmType.Experimental, []),
                new Arm("arm.B", "Placebo", ArmType.PlaceboComparator, []),
            ],
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(Week12, "Week 12 visit", "ep.treatment", [ActivityLocation.Site], [VitalsSpec], new ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(VitalsSpec, "Vital signs", [new CollectionItem(Systolic, sbp, "mm[Hg]", Required: true)]),
                new CollectionSpec(
                    AdverseEventSpec,
                    "Adverse events",
                    [
                        new CollectionItem(AeTerm, aeTerm, Required: true, ValueKind: ObservationValueKind.Text),
                        new CollectionItem(AeSerious, aeSerious, Required: true, ValueKind: ObservationValueKind.Boolean),
                    ]),
            ],
        };

        var id = ProtocolVersionId.New();
        await fixture.ArrangeAsync(new CreateProtocolDraft(tenant, study, id, ProtocolDesignDocument.Write(design), Key(), Reason), ct);
        var inReview = await fixture.ArrangeAsync(new SubmitProtocolVersionForReview(tenant, id, Key(), Reason), ct);
        var signature = await fixture.ArrangeAsync(
            new Attest(tenant, AttestationId.New(), AttestationMeaning.Approved,
                new AttestationTarget(AttestationTarget.ProtocolVersionKind, id.Value, inReview.ContentHash!),
                SystemClock.Instance.UtcNow.AddMinutes(-1), "pwd+otp", Key(), "Approving the protocol"),
            ct);
        await fixture.ArrangeAsync(new ApproveProtocolVersion(tenant, id, signature, Key(), Reason), ct);

        var bytes = $"ethics-{Guid.NewGuid():N}";
        var ethics = ArtifactVersionId.New();
        await fixture.ArrangeAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), ethics, ArtifactKind.EthicsApproval, study, "Ethics approval letter",
                new ArtifactContent(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), bytes.Length, "application/pdf", $"blob://evidence/{bytes}"), Key(), Reason),
            ct);
        var effectivity = EffectivityId.New();
        var from = SystemClock.Instance.UtcNow.AddHours(-2);
        await fixture.ArrangeAsync(new DeclareEffectivity(tenant, effectivity, id, EffectivityScope.Study(), from, Key(), Reason), ct);
        await fixture.ArrangeAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, from.AddMinutes(1), [ethics], Key(), "Ethics approval received"), ct);
        return id;
    }

    private static async Task GrantAsync(
        KernelHostJourneyFixture fixture, TenantId tenant, string person, AuthorityScope scope, DateTimeOffset from, CancellationToken ct, params string[] capabilities)
    {
        foreach (var capability in capabilities)
        {
            await fixture.ArrangeAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(person), capability, null, scope, from, null, false, Key(), "Appointed to the study"), ct);
        }
    }
}
