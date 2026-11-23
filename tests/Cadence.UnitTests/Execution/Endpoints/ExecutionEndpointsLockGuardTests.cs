using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Cgt;
using Cadence.Apps.Execution.Domain.Coding;
using Cadence.Apps.Execution.Domain.Devices;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain;
using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Exercises every mutating standalone route in <c>apps/execution</c> (all 33 documented OpenAPI write endpoints plus
/// the legacy <c>/consents</c> alias) through the real <c>Program.cs</c> pipeline and verifies each rejects writes with
/// HTTP 423 Locked when <see cref="IKernelLockGuard"/> reports a covering kernel lock, and fails closed with HTTP 409
/// when the kernel lock check is unavailable.
/// </summary>
public sealed class ExecutionEndpointsLockGuardTests : IAsyncLifetime
{
    private const string StudyCodeValue = "STUDY-LOCK-01";
    private const string SiteCodeValue = "SITE-LOCK-01";
    private const string SubjectCodeValue = "SUBJ-LOCK-001";
    private const string Reason = "Attempted write against locked clinical scope";

    private static readonly Guid RangeId = Guid.Parse("11111111-1111-7111-8111-111111111111");
    private static readonly Guid CocId = Guid.Parse("33333333-3333-7333-8333-333333333333");
    private static readonly Guid QuarantineId = Guid.Parse("44444444-4444-7444-8444-444444444444");
    private static readonly Guid SynonymId = Guid.Parse("55555555-5555-7555-8555-555555555555");
    private const string DossierId = "DOSSIER-LOCK-01";

    private readonly ConfigurableLockGuard _lockGuard = new();
    private ExecutionApiFactory _baseFactory = null!;
    private WebApplicationFactory<Cadence.Apps.Execution.Program> _factory = null!;
    private Guid _schemeId;
    private string _stagedReviewId = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _baseFactory = new ExecutionApiFactory();
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKernelLockGuard>();
                services.AddSingleton<IKernelLockGuard>(_lockGuard);
            });
        });
        using var bootstrapClient = _factory.CreateSignedClient();
        using var scope = _factory.Services.CreateScope();

        using var tenantBinding = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(TestSecurityContext.TenantCode);
        scope.ServiceProvider.GetRequiredService<IAuditActorScope>()
            .RunAs(new ServiceAccount("svc:lock-guard-test", "Seed standalone entities for lock guard verification"));

        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var clock = new FakeClock();
        var now = clock.UtcNow;

        db.LabReferenceRanges.Add(new LabReferenceRange(
            id: RangeId,
            studyId: StudyCodeValue,
            testCode: "ALT",
            testName: "Alanine Aminotransferase",
            source: "LOCAL",
            siteId: SiteCodeValue,
            unit: "U/L",
            normalizedUnit: "U/L",
            sexApplicability: "ALL",
            ageLow: 18,
            ageHigh: 99,
            lowBound: 7,
            highBound: 56,
            criticalLow: null,
            criticalHigh: null,
            loincCode: "1742-6",
            createdBy: "dm-seed",
            reasonForChange: "Seeded range",
            createdAt: now.UtcDateTime));

        var draftedScheme = RandomizationScheme.Draft(
            new StudyCode(StudyCodeValue),
            version: 1,
            RandomizationMethod.PermutedBlock,
            new Dictionary<string, int> { ["ACTIVE"] = 1, ["PLACEBO"] = 1 },
            stratificationFactors: null,
            blockSizes: [4],
            slotsPerStratum: 8,
            preferredArmProbability: 0.75,
            requiresKit: false,
            draftedBy: "stat-drafter",
            reasonForChange: "Seeded draft scheme",
            clock: clock).Value!;
        _schemeId = draftedScheme.Id;
        db.RandomizationSchemes.Add(draftedScheme);

        var subject = ClinicalSubject.Create(
            new SubjectCode(SubjectCodeValue),
            new StudyCode(StudyCodeValue),
            new SiteCode(SiteCodeValue),
            enrollmentIndex: 1,
            encryptedDemographics: null,
            stratFactors: null);
        db.ClinicalSubjects.Add(subject);

        db.CGTChainsOfCustody.Add(new CgtChainOfCustody(
            CocId,
            StudyCodeValue,
            SiteCodeValue,
            SubjectCodeValue,
            din: "=W12342612345600",
            collectionSiteId: SiteCodeValue,
            harvestedAt: now));

        db.CecDossiers.Add(CecDossier.Initialize(
            dossierId: DossierId,
            eventId: "EVT-01",
            studyId: StudyCodeValue,
            subjectId: SubjectCodeValue,
            maskedSubjectId: "MASK-001",
            candidateEventType: "MACE",
            reportedTerm: "Myocardial infarction",
            reportedOnsetDate: "2026-06-01",
            siteId: SiteCodeValue,
            maskedSiteId: "SITE-MASK-01"));

        db.DeviceQuarantineRecords.Add(new DeviceQuarantineRecord(
            QuarantineId,
            StudyCodeValue,
            SiteCodeValue,
            "Suspected seal defect",
            now,
            serialNumber: "SN-LOCK-01"));

        db.SynonymRules.Add(new SynonymRule(
            SynonymId,
            scope: "STUDY",
            studyId: StudyCodeValue,
            verbatimText: "headache",
            dictionaryType: "MEDDRA",
            dictionaryVersion: "27.0",
            targetCode: "10019211",
            targetTerm: "Headache",
            targetLevel: "PT",
            importJobId: Guid.NewGuid(),
            createdBy: "dm-seed",
            reasonForChange: "Seeded synonym",
            createdAt: now.UtcDateTime));

        var stagedReview = StagedAdverseEventReview.Create(
            studyId: new StudyCode(StudyCodeValue),
            subjectId: new SubjectCode(SubjectCodeValue),
            testCode: new TestCode("ALT"),
            testName: "Alanine Aminotransferase",
            labValue: 320.0,
            unit: "U/L",
            ctcaeGrade: 3,
            toxicityTerm: "ALT increased",
            clinicalDescription: "Grade 3 elevation",
            isHysLawWarning: false,
            isDeltaCheckAlert: false,
            deltaPercentage: null,
            siteId: new SiteCode(SiteCodeValue));
        db.StagedAdverseEventReviews.Add(stagedReview);
        _stagedReviewId = stagedReview.Id;

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        _baseFactory.Dispose();
        return ValueTask.CompletedTask;
    }

    public static TheoryData<string> MutatingRouteKeys => new()
    {
        "LabRange.Create",
        "LabRange.Update",
        "LabRange.Delete",
        "LabRange.ImportCommit",
        "Randomization.DraftScheme",
        "Randomization.ApproveScheme",
        "Randomization.RandomizeSubject",
        "Cgt.Apheresis",
        "Cgt.Milestone",
        "Cgt.ManufacturingBind",
        "Cgt.BedsideVerify",
        "Cgt.Infuse",
        "Cgt.Returns",
        "Cgt.DestructionCertificate",
        "IpKit.ReconcileBatch",
        "IpKit.SeedInventory",
        "Subject.Create",
        "Subject.Consent",
        "Subject.ConsentsAlias",
        "Subject.AdvanceState",
        "Subject.AssignSite",
        "Subject.UpdateDemographics",
        "Subject.DeleteDemographics",
        "Cec.ChairAdjudicate",
        "SiteCompliance.EtmfWebhook",
        "SiteCompliance.ActivateSite",
        "Device.RegisterInventory",
        "Device.Dispense",
        "Device.LogDeficiency",
        "Device.InitiateQuarantine",
        "Device.ReleaseQuarantine",
        "Synonym.Import",
        "Synonym.Deactivate",
        "LabToxicity.ReviewAction",
    };

    [Theory]
    [MemberData(nameof(MutatingRouteKeys))]
    public async Task MutatingRoute_WhenCoveringKernelScopeIsLocked_Returns423Locked(string routeKey)
    {
        var ct = TestContext.Current.CancellationToken;
        _lockGuard.NextError = DomainError.Conflict("K.Observation.Locked", "Scope is locked in the Clinical Kernel.");
        _lockGuard.CallCount = 0;

        var (role, request) = BuildMutatingRequest(routeKey);
        using var client = CreateClientForRole(role);
        using var requestMessage = request;
        using var response = await client.SendAsync(requestMessage, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.Locked, $"{routeKey} must reject writes with HTTP 423 when locked ({body})");
        _lockGuard.CallCount.Should().BeGreaterThan(0, $"{routeKey} must invoke IKernelLockGuard");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("K.Observation.Locked");
    }

    [Fact]
    public async Task MutatingRoute_WhenKernelLockCheckIsUnavailable_FailsClosedWith409AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        _lockGuard.NextError = DomainError.Conflict("KERNEL_LOCK_CHECK_UNAVAILABLE", "Clinical Kernel unreachable.");

        int subjectsBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            using var tenantBinding = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(TestSecurityContext.TenantCode);
            subjectsBefore = await scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().ClinicalSubjects.CountAsync(ct);
        }

        var (role, request) = BuildMutatingRequest("Subject.Create");
        using var client = CreateClientForRole(role);
        using var requestMessage = request;
        using var response = await client.SendAsync(requestMessage, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("KERNEL_LOCK_CHECK_UNAVAILABLE");

        using (var scope = _factory.Services.CreateScope())
        {
            using var tenantBinding = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(TestSecurityContext.TenantCode);
            var subjectsAfter = await scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().ClinicalSubjects.CountAsync(ct);
            subjectsAfter.Should().Be(subjectsBefore);
        }
    }

    private HttpClient CreateClientForRole(string role)
    {
        string userId = $"user-{role.Replace(' ', '-').ToLowerInvariant()}";
        var identity = TestSecurityContext.Identity(userId, role) with
        {
            SiteIds = [SiteCodeValue],
            StudyIds = [StudyCodeValue],
        };
        return _factory.CreateSignedClient(identity);
    }

    private (string Role, HttpRequestMessage Request) BuildMutatingRequest(string routeKey)
    {
        return routeKey switch
        {
            "LabRange.Create" => (
                "Data Manager",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/lab-ranges", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    source = "LOCAL",
                    test_code = "AST",
                    test_name = "Aspartate Aminotransferase",
                    unit = "U/L",
                    low_bound = 10.0,
                    high_bound = 40.0,
                })),

            "LabRange.Update" => (
                "Data Manager",
                JsonRequest(HttpMethod.Put, $"/api/v1/execution/lab-ranges/{RangeId:D}", new
                {
                    high_bound = 60.0,
                })),

            "LabRange.Delete" => (
                "Data Manager",
                JsonRequest(HttpMethod.Delete, $"/api/v1/execution/lab-ranges/{RangeId:D}", body: null)),

            "LabRange.ImportCommit" => (
                "Data Manager",
                MultipartLabImportRequest()),

            "Randomization.DraftScheme" => (
                RoleNormalization.ROLE_UNBLINDED_STATISTICIAN,
                JsonRequest(HttpMethod.Post, "/api/v1/execution/randomization/schemes", new
                {
                    study_id = StudyCodeValue,
                    method = "permuted_block",
                    arms = new Dictionary<string, int> { ["ACTIVE"] = 1, ["PLACEBO"] = 1 },
                    block_sizes = new[] { 4 },
                    slots_per_stratum = 8,
                    requires_kit = false,
                })),

            "Randomization.ApproveScheme" => (
                RoleNormalization.ROLE_UNBLINDED_STATISTICIAN,
                SignedJsonRequest(
                    RoleNormalization.ROLE_UNBLINDED_STATISTICIAN,
                    HttpMethod.Post,
                    RandomizationEndpoints.ApproveSchemeAction(_schemeId),
                    RandomizationEndpoints.ApproveSchemeSemanticAction,
                    body: new { })),

            "Randomization.RandomizeSubject" => (
                "Site Investigator",
                SignedJsonRequest(
                    "Site Investigator",
                    HttpMethod.Post,
                    RandomizationEndpoints.RandomizeSubjectAction(SubjectCodeValue),
                    RandomizationEndpoints.RandomizeSubjectSemanticAction,
                    new { stratification_factors = new Dictionary<string, string>() })),

            "Cgt.Apheresis" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/cgt/apheresis", new
                {
                    study_id = StudyCodeValue,
                    collection_site_id = SiteCodeValue,
                    subject_id = SubjectCodeValue,
                })),

            "Cgt.Milestone" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/cgt/milestone", new
                {
                    study_id = StudyCodeValue,
                    chain_of_custody_id = CocId,
                    stage = "TRANSIT",
                    milestone_type = "COURIER_PICKUP",
                })),

            "Cgt.ManufacturingBind" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/cgt/manufacturing-bind", new
                {
                    study_id = StudyCodeValue,
                    chain_of_custody_id = CocId,
                    batch_id = "BATCH-01",
                    manufacturing_facility_id = "MFG-01",
                })),

            "Cgt.BedsideVerify" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/cgt/bedside-verify", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    subject_id = SubjectCodeValue,
                    din = "=W12342612345600",
                    batch_id = "BATCH-01",
                    bag_barcode_payload = "=W12342612345600",
                    patient_wristband_id = SubjectCodeValue,
                    primary_verifier_id = "nurse-1",
                    secondary_verifier_id = "nurse-2",
                })),

            "Cgt.Infuse" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/cgt/infuse", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    subject_id = SubjectCodeValue,
                    chain_of_custody_id = CocId,
                    unlock_token = "tok-01",
                    primary_verifier_id = "nurse-1",
                })),

            "Cgt.Returns" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/cgt/returns", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    subject_id = SubjectCodeValue,
                    kit_number = "KIT-01",
                    vial_identifier = "VIAL-01",
                })),

            "Cgt.DestructionCertificate" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/cgt/destruction-certificate", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    lot_number = "LOT-01",
                    kit_numbers = new[] { "KIT-01" },
                    vial_identifiers = new[] { "VIAL-01" },
                    destruction_reason = "Expired",
                    destruction_method = "Incineration",
                    destruction_facility = "Site Pharmacy",
                    witness_1_id = "pharm-1",
                    witness_2_id = "pharm-2",
                    coc_id = CocId,
                })),

            "IpKit.ReconcileBatch" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/kits/reconcile-batch", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    disposition_action = "DESTROYED_AT_SITE",
                    items = new[]
                    {
                        new { kit_number = "KIT-01", lot_number = "LOT-01", return_condition = "INTACT" },
                    },
                })),

            "IpKit.SeedInventory" => (
                "Data Manager",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/rtsm/kits/seed-inventory", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    kits = new[]
                    {
                        new { kit_number = "KIT-SEED-01", lot_number = "LOT-01", expiry_date = "2027-06-01T00:00:00Z", status = "AVAILABLE" },
                    },
                })),

            "Subject.Create" => (
                "Site Investigator",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/subjects", new
                {
                    subject_id = "SUBJ-NEW-002",
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                })),

            "Subject.Consent" => (
                "Site Investigator",
                JsonRequest(HttpMethod.Post, $"/api/v1/execution/subjects/{SubjectCodeValue}/consent", new
                {
                    protocol_version = new { study_id = StudyCodeValue, version_tag = "1.0.0", version_index = 1 },
                    icf_signed = true,
                    icf_signed_date = "2026-06-01T10:00:00Z",
                })),

            "Subject.ConsentsAlias" => (
                "Site Investigator",
                JsonRequest(HttpMethod.Post, $"/api/v1/execution/subjects/{SubjectCodeValue}/consents", new
                {
                    protocol_version = new { study_id = StudyCodeValue, version_tag = "1.0.0", version_index = 1 },
                    icf_signed = true,
                    icf_signed_date = "2026-06-01T10:00:00Z",
                })),

            "Subject.AdvanceState" => (
                "Site Investigator",
                JsonRequest(HttpMethod.Patch, $"/api/v1/execution/subjects/{SubjectCodeValue}/state", new
                {
                    status = "ENROLLED",
                })),

            "Subject.AssignSite" => (
                "Site Investigator",
                JsonRequest(HttpMethod.Patch, $"/api/v1/execution/subjects/{SubjectCodeValue}/site", new
                {
                    site_id = SiteCodeValue,
                })),

            "Subject.UpdateDemographics" => (
                "Site Investigator",
                JsonRequest(HttpMethod.Put, $"/api/v1/execution/subjects/{SubjectCodeValue}/demographics", new
                {
                    demographics = new { gender = "F", birthdate = "1985-03-10" },
                })),

            "Subject.DeleteDemographics" => (
                "Site Investigator",
                JsonRequest(HttpMethod.Delete, $"/api/v1/execution/subjects/{SubjectCodeValue}/demographics", body: null)),

            "Cec.ChairAdjudicate" => (
                PermissionMatrix.CecChairRole,
                SignedJsonRequest(
                    PermissionMatrix.CecChairRole,
                    HttpMethod.Post,
                    CecEndpoints.ChairAdjudicateAction(DossierId),
                    CecEndpoints.ChairAdjudicateSemanticAction,
                    new
                    {
                        final_classification = "CONFIRMED_MACE",
                        final_subtype = "TYPE_1_MI",
                        final_adjudicated_onset_date = "2026-06-01",
                        adjudication_rationale = "ECG + troponin criteria met",
                    })),

            "SiteCompliance.EtmfWebhook" => (
                PermissionMatrix.EtmfServiceRole,
                JsonRequest(HttpMethod.Post, "/api/v1/execution/webhooks/etmf", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    milestone = "SITE_ACTIVATION",
                    is_complete = true,
                })),

            "SiteCompliance.ActivateSite" => (
                "Data Manager",
                JsonRequest(HttpMethod.Post, $"/api/v1/execution/sites/{SiteCodeValue}/activate", new
                {
                    study_id = StudyCodeValue,
                })),

            "Device.RegisterInventory" => (
                "Data Manager",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/devices/inventory", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    brand_name = "Cadence Pump",
                    udi_di = "00884838000018",
                    lot_number = "LOT-DEV-01",
                    serial_number = "SN-DEV-01",
                })),

            "Device.Dispense" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/devices/dispense", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    subject_id = SubjectCodeValue,
                    serial_number = "SN-DEV-01",
                })),

            "Device.LogDeficiency" => (
                "Site Investigator",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/devices/deficiencies", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    subject_id = SubjectCodeValue,
                    udi_di = "00884838000018",
                    lot_number = "LOT-DEV-01",
                    deficiency_description = "Battery alarm",
                    incident_date = "2026-06-02T09:00:00Z",
                    awareness_date = "2026-06-02T09:30:00Z",
                })),

            "Device.InitiateQuarantine" => (
                "Pharmacist",
                JsonRequest(HttpMethod.Post, "/api/v1/execution/devices/quarantine", new
                {
                    study_id = StudyCodeValue,
                    site_id = SiteCodeValue,
                    reason = "Temperature excursion",
                    serial_number = "SN-DEV-01",
                })),

            "Device.ReleaseQuarantine" => (
                RoleNormalization.ROLE_SPONSOR_MM,
                JsonRequest(HttpMethod.Post, "/api/v1/execution/devices/quarantine/release", new
                {
                    quarantine_id = QuarantineId.ToString("D"),
                    release_justification = "Manufacturer stability letter verified",
                    signature_token = "sig-release",
                })),

            "Synonym.Import" => (
                "Data Manager",
                MultipartSynonymImportRequest()),

            "Synonym.Deactivate" => (
                "Data Manager",
                JsonRequest(HttpMethod.Delete, $"/api/v1/execution/coding/synonyms/{SynonymId:D}", body: null)),

            "LabToxicity.ReviewAction" => (
                "Site Investigator",
                SignedJsonRequest(
                    "Site Investigator",
                    HttpMethod.Post,
                    LabToxicityEndpoints.ReviewActionPath(_stagedReviewId),
                    LabToxicityEndpoints.ReviewActionSemanticAction,
                    new
                    {
                        action = "DISMISS",
                        reason_for_change = "Repeat lab normal",
                    })),

            _ => throw new ArgumentOutOfRangeException(nameof(routeKey), routeKey, "Unknown mutating route key."),
        };
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static HttpRequestMessage SignedJsonRequest(
        string role,
        HttpMethod method,
        string path,
        string semanticAction,
        object body)
    {
        string userId = $"user-{role.Replace(' ', '-').ToLowerInvariant()}";
        var request = JsonRequest(method, path, body);
        request.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(userId, path, semanticAction));
        return request;
    }

    private static HttpRequestMessage MultipartLabImportRequest()
    {
        var form = new MultipartFormDataContent();
        form.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes("test_code,unit,lower_limit,upper_limit,sex,age_min,age_max\nALT,U/L,7,56,ALL,18,99\n")),
            "file",
            "ranges.csv");
        form.Add(new StringContent(StudyCodeValue), "study_id");
        form.Add(new StringContent(SiteCodeValue), "site_id");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/execution/labs/import/commit") { Content = form };
        request.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        return request;
    }

    private static HttpRequestMessage MultipartSynonymImportRequest()
    {
        var form = new MultipartFormDataContent();
        form.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes("verbatim_text,dictionary_type,dictionary_version,target_code,target_term,target_level\nheadache,MEDDRA,27.0,10019211,Headache,PT\n")),
            "file",
            "synonyms.csv");
        form.Add(new StringContent("STUDY"), "scope");
        form.Add(new StringContent(StudyCodeValue), "study_id");
        form.Add(new StringContent(Reason), "reason_for_change");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/execution/coding/synonyms/import") { Content = form };
        request.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        return request;
    }

    private sealed class ConfigurableLockGuard : IKernelLockGuard
    {
        public DomainError? NextError { get; set; }

        public int CallCount { get; set; }

        public Task<DomainError?> CheckWriteAllowedAsync(
            string? studyCode,
            string? siteCode = null,
            string? subjectCode = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(NextError);
        }
    }
}
