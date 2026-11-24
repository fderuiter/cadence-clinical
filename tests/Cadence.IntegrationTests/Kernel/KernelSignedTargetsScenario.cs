using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Intelligence;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.TestSupport.Kernel.Rules;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Builds a tenant and study exercising all 10 signed attestation target kinds through the kernel command pipeline,
/// including study vs tenant artifact versions, recorded vs unrecorded lock assertions, observations and assertions.
/// </summary>
internal sealed class KernelSignedTargetsScenario(KernelPostgresFixture fixture)
{
    private const string Reason = "Signed targets scenario";
    private const string PackKey = "signed-targets-pack";

    public async Task<SignedTargetsWorld> CreateAsync(CancellationToken ct)
    {
        var forms = new KernelCollectionScenario(fixture);
        var form = await forms.NewFormAsync(ct);
        var tenant = form.Subject.Tenant;
        var study = form.Subject.Study;
        var studySite = form.Subject.StudySite;
        var enrollment = form.Subject.Enrollment;

        // 1. protocol_version (already signed during EffectiveVersionAsync; fetch the recorded approval attestation).
        AttestationId protocolVersionAttestation;
        await using (var db = fixture.CreateContext(tenant))
        {
            protocolVersionAttestation = await db.Attestations.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.TargetKind == NodeKinds.ProtocolVersion && a.TargetId == form.Version.Value)
                .Select(a => a.Id)
                .SingleAsync(ct);
        }

        // 2. standard_version & 3. library_item
        var standardVersion = await forms.Protocol.OkAsync(
            new RegisterStandardVersion(
                tenant,
                StandardVersionId.New(),
                StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package,
                null,
                KernelProtocolScenario.Key(),
                Reason),
            ct);
        var standardHash = (await fixture.Standards.FindStandardVersionAsync(tenant, standardVersion, ct))!.ContentHash;
        var standardAttestation = await SignAsync(
            tenant,
            AttestationMeaning.Approved,
            new AttestationTarget(NodeKinds.StandardVersion, standardVersion.Value, standardHash),
            ct);
        await forms.Protocol.OkAsync(new ApproveStandardVersion(tenant, standardVersion, standardAttestation, KernelProtocolScenario.Key(), Reason), ct);

        var draftedPack = await forms.Protocol.OkAsync(
            new DraftLibraryItem(
                tenant,
                LibraryItemId.New(),
                LibraryItemKind.RulePack,
                PackKey,
                "Signed targets rule pack",
                RulePackJson.ToJson(SampleRulePacks.Sample()),
                [],
                KernelProtocolScenario.Key(),
                Reason),
            ct);
        var libraryItem = draftedPack.Id;
        var libraryItemAttestation = await SignAsync(
            tenant,
            AttestationMeaning.Approved,
            new AttestationTarget(NodeKinds.LibraryItem, libraryItem.Value, draftedPack.ContentHash),
            ct);
        await forms.Protocol.OkAsync(new ApproveLibraryItem(tenant, libraryItem, libraryItemAttestation, KernelProtocolScenario.Key(), Reason), ct);
        await forms.Protocol.OkAsync(
            new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [standardVersion], [], [libraryItem], KernelProtocolScenario.Key(), Reason),
            ct);

        // 4. enrollment
        AttestationTarget enrollmentTarget;
        await using (var db = fixture.CreateContext(tenant))
        {
            enrollmentTarget = (await db.Enrollments.AsNoTracking().SingleAsync(e => e.Id == enrollment, ct)).SignableTarget;
        }

        var enrollmentAttestation = await SignAsync(tenant, AttestationMeaning.Reviewed, enrollmentTarget, ct);

        // 5. artifact_version (study-scoped and tenant-wide)
        var studyArtifact = ArtifactId.New();
        var studyArtifactVersion = ArtifactVersionId.New();
        var studyArtifactTarget = await RegisterAndTargetArtifactAsync(forms.Protocol, tenant, studyArtifact, studyArtifactVersion, study, "study-signed-doc", ct);
        var studyArtifactAttestation = await SignAsync(tenant, AttestationMeaning.Approved, studyArtifactTarget, ct);

        var tenantArtifact = ArtifactId.New();
        var tenantArtifactVersion = ArtifactVersionId.New();
        var tenantArtifactTarget = await RegisterAndTargetArtifactAsync(forms.Protocol, tenant, tenantArtifact, tenantArtifactVersion, null, "tenant-signed-sop", ct);
        var tenantArtifactAttestation = await SignAsync(tenant, AttestationMeaning.Approved, tenantArtifactTarget, ct);

        // 6. collection_instance + member observation + standalone observation + verification assertion
        var saved = await forms.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                form,
                KernelCollectionScenario.Value(KernelCollectionScenario.Systolic, KernelCollectionScenario.Mmhg(122)),
                KernelCollectionScenario.Value(KernelCollectionScenario.Diastolic, KernelCollectionScenario.Mmhg(78))),
            ct);
        var formObservation = saved.Values.Single(v => v.ItemId == KernelCollectionScenario.Systolic).ObservationId;
        foreach (var savedItem in saved.Values)
        {
            await forms.Protocol.OkAsync(
                new VerifyObservation(tenant, AssertionId.New(), enrollment, savedItem.ObservationId, savedItem.Revision, KernelProtocolScenario.Key(), Reason),
                ct);
        }

        string collectionHash;
        await using (var db = fixture.CreateContext(tenant))
        {
            var members = await db.CollectionInstances.AsNoTracking()
                .Where(c => c.Id == form.Instance)
                .SelectMany(c => c.Members.Select(m => m.ObservationId))
                .ToListAsync(ct);
            var observations = await db.Observations.AsNoTracking()
                .Where(o => members.Contains(o.Id))
                .ToListAsync(ct);
            collectionHash = CollectionInstance.ContentHashOf(
                observations.Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value)));
        }

        var collectionAttestation = AttestationId.New();
        await forms.Protocol.OkAsync(
            new SignCollectionInstance(
                tenant,
                collectionAttestation,
                form.Instance,
                enrollment,
                AttestationMeaning.Responsibility,
                collectionHash,
                forms.Tick(),
                "pwd+otp",
                KernelProtocolScenario.Key(),
                Reason),
            ct);

        var standaloneConcept = await forms.Protocol.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                Cadence.Kernel.Knowledge.ConceptOrigin.Sponsor,
                null,
                Cadence.Kernel.Knowledge.ConceptKind.BiomedicalConcept,
                "Heart rate",
                "Heart rate measurement.",
                KernelProtocolScenario.Key(),
                Reason),
            ct);
        var standaloneObservationState = await forms.Protocol.OkAsync(
            new RecordObservation(
                tenant,
                ObservationId.New(),
                enrollment,
                standaloneConcept,
                new QuantityValue(68m, "/min"),
                forms.Tick().AddMinutes(-2),
                KernelProtocolScenario.Key(),
                Reason),
            ct);
        var standaloneObservation = standaloneObservationState.ObservationId;
        var verificationAssertionState = await forms.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollment, standaloneObservation, 1, KernelProtocolScenario.Key(), Reason),
            ct);

        // 7. assertion: recorded lock assertion + unrecorded lock assertion (Attest committed before a failed/unrun ApplyLock)
        var lockCommand = await forms.Observations.LockAsync(tenant, study, NodeRef.For(form.Instance), ct);
        await forms.Protocol.OkAsync(lockCommand, ct);
        var recordedLockAssertion = lockCommand.AssertionId;
        var recordedLockAttestation = lockCommand.AttestationId;

        var unrecordedLockAssertion = AssertionId.New();
        var unrecordedLockAttestation = await SignAsync(
            tenant,
            AttestationMeaning.Approved,
            LockStatement.Target(
                unrecordedLockAssertion,
                LockStatement.Apply(unrecordedLockAssertion, LockKind.Frozen, NodeRef.For(study))),
            ct);

        // 8. proposal
        var proposal = ProposalId.New();
        var previousActor = fixture.CurrentActor.Actor;
        try
        {
            fixture.CurrentActor.Actor = Actor.AutomationAgent("signed-targets-agent").ActingFor(previousActor!);
            var proposed = await fixture.Sender.SendAsync(
                new Propose(
                    tenant,
                    proposal,
                    study,
                    "Propose protocol clarification",
                    studyArtifactVersion,
                    [NodeRef.For(study)],
                    ContentHash.Compute("""{"simulation":"signed-targets"}"""),
                    ["protocol.approve"],
                    fixture.Clock.UtcNow.AddDays(3),
                    "claude-opus-5",
                    KernelProtocolScenario.Key(),
                    Reason),
                cancellationToken: ct);
            proposed.IsSuccess.Should().BeTrue(proposed.Error?.Message);
        }
        finally
        {
            fixture.CurrentActor.Actor = previousActor;
        }

        AttestationTarget proposalTarget;
        await using (var db = fixture.CreateContext(tenant))
        {
            proposalTarget = (await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposal, ct)).SignableTarget;
        }

        var proposalAttestation = await SignAsync(tenant, AttestationMeaning.Approved, proposalTarget, ct);
        await forms.Protocol.OkAsync(new ApproveProposal(tenant, proposal, proposalAttestation, KernelProtocolScenario.Key(), Reason), ct);

        // 9. validation_finding
        forms.Tick();
        var validationRequest = new ValidationRequest(
            tenant,
            study,
            ValidationStage.Standardization,
            new DateOnly(2026, 9, 30),
            [
                new RuleRecord("VITALS", "row-a", new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["SYS"] = RuleValue.FromNumber(90m),
                    ["DIA"] = RuleValue.FromNumber(100m),
                }),
            ],
            KernelProtocolScenario.Key(),
            Reason)
        {
            Standard = "SDTMIG",
            StandardVersion = "3.4",
            Datasets = ["VITALS"],
        };
        var validationOutcome = (await fixture.Validation.RunAsync(validationRequest, ct)).Value!;
        var finding = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct)).Findings.Single(f => f.RuleId == "R1").Id;
        const string FindingExplanation = "Confirmed reversible cuff placement during review.";
        var findingAttestation = await SignAsync(
            tenant,
            AttestationMeaning.Approved,
            new AttestationTarget(
                NodeKinds.ValidationFinding,
                finding.Value,
                FindingDecisionContent.Hash(finding, validationOutcome.RunId, FindingStatus.Explained, FindingExplanation)),
            ct);
        await forms.Protocol.OkAsync(new ExplainFinding(tenant, finding, FindingExplanation, findingAttestation, KernelProtocolScenario.Key(), Reason), ct);

        // 10. task_configuration (attested vs unattested)
        var echoTask = IntelligenceModulePostgresFixture.EchoTask;
        var attestedTaskConfiguration = TaskConfigurationId.FromGuid(TaskConfiguration.AttestationTargetId(echoTask.Key, echoTask.Version));
        var taskConfigurationAttestation = await SignAsync(
            tenant,
            AttestationMeaning.Approved,
            new AttestationTarget(NodeKinds.TaskConfiguration, attestedTaskConfiguration.Value, echoTask.ContentHash),
            ct);
        var unattestedTaskConfiguration = TaskConfigurationId.FromGuid(TaskConfiguration.AttestationTargetId("test.unattested", 1));

        return new SignedTargetsWorld(
            tenant,
            study,
            studySite,
            enrollment,
            form.Version,
            protocolVersionAttestation,
            standardVersion,
            standardAttestation,
            libraryItem,
            libraryItemAttestation,
            enrollmentAttestation,
            studyArtifact,
            studyArtifactVersion,
            studyArtifactAttestation,
            tenantArtifact,
            tenantArtifactVersion,
            tenantArtifactAttestation,
            form.Instance,
            collectionAttestation,
            formObservation,
            standaloneObservation,
            verificationAssertionState.AssertionId,
            recordedLockAssertion,
            recordedLockAttestation,
            unrecordedLockAssertion,
            unrecordedLockAttestation,
            proposal,
            proposalAttestation,
            finding,
            findingAttestation,
            attestedTaskConfiguration,
            taskConfigurationAttestation,
            unattestedTaskConfiguration);
    }

    private async Task<AttestationId> SignAsync(TenantId tenant, AttestationMeaning meaning, AttestationTarget target, CancellationToken ct)
    {
        var result = await fixture.Sender.SendAsync(
            new Attest(tenant, AttestationId.New(), meaning, target, fixture.Clock.UtcNow, "pwd+otp", KernelProtocolScenario.Key(), Reason),
            cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private static async Task<AttestationTarget> RegisterAndTargetArtifactAsync(
        KernelProtocolScenario protocol,
        TenantId tenant,
        ArtifactId artifact,
        ArtifactVersionId version,
        StudyId? study,
        string label,
        CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(label);
        var hash = ContentHash.OfBytes(bytes);
        var content = new ArtifactContent(hash, bytes.Length, "application/pdf", $"blob://evidence/{label}");
        await protocol.OkAsync(
            new RegisterArtifact(tenant, artifact, version, ArtifactKind.EthicsApproval, study, label, content, KernelProtocolScenario.Key(), Reason),
            ct);
        return new AttestationTarget(NodeKinds.ArtifactVersion, version.Value, hash);
    }
}

internal sealed record SignedTargetsWorld(
    TenantId Tenant,
    StudyId Study,
    StudySiteId StudySite,
    EnrollmentId Enrollment,
    ProtocolVersionId ProtocolVersion,
    AttestationId ProtocolVersionAttestation,
    StandardVersionId StandardVersion,
    AttestationId StandardVersionAttestation,
    LibraryItemId LibraryItem,
    AttestationId LibraryItemAttestation,
    AttestationId EnrollmentAttestation,
    ArtifactId StudyArtifact,
    ArtifactVersionId StudyArtifactVersion,
    AttestationId StudyArtifactAttestation,
    ArtifactId TenantArtifact,
    ArtifactVersionId TenantArtifactVersion,
    AttestationId TenantArtifactAttestation,
    CollectionInstanceId CollectionInstance,
    AttestationId CollectionInstanceAttestation,
    ObservationId FormObservation,
    ObservationId StandaloneObservation,
    AssertionId VerificationAssertion,
    AssertionId RecordedLockAssertion,
    AttestationId RecordedLockAttestation,
    AssertionId UnrecordedLockAssertion,
    AttestationId UnrecordedLockAttestation,
    ProposalId Proposal,
    AttestationId ProposalAttestation,
    ValidationFindingId ValidationFinding,
    AttestationId ValidationFindingAttestation,
    TaskConfigurationId AttestedTaskConfiguration,
    AttestationId TaskConfigurationAttestation,
    TaskConfigurationId UnattestedTaskConfiguration);
