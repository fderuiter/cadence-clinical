using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cgt;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Devices;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Application.Supply;
using Cadence.Apps.Execution.Application.Toxicity;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Cgt;
using Cadence.Apps.Execution.Domain.Coding;
using Cadence.Apps.Execution.Domain.Devices;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Exhaustive route-level verification across all 41 POST/PUT/PATCH/DELETE endpoints in <c>apps/execution/src/Endpoints</c>:
/// all 34 mutating endpoints fail closed with <c>423 Locked</c> (<c>code: "K.Observation.Locked"</c>) when
/// <see cref="IKernelLockGuard"/> reports an active covering <c>Frozen</c> or <c>Locked</c> assertion, while all 7
/// stateless validation/parsing/conversion/evaluation endpoints remain available (<c>200 OK</c>) on locked studies
/// (#5376, #5466).
/// </summary>
public sealed class ExecutionLockGuardRoutingTests : IDisposable
{
    private const string Reason = "Kernel lock guard route verification with clinical justification exceeding fifty characters.";
    private const string Study = "STUDY-LOCK-ROUTE";
    private const string Site = "SITE-LOCK-01";
    private const string Subject = "SUBJ-LOCK-01";
    private const string EnrolledSubject = "SUBJ-LOCK-ENR-01";
    private const string Tenant = "tenant-lock-route";

    private readonly ExecutionApiFactory _baseFactory = new();
    private readonly ToggleableKernelLockGuard _lockGuard = new();
    private readonly WebApplicationFactory<Cadence.Apps.Execution.Program> _factory;

    public ExecutionLockGuardRoutingTests()
    {
        _factory = _baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKernelLockGuard>();
                services.AddSingleton<IKernelLockGuard>(_lockGuard);
            }));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _baseFactory.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task All34MutatingRoutes_Return423ObservationLockedWhenStudyIsLocked_WhileAll7StatelessRoutesReturn200()
    {
        var seeded = await SeedFixturesAsync();
        _lockGuard.IsLocked = true;

        using var crcClient = Client("crc-lock", ["CRC"], [Site]);
        using var piClient = Client("pi-lock", ["Site Investigator"], [Site]);
        using var pharmClient = Client("pharm-lock", ["Pharmacist"], [Site], unblinded: true);
        using var dmClient = Client("dm-lock", ["DATA_MANAGER", "SPONSOR_DESIGNER", "Medical Coder", "Safety Officer"], [Site], allStudies: true);
        using var statClient = Client("stat-lock", [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN], [Site]);
        using var chairClient = Client("chair-lock", [PermissionMatrix.CecChairRole], []);
        using var etmfClient = Client("svc:etmf-lock", [PermissionMatrix.EtmfServiceRole], []);

        var mutatingCalls = new List<(string Name, Func<Task<HttpResponseMessage>> Send)>
        {
            // 1. CecEndpoints (1)
            ("POST /api/v1/execution/cec/dossiers/{id}/chair-adjudicate", () =>
            {
                string path = CecEndpoints.ChairAdjudicateAction(seeded.DossierId);
                string sig = TestSecurityContext.CreateSigToken("chair-lock", path, CecEndpoints.ChairAdjudicateSemanticAction, [PermissionMatrix.CecChairRole]);
                return SendJsonAsync(chairClient, HttpMethod.Post, path, new { final_classification = "MACE", final_adjudicated_onset_date = "2026-01-01", adjudication_rationale = Reason }, sig);
            }),

            // 2. CgtEndpoints (7)
            ("POST /api/v1/execution/rtsm/cgt/apheresis", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/rtsm/cgt/apheresis",
                    new CgtApheresisCreateRequest(Study, Site, Subject, "DIN-9901", FakeClock.DefaultStart))),
            ("POST /api/v1/execution/rtsm/cgt/milestone", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/rtsm/cgt/milestone",
                    new CgtMilestoneCreateRequest(Study, seeded.CocId, "COLLECTION", "HARVESTED"))),
            ("POST /api/v1/execution/rtsm/cgt/manufacturing-bind", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/rtsm/cgt/manufacturing-bind",
                    new CgtManufacturingBindRequest(Study, seeded.CocId, "BATCH-01", "MFG-01"))),
            ("POST /api/v1/execution/rtsm/cgt/bedside-verify", () =>
            {
                const string path = "/api/v1/execution/rtsm/cgt/bedside-verify";
                string sig = TestSecurityContext.CreateSigToken("crc-lock", path, CgtEndpoints.BedsideVerifySemanticAction, ["CRC"]);
                return SendJsonAsync(crcClient, HttpMethod.Post, path,
                    new CgtBedsideVerificationRequest(Study, Site, EnrolledSubject, "DIN-SEED-01", "BATCH-01", "DIN-SEED-01", EnrolledSubject, "crc-lock", SecondaryVerifierId: "pi-lock"), sig);
            }),
            ("POST /api/v1/execution/rtsm/cgt/infuse", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/rtsm/cgt/infuse",
                    new CgtInfusionExecuteRequest(Study, Site, EnrolledSubject, seeded.CocId, "unlock-tok", "crc-lock"))),
            ("POST /api/v1/execution/rtsm/cgt/returns", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/rtsm/cgt/returns",
                    new CgtVialReturnReconciliationRequest(Study, Site, EnrolledSubject, "KIT-LOCK-01", "VIAL-01"))),
            ("POST /api/v1/execution/rtsm/cgt/destruction-certificate", () =>
            {
                const string path = "/api/v1/execution/rtsm/cgt/destruction-certificate";
                string sig = TestSecurityContext.CreateSigToken("crc-lock", path, CgtEndpoints.DestructionCertificateSemanticAction, ["CRC"]);
                return SendJsonAsync(crcClient, HttpMethod.Post, path,
                    new CgtDestructionCertificateCreateRequest(Study, Site, "LOT-LOCK-01", ["KIT-LOCK-01"], ["VIAL-01"], "Expired", "INCINERATION", "Site Lab", "crc-lock", Witness2Id: "pi-lock", SubjectId: EnrolledSubject, CocId: seeded.CocId), sig);
            }),

            // 3. DeviceEndpoints (5)
            ("POST /api/v1/execution/devices/inventory", () =>
                SendJsonAsync(pharmClient, HttpMethod.Post, "/api/v1/execution/devices/inventory",
                    new DeviceInventoryCreateRequest(Study, Site, "CardioPump", "00850006781111", "LOT-DEV-1", SerialNumber: "SN-NEW-01"))),
            ("POST /api/v1/execution/devices/dispense", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/devices/dispense",
                    new DeviceDispensationRequest(Study, Site, EnrolledSubject, DeviceInventoryId: seeded.DeviceId.ToString(), SerialNumber: "SN-SEED-01"))),
            ("POST /api/v1/execution/devices/deficiencies", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/devices/deficiencies",
                    new DeviceDeficiencyCreateRequest(Study, Site, "00850006781111", "LOT-DEV-1", "Battery warning", FakeClock.DefaultStart, FakeClock.DefaultStart, SubjectId: EnrolledSubject, DeviceInventoryId: seeded.DeviceId.ToString()))),
            ("POST /api/v1/execution/devices/quarantine", () =>
                SendJsonAsync(pharmClient, HttpMethod.Post, "/api/v1/execution/devices/quarantine",
                    new DeviceQuarantineCreateRequest(Study, Site, "Safety inspection", DeviceInventoryId: seeded.DeviceId.ToString(), SerialNumber: "SN-SEED-01"))),
            ("POST /api/v1/execution/devices/quarantine/release", () =>
            {
                const string path = "/api/v1/execution/devices/quarantine/release";
                string sig = TestSecurityContext.CreateSigToken("dm-lock", path, DeviceEndpoints.QuarantineReleaseSemanticAction, ["DATA_MANAGER", "SPONSOR_DESIGNER", "Medical Coder", "Safety Officer"]);
                return SendJsonAsync(dmClient, HttpMethod.Post, path,
                    new DeviceQuarantineReleaseRequest(seeded.QuarantineId.ToString(), "Inspection passed", "sig-tok"), sig);
            }),

            // 4. IpKitEndpoints (2)
            ("POST /api/v1/execution/rtsm/kits/reconcile-batch", () =>
            {
                const string path = "/api/v1/execution/rtsm/kits/reconcile-batch";
                string sig = TestSecurityContext.CreateSigToken("pharm-lock", path, IpKitEndpoints.ReconcileBatchSemanticAction, ["Pharmacist"]);
                return SendJsonAsync(pharmClient, HttpMethod.Post, path,
                    new IpKitBatchReconciliationRequest(Study, Site, [new IpKitItemReconcileRequest("KIT-SEED-01", "LOT-KIT-1", "LOT-KIT-1", 0, 0, "RETURNED", "Unused")], ReasonForChange: Reason), sig);
            }),
            ("POST /api/v1/execution/rtsm/kits/seed-inventory", () =>
                SendJsonAsync(dmClient, HttpMethod.Post, "/api/v1/execution/rtsm/kits/seed-inventory",
                    new SeedInventoryRequest(Study, Site, [new IpKitSeedItem("KIT-NEW-01", "LOT-KIT-1", FakeClock.DefaultStart.AddYears(1), "AVAILABLE")]))),

            // 5. LabRangeEndpoints (4)
            ("POST /api/v1/execution/lab-ranges", () =>
                SendJsonAsync(dmClient, HttpMethod.Post, "/api/v1/execution/lab-ranges",
                    new LabReferenceRangeCreateRequest(Study, "AST", "Aspartate Aminotransferase", SiteId: Site, Unit: "U/L", LowBound: 8, HighBound: 48))),
            ("PUT /api/v1/execution/lab-ranges/{id}", () =>
                SendJsonAsync(dmClient, HttpMethod.Put, $"/api/v1/execution/lab-ranges/{seeded.LabRangeId}",
                    new LabReferenceRangeUpdateRequest(LowBound: 9, HighBound: 50))),
            ("DELETE /api/v1/execution/lab-ranges/{id}", () =>
                SendEmptyWithReasonAsync(dmClient, HttpMethod.Delete, $"/api/v1/execution/lab-ranges/{seeded.LabRangeId}")),
            ("POST /api/v1/execution/labs/import/commit", () =>
                SendMultipartAsync(dmClient, "/api/v1/execution/labs/import/commit", BuildLabRangeImportForm())),

            // 6. LabToxicityEndpoints (1)
            ("POST /api/v1/execution/labs/toxicity/review/{review_id}/action", () =>
            {
                string path = LabToxicityEndpoints.ReviewActionPath(seeded.ToxicityReviewId);
                string sig = TestSecurityContext.CreateSigToken("pi-lock", path, LabToxicityEndpoints.ReviewActionSemanticAction, [RoleNormalization.ROLE_INVESTIGATOR]);
                return SendJsonAsync(piClient, HttpMethod.Post, path, new StagedAdverseEventActionRequest("DISMISS", Reason), sig);
            }),

            // 7. RandomizationEndpoints (3)
            ("POST /api/v1/execution/randomization/schemes", () =>
                SendJsonAsync(statClient, HttpMethod.Post, "/api/v1/execution/randomization/schemes",
                    new RandomizationSchemeDraftRequest(new StudyCode(Study), "minimization", new Dictionary<string, int> { ["A"] = 1, ["B"] = 1 }, null, null, null, 0.8, false))),
            ("POST /api/v1/execution/randomization/schemes/{schemeId}/approve", () =>
            {
                string path = RandomizationEndpoints.ApproveSchemeAction(seeded.DraftSchemeId);
                string sig = TestSecurityContext.CreateSigToken("stat-lock", path, RandomizationEndpoints.ApproveSchemeSemanticAction, [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN]);
                return SendEmptyWithReasonAsync(statClient, HttpMethod.Post, path, sig);
            }),
            ("POST /api/v1/execution/subjects/{subjectId}/randomize", () =>
            {
                string path = RandomizationEndpoints.RandomizeSubjectAction(EnrolledSubject);
                string sig = TestSecurityContext.CreateSigToken("crc-lock", path, RandomizationEndpoints.RandomizeSubjectSemanticAction, [RoleNormalization.ROLE_CRC]);
                return SendJsonAsync(crcClient, HttpMethod.Post, path, new RandomizeSubjectRequest(null), sig);
            }),

            // 8. SiteComplianceEndpoints (2)
            ("POST /api/v1/execution/webhooks/etmf", () =>
                SendJsonAsync(etmfClient, HttpMethod.Post, "/api/v1/execution/webhooks/etmf",
                    new EtmfCompletenessWebhookPayload(Study, Site, "SITE_ACTIVATION", true, null))),
            ("POST /api/v1/execution/sites/{siteId}/activate", () =>
                SendJsonAsync(dmClient, HttpMethod.Post, $"/api/v1/execution/sites/{Site}/activate",
                    new SiteActivationRequest(Study))),

            // 9. SubjectEndpoints (7)
            ("POST /api/v1/execution/subjects", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/subjects",
                    new SubjectCreateRequest("SUBJ-NEW-CREATE", Study, Site, null))),
            ("POST /api/v1/execution/subjects/{subjectId}/consent", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, $"/api/v1/execution/subjects/{Subject}/consent",
                    new SubjectConsentRequest(new ProtocolVersionPayload(Study, "1.0", 1), IcfSigned: true, IcfSignedDate: FakeClock.DefaultStart))),
            ("POST /api/v1/execution/subjects/{subjectId}/consents", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, $"/api/v1/execution/subjects/{Subject}/consents",
                    new SubjectConsentRequest(new ProtocolVersionPayload(Study, "1.0", 1), IcfSigned: true, IcfSignedDate: FakeClock.DefaultStart))),
            ("PATCH /api/v1/execution/subjects/{subjectId}/state", () =>
                SendJsonAsync(crcClient, HttpMethod.Patch, $"/api/v1/execution/subjects/{Subject}/state",
                    new SubjectStateUpdateRequest("ENROLLED", null))),
            ("PATCH /api/v1/execution/subjects/{subjectId}/site", () =>
                SendJsonAsync(crcClient, HttpMethod.Patch, $"/api/v1/execution/subjects/{Subject}/site",
                    new SubjectSiteAssignmentRequest(Site))),
            ("PUT /api/v1/execution/subjects/{subjectId}/demographics", () =>
                SendJsonAsync(crcClient, HttpMethod.Put, $"/api/v1/execution/subjects/{Subject}/demographics",
                    new SubjectDemographicsUpdateRequest(new DemographicsPayload("Alex Patient", "1980-01-01", "M", null), null))),
            ("DELETE /api/v1/execution/subjects/{subjectId}/demographics", () =>
                SendEmptyWithReasonAsync(crcClient, HttpMethod.Delete, $"/api/v1/execution/subjects/{Subject}/demographics")),

            // 10. SynonymEndpoints (2)
            ("POST /api/v1/execution/coding/synonyms/import", () =>
                SendMultipartAsync(dmClient, "/api/v1/execution/coding/synonyms/import", BuildStudySynonymForm())),
            ("DELETE /api/v1/execution/coding/synonyms/{id}", () =>
                SendEmptyWithReasonAsync(dmClient, HttpMethod.Delete, $"/api/v1/execution/coding/synonyms/{seeded.StudySynonymId}?reason_for_change=cleanup")),
        };

        mutatingCalls.Should().HaveCount(34);

        foreach (var (name, send) in mutatingCalls)
        {
            using var response = await send();
            string body = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Locked, $"mutating route '{name}' must return 423 Locked when covered by a kernel lock, but returned {(int)response.StatusCode}: {body}");
            using var problem = JsonDocument.Parse(body);
            problem.RootElement.GetProperty("code").GetString().Should().Be(
                HttpKernelLockGuard.ObservationLockedCode,
                $"mutating route '{name}' must report K.Observation.Locked");
        }

        // All 7 stateless POST routes remain 200 OK even while IsLocked == true!
        var statelessCalls = new List<(string Name, Func<Task<HttpResponseMessage>> Send)>
        {
            ("POST /api/v1/execution/rtsm/cgt/verify-barcode", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/rtsm/cgt/verify-barcode",
                    new CgtBarcodeVerifyRequest($"STUDY:{Study}|SITE:{Site}|SUBJ:{EnrolledSubject}|DIN:DIN-SEED-01|BATCH:BATCH-01"))),
            ("POST /api/v1/execution/devices/parse-udi", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/devices/parse-udi",
                    new UdiParseRequest("(01)00850006781111(10)LOT123(21)SN001"))),
            ("POST /api/v1/execution/rtsm/kits/validate-scan", () =>
                SendJsonAsync(pharmClient, HttpMethod.Post, "/api/v1/execution/rtsm/kits/validate-scan",
                    new IpKitScanValidateRequest("KIT-SEED-01", Study, Site, "LOT-KIT-1"))),
            ("POST /api/v1/execution/labs/import/dry-run", () =>
                SendMultipartAsync(dmClient, "/api/v1/execution/labs/import/dry-run", BuildLabRangeImportForm())),
            ("POST /api/v1/execution/labs/convert-unit", () =>
                SendJsonAsync(dmClient, HttpMethod.Post, "/api/v1/execution/labs/convert-unit",
                    new UnitConversionRequest("GLUC", 5.5, "mmol/L", "mg/dL"))),
            ("POST /api/v1/execution/labs/toxicity/evaluate", () =>
                SendJsonAsync(crcClient, HttpMethod.Post, "/api/v1/execution/labs/toxicity/evaluate",
                    new LabToxicityEvaluateRequest("ALT", 180, "U/L", RefLow: 7, RefHigh: 40))),
            ("POST /api/v1/execution/coding/synonyms/validate", () =>
                SendMultipartAsync(dmClient, "/api/v1/execution/coding/synonyms/validate", BuildStudySynonymForm())),
        };

        statelessCalls.Should().HaveCount(7);

        foreach (var (name, send) in statelessCalls)
        {
            using var response = await send();
            string body = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                $"stateless route '{name}' must remain 200 OK even when the study is locked, but returned {(int)response.StatusCode}: {body}");
        }
    }

    private HttpClient Client(string userId, string[] roles, string[] siteIds, bool unblinded = false, bool allStudies = false) =>
        _factory.CreateSignedClient(new GatewayIdentity(
            UserId: userId,
            Roles: roles,
            SiteIds: siteIds,
            TenantId: Tenant,
            StudyIds: allStudies ? [StudyAccessEvaluator.AllStudies] : [Study],
            UnblindedAccess: unblinded,
            ChangeReason: Reason));

    private static async Task<HttpResponseMessage> SendJsonAsync<TBody>(
        HttpClient client,
        HttpMethod method,
        string path,
        TBody body,
        string? sigToken = null)
    {
        using var req = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: ExecutionApiFactory.JsonOptions),
        };
        req.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        if (sigToken is not null)
        {
            req.Headers.Add(GatewayHeaders.SigToken, sigToken);
        }

        return await client.SendAsync(req, Ct);
    }

    private static async Task<HttpResponseMessage> SendEmptyWithReasonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? sigToken = null)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        if (sigToken is not null)
        {
            req.Headers.Add(GatewayHeaders.SigToken, sigToken);
        }

        return await client.SendAsync(req, Ct);
    }

    private static async Task<HttpResponseMessage> SendMultipartAsync(
        HttpClient client,
        string path,
        MultipartFormDataContent form)
    {
        using (form)
        using (var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = form })
        {
            req.Headers.Add(GatewayHeaders.ChangeReason, Reason);
            return await client.SendAsync(req, Ct);
        }
    }

    private static MultipartFormDataContent BuildLabRangeImportForm()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "test_code,unit,lower_limit,upper_limit,sex,age_min,age_max\nALT,U/L,7,56,ALL,18,99\n"));
        form.Add(file, "file", "ranges.csv");
        form.Add(new StringContent(Study), "study_id");
        form.Add(new StringContent(Site), "site_id");
        return form;
    }

    private static MultipartFormDataContent BuildStudySynonymForm()
    {
        var form = new MultipartFormDataContent();
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "verbatim_text,dictionary_type,dictionary_version,target_code,target_term,target_level\nheadache,MEDDRA,26.0,10019211,Headache,PT\n"));
        form.Add(csv, "file", "synonyms.csv");
        form.Add(new StringContent("STUDY"), "scope");
        form.Add(new StringContent(Study), "study_id");
        form.Add(new StringContent(Reason), "reason_for_change");
        return form;
    }

    private async Task<SeededIds> SeedFixturesAsync()
    {
        using var warmClient = _factory.CreateSignedClient();
        using var scope = _factory.Services.CreateSeedingScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();

        var studyCode = new StudyCode(Study);
        var siteCode = new SiteCode(Site);

        var screeningSubject = ClinicalSubject.Create(new SubjectCode(Subject), studyCode, siteCode, 0, "cipher-01", null);
        var enrolledSubject = ClinicalSubject.Create(new SubjectCode(EnrolledSubject), studyCode, siteCode, 1, "cipher-02", null);
        enrolledSubject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();

        var draftScheme = RandomizationScheme.Draft(
            studyCode,
            version: 1,
            RandomizationMethod.Minimization,
            new Dictionary<string, int> { ["A"] = 1, ["B"] = 1 },
            new Dictionary<string, IReadOnlyList<string>> { ["sex"] = ["F", "M"] },
            null,
            0,
            0.8,
            false,
            "stat-seed",
            Reason,
            clock).Value!;

        var coc = new CgtChainOfCustody(Guid.NewGuid(), Study, Site, EnrolledSubject, "DIN-SEED-01", Site, clock.UtcNow);
        const string dossierId = "DOSSIER-LOCK-01";
        var dossier = CecDossier.Initialize(dossierId, "EVT-LOCK-01", Study, EnrolledSubject, "MASK-SUBJ-01", "MACE", "MI", "2026-01-01", Site, "MASK-SITE-01");
        var review = StagedAdverseEventReview.Create(studyCode, new SubjectCode(EnrolledSubject), new TestCode("ALT"), "ALT", 280, "U/L", 3, "ALT increased", "Grade 3", false, false, null, siteId: siteCode);

        var device = new DeviceInventoryItem(Guid.NewGuid(), Study, Site, "CardioPump", "00850006781111", "LOT-DEV-1", serialNumber: "SN-SEED-01");
        var quarantine = new DeviceQuarantineRecord(Guid.NewGuid(), Study, Site, "Initial quarantine", clock.UtcNow, deviceInventoryId: device.Id.ToString(), serialNumber: "SN-SEED-01");
        var labRange = new LabReferenceRange(Guid.NewGuid(), Study, "HGB", "Hemoglobin", "LOCAL", Site, "g/dL", "g/dL", "ALL", 18, 99, 12, 16, null, null, "718-7", "svc:seed", Reason, clock.UtcNow.UtcDateTime);
        var kit = new IpKitInventoryItem(Guid.NewGuid(), "KIT-SEED-01", Study, Site, "LOT-KIT-1", clock.UtcNow.AddYears(1), "AVAILABLE");
        var studySynonym = new SynonymRule(Guid.NewGuid(), "STUDY", Study, "NAUSEA", "MEDDRA", "26.0", "10028813", "Nausea", "PT", Guid.NewGuid(), "svc:seed", Reason, clock.UtcNow.UtcDateTime);

        db.ClinicalSubjects.AddRange(screeningSubject, enrolledSubject);
        db.RandomizationSchemes.Add(draftScheme);
        db.CGTChainsOfCustody.Add(coc);
        db.CecDossiers.Add(dossier);
        db.StagedAdverseEventReviews.Add(review);
        db.DeviceInventoryItems.Add(device);
        db.DeviceQuarantineRecords.Add(quarantine);
        db.LabReferenceRanges.Add(labRange);
        db.IpKitInventoryItems.Add(kit);
        db.SynonymRules.Add(studySynonym);
        await db.SaveChangesAsync(Ct);

        return new SeededIds(
            draftScheme.Id,
            coc.Id,
            dossierId,
            review.Id,
            device.Id,
            quarantine.Id,
            labRange.Id,
            studySynonym.Id);
    }

    private sealed record SeededIds(
        Guid DraftSchemeId,
        Guid CocId,
        string DossierId,
        string ToxicityReviewId,
        Guid DeviceId,
        Guid QuarantineId,
        Guid LabRangeId,
        Guid StudySynonymId);

    private sealed class ToggleableKernelLockGuard : IKernelLockGuard
    {
        private static readonly Task<Result<bool>> Unlocked = Task.FromResult<Result<bool>>(true);

        public volatile bool IsLocked;

        public Task<Result<bool>> EnsureUnlockedAsync(
            string? studyCode,
            string? siteCode = null,
            string? subjectCode = null,
            CancellationToken cancellationToken = default) =>
            IsLocked
                ? Task.FromResult<Result<bool>>(DomainError.Conflict(
                    HttpKernelLockGuard.ObservationLockedCode,
                    "Scope is frozen or locked in the Clinical Kernel."))
                : Unlocked;

        public Task<Result<bool>> EnsureUnlockedForServiceAsync(
            ServiceAccount serviceAccount,
            string? tenantCode,
            string? studyCode,
            string? siteCode = null,
            string? subjectCode = null,
            CancellationToken cancellationToken = default) =>
            EnsureUnlockedAsync(studyCode, siteCode, subjectCode, cancellationToken);
    }
}
