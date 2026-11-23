using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Database.Signatures;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Authorization;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Kernel.Validation;

/// <summary>
/// Unit and HTTP host tests for <see cref="KernelAuthorityStore"/> scope paths (<c>library_item</c>, <c>validation_finding</c>),
/// <see cref="KernelStandardsRegistry"/> <c>GovernedBy</c> lineage, <see cref="KernelCollectionInstanceRecords"/> signature-preserving
/// <see cref="NullFlavor.NotAsked"/> placeholders, <c>KernelStudyImportEndpoints</c> ODM rule-pack governance, and
/// <c>KernelValidationEndpoints</c> site isolation, blinding redaction, <c>site_code</c>/<c>subject_code</c>, pagination, and finding decisions.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ValidationEndpointsAndAuthorityTests : IDisposable
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Epoch = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly GatewayIdentity DataManagerIdentity = TestSecurityContext.Identity("dm.study", "Data Manager");
    private static readonly GatewayIdentity Site101CrcIdentity = TestSecurityContext.Identity("crc.101", "Clinical Research Coordinator");
    private static readonly GatewayIdentity UnblindedMonitorIdentity = TestSecurityContext.Identity("monitor.unblind", "Clinical Research Associate");

    private const string OdmWithRangeCheckXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.RULES.1" FileType="Snapshot" ODMVersion="1.3.2" CreationDateTime="2026-02-01T00:00:00Z" Originator="Sponsor EDC">
          <Study OID="S.CADENCE">
            <GlobalVariables>
              <StudyName>Cardiovascular Outcomes Protocol</StudyName>
              <StudyDescription>Demo</StudyDescription>
              <ProtocolName>CADENCE-101</ProtocolName>
            </GlobalVariables>
            <MetaDataVersion OID="MDV.1" Name="Draft 1">
              <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
              </FormDef>
              <ItemGroupDef OID="IG.VS" Name="Vital Signs" Repeating="No">
                <ItemRef ItemOID="IT.SYS" OrderNumber="1" Mandatory="Yes"/>
              </ItemGroupDef>
              <ItemDef OID="IT.SYS" Name="SYSBP" DataType="integer">
                <Alias Context="prompt" Name="Systolic Blood Pressure"/>
                <RangeCheck Comparator="LE" SoftHard="Soft">
                  <CheckValue>220</CheckValue>
                  <ErrorMessage><TranslatedText xml:lang="en">Systolic BP exceeds 220 mmHg.</TranslatedText></ErrorMessage>
                </RangeCheck>
              </ItemDef>
            </MetaDataVersion>
          </Study>
        </ODM>
        """;

    private readonly List<WebApplicationFactory<Cadence.Kernel.Host.Program>> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    [Fact(DisplayName = "KernelAuthorityStore_AndStandardsRegistry_ResolveStudyAndSiteScopePaths_ForLibraryItemsAndFindings")]
    public async Task KernelAuthorityStoreAndStandardsRegistryResolveStudyAndSiteScopePathsForLibraryItemsAndFindings()
    {
        var ct = TestContext.Current.CancellationToken;
        using var persistence = new InMemoryKernelPersistence();

        var sponsorId = OrganizationId.New();
        var institutionId = OrganizationId.New();
        var studyId = StudyId.New();
        var siteId = SiteId.New();
        var studySiteId = StudySiteId.New();
        var protocolVersionId = ProtocolVersionId.New();
        var governedPackId = LibraryItemId.New();
        var unlinkedPackId = LibraryItemId.New();

        await using var db = persistence.Factory.Create(Tenant);
        db.Organizations.Add(new Organization(sponsorId, Tenant, "SPON", "Sponsor", OrganizationKind.Sponsor, "seed", "Seed"));
        db.Organizations.Add(new Organization(institutionId, Tenant, "INST", "Hospital", OrganizationKind.SiteInstitution, "seed", "Seed"));
        db.Studies.Add(new Study(studyId, Tenant, sponsorId, "CAD-101", "Study", "Phase 3", "seed", "Seed"));
        db.Sites.Add(new Site(siteId, Tenant, institutionId, "SITE-101", "Boston", "US", "seed", "Seed"));
        db.StudySites.Add(new StudySite(studySiteId, Tenant, studyId, siteId, "Active", "seed", "Seed"));

        var pv = ProtocolVersion.CreateFirst(protocolVersionId, Tenant, studyId, ProtocolDesignDocument.Write(new ProtocolDesign { Title = "CAD-101" })).Value!;
        db.ProtocolVersions.Add(pv);

        using var packJson = JsonDocument.Parse("""
            {
              "authority": "Study",
              "title": "Study Pack",
              "rules": [
                {
                  "id": "VS-1",
                  "version": 1,
                  "citation": { "document": "Protocol CAD-101", "section": "6.1" },
                  "severity": "Warning",
                  "applicability": { "stage": "Collection", "dataset": "VS" },
                  "condition": { "op": ">", "args": [{ "var": "SYSBP" }, 180] },
                  "explanation": "High BP"
                }
              ]
            }
            """);
        var governedPack = LibraryItem.Draft(
            governedPackId, Tenant, LibraryItemKind.RulePack, "study-pack", 1, "Study Pack", packJson.RootElement, []).Item!;
        var unlinkedPack = LibraryItem.Draft(
            unlinkedPackId, Tenant, LibraryItemKind.RulePack, "shared-pack", 1, "Shared Pack", packJson.RootElement, []).Item!;
        db.Set<LibraryItem>().AddRange(governedPack, unlinkedPack);

        db.Set<LineageEdgeRecord>().Add(new LineageEdgeRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant,
            FromKind = NodeKinds.ProtocolVersion,
            FromId = protocolVersionId.Value,
            FromVersion = 1,
            ToKind = NodeKinds.LibraryItem,
            ToId = governedPackId.Value,
            ToVersion = 1,
            Kind = LineageEdgeKind.GovernedBy,
            ActivityId = Guid.CreateVersion7(),
            RecordedAt = Epoch,
        });

        var enrollment = Enrollment.Screen(
            EnrollmentId.New(), Tenant, ParticipantId.New(), studyId, studySiteId, SubjectCode.From("SUBJ-101-001"), Epoch, Epoch).Value!;
        db.Enrollments.Add(enrollment);
        var form = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment, protocolVersionId, "VS", "act.v1", Epoch, "crc", Epoch);
        db.CollectionInstances.Add(form);

        var runCommand = new RecordValidationRun(
            Tenant,
            ValidationRunId.New(),
            studyId,
            ValidationStage.Collection,
            [governedPackId],
            DateOnly.FromDateTime(Epoch.UtcDateTime),
            null,
            null,
            null,
            [],
            ["VS"],
            Epoch,
            Epoch,
            [
                new ValidationDetection(
                    "study-pack",
                    "VS-1",
                    1,
                    ValidationSeverity.Warning,
                    new FindingTarget(FindingTargetKind.DatasetRecord, "VS", form.Id.Value.ToString("D"), "SYSBP"),
                    "High BP",
                    null,
                    []),
            ],
            "run-1",
            "Seed run");
        var run = ValidationRun.Record(
            Tenant,
            runCommand,
            [new RunRulePack(governedPackId, "study-pack", 1, governedPack.ContentHash, 1)],
            Epoch).Run!;
        db.Set<ValidationRun>().Add(run);

        var studyLevelFinding = ValidationFinding.Start(ValidationFindingId.New(), Tenant, studyId);
        db.Set<ValidationFinding>().Add(studyLevelFinding);
        using (db.PermitPipelineWrites())
        {
            await db.SaveChangesAsync(ct);
        }

        var siteFindingId = run.Detections.Single().FindingId;
        var store = new KernelAuthorityStore(persistence.Factory);
        var paths = await store.PathsOfAsync(
            Tenant,
            [
                NodeRef.For(governedPackId),
                NodeRef.For(unlinkedPackId),
                NodeRef.For(siteFindingId),
                NodeRef.For(studyLevelFinding.Id),
            ],
            ct);

        paths[NodeRef.For(governedPackId)].Should().Be(new ScopePath(sponsorId, studyId));
        paths[NodeRef.For(unlinkedPackId)].Should().Be(ScopePath.TenantWide);
        paths[NodeRef.For(siteFindingId)].Should().Be(new ScopePath(sponsorId, studyId, "US", studySiteId));
        paths[NodeRef.For(studyLevelFinding.Id)].Should().Be(new ScopePath(sponsorId, studyId));

        var registry = new KernelStandardsRegistry(persistence.Factory);
        var loadedGoverned = await registry.FindLibraryItemAsync(Tenant, governedPackId, ct);
        loadedGoverned!.GoverningProtocolVersions.Should().Equal(protocolVersionId);

        var loadedUnlinked = await registry.FindLibraryItemAsync(Tenant, unlinkedPackId, ct);
        loadedUnlinked!.GoverningProtocolVersions.Should().BeEmpty();
    }

    [Fact(DisplayName = "KernelCollectionInstanceRecords_PreservesActiveSignature_WhenNotAskedPlaceholderIsAdded")]
    public async Task KernelCollectionInstanceRecordsPreservesActiveSignatureWhenNotAskedPlaceholderIsAdded()
    {
        var ct = TestContext.Current.CancellationToken;
        using var persistence = new InMemoryKernelPersistence();

        var sponsorId = OrganizationId.New();
        var institutionId = OrganizationId.New();
        var studyId = StudyId.New();
        var siteId = SiteId.New();
        var studySiteId = StudySiteId.New();
        var protocolVersionId = ProtocolVersionId.New();
        var sysConcept = new ConceptRef(ConceptId.New(), 1);
        var diaConcept = new ConceptRef(ConceptId.New(), 1);

        var design = new ProtocolDesign
        {
            Title = "CAD-101",
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital signs",
                    [
                        new CollectionItem("SYSBP", sysConcept, Required: true),
                        new CollectionItem("DIABP", diaConcept, Required: true),
                    ]),
            ],
        };

        CollectionInstanceId formId;
        ObservationId diaObsId;
        await using (var db = persistence.Factory.Create(Tenant))
        {
            db.Organizations.Add(new Organization(sponsorId, Tenant, "SPON", "Sponsor", OrganizationKind.Sponsor, "seed", "Seed"));
            db.Organizations.Add(new Organization(institutionId, Tenant, "INST", "Hospital", OrganizationKind.SiteInstitution, "seed", "Seed"));
            db.Studies.Add(new Study(studyId, Tenant, sponsorId, "CAD-101", "Study", "Phase 3", "seed", "Seed"));
            db.Sites.Add(new Site(siteId, Tenant, institutionId, "SITE-101", "Boston", "US", "seed", "Seed"));
            db.StudySites.Add(new StudySite(studySiteId, Tenant, studyId, siteId, "Active", "seed", "Seed"));
            db.ProtocolVersions.Add(ProtocolVersion.CreateFirst(protocolVersionId, Tenant, studyId, ProtocolDesignDocument.Write(design)).Value!);

            var enrollment = Enrollment.Screen(
                EnrollmentId.New(), Tenant, ParticipantId.New(), studyId, studySiteId, SubjectCode.From("SUBJ-101-001"), Epoch, Epoch).Value!;
            db.Enrollments.Add(enrollment);

            var form = CollectionInstance.Open(
                CollectionInstanceId.New(), enrollment, protocolVersionId, "cs.vitals", "act.v1", Epoch, "pi@site.test", Epoch);
            formId = form.Id;

            var sysObs = Observation.Record(
                ObservationId.New(), Tenant, enrollment.Id, enrollment.ParticipantId, studyId, sysConcept,
                new QuantityValue(122m, "mm[Hg]"), Epoch, "Initial vitals", "crc@site.test", Epoch).Value!;
            form.Join("SYSBP", sysObs.Id, Epoch);

            var signedHash = CollectionInstance.ContentHashOf([new CollectionContentEntry(sysObs.Id, 1, sysObs.Value)]);
            var attestation = Attestation.Sign(
                AttestationId.New(),
                Tenant,
                Actor.Person("pi@site.test"),
                AttestationMeaning.Approved,
                new AttestationTarget(NodeKinds.CollectionInstance, form.Id.Value, signedHash),
                Epoch.AddMinutes(1),
                "step-up-token",
                Epoch.AddMinutes(1),
                "Investigator eCRF sign-off").Value!;

            // Automated edit-check seeds revision-1 MissingValue(NotAsked) on blank DIABP after PI signed SYSBP.
            diaObsId = ObservationId.New();
            var unaskedObs = Observation.Record(
                diaObsId, Tenant, enrollment.Id, enrollment.ParticipantId, studyId, diaConcept,
                new MissingValue(NullFlavor.NotAsked), Epoch, "Seed unasked item", "svc:execution-edit-checks", Epoch.AddMinutes(2)).Value!;
            form.Join("DIABP", unaskedObs.Id, Epoch.AddMinutes(2));

            db.Observations.AddRange(sysObs, unaskedObs);
            db.CollectionInstances.Add(form);
            db.Attestations.Add(attestation);
            using (db.PermitPipelineWrites())
            {
                await db.SaveChangesAsync(ct);
            }
        }

        var reader = new KernelCollectionInstanceRecords(persistence.Factory);
        var recordAfterPlaceholder = await reader.FindAsync(Tenant, formId, ct);
        recordAfterPlaceholder.Should().NotBeNull();
        recordAfterPlaceholder!.Attestations.Should().ContainSingle()
            .Which.Superseded.Should().BeFalse("revision-1 NotAsked placeholder must not invalidate an active investigator signature");

        var summaries = await reader.ListForEnrollmentAsync(Tenant, recordAfterPlaceholder.EnrollmentId, ct);
        summaries.Should().ContainSingle().Which.Should().Match<CollectionInstanceSummary>(s => s.Signed && !s.SignatureSuperseded);

        // Once DIABP is clinically corrected (revision 2), the signature IS superseded.
        await using (var db = persistence.Factory.Create(Tenant))
        {
            var dia = await db.Observations.Include(o => o.Revisions).SingleAsync(o => o.Id == diaObsId, ct);
            dia.Correct(new QuantityValue(78m, "mm[Hg]"), "Entered missing diastolic reading", "crc@site.test", Epoch.AddMinutes(5)).Should().BeNull();
            using (db.PermitPipelineWrites())
            {
                await db.SaveChangesAsync(ct);
            }
        }

        var recordAfterCorrection = await reader.FindAsync(Tenant, formId, ct);
        recordAfterCorrection!.Attestations.Should().ContainSingle()
            .Which.Superseded.Should().BeTrue("a clinical correction to revision 2 must supersede the prior signature");
    }

    [Fact(DisplayName = "ImportOdmRulePack_SetsGovernedByProtocolVersion_AndPermitsStudyScopedApproval")]
    public async Task ImportOdmRulePackSetsGovernedByProtocolVersionAndPermitsStudyScopedApproval()
    {
        var ct = TestContext.Current.CancellationToken;
        var world = new InMemoryValidationWorld(Tenant);
        using var dmClient = CreateClient(world, DataManagerIdentity);

        using var importReq = MultipartOdmPost(
            "/api/v1/kernel/studies/CADENCE-101/protocol-imports",
            OdmWithRangeCheckXml,
            "vs-rules.xml",
            "odm-rule-import-1",
            "Import ODM with RangeCheck");
        using var importRes = await dmClient.SendAsync(importReq, ct);
        importRes.StatusCode.Should().Be(HttpStatusCode.Created, await importRes.Content.ReadAsStringAsync(ct));

        var draftedPack = world.LibraryItems.Should().ContainSingle().Subject;
        var importedVersionId = world.Versions.Should().ContainSingle().Subject.Summary.Id;
        draftedPack.GoverningProtocolVersions.Should().Equal(importedVersionId);

        // Study-scoped Data Manager (NOT tenant-scoped) approves the study-governed rule pack via step-up signature.
        var packRef = NodeRef.For(draftedPack.Id).ToString();
        var approvePath = $"/api/v1/kernel/library-items/{Uri.EscapeDataString(packRef)}/approve";
        var sigToken = TestSecurityContext.CreateSigToken(
            DataManagerIdentity.UserId,
            KernelValidationEndpoints.ApproveLibraryItemAction(packRef),
            KernelValidationEndpoints.ApproveLibraryItemSemanticAction,
            DataManagerIdentity.Roles);

        using var approveReq = SignedPost(approvePath, payload: null, "approve-pack-1", "Rule pack governance approval", sigToken);
        using var approveRes = await dmClient.SendAsync(approveReq, ct);
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveRes.Content.ReadAsStringAsync(ct));
        var body = JsonDocument.Parse(await approveRes.Content.ReadAsStringAsync(ct)).RootElement;
        body.GetProperty("status").GetString().Should().Be("Approved");
    }

    [Fact(DisplayName = "ValidationEndpoints_EnforceSiteIsolationBlindingCodesPagination_AndFindingDecisions")]
    [KernelRule("K-AUTH-4")]
    public async Task ValidationEndpointsEnforceSiteIsolationBlindingCodesPaginationAndFindingDecisions()
    {
        var ct = TestContext.Current.CancellationToken;
        var world = new InMemoryValidationWorld(Tenant);
        world.SeedClinicalFormsAndFindings(extraSite101Findings: 510);

        using var site101Client = CreateClient(world, Site101CrcIdentity);
        using var unblindedClient = CreateClient(world, UnblindedMonitorIdentity);
        using var dmClient = CreateClient(world, DataManagerIdentity);

        var packRef = NodeRef.For(world.ActivePackId).ToString();

        // 1. PreviewStudyRulePackAsync: Site-101 CRC sees ONLY SITE-101 non-blinded violations, with site_code & subject_code, while blinded treatment violations are suppressed.
        using (var previewReq = SignedPost(
            $"/api/v1/kernel/studies/CADENCE-101/rule-packs/{Uri.EscapeDataString(packRef)}/preview",
            payload: null,
            idempotencyKey: "preview-crc-101",
            reason: "Preview study rule pack"))
        {
            using var previewRes = await site101Client.SendAsync(previewReq, ct);
            previewRes.StatusCode.Should().Be(HttpStatusCode.OK, await previewRes.Content.ReadAsStringAsync(ct));
            var rawPreview = await previewRes.Content.ReadAsStringAsync(ct);
            rawPreview.Should()
                .NotContain("Active Drug 50mg")
                .And.NotContain("TRT_ARM_CHECK")
                .And.NotContain("SITE-102")
                .And.NotContain("SUBJ-102-001");

            var previewJson = JsonDocument.Parse(rawPreview).RootElement;
            var violations = previewJson.GetProperty("violations").EnumerateArray().ToList();
            violations.Should().ContainSingle(v => v.GetProperty("rule_id").GetString() == "VS_SYSBP_MAX");
            violations.Should().OnlyContain(v =>
                v.GetProperty("site_code").GetString() == "SITE-101"
                && v.GetProperty("subject_code").GetString() == "SUBJ-101-001");
        }

        // Unblinded study monitor sees blinded treatment violations and cross-site violations in preview.
        using (var unblindedPreviewReq = SignedPost(
            $"/api/v1/kernel/studies/CADENCE-101/rule-packs/{Uri.EscapeDataString(packRef)}/preview",
            payload: null,
            idempotencyKey: "preview-monitor-unblind",
            reason: "Preview study rule pack as unblinded monitor"))
        {
            using var unblindedPreviewRes = await unblindedClient.SendAsync(unblindedPreviewReq, ct);
            unblindedPreviewRes.StatusCode.Should().Be(HttpStatusCode.OK, await unblindedPreviewRes.Content.ReadAsStringAsync(ct));
            var unblindedPreviewJson = JsonDocument.Parse(await unblindedPreviewRes.Content.ReadAsStringAsync(ct)).RootElement;
            var unblindedViolations = unblindedPreviewJson.GetProperty("violations").EnumerateArray().ToList();
            unblindedViolations.Should().Contain(v =>
                v.GetProperty("rule_id").GetString() == "TRT_ARM_CHECK"
                && v.GetProperty("site_code").GetString() == "SITE-101"
                && v.GetProperty("subject_code").GetString() == "SUBJ-101-001");
        }

        // 2. ListStudyFindingsAsync: Site-101 CRC paginates across >500 findings, excludes SITE-102, and suppresses blinded treatment findings.
        var crcFindingsRaw = await site101Client.GetStringAsync(
            "/api/v1/kernel/studies/CADENCE-101/validation/findings?limit=500", ct);
        crcFindingsRaw.Should().NotContain("Active Drug 50mg").And.NotContain("TRT_ARM_CHECK").And.NotContain("SITE-102");
        var crcFindingsDoc = JsonDocument.Parse(crcFindingsRaw).RootElement;
        crcFindingsDoc.GetProperty("total").GetInt32().Should().Be(511, "510 extra + 1 non-blinded SITE-101 finding, excluding SITE-102 and blinded TRT_ARM_CHECK");
        var crcFindings = crcFindingsDoc.GetProperty("findings").EnumerateArray().ToList();
        crcFindings.Should().OnlyContain(f => f.GetProperty("site_code").GetString() == "SITE-101");

        // 3. GetFindingAsync: Site-101 CRC gets 403 on SITE-102 finding AND gets 403 on SITE-101 blinded finding without enrollment.unblind.
        var site102FindingRef = NodeRef.For(world.Site102FindingId).ToString();
        using (var forbiddenRes = await site101Client.GetAsync(
            $"/api/v1/kernel/validation/findings/{Uri.EscapeDataString(site102FindingRef)}", ct))
        {
            forbiddenRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var site101BlindedRef = NodeRef.For(world.Site101BlindedFindingId).ToString();
        using (var blindedForbiddenRes = await site101Client.GetAsync(
            $"/api/v1/kernel/validation/findings/{Uri.EscapeDataString(site101BlindedRef)}", ct))
        {
            blindedForbiddenRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // Unblinded study monitor sees both SITE-101 and SITE-102 and sees raw treatment value "Active Drug 50mg".
        var unblindedListDoc = await unblindedClient.GetFromJsonAsync<JsonElement>(
            "/api/v1/kernel/studies/CADENCE-101/validation/findings?limit=500", ct);
        unblindedListDoc.GetProperty("total").GetInt32().Should().Be(513, "510 extra + 2 SITE-101 findings + 1 SITE-102 finding");
        var unblindedDetail = await unblindedClient.GetFromJsonAsync<JsonElement>(
            $"/api/v1/kernel/validation/findings/{Uri.EscapeDataString(site101BlindedRef)}", ct);
        unblindedDetail.GetProperty("finding").GetProperty("observed")[0].GetProperty("value").GetString()
            .Should().Be("Active Drug 50mg");

        // 4. Finding decision endpoints: investigate -> classify -> accept (signed) -> reopen -> explain (signed).
        var targetRef = NodeRef.For(world.Site101VitalsFindingId).ToString();
        var escapedRef = Uri.EscapeDataString(targetRef);

        using (var invReq = SignedPost($"/api/v1/kernel/validation/findings/{escapedRef}/investigate", null, "dec-inv-1", "Begin investigation"))
        {
            using var invRes = await site101Client.SendAsync(invReq, ct);
            invRes.StatusCode.Should().Be(HttpStatusCode.OK, await invRes.Content.ReadAsStringAsync(ct));
            JsonDocument.Parse(await invRes.Content.ReadAsStringAsync(ct)).RootElement
                .GetProperty("status").GetString().Should().Be("Investigating");
        }

        using (var clsReq = SignedPost(
            $"/api/v1/kernel/validation/findings/{escapedRef}/classify",
            new { classification = "DataIssue" },
            "dec-cls-1",
            "Classify as data issue"))
        {
            using var clsRes = await site101Client.SendAsync(clsReq, ct);
            clsRes.StatusCode.Should().Be(HttpStatusCode.OK, await clsRes.Content.ReadAsStringAsync(ct));
            JsonDocument.Parse(await clsRes.Content.ReadAsStringAsync(ct)).RootElement
                .GetProperty("classification").GetString().Should().Be("DataIssue");
        }

        var acceptSig = TestSecurityContext.CreateSigToken(
            DataManagerIdentity.UserId,
            KernelValidationEndpoints.AcceptFindingAction(targetRef),
            KernelValidationEndpoints.AcceptFindingSemanticAction,
            DataManagerIdentity.Roles);
        using (var accReq = SignedPost(
            $"/api/v1/kernel/validation/findings/{escapedRef}/accept",
            new { rationale = "Confirmed standing cuff measurement against source chart" },
            "dec-acc-1",
            "Validation finding disposition",
            acceptSig))
        {
            using var accRes = await dmClient.SendAsync(accReq, ct);
            accRes.StatusCode.Should().Be(HttpStatusCode.OK, await accRes.Content.ReadAsStringAsync(ct));
            JsonDocument.Parse(await accRes.Content.ReadAsStringAsync(ct)).RootElement
                .GetProperty("status").GetString().Should().Be("Accepted");
        }

        using (var reoReq = SignedPost($"/api/v1/kernel/validation/findings/{escapedRef}/reopen", null, "dec-reo-1", "Reopen for reviewer-guide note"))
        {
            using var reoRes = await dmClient.SendAsync(reoReq, ct);
            reoRes.StatusCode.Should().Be(HttpStatusCode.OK, await reoRes.Content.ReadAsStringAsync(ct));
            JsonDocument.Parse(await reoRes.Content.ReadAsStringAsync(ct)).RootElement
                .GetProperty("status").GetString().Should().Be("Open");
        }

        var explainSig = TestSecurityContext.CreateSigToken(
            DataManagerIdentity.UserId,
            KernelValidationEndpoints.ExplainFindingAction(targetRef),
            KernelValidationEndpoints.ExplainFindingSemanticAction,
            DataManagerIdentity.Roles);
        using var expReq = SignedPost(
            $"/api/v1/kernel/validation/findings/{escapedRef}/explain",
            new { explanation = "Documented in ADRG section 4.2" },
            "dec-exp-1",
            "Validation finding disposition",
            explainSig);
        using var expRes = await dmClient.SendAsync(expReq, ct);
        expRes.StatusCode.Should().Be(HttpStatusCode.OK, await expRes.Content.ReadAsStringAsync(ct));
        JsonDocument.Parse(await expRes.Content.ReadAsStringAsync(ct)).RootElement
            .GetProperty("status").GetString().Should().Be("Explained");
    }

    private HttpClient CreateClient(InMemoryValidationWorld world, GatewayIdentity identity)
    {
        var factory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITenantDirectory>();
                services.AddSingleton<ITenantDirectory>(world);
                services.RemoveAll<ITenantEntitlementReader>();
                services.AddSingleton<ITenantEntitlementReader>(world);
                services.RemoveAll<IAuthorityStore>();
                services.AddSingleton<IAuthorityStore>(world);
                services.RemoveAll<IStudyDirectory>();
                services.AddSingleton<IStudyDirectory>(world);
                services.RemoveAll<IProtocolVersionReader>();
                services.AddSingleton<IProtocolVersionReader>(world);
                services.RemoveAll<IConceptRegistry>();
                services.AddSingleton<IConceptRegistry>(world);
                services.RemoveAll<IStandardsRegistry>();
                services.AddSingleton<IStandardsRegistry>(world);
                services.RemoveAll<ICollectionInstanceRecords>();
                services.AddSingleton<ICollectionInstanceRecords>(world);
                services.RemoveAll<IValidationFindings>();
                services.AddSingleton<IValidationFindings>(world);
                services.RemoveAll<IKernelNavigator>();
                services.AddSingleton<IKernelNavigator>(world);
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore, InMemoryDurableSigTokenUseStore>();
                services.RemoveAll<IKernelCommandSender>();
                services.AddScoped<IKernelCommandSender>(sp =>
                    new InMemoryValidationCommandSender(world, sp.GetRequiredService<ICurrentActor>(), sp.GetRequiredService<IAuthorizer>()));

                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }
            });
        });
        _factories.Add(factory);
        return factory.CreateSignedClient(identity);
    }

    private static HttpRequestMessage SignedPost(
        string path,
        object? payload,
        string idempotencyKey,
        string reason,
        string? signatureToken = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = payload is null ? null : JsonContent.Create(payload),
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        if (signatureToken is not null)
        {
            req.Headers.Add(GatewayHeaders.SigToken, signatureToken);
        }

        return req;
    }

    private static HttpRequestMessage MultipartOdmPost(string path, string xml, string fileName, string idempotencyKey, string reason)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        multipart.Add(fileContent, "file", fileName);

        var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = multipart,
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        return req;
    }

    private sealed class InMemoryKernelPersistence : IDisposable
    {
        private readonly string _dbName = $"kernel-val-{Guid.NewGuid():N}";

        public InMemoryKernelPersistence()
        {
            Factory = new KernelTenantDbContextFactory(new InMemoryDbContextFactory(_dbName));
        }

        public KernelTenantDbContextFactory Factory { get; }

        public void Dispose()
        {
            using var db = Factory.Create(Tenant);
            db.Database.EnsureDeleted();
        }

        private sealed class InMemoryDbContextFactory(string dbName) : IDbContextFactory<KernelDbContext>
        {
            public KernelDbContext CreateDbContext()
            {
                var options = new DbContextOptionsBuilder<KernelDbContext>()
                    .UseInMemoryDatabase(dbName)
                    .Options;
                return new KernelDbContext(options);
            }
        }
    }

    private sealed class InMemoryDurableSigTokenUseStore : IDurableSigTokenUseStore
    {
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, CancellationToken cancellationToken)
        {
            lock (_used)
            {
                return Task.FromResult(_used.Add(tokenId));
            }
        }
    }

    private sealed class InMemoryValidationWorld :
        ITenantDirectory,
        ITenantEntitlementReader,
        IAuthorityStore,
        IStudyDirectory,
        IProtocolVersionReader,
        IConceptRegistry,
        IStandardsRegistry,
        ICollectionInstanceRecords,
        IValidationFindings,
        IKernelNavigator
    {
        private readonly TenantId _tenant;

        public InMemoryValidationWorld(TenantId tenant)
        {
            _tenant = tenant;
            SponsorId = OrganizationId.New();
            StudyId = StudyId.New();
            Site101Id = StudySiteId.New();
            Site102Id = StudySiteId.New();
            var physicalSite101 = SiteId.New();
            var physicalSite102 = SiteId.New();
            Studies.Add(new StudyDirectoryEntry(StudyId, 1, "CADENCE-101", "Demo Study", "Phase 3", SponsorId, 1, "SPON", "Sponsor", 2));
            Sites.AddRange([
                new SiteDirectoryEntry(physicalSite101, 1, SponsorId, 1, "SPON", "Sponsor", "SITE-101", "Site 101", "US"),
                new SiteDirectoryEntry(physicalSite102, 1, SponsorId, 1, "SPON", "Sponsor", "SITE-102", "Site 102", "US"),
            ]);
            StudySites.AddRange([
                new StudySiteDirectoryEntry(Site101Id, 1, StudyId, physicalSite101, 1, "SITE-101", "Site 101", "US", SponsorId, 1, "SPON", "Sponsor", "Active"),
                new StudySiteDirectoryEntry(Site102Id, 1, StudyId, physicalSite102, 1, "SITE-102", "Site 102", "US", SponsorId, 1, "SPON", "Sponsor", "Active"),
            ]);

            // Study-scoped Data Manager (library_item.draft & library_item.approve granted ONLY at Study scope; concept.define at Tenant)
            GrantRole(
                Actor.Person(DataManagerIdentity.UserId),
                "DM",
                [
                    "protocol.draft",
                    "protocol.revise_draft",
                    "library_item.draft",
                    "library_item.approve",
                    "attestation.sign",
                    "study.declare_standards",
                    "validation_finding.investigate",
                    "validation_finding.classify",
                    "validation_finding.accept",
                    "validation_finding.explain",
                    "validation_finding.reopen",
                ],
                AuthorityScope.Study(StudyId));
            Grants.Add(Grant.Issue(
                GrantId.New(),
                _tenant,
                Actor.Person(DataManagerIdentity.UserId),
                "concept.define",
                null,
                AuthorityScope.Tenant(),
                Epoch,
                null,
                false,
                null,
                Actor.ServiceAccount("seed"),
                Epoch).Value!);

            // Site-101 CRC (StudySite scope, blinded — NO enrollment.unblind)
            GrantRole(
                Actor.Person(Site101CrcIdentity.UserId),
                "CRC_101",
                [
                    "observation.record",
                    "validation_finding.investigate",
                    "validation_finding.classify",
                ],
                AuthorityScope.StudySite(Site101Id));

            // Unblinded study monitor (Study scope WITH direct enrollment.unblind grant per K-AUTH-4)
            GrantRole(
                Actor.Person(UnblindedMonitorIdentity.UserId),
                "UNBLIND_CRA",
                [
                    "validation_finding.investigate",
                ],
                AuthorityScope.Study(StudyId));
            Grants.Add(Grant.Issue(
                GrantId.New(),
                _tenant,
                Actor.Person(UnblindedMonitorIdentity.UserId),
                "enrollment.unblind",
                null,
                AuthorityScope.Study(StudyId),
                Epoch,
                null,
                false,
                null,
                Actor.ServiceAccount("seed"),
                Epoch).Value!);
        }

        public OrganizationId SponsorId { get; }

        public StudyId StudyId { get; }

        public StudySiteId Site101Id { get; }

        public StudySiteId Site102Id { get; }

        public LibraryItemId ActivePackId { get; private set; }

        public ValidationFindingId Site101VitalsFindingId { get; private set; }

        public ValidationFindingId Site101BlindedFindingId { get; private set; }

        public ValidationFindingId Site102FindingId { get; private set; }

        public List<Grant> Grants { get; } = [];

        public List<Role> Roles { get; } = [];

        public List<StudyDirectoryEntry> Studies { get; } = [];

        public List<SiteDirectoryEntry> Sites { get; } = [];

        public List<StudySiteDirectoryEntry> StudySites { get; } = [];

        public List<ConceptVersionView> Concepts { get; } = [];

        public List<(ProtocolVersionSummary Summary, string Content)> Versions { get; } = [];

        public List<LibraryItemView> LibraryItems { get; } = [];

        public List<CollectionInstanceRecord> Forms { get; } = [];

        public List<ValidationFindingView> Findings { get; } = [];

        public Dictionary<NodeRef, (string Code, StudySiteId? SiteId)> NodeCodes { get; } = [];

        public Dictionary<string, object> IdempotentResults { get; } = new(StringComparer.Ordinal);

        public Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
            TenantId tenantId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken,
            DateTimeOffset? knownAt = null) =>
            Task.FromResult(new TenantEntitlementSnapshot(
                tenantId,
                SubscriptionId.New(),
                CommercialTier.Complete,
                SubscriptionStatus.Active,
                Epoch,
                null,
                IsReadOnly: false,
                [
                    ProductFamily.Blueprint,
                    ProductFamily.Capture,
                    ProductFamily.Orbit,
                    ProductFamily.Engage,
                    ProductFamily.Allocate,
                    ProductFamily.Vigil,
                    ProductFamily.Lens,
                    ProductFamily.Assure,
                ]));

        public void GrantRole(Actor grantee, string code, string[] capabilities, AuthorityScope scope)
        {
            var role = Role.Define(RoleId.New(), _tenant, code, code, capabilities).Value!;
            Roles.Add(role);
            Grants.Add(Grant.Issue(GrantId.New(), _tenant, grantee, null, role.Id, scope, Epoch, null, false, null, Actor.ServiceAccount("seed"), Epoch).Value!);
        }

        public void SeedClinicalFormsAndFindings(int extraSite101Findings)
        {
            var pvId = ProtocolVersionId.New();
            var sysConcept = new ConceptRef(ConceptId.New(), 1);
            var trtConcept = new ConceptRef(ConceptId.New(), 1);
            Concepts.Add(new ConceptVersionView(
                sysConcept, ConceptOrigin.Sponsor, "SYSBP", ConceptKind.BiomedicalConcept, "Systolic BP", string.Empty, ConceptStatus.Active, 1, ConceptMappingState.Unmapped, Epoch));
            Concepts.Add(new ConceptVersionView(
                trtConcept, ConceptOrigin.Sponsor, "RAND_ARM", ConceptKind.BiomedicalConcept, "Randomized Treatment Arm", string.Empty, ConceptStatus.Active, 1, ConceptMappingState.Unmapped, Epoch));

            var design = new ProtocolDesign
            {
                Title = "CAD-101",
                CollectionSpecs =
                [
                    new CollectionSpec(
                        "VS",
                        "Vital Signs",
                        [
                            new CollectionItem("SYSBP", sysConcept, Required: true),
                            new CollectionItem("RAND_ARM", trtConcept, Required: false),
                        ]),
                ],
            };
            Versions.Add((new ProtocolVersionSummary(pvId, StudyId, 1, null, ProtocolVersionStatus.Approved, "sha256:" + new string('1', 64), null, null, null), ProtocolDesignDocument.Write(design)));

            var enroll101 = EnrollmentId.New();
            var enroll102 = EnrollmentId.New();
            NodeCodes[NodeRef.For(Site101Id)] = ("SITE-101", Site101Id);
            NodeCodes[NodeRef.For(Site102Id)] = ("SITE-102", Site102Id);
            NodeCodes[NodeRef.For(enroll101)] = ("SUBJ-101-001", Site101Id);
            NodeCodes[NodeRef.For(enroll102)] = ("SUBJ-102-001", Site102Id);

            var form101 = new CollectionInstanceRecord(
                CollectionInstanceId.New(),
                enroll101,
                ParticipantId.New(),
                StudyId,
                Site101Id,
                pvId,
                "VS",
                "act.v1",
                Epoch,
                "crc.101",
                Epoch,
                3,
                design.CollectionSpecs[0].Items,
                [
                    new CollectedValueView("SYSBP", ObservationId.New(), sysConcept, new QuantityValue(240m, "mm[Hg]"), Epoch, 1, "crc.101", Epoch),
                    new CollectedValueView("RAND_ARM", ObservationId.New(), trtConcept, new TextValue("Active Drug 50mg"), Epoch, 1, "randomizer", Epoch),
                ],
                [],
                [],
                [],
                null,
                "Vital Signs");

            var form102 = new CollectionInstanceRecord(
                CollectionInstanceId.New(),
                enroll102,
                ParticipantId.New(),
                StudyId,
                Site102Id,
                pvId,
                "VS",
                "act.v1",
                Epoch,
                "crc.102",
                Epoch,
                2,
                design.CollectionSpecs[0].Items,
                [
                    new CollectedValueView("SYSBP", ObservationId.New(), sysConcept, new QuantityValue(250m, "mm[Hg]"), Epoch, 1, "crc.102", Epoch),
                ],
                [],
                [],
                [],
                null,
                "Vital Signs");

            Forms.AddRange([form101, form102]);

            ActivePackId = LibraryItemId.New();
            const string packJson = """
                {
                  "authority": "Study",
                  "title": "Cardiovascular Checks",
                  "rules": [
                    {
                      "id": "VS_SYSBP_MAX",
                      "version": 1,
                      "citation": { "document": "Protocol CAD-101", "section": "6.1" },
                      "severity": "Warning",
                      "applicability": { "stage": "Collection", "dataset": "VS" },
                      "condition": { "op": ">", "args": [{ "var": "SYSBP" }, 220] },
                      "explanation": "Systolic BP exceeds 220 mmHg."
                    },
                    {
                      "id": "TRT_ARM_CHECK",
                      "version": 1,
                      "citation": { "document": "Protocol CAD-101", "section": "6.2" },
                      "severity": "Warning",
                      "applicability": { "stage": "Collection", "dataset": "VS" },
                      "condition": { "op": "==", "args": [{ "var": "RAND_ARM" }, "Active Drug 50mg"] },
                      "explanation": "Participant assigned to Active Drug 50mg requires PK sample."
                    }
                  ]
                }
                """;
            using var doc = JsonDocument.Parse(packJson);
            LibraryItems.Add(new LibraryItemView(
                ActivePackId,
                LibraryItemKind.RulePack,
                "cadence-101-pack",
                1,
                "Cardiovascular Checks",
                doc.RootElement.Clone(),
                "sha256:" + new string('c', 64),
                GovernanceStatus.Approved,
                [],
                null,
                null)
            {
                GoverningProtocolVersions = [pvId],
            });

            var runId = ValidationRunId.New();
            Site101VitalsFindingId = ValidationFindingId.New();
            Site101BlindedFindingId = ValidationFindingId.New();
            Site102FindingId = ValidationFindingId.New();

            Findings.Add(new ValidationFindingView(
                Site101VitalsFindingId,
                StudyId,
                ValidationStage.Collection,
                "cadence-101-pack",
                ActivePackId,
                "VS_SYSBP_MAX",
                1,
                ValidationSeverity.Warning,
                new FindingTarget(FindingTargetKind.DatasetRecord, "VS", form101.CollectionInstanceId.Value.ToString("D"), "SYSBP"),
                "Systolic BP exceeds 220 mmHg.",
                null,
                [new ObservedVariable("SYSBP", "240")],
                null,
                null,
                null,
                FindingStatus.Open,
                runId,
                runId,
                null,
                null,
                null));

            Findings.Add(new ValidationFindingView(
                Site101BlindedFindingId,
                StudyId,
                ValidationStage.Collection,
                "cadence-101-pack",
                ActivePackId,
                "TRT_ARM_CHECK",
                1,
                ValidationSeverity.Warning,
                new FindingTarget(FindingTargetKind.DatasetRecord, "VS", form101.CollectionInstanceId.Value.ToString("D"), "RAND_ARM"),
                "Participant assigned to Active Drug 50mg requires PK sample.",
                null,
                [new ObservedVariable("RAND_ARM", "Active Drug 50mg")],
                null,
                null,
                null,
                FindingStatus.Open,
                runId,
                runId,
                null,
                null,
                null));

            Findings.Add(new ValidationFindingView(
                Site102FindingId,
                StudyId,
                ValidationStage.Collection,
                "cadence-101-pack",
                ActivePackId,
                "VS_SYSBP_MAX",
                1,
                ValidationSeverity.Warning,
                new FindingTarget(FindingTargetKind.DatasetRecord, "VS", form102.CollectionInstanceId.Value.ToString("D"), "SYSBP"),
                "Systolic BP exceeds 220 mmHg at Site 102.",
                null,
                [new ObservedVariable("SYSBP", "250")],
                null,
                null,
                null,
                FindingStatus.Open,
                runId,
                runId,
                null,
                null,
                null));

            for (var i = 0; i < extraSite101Findings; i++)
            {
                Findings.Add(new ValidationFindingView(
                    ValidationFindingId.New(),
                    StudyId,
                    ValidationStage.Collection,
                    "cadence-101-pack",
                    ActivePackId,
                    $"EXTRA_{i:D3}",
                    1,
                    ValidationSeverity.Notice,
                    new FindingTarget(FindingTargetKind.DatasetRecord, "VS", form101.CollectionInstanceId.Value.ToString("D"), "SYSBP"),
                    $"Extra check {i}",
                    null,
                    [],
                    null,
                    null,
                    null,
                    FindingStatus.Open,
                    runId,
                    runId,
                    null,
                    null,
                    null));
            }
        }

        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(code == TestSecurityContext.TenantCode ? (TenantId?)_tenant : null);

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([.. Grants.Where(g => g.Grantee == actor)]);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult(Grants.SingleOrDefault(g => g.Id == grant));

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([.. Roles.Where(r => roles.Contains(r.Id))]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
        {
            if (resource.Kind == NodeKinds.Grant)
            {
                var grant = Grants.FirstOrDefault(g => g.Id == GrantId.FromGuid(resource.Id));
                if (grant is not null)
                {
                    return grant.Scope.Level switch
                    {
                        ScopeLevel.Study when grant.Scope.TargetId is { } sid => Task.FromResult(new ScopePath(SponsorId, StudyId.FromGuid(sid))),
                        ScopeLevel.StudySite when grant.Scope.TargetId is { } ssid => Task.FromResult(new ScopePath(SponsorId, StudyId, "US", StudySiteId.FromGuid(ssid))),
                        _ => Task.FromResult(ScopePath.TenantWide),
                    };
                }
            }

            if (resource.Kind == NodeKinds.Study && StudyId.FromGuid(resource.Id) == StudyId)
            {
                return Task.FromResult(new ScopePath(SponsorId, StudyId));
            }

            if (resource.Kind == NodeKinds.ProtocolVersion)
            {
                var (summary, _) = Versions.FirstOrDefault(x => x.Summary.Id == ProtocolVersionId.FromGuid(resource.Id));
                if (summary is not null)
                {
                    return Task.FromResult(new ScopePath(SponsorId, summary.StudyId));
                }
            }

            if (resource.Kind == NodeKinds.StudySite)
            {
                var siteId = StudySiteId.FromGuid(resource.Id);
                if (siteId == Site101Id || siteId == Site102Id)
                {
                    return Task.FromResult(new ScopePath(SponsorId, StudyId, "US", siteId));
                }
            }

            if (resource.Kind == NodeKinds.Enrollment && NodeCodes.TryGetValue(resource.Unversioned(), out var info) && info.SiteId is { } enrolledSite)
            {
                return Task.FromResult(new ScopePath(SponsorId, StudyId, "US", enrolledSite));
            }

            if (resource.Kind == NodeKinds.CollectionInstance)
            {
                var form = Forms.FirstOrDefault(f => f.CollectionInstanceId == CollectionInstanceId.FromGuid(resource.Id));
                if (form is not null)
                {
                    return Task.FromResult(new ScopePath(SponsorId, form.StudyId, "US", form.StudySiteId));
                }
            }

            if (resource.Kind == NodeKinds.Observation)
            {
                var obsId = ObservationId.FromGuid(resource.Id);
                var form = Forms.FirstOrDefault(f => f.Values.Any(v => v.ObservationId == obsId));
                if (form is not null)
                {
                    return Task.FromResult(new ScopePath(SponsorId, form.StudyId, "US", form.StudySiteId));
                }
            }

            if (resource.Kind == NodeKinds.LibraryItem)
            {
                var item = LibraryItems.FirstOrDefault(i => i.Id == LibraryItemId.FromGuid(resource.Id));
                if (item is { GoverningProtocolVersions.Count: > 0 })
                {
                    return Task.FromResult(new ScopePath(SponsorId, StudyId));
                }
            }

            if (resource.Kind == NodeKinds.ValidationFinding)
            {
                var finding = Findings.FirstOrDefault(f => f.Id == ValidationFindingId.FromGuid(resource.Id));
                if (finding is not null)
                {
                    if (Guid.TryParse(finding.Target.Key, out var fg))
                    {
                        var form = Forms.FirstOrDefault(f => f.CollectionInstanceId == CollectionInstanceId.FromGuid(fg));
                        if (form is not null)
                        {
                            return Task.FromResult(new ScopePath(SponsorId, StudyId, "US", form.StudySiteId));
                        }
                    }

                    return Task.FromResult(new ScopePath(SponsorId, finding.Study));
                }
            }

            return Task.FromResult(ScopePath.TenantWide);
        }

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([]);

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([]);

        public Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyDirectoryEntry>>(Studies);

        public Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Studies.FirstOrDefault(s => s.StudyId == studyId));

        public Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Studies.FirstOrDefault(s => string.Equals(s.StudyCode, studyCode, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationDirectoryEntry>>([]);

        public Task<OrganizationDirectoryEntry?> FindOrganizationAsync(TenantId tenant, OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationDirectoryEntry?>(null);

        public Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(TenantId tenant, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationDirectoryEntry?>(null);

        public Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SiteDirectoryEntry>>(Sites);

        public Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sites.FirstOrDefault(s => s.SiteId == siteId));

        public Task<SiteDirectoryEntry?> FindSiteByCodeAsync(TenantId tenant, string siteCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sites.FirstOrDefault(s => string.Equals(s.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteDirectoryEntry>>([.. StudySites.Where(s => s.StudyId == studyId)]);

        public Task<StudySiteDirectoryEntry?> FindStudySiteAsync(TenantId tenant, StudySiteId studySiteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StudySites.FirstOrDefault(s => s.StudySiteId == studySiteId));

        Task<IReadOnlyList<ProtocolVersionSummary>> IProtocolVersionReader.ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProtocolVersionSummary>>([.. Versions.Where(v => v.Summary.StudyId == study).Select(v => v.Summary)]);

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<ProtocolVersionSummary?>(Versions.FirstOrDefault(v => v.Summary.Id == version).Summary);

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<SoaMatrix?>(null);

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
        {
            var (Summary, Content) = Versions.FirstOrDefault(v => v.Summary.Id == version);
            if (Summary is null)
            {
                return Task.FromResult<ProtocolDesign?>(null);
            }

            var parsed = ProtocolDesignDocument.Read(Content);
            return Task.FromResult(parsed.IsValid ? parsed.Design : null);
        }

        public Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default) =>
            Task.FromResult(Concepts.FirstOrDefault(c => c.Concept == concept));

        public Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            Task.FromResult(Concepts.FirstOrDefault(c => c.Concept.ConceptId == concept));

        public Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConceptVersionView?>(null);

        public Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<CodeSystemVersionView?>(null);

        public Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([]);

        public Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([]);

        public Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConceptMappingView>>([]);

        public Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConceptMappingView>>([]);

        public Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StandardVersionSummary>>([]);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, string standardCode, string version, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardPackage?>(null);

        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult(LibraryItems.FirstOrDefault(i => i.Id == id));

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>([.. LibraryItems.Where(i => i.Kind == kind && i.Key == key)]);

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemsAsync(TenantId tenant, LibraryItemKind? kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>(kind is null ? LibraryItems : [.. LibraryItems.Where(i => i.Kind == kind)]);

        public Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<StudyStandardsProfileView?>(null);

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.FirstOrDefault(f => f.CollectionInstanceId == instance));

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([.. Forms.Where(f => f.EnrollmentId == enrollment)]);

        Task<IReadOnlyList<CollectionInstanceRecord>> ICollectionInstanceRecords.ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([.. Forms.Where(f => f.StudyId == study)]);

        public Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken) =>
            Task.FromResult(Findings.FirstOrDefault(f => f.Id == finding));

        public Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken)
        {
            var filtered = Findings.Where(f => f.Study == query.Study).ToList();
            var slice = filtered.Skip(query.Skip).Take(query.Take).ToList();
            return Task.FromResult(new FindingPage(slice, filtered.Count));
        }

        public Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken) =>
            Task.FromResult<ValidationRunSummary?>(null);

        public Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            var unversioned = node.Unversioned();
            if (unversioned.Kind == NodeKinds.Study && StudyId.FromGuid(unversioned.Id) == StudyId)
            {
                return Task.FromResult<NodeSummary?>(new NodeSummary(NodeRef.For(StudyId, 1), "Demo Study", "CADENCE-101", "Phase 3"));
            }

            if (NodeCodes.TryGetValue(unversioned, out var entry))
            {
                return Task.FromResult<NodeSummary?>(new NodeSummary(unversioned.AtVersion(1), entry.Code, entry.Code));
            }

            return Task.FromResult<NodeSummary?>(null);
        }

        public Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ZoomStep>?>([]);

        public Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScopeChild>?>([]);
    }

    private sealed class InMemoryValidationCommandSender(
        InMemoryValidationWorld world,
        ICurrentActor currentActor,
        IAuthorizer authorizer) : IKernelCommandSender
    {
        public async Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (currentActor.Actor is not { } actor)
            {
                return CommandResult<TResult>.Failure(KernelErrors.ActorUnresolved);
            }

            var auth = await authorizer.AuthorizeAsync(actor, command, cancellationToken).ConfigureAwait(false);
            if (!auth.Permitted)
            {
                return CommandResult<TResult>.Failure(KernelErrors.AuthorizationDenied);
            }

            if (world.IdempotentResults.TryGetValue(command.IdempotencyKey, out var cached))
            {
                return CommandResult<TResult>.Success((TResult)cached);
            }

            object outcome = command switch
            {
                DefineConcept def => ExecuteDefineConcept(def),
                CreateProtocolDraft draft => ExecuteCreateDraft(draft),
                DraftLibraryItem draftItem => ExecuteDraftLibraryItem(draftItem),
                Attest attest => attest.AttestationId,
                ApproveLibraryItem approveItem => ExecuteApproveLibraryItem(approveItem),
                InvestigateFinding inv => UpdateFinding(inv.FindingId, status: FindingStatus.Investigating, actor.Id),
                ClassifyFinding cls => UpdateFinding(cls.FindingId, classification: cls.Classification, actorId: actor.Id),
                AcceptFinding acc => UpdateFinding(acc.FindingId, status: FindingStatus.Accepted, actorId: actor.Id, text: acc.Rationale, attest: acc.AttestationId),
                ExplainFinding exp => UpdateFinding(exp.FindingId, status: FindingStatus.Explained, actorId: actor.Id, text: exp.Explanation, attest: exp.AttestationId),
                ReopenFinding reo => UpdateFinding(reo.FindingId, status: FindingStatus.Open, actorId: actor.Id),
                _ => throw new NotSupportedException(command.GetType().Name),
            };

            world.IdempotentResults[command.IdempotencyKey] = outcome;
            return CommandResult<TResult>.Success((TResult)outcome);
        }

        private ConceptRef ExecuteDefineConcept(DefineConcept cmd)
        {
            var cRef = new ConceptRef(cmd.ConceptId, 1);
            world.Concepts.Add(new ConceptVersionView(cRef, cmd.Origin, cmd.ExternalCode, cmd.Kind, cmd.PreferredLabel, cmd.Definition, ConceptStatus.Active, 1, ConceptMappingState.Unmapped, Epoch));
            return cRef;
        }

        private ProtocolVersionState ExecuteCreateDraft(CreateProtocolDraft cmd)
        {
            var summary = new ProtocolVersionSummary(cmd.ProtocolVersionId, cmd.StudyId, 1, null, ProtocolVersionStatus.Draft, null, null, null, null);
            world.Versions.Add((summary, cmd.Content));
            return new ProtocolVersionState(cmd.ProtocolVersionId, cmd.StudyId, 1, ProtocolVersionStatus.Draft, null);
        }

        private LibraryItemVersion ExecuteDraftLibraryItem(DraftLibraryItem cmd)
        {
            var drafted = LibraryItem.Draft(cmd.LibraryItemId, cmd.TenantId, cmd.Kind, cmd.Key, 1, cmd.Title, cmd.Content, cmd.StandardVersions).Item!;
            var governing = cmd.GoverningProtocolVersionId is { IsEmpty: false } pv ? new[] { pv } : [];
            world.LibraryItems.Add(new LibraryItemView(
                drafted.Id,
                drafted.Kind,
                drafted.Key,
                drafted.ItemVersion,
                drafted.Title,
                cmd.Content.Clone(),
                drafted.ContentHash,
                GovernanceStatus.Draft,
                cmd.StandardVersions,
                null,
                null)
            {
                GoverningProtocolVersions = governing,
            });
            return drafted.ToVersion();
        }

        private LibraryItemVersion ExecuteApproveLibraryItem(ApproveLibraryItem cmd)
        {
            var idx = world.LibraryItems.FindIndex(i => i.Id == cmd.LibraryItemId);
            var updated = world.LibraryItems[idx] with
            {
                Status = GovernanceStatus.Approved,
                ApprovalAttestation = cmd.AttestationId,
            };
            world.LibraryItems[idx] = updated;
            return new LibraryItemVersion(updated.Id, updated.Kind, updated.Key, updated.Version, updated.ContentHash);
        }

        private ValidationFindingId UpdateFinding(
            ValidationFindingId id,
            FindingStatus? status = null,
            string actorId = "actor",
            FindingClassification? classification = null,
            string? text = null,
            AttestationId? attest = null)
        {
            var idx = world.Findings.FindIndex(f => f.Id == id);
            var existing = world.Findings[idx];
            var nextStatus = status ?? existing.Status;
            var nextDecision = status is not null
                ? new FindingDecision(nextStatus, actorId, Epoch, text, existing.LastDetectedRun, attest)
                : existing.Decision;
            world.Findings[idx] = existing with
            {
                Status = nextStatus,
                Classification = classification ?? existing.Classification,
                Decision = nextDecision,
            };
            return id;
        }
    }
}
