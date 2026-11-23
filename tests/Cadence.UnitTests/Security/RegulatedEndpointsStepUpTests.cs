using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.UnitTests.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// End-to-end HTTP verification of 21 CFR Part 11 step-up credential verification (<c>X-Sig-Token</c>), deterministic
/// <c>batch_id</c> binding, mixed-mode conditional enforcement, single-use replay rejection, and verified signer
/// attribution across all twelve regulated routes in <c>execution</c>, <c>ctms</c>, <c>payments</c>, <c>imaging</c>,
/// and <c>interop</c>.
/// </summary>
public sealed partial class RegulatedEndpointsStepUpTests
{
    private const string StudyId = "STUDY-STEPUP-1";
    private const string SiteId = "SITE-101";
    private const string UnblindReason =
        "Acute Grade 4 neurological SAE requiring immediate treatment arm disclosure to ER team.";

    private static readonly DateTimeOffset FixedNow = new(2026, 11, 23, 14, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*){1,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalSemanticActionRegex();

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public void AllTwelveSemanticActions_ConformToGatewayGrammar()
    {
        string[] semanticActions =
        [
            SubjectEndpoints.UnblindSemanticAction,
            DoaEndpoints.SignOffSemanticAction,
            MonitoringVisitEndpoints.SignOffSemanticAction,
            LetterTemplateEndpoints.CompileSemanticAction,
            StartupGreenlightEndpoints.CertifySemanticAction,
            StartupGreenlightEndpoints.BatchCertifySemanticAction,
            PaymentEndpoints.RemittanceSignSemanticAction,
            ImagingAdjudicationEndpoints.ChairAdjudicateSemanticAction,
            EsourceEndpoints.AcceptSemanticAction,
            EsourceEndpoints.BatchAcceptSemanticAction,
            EsourceEndpoints.RejectSemanticAction,
            EsourceEndpoints.BatchRejectSemanticAction,
        ];

        semanticActions.Should().HaveCount(12)
            .And.OnlyHaveUniqueItems()
            .And.OnlyContain(action => CanonicalSemanticActionRegex().IsMatch(action));
    }

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ExecutionSubjectState_EnforcesStepUpOnlyForUnblinded_AndLeavesBlindedSubjectUnchangedOnRejection()
    {
        var clock = new FakeClock(FixedNow);
        await using var baseFactory = new ExecutionApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(host =>
        {
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(new ReplayCache(clock));
            });
        });
        string subjectScreened = "SUBJ-ENR-01";
        string subjectRandomized = "SUBJ-UNB-01";

        using (var scope = factory.Services.CreateScope())
        {
            var tenantAccessor = scope.ServiceProvider.GetService<CurrentTenantAccessor>();
            tenantAccessor?.UseNonHttpSeedingFallbackTenant(TestSecurityContext.TenantCode);
            scope.ServiceProvider.GetRequiredService<IAuditActorScope>()
                .RunAs(new ServiceAccount("svc:test-seeder", "Seed test subjects"));
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();

            var screened = ClinicalSubject.Create(
                new SubjectCode(subjectScreened),
                new StudyCode(StudyId),
                new SiteCode(SiteId),
                enrollmentIndex: 1,
                encryptedDemographics: "cipher-1",
                stratFactors: null);
            var randomized = ClinicalSubject.Create(
                new SubjectCode(subjectRandomized),
                new StudyCode(StudyId),
                new SiteCode(SiteId),
                enrollmentIndex: 2,
                encryptedDemographics: "cipher-2",
                stratFactors: null);
            randomized.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
            randomized.Randomize(Guid.NewGuid(), kitReference: null, new Dictionary<string, object?>()).IsSuccess.Should().BeTrue();

            db.SiteComplianceCaches.Add(
                SiteComplianceCache.Create(
                    new StudyCode(StudyId),
                    new SiteCode(SiteId),
                    "SITE_ACTIVATION",
                    isComplete: true,
                    []));
            db.ClinicalSubjects.AddRange(screened, randomized);
            await db.SaveChangesAsync(Ct);
        }

        var pi = TestSecurityContext.Identity("pi-stepup-1", "Principal Investigator") with
        {
            StudyIds = [StudyId],
            SiteIds = [SiteId],
            ChangeReason = UnblindReason,
        };
        using var client = factory.CreateSignedClient(pi);

        // 1. Non-unblinding transitions (ENROLLED, WITHDRAWN) succeed WITHOUT X-Sig-Token
        var enrollRes = await client.PatchAsJsonAsync(
            $"/api/v1/execution/subjects/{subjectScreened}/state",
            new { status = "ENROLLED" },
            Ct);
        enrollRes.StatusCode.Should().Be(HttpStatusCode.OK, await enrollRes.Content.ReadAsStringAsync(Ct));

        var withdrawRes = await client.PatchAsJsonAsync(
            $"/api/v1/execution/subjects/{subjectScreened}/state",
            new { status = "WITHDRAWN" },
            Ct);
        withdrawRes.StatusCode.Should().Be(HttpStatusCode.OK, await withdrawRes.Content.ReadAsStringAsync(Ct));

        // 2. UNBLINDED transition WITHOUT X-Sig-Token returns 401 REAUTHENTICATION_REQUIRED and leaves subject blinded
        string unblindPath = SubjectEndpoints.UnblindAction(subjectRandomized);
        var missingRes = await client.PatchAsJsonAsync(unblindPath, new { status = "UNBLINDED" }, Ct);
        await AssertReauthenticationRequiredAsync(missingRes);

        // 3. Mismatched subject/action/semantic_action/expired tokens each return 401 REAUTHENTICATION_REQUIRED
        string wrongSubToken = IssueToken("other-pi", "Other PI", unblindPath, SubjectEndpoints.UnblindSemanticAction, FixedNow);
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(client, HttpMethod.Patch, unblindPath, new { status = "UNBLINDED" }, wrongSubToken));

        string wrongActionToken = IssueToken(pi.UserId, "Dr. PI", "/api/v1/execution/subjects/OTHER/state", SubjectEndpoints.UnblindSemanticAction, FixedNow);
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(client, HttpMethod.Patch, unblindPath, new { status = "UNBLINDED" }, wrongActionToken));

        string wrongSemanticToken = IssueToken(pi.UserId, "Dr. PI", unblindPath, "execution.subject.randomize", FixedNow);
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(client, HttpMethod.Patch, unblindPath, new { status = "UNBLINDED" }, wrongSemanticToken));

        string expiredToken = IssueToken(pi.UserId, "Dr. PI", unblindPath, SubjectEndpoints.UnblindSemanticAction, FixedNow.AddMinutes(-15));
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(client, HttpMethod.Patch, unblindPath, new { status = "UNBLINDED" }, expiredToken));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var storedBefore = await db.ClinicalSubjects.AsNoTracking().SingleAsync(s => s.SubjectCode == new SubjectCode(subjectRandomized), Ct);
            storedBefore.IsUnblinded.Should().BeFalse();
            storedBefore.Status.Should().Be(SubjectState.Randomized);
        }

        // 4. Valid X-Sig-Token succeeds (200 OK) and immediate replay fails with 401 REAUTHENTICATION_REQUIRED
        string validToken = IssueToken(pi.UserId, "Dr. PI", unblindPath, SubjectEndpoints.UnblindSemanticAction, FixedNow);
        var okRes = await SendWithSigTokenAsync(client, HttpMethod.Patch, unblindPath, new { status = "UNBLINDED" }, validToken);
        okRes.StatusCode.Should().Be(HttpStatusCode.OK, await okRes.Content.ReadAsStringAsync(Ct));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var storedAfter = await db.ClinicalSubjects.AsNoTracking().SingleAsync(s => s.SubjectCode == new SubjectCode(subjectRandomized), Ct);
            storedAfter.IsUnblinded.Should().BeTrue();
            storedAfter.UnblindedBy.Should().Be(pi.UserId);
        }

        var replayRes = await SendWithSigTokenAsync(client, HttpMethod.Patch, unblindPath, new { status = "UNBLINDED" }, validToken);
        await AssertReauthenticationRequiredAsync(replayRes);
    }

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task CtmsFiveRegulatedRoutes_EnforceStepUpBatchBindingConditionalDraftAndVerifiedLetterSigner()
    {
        var clock = new FakeClock(FixedNow);
        await using var factory = CreateInMemoryServiceFactory<Cadence.Apps.Ctms.Program, CtmsDbContext>(clock);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            db.CtmsSiteGreenlightGates.AddRange(
                CreateCompliantGate("GATE-101", SiteId),
                CreateCompliantGate("GATE-201", "SITE-201"),
                CreateCompliantGate("GATE-202", "SITE-202"));
            db.CtmsDelegations.Add(new CtmsDelegation("DOA-1")
            {
                StudyId = StudyId,
                SiteId = SiteId,
                StaffUserId = "staff-1",
                TaskCodes = "[\"INFORMED_CONSENT\"]",
                StartDate = "2026-11-23",
                CreatedAt = FixedNow.UtcDateTime,
                CreatedBy = "pi-1",
                ReasonForChange = "Initial delegation",
            });
            var visit = new CtmsMonitoringVisit("VISIT-1", CtmsMonitoringVisitStatus.Completed)
            {
                StudyId = StudyId,
                SiteId = SiteId,
                CraId = "cra-1",
                VisitType = "IMV",
                ScheduledDate = FixedNow,
                ActualDate = FixedNow,
                CreatedAt = FixedNow.UtcDateTime,
                CreatedBy = "cra-1",
                ReasonForChange = "Completed IMV",
            };
            db.CtmsMonitoringVisits.Add(visit);
            await db.SaveChangesAsync(Ct);
        }

        var pi = TestSecurityContext.Identity("pi-1", RoleNormalization.ROLE_INVESTIGATOR) with { StudyIds = [StudyId], SiteIds = [SiteId] };
        var dm = TestSecurityContext.Identity("dm-1", "Data Manager") with { StudyIds = [StudyId], SiteIds = [], ChangeReason = "Sponsor operational sign-off" };
        var cra = TestSecurityContext.Identity("cra-verified-1", "CRA") with { StudyIds = [StudyId], SiteIds = [SiteId], ChangeReason = "Visit letter compilation" };

        using var piClient = factory.CreateSignedClient(pi);
        using var dmClient = factory.CreateSignedClient(dm);
        using var craClient = factory.CreateSignedClient(cra);

        // 1. POST /api/v1/ctms/doa/sign-off
        await AssertReauthenticationRequiredAsync(
            await piClient.PostAsJsonAsync(DoaEndpoints.SignOffAction, new { record_id = "DOA-1", reason_for_change = "PI endorsement" }, Ct));

        string doaToken = IssueToken(pi.UserId, "Dr. Verified PI", DoaEndpoints.SignOffAction, DoaEndpoints.SignOffSemanticAction, FixedNow);
        var doaOk = await SendWithSigTokenAsync(
            piClient, HttpMethod.Post, DoaEndpoints.SignOffAction, new { record_id = "DOA-1", reason_for_change = "PI endorsement" }, doaToken);
        doaOk.StatusCode.Should().Be(HttpStatusCode.OK, await doaOk.Content.ReadAsStringAsync(Ct));
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(piClient, HttpMethod.Post, DoaEndpoints.SignOffAction, new { record_id = "DOA-1", reason_for_change = "Replay" }, doaToken));

        // 2. POST /api/v1/ctms/monitoring-visits/VISIT-1/sign-off
        string visitSignPath = MonitoringVisitEndpoints.SignOffAction("VISIT-1");
        await AssertReauthenticationRequiredAsync(await dmClient.PostAsJsonAsync(visitSignPath, new { }, Ct));

        string visitToken = IssueToken(dm.UserId, "Verified Monitor Lead", visitSignPath, MonitoringVisitEndpoints.SignOffSemanticAction, FixedNow);
        var visitOk = await SendWithSigTokenAsync(dmClient, HttpMethod.Post, visitSignPath, new { }, visitToken);
        visitOk.StatusCode.Should().Be(HttpStatusCode.OK, await visitOk.Content.ReadAsStringAsync(Ct));
        await AssertReauthenticationRequiredAsync(await SendWithSigTokenAsync(dmClient, HttpMethod.Post, visitSignPath, new { }, visitToken));

        // 3. POST /api/v1/ctms/letters/compile (mixed-mode: Finalize=false without token; Finalize=true requires token & stamps verified signer)
        string compileUrl = $"{LetterTemplateEndpoints.CompileAction}?templateName=confirmation_letter";
        var draftPayload = new
        {
            craId = cra.UserId,
            createdAt = "2026-11-23",
            studyId = StudyId,
            siteId = SiteId,
            visitId = "VISIT-1",
            visitType = "IMV",
            finalize = false,
        };
        var draftRes = await craClient.PostAsJsonAsync(compileUrl, draftPayload, Ct);
        draftRes.StatusCode.Should().Be(HttpStatusCode.OK, await draftRes.Content.ReadAsStringAsync(Ct));
        var draftJson = await draftRes.Content.ReadFromJsonAsync<JsonElement>(Ct);
        draftJson.GetProperty("isSigned").GetBoolean().Should().BeFalse();

        var finalizeWithForgedSigner = new
        {
            craId = "forged-cra-id",
            createdAt = "2026-11-23",
            studyId = StudyId,
            siteId = SiteId,
            visitId = "VISIT-1",
            visitType = "IMV",
            finalize = true,
            signerName = "Dr. Forged Attacker",
            signatureMeaning = "I approve this monitoring confirmation letter.",
        };
        await AssertReauthenticationRequiredAsync(await craClient.PostAsJsonAsync(compileUrl, finalizeWithForgedSigner, Ct));

        string letterToken = IssueToken(
            cra.UserId,
            "Dr. Verified Monitor",
            LetterTemplateEndpoints.CompileAction,
            LetterTemplateEndpoints.CompileSemanticAction,
            FixedNow);
        var finalizedRes = await SendWithSigTokenAsync(craClient, HttpMethod.Post, compileUrl, finalizeWithForgedSigner, letterToken);
        finalizedRes.StatusCode.Should().Be(HttpStatusCode.OK, await finalizedRes.Content.ReadAsStringAsync(Ct));
        var finalizedJson = await finalizedRes.Content.ReadFromJsonAsync<JsonElement>(Ct);
        finalizedJson.GetProperty("isSigned").GetBoolean().Should().BeTrue();
        string manifestJson = finalizedJson.GetProperty("signatureManifestation").GetString()!;
        manifestJson.Should().Contain("Dr. Verified Monitor").And.NotContain("Dr. Forged Attacker");
        string letterId = finalizedJson.GetProperty("letterId").GetString()!;

        var pdfRes = await craClient.GetAsync($"/api/v1/ctms/letters/{letterId}/pdf", Ct);
        pdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        string pdfText = Encoding.UTF8.GetString(await pdfRes.Content.ReadAsByteArrayAsync(Ct));
        pdfText.Should().Contain("Dr. Verified Monitor").And.NotContain("Dr. Forged Attacker");

        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(craClient, HttpMethod.Post, compileUrl, finalizeWithForgedSigner, letterToken));

        // 4. POST /api/v1/ctms/startup/sites/SITE-101/greenlight/certify
        string certifyPath = StartupGreenlightEndpoints.CertifyAction(SiteId);
        string certifyUrl = $"{certifyPath}?study_id={StudyId}&country_code=US";
        await AssertReauthenticationRequiredAsync(await dmClient.PostAsync(certifyUrl, content: null, Ct));

        string certifyToken = IssueToken(dm.UserId, "Verified DM", certifyPath, StartupGreenlightEndpoints.CertifySemanticAction, FixedNow);
        var certifyOk = await SendWithSigTokenAsync(dmClient, HttpMethod.Post, certifyUrl, body: null, certifyToken);
        certifyOk.StatusCode.Should().Be(HttpStatusCode.OK, await certifyOk.Content.ReadAsStringAsync(Ct));
        await AssertReauthenticationRequiredAsync(await SendWithSigTokenAsync(dmClient, HttpMethod.Post, certifyUrl, body: null, certifyToken));

        // 5. POST /api/v1/ctms/startup/matrix/batch-certify (deterministic batch_id over sorted site IDs)
        var batchSitesPayload = new
        {
            study_id = StudyId,
            country_code = "US",
            site_ids = new[] { "SITE-202", "SITE-201" },
        };
        await AssertReauthenticationRequiredAsync(
            await dmClient.PostAsJsonAsync(StartupGreenlightEndpoints.BatchCertifyAction, batchSitesPayload, Ct));

        string wrongBatchToken = IssueToken(
            dm.UserId,
            "Verified DM",
            StartupGreenlightEndpoints.BatchCertifyAction,
            StartupGreenlightEndpoints.BatchCertifySemanticAction,
            FixedNow,
            batchId: SigTokenBatchId.FromIds(["SITE-201", "SITE-999"]));
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(dmClient, HttpMethod.Post, StartupGreenlightEndpoints.BatchCertifyAction, batchSitesPayload, wrongBatchToken));

        string validBatchToken = IssueToken(
            dm.UserId,
            "Verified DM",
            StartupGreenlightEndpoints.BatchCertifyAction,
            StartupGreenlightEndpoints.BatchCertifySemanticAction,
            FixedNow,
            batchId: SigTokenBatchId.FromIds(["SITE-201", "SITE-202"]));
        var batchCertifyOk = await SendWithSigTokenAsync(
            dmClient, HttpMethod.Post, StartupGreenlightEndpoints.BatchCertifyAction, batchSitesPayload, validBatchToken);
        batchCertifyOk.StatusCode.Should().Be(HttpStatusCode.OK, await batchCertifyOk.Content.ReadAsStringAsync(Ct));
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(dmClient, HttpMethod.Post, StartupGreenlightEndpoints.BatchCertifyAction, batchSitesPayload, validBatchToken));
    }

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task PaymentsRemittanceSign_RequiresStepUpToken_AndLeavesRemittanceUnsignedWhenMissing()
    {
        var clock = new FakeClock(FixedNow);
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Ct);
        await using var factory = new WebApplicationFactory<Cadence.Apps.Payments.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys();
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(new ReplayCache(clock));
                services.RemoveAll<DbContextOptions<PaymentsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<PaymentsDbContext>>();
                services.AddDbContext<PaymentsDbContext>(options => options.UseSqlite(connection));
            });
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            await db.Database.EnsureCreatedAsync(Ct);
            db.SelfBillingInvoices.Add(new SelfBillingInvoice
            {
                Id = "INV-1",
                InvoiceNumber = "INV-STUDY-1-0001",
                StudyId = StudyId,
                SiteId = SiteId,
                ContractId = "CNT-1",
                PeriodStart = "2026-11-01",
                PeriodEnd = "2026-11-30",
                TotalGrossAmount = 500m,
                TotalNetAmount = 450m,
                Status = "APPROVED",
                ApprovedBy = "approver-finance-0",
                ApprovedAt = FixedNow.AddHours(-1),
                CreatedAt = FixedNow.AddHours(-2),
                CreatedBy = "approver-finance-0",
                UpdatedAt = FixedNow.AddHours(-1),
            });
            db.RemittanceAdvices.Add(new RemittanceAdvice
            {
                Id = "REM-1",
                RemittanceNumber = "REM-INV-1",
                InvoiceId = "INV-1",
                StudyId = StudyId,
                SiteId = SiteId,
                NetAmount = 450m,
                PdfSha256Hash = "deadbeef",
                CreatedAt = FixedNow.AddHours(-1),
                CreatedBy = "approver-finance-0",
                UpdatedAt = FixedNow.AddHours(-1),
            });
            await db.SaveChangesAsync(Ct);
        }

        var finance = TestSecurityContext.Identity("finance-signer-1", "finance officer") with
        {
            StudyIds = [StudyId],
            ChangeReason = "Sign remittance release",
        };
        using var client = factory.CreateSignedClient(finance);

        string signPath = PaymentEndpoints.RemittanceSignAction("INV-1");
        var missingRes = await client.PostAsJsonAsync(signPath, new { signature_reason = "Approve remittance release" }, Ct);
        await AssertReauthenticationRequiredAsync(missingRes);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var rem = await db.RemittanceAdvices.AsNoTracking().SingleAsync(r => r.InvoiceId == "INV-1", Ct);
            rem.IsSigned.Should().BeFalse();
        }

        string token = IssueToken(finance.UserId, "Verified Finance Officer", signPath, PaymentEndpoints.RemittanceSignSemanticAction, FixedNow);
        var okRes = await SendWithSigTokenAsync(client, HttpMethod.Post, signPath, new { signature_reason = "Approve remittance release" }, token);
        okRes.StatusCode.Should().Be(HttpStatusCode.OK, await okRes.Content.ReadAsStringAsync(Ct));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var rem = await db.RemittanceAdvices.AsNoTracking().SingleAsync(r => r.InvoiceId == "INV-1", Ct);
            rem.IsSigned.Should().BeTrue();
            rem.SignedBy.Should().Be(finance.UserId);
        }

        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(client, HttpMethod.Post, signPath, new { signature_reason = "Replay" }, token));
    }

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ImagingChairAdjudicate_RequiresStepUpToken_AndStampsVerifiedChairIdentityIgnoringBodySignerName()
    {
        var clock = new FakeClock(FixedNow);
        await using var factory = CreateInMemoryServiceFactory<Cadence.Apps.Imaging.Program, ImagingDbContext>(clock);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImagingDbContext>();
            db.ImagingAdjudicationSessions.Add(new ImagingAdjudicationSession
            {
                Id = "ADJ-1",
                StudyId = StudyId,
                SiteId = SiteId,
                SubjectId = "SUBJ-001",
                TimepointName = "WEEK-12",
                WorkflowStatus = "PENDING_CHAIR_ADJUDICATION",
                IsResponseDiscordant = true,
                DiscordanceReasonsJson = "[\"Response category discordance: PR vs SD\"]",
                CreatedAt = FixedNow.AddHours(-2),
                CreatedBy = "reader-seed",
                UpdatedAt = FixedNow.AddHours(-1),
            });
            await db.SaveChangesAsync(Ct);
        }

        var chair = TestSecurityContext.Identity("chair-verified-1", "Medical Monitor") with
        {
            StudyIds = [StudyId],
            ChangeReason = "Chair adjudication tie-break",
        };
        using var mmClient = factory.CreateSignedClient(chair);

        string adjudicatePath = ImagingAdjudicationEndpoints.ChairAdjudicateAction("ADJ-1");
        var payload = new
        {
            finalConsensusResponse = "PR",
            finalConsensusSldMm = 32.5,
            bindingReader = "READER_1",
            adjudicationRationale = "Confirmed target lesion 34% reduction from baseline on portal venous phase.",
            signerName = "Dr. Forged Attacker",
            signatureMeaning = "I adjudicate and lock the final RECIST 1.1 response.",
        };

        await AssertReauthenticationRequiredAsync(await mmClient.PostAsJsonAsync(adjudicatePath, payload, Ct));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImagingDbContext>();
            var untouched = await db.ImagingAdjudicationSessions.AsNoTracking().SingleAsync(s => s.Id == "ADJ-1", Ct);
            untouched.WorkflowStatus.Should().Be("PENDING_CHAIR_ADJUDICATION");
            untouched.ElectronicSignatureJson.Should().BeNull();
        }

        string token = IssueToken(
            chair.UserId,
            "Dr. Verified Chair",
            adjudicatePath,
            ImagingAdjudicationEndpoints.ChairAdjudicateSemanticAction,
            FixedNow);
        var okRes = await SendWithSigTokenAsync(mmClient, HttpMethod.Post, adjudicatePath, payload, token);
        okRes.StatusCode.Should().Be(HttpStatusCode.OK, await okRes.Content.ReadAsStringAsync(Ct));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImagingDbContext>();
            var finalized = await db.ImagingAdjudicationSessions.AsNoTracking().SingleAsync(s => s.Id == "ADJ-1", Ct);
            finalized.WorkflowStatus.Should().Be("FINALIZED_BY_CHAIR");
            finalized.AdjudicatorId.Should().Be("chair-verified-1");
            finalized.AdjudicatorName.Should().Be("Dr. Verified Chair");
            finalized.LockedBy.Should().Be("chair-verified-1");
            finalized.ElectronicSignatureJson.Should().Contain("Dr. Verified Chair")
                .And.Contain("chair-verified-1")
                .And.NotContain("Dr. Forged Attacker");
        }

        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(mmClient, HttpMethod.Post, adjudicatePath, payload, token));
    }

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task InteropFourEsourceReconcileRoutes_EnforceStepUpAndDeterministicBatchIdBinding()
    {
        var clock = new FakeClock(FixedNow);
        await using var factory = CreateInMemoryServiceFactory<Cadence.Apps.Interop.Program, InteropDbContext>(clock);

        string[] candidateIds = ["CAND-1", "CAND-2", "CAND-3", "CAND-4", "CAND-5", "CAND-6"];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
            db.EhrReconciliationCandidateRecords.AddRange(candidateIds.Select((id, idx) => new EhrReconciliationCandidateRecord
            {
                Id = id,
                StudyId = StudyId,
                SiteId = SiteId,
                SubjectId = $"SUBJ-{idx + 1:D3}",
                SubjectPseudonym = $"P-{idx + 1:D3}",
                EhrSystemId = "epic-prod",
                FhirResourceType = "Observation",
                FhirResourceId = $"obs-{id}",
                SourceTimestamp = FixedNow.AddHours(-1),
                TargetDomain = "VS",
                TargetVariable = "SYSBP",
                CandidateValueJson = "{\"value\":122}",
                MappingProvenance = "LOINC 8480-6 -> VS.SYSBP",
                CreatedAt = FixedNow.AddHours(-1),
            }));
            await db.SaveChangesAsync(Ct);
        }

        var crc = TestSecurityContext.Identity("crc-stepup-1", "crc") with
        {
            StudyIds = [StudyId],
            SiteIds = [SiteId],
            ChangeReason = "eSource reconciliation review",
        };
        using var crcClient = factory.CreateSignedClient(crc);

        // 1. Single accept
        var acceptPayload = new { candidateId = "CAND-1", commitReason = "Verified source chart", changeReason = "Accept candidate" };
        await AssertReauthenticationRequiredAsync(await crcClient.PostAsJsonAsync(EsourceEndpoints.AcceptAction, acceptPayload, Ct));

        string acceptToken = IssueToken(crc.UserId, "Verified CRC", EsourceEndpoints.AcceptAction, EsourceEndpoints.AcceptSemanticAction, FixedNow);
        (await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.AcceptAction, acceptPayload, acceptToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.AcceptAction, acceptPayload, acceptToken));

        // 2. Batch accept (with reversed ID order proving deterministic sorting + wrong batch_id rejection)
        var batchAcceptIds = new[] { "CAND-3", "CAND-2" };
        var batchAcceptPayload = new { candidateIds = batchAcceptIds, commitReason = "Batch verified", changeReason = "Batch accept" };
        await AssertReauthenticationRequiredAsync(await crcClient.PostAsJsonAsync(EsourceEndpoints.BatchAcceptAction, batchAcceptPayload, Ct));

        string wrongBatchAcceptToken = IssueToken(
            crc.UserId,
            "Verified CRC",
            EsourceEndpoints.BatchAcceptAction,
            EsourceEndpoints.BatchAcceptSemanticAction,
            FixedNow,
            batchId: SigTokenBatchId.FromIds(["CAND-2", "CAND-999"]));
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.BatchAcceptAction, batchAcceptPayload, wrongBatchAcceptToken));

        string batchAcceptToken = IssueToken(
            crc.UserId,
            "Verified CRC",
            EsourceEndpoints.BatchAcceptAction,
            EsourceEndpoints.BatchAcceptSemanticAction,
            FixedNow,
            batchId: SigTokenBatchId.FromIds(["CAND-2", "CAND-3"]));
        (await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.BatchAcceptAction, batchAcceptPayload, batchAcceptToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.BatchAcceptAction, batchAcceptPayload, batchAcceptToken));

        // 3. Single reject
        var rejectPayload = new { candidateId = "CAND-4", rejectionReason = "Artifactual cuff reading", changeReason = "Reject candidate" };
        await AssertReauthenticationRequiredAsync(await crcClient.PostAsJsonAsync(EsourceEndpoints.RejectAction, rejectPayload, Ct));

        string rejectToken = IssueToken(crc.UserId, "Verified CRC", EsourceEndpoints.RejectAction, EsourceEndpoints.RejectSemanticAction, FixedNow);
        (await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.RejectAction, rejectPayload, rejectToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.RejectAction, rejectPayload, rejectToken));

        // 4. Batch reject
        var batchRejectIds = new[] { "CAND-6", "CAND-5" };
        var batchRejectPayload = new { candidateIds = batchRejectIds, rejectionReason = "Duplicate import", changeReason = "Batch reject" };
        await AssertReauthenticationRequiredAsync(await crcClient.PostAsJsonAsync(EsourceEndpoints.BatchRejectAction, batchRejectPayload, Ct));

        string batchRejectToken = IssueToken(
            crc.UserId,
            "Verified CRC",
            EsourceEndpoints.BatchRejectAction,
            EsourceEndpoints.BatchRejectSemanticAction,
            FixedNow,
            batchId: SigTokenBatchId.FromIds(["CAND-5", "CAND-6"]));
        (await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.BatchRejectAction, batchRejectPayload, batchRejectToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertReauthenticationRequiredAsync(
            await SendWithSigTokenAsync(crcClient, HttpMethod.Post, EsourceEndpoints.BatchRejectAction, batchRejectPayload, batchRejectToken));
    }

    private static CtmsSiteGreenlightGate CreateCompliantGate(string gateId, string siteId) =>
        new(gateId)
        {
            StudyId = StudyId,
            SiteId = siteId,
            SiteName = $"Site {siteId}",
            CountryCode = "US",
            ReadinessPercentage = 100,
            FacetRegulatoryIrb = true,
            FacetCtaBudget = true,
            FacetEisfBaseline = true,
            FacetDoaLog = true,
            FacetStaffTraining = true,
            FacetIpRelease = true,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = "seed",
            ReasonForChange = "Seeded compliant gate",
        };

    private static string IssueToken(
        string userId,
        string username,
        string action,
        string semanticAction,
        DateTimeOffset issuedAt,
        string? batchId = null) =>
        SigTokenIssuer.Issue(
            new SigTokenGrant(userId, username, ["Data Manager"], action, semanticAction, batchId),
            issuedAt,
            GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret));

    private static async Task<HttpResponseMessage> SendWithSigTokenAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        object? body,
        string sigToken)
    {
        using var req = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        req.Headers.Add(GatewayHeaders.SigToken, sigToken);
        return await client.SendAsync(req, Ct);
    }

    private static async Task AssertReauthenticationRequiredAsync(HttpResponseMessage response)
    {
        string raw = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, raw);
        using var doc = JsonDocument.Parse(raw);
        doc.RootElement.GetProperty("error").GetString().Should().Be(SigTokenVerifier.ReauthenticationRequired);
        doc.RootElement.GetProperty("detail").GetString().Should().Be(SigTokenVerifier.ReauthenticationRequired);
    }

    private static WebApplicationFactory<TProgram> CreateInMemoryServiceFactory<TProgram, TContext>(IClock clock)
        where TProgram : class
        where TContext : DbContext
    {
        string dbName = $"stepup-{typeof(TContext).Name}-{Guid.NewGuid():N}";
        return new WebApplicationFactory<TProgram>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestStorage()
                .UseTestFieldProtectionKeys()
                .UseTestSceSigningKey()
                .UseTestImagingDeidentificationKeys();

            host.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType != typeof(SigTokenUseStoreStartupCheck))
                    .ToList())
                {
                    services.Remove(descriptor);
                }

                var inMemoryServices = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.RemoveAll<IClock>();
                services.AddSingleton(clock);
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(new ReplayCache(clock));

                services.RemoveAll<DbContextOptions<TContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
                services.RemoveAll<CurrentTenantAccessor>();
                services.AddSingleton<CurrentTenantAccessor>(sp =>
                {
                    var accessor = new CurrentTenantAccessor(
                        sp.GetRequiredService<ITenantCodeResolver>(),
                        sp.GetService<IHttpContextAccessor>());
                    accessor.UseNonHttpSeedingFallbackTenant(TestSecurityContext.TenantCode);
                    return accessor;
                });

                services.AddDbContext<TContext>((sp, options) =>
                {
                    options.UseInMemoryDatabase(dbName)
                        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                        .UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: false)
                        .AddInterceptors(StandaloneTenantSaveChangesInterceptor.Instance);
                    options.UseInternalServiceProvider(inMemoryServices);
                });
            });
        });
    }
}
