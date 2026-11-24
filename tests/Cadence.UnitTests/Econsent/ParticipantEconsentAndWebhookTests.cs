using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Endpoints;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Apps.Econsent.Services;
using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.UnitTests.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Xunit;

using ExecutionSubjectConsent = Cadence.Apps.Execution.Domain.Subjects.SubjectConsent;

namespace Cadence.UnitTests.Econsent;

/// <summary>
/// Unit and HTTP verification for caller-bound participant eConsent self-service routes,
/// comprehension check grading/gating, single-token amendment re-consent, and signed
/// service-to-service webhook synchronization into Execution.
/// </summary>
public sealed class ParticipantEconsentAndWebhookTests : IDisposable
{
    private const string StudyCodeValue = "DEMO-01";
    private const string SiteCodeValue = "SITE-101";
    private const string SubjectSubA = "7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d";
    private const string SubjectSubB = "9f8e7d6c-5b4a-4321-9876-1a2b3c4d5e6f";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 23, 16, 0, 0, TimeSpan.Zero));

    public ParticipantEconsentAndWebhookTests()
    {
        _connection.Open();
        using var ctx = CreateEconsentContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public void PermissionMatrix_SubjectHoldsOnlySelfServiceEconsentPermissions_AndServiceRoleHoldsExecutionSync()
    {
        var subjectPrincipal = new Principal
        {
            UserId = SubjectSubA,
            Roles = RoleNormalization.GetNormalizedRoles([RoleNormalization.ROLE_SUBJECT]),
            RawRoles = [RoleNormalization.ROLE_SUBJECT],
        };

        PermissionMatrix.HasPermission(subjectPrincipal, "econsent_self_consent:read").Should().BeTrue();
        PermissionMatrix.HasPermission(subjectPrincipal, "econsent_self_consent:verify").Should().BeTrue();
        PermissionMatrix.HasPermission(subjectPrincipal, "econsent_self_consent:sign").Should().BeTrue();
        PermissionMatrix.HasPermission(subjectPrincipal, "econsent_self_reconsent:read").Should().BeTrue();
        PermissionMatrix.HasPermission(subjectPrincipal, "econsent_self_reconsent:sign").Should().BeTrue();

        PermissionMatrix.HasPermission(subjectPrincipal, "econsent_template:publish").Should().BeFalse();
        PermissionMatrix.HasPermission(subjectPrincipal, "econsent_consent:capture").Should().BeFalse();
        PermissionMatrix.HasPermission(subjectPrincipal, "subject_enrollment:econsent_sync").Should().BeFalse();

        var servicePrincipal = new Principal
        {
            UserId = "svc:econsent",
            Roles = [PermissionMatrix.EconsentServiceRole],
            RawRoles = [PermissionMatrix.EconsentServiceRole],
        };
        PermissionMatrix.HasPermission(servicePrincipal, "subject_enrollment:econsent_sync").Should().BeTrue();

        foreach (string staffRole in new[] { RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_INVESTIGATOR })
        {
            var staffPrincipal = new Principal
            {
                UserId = $"staff:{staffRole}",
                Roles = RoleNormalization.GetNormalizedRoles([staffRole]),
                RawRoles = [staffRole],
            };
            PermissionMatrix.HasPermission(staffPrincipal, "econsent_consent:capture").Should().BeTrue();
            PermissionMatrix.HasPermission(staffPrincipal, "econsent_self_consent:read").Should().BeTrue();
            PermissionMatrix.HasPermission(staffPrincipal, "econsent_self_consent:sign").Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApprovedContent_ComposesClausesAndAppliesApprovedTranslations_WhileRejectingUnassignedStudy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateEconsentContext();
        DateTimeOffset now = _clock.UtcNow;

        db.ConsentClauses.Add(new ConsentClause
        {
            Id = "cl-1",
            ClauseId = "clause_purpose",
            StudyId = StudyCodeValue,
            Title = "Study Purpose",
            Text = "This clinical trial evaluates daily symptom tracking.",
            VersionIndex = 1,
            CreatedAt = now,
            CreatedBy = "dm-1",
            ReasonForChange = "Initial ICF",
        });
        db.ConsentTranslations.Add(new ConsentTranslation
        {
            Id = "tr-es-1",
            TranslationId = "tr-clause-purpose-es",
            SourceId = "clause_purpose",
            SourceType = "CLAUSE",
            SourceVersionIndex = 1,
            LanguageCode = "es",
            TranslatedTitle = "Proposito del estudio",
            TranslatedText = "Este ensayo clinico evalua el seguimiento diario de sintomas.",
            Status = "APPROVED",
            VersionIndex = 1,
            CreatedAt = now,
            CreatedBy = "translator-1",
            ReasonForChange = "Approved Spanish translation",
        });
        db.ConsentTemplates.Add(new ConsentTemplate
        {
            Id = "tpl-row-1",
            TemplateId = "template-icf",
            StudyId = StudyCodeValue,
            TemplateName = "Cardio Trial Informed Consent",
            ProtocolVersion = "v1.0",
            IsPublished = true,
            RequiresReconsent = false,
            VersionIndex = 1,
            ClausesJson = """["clause_purpose"]""",
            CreatedAt = now,
            CreatedBy = "dm-1",
            ReasonForChange = "Publish v1",
        });
        await db.SaveChangesAsync(ct);

        var subjectContext = CreateSubjectHttpContext(SubjectSubA, [StudyCodeValue], [SiteCodeValue]);
        var esResult = await EconsentEndpoints.GetApprovedContentAsync("template-icf", "es", db, subjectContext, ct);
        var ok = esResult.Should().BeOfType<Ok<ApprovedConsentContentResponse>>().Subject;
        ok.Value!.StudyCode.Should().Be(StudyCodeValue);
        ok.Value.LanguageCode.Should().Be("es");
        ok.Value.DigestSha256.Should().HaveLength(64);
        ok.Value.Clauses.Should().ContainSingle(c =>
            c.ClauseId == "clause_purpose"
            && c.Title == "Proposito del estudio"
            && c.Text.Contains("seguimiento diario"));

        var wrongStudyContext = CreateSubjectHttpContext(SubjectSubA, ["OTHER-STUDY-99"], [SiteCodeValue]);
        var deniedResult = await EconsentEndpoints.GetApprovedContentAsync("template-icf", "en", db, wrongStudyContext, ct);
        deniedResult.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ComprehensionAndCapture_BlocksOtherParticipantsWith403_GatesSigningUntilPassed_AndInvokesExecutionWebhook()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateEconsentContext();
        DateTimeOffset now = _clock.UtcNow;

        db.ConsentTemplates.Add(new ConsentTemplate
        {
            Id = "tpl-row-2",
            TemplateId = "template-icf",
            StudyId = StudyCodeValue,
            TemplateName = "Cardio Trial ICF",
            ProtocolVersion = "v1.0",
            IsPublished = true,
            VersionIndex = 1,
            CreatedAt = now,
            CreatedBy = "dm-1",
            ReasonForChange = "Publish v1",
        });
        db.ComprehensionChecks.Add(new ComprehensionCheck
        {
            Id = "chk-1",
            TemplateId = "template-icf",
            VersionIndex = 1,
            QuestionsJson = """[{"id":"q1","text":"Is participation voluntary?","options":["Yes","No"],"expected_answer":"Yes","correct_option":"Yes"}]""",
            ExpectedAnswersJson = """{"q1":"Yes"}""",
            ThresholdPolicyJson = """{"required_score":100}""",
            CreatedAt = now,
            CreatedBy = "dm-1",
            ReasonForChange = "Configure check",
        });
        await db.SaveChangesAsync(ct);

        var subjectContext = CreateSubjectHttpContext(SubjectSubA, [StudyCodeValue], [SiteCodeValue]);

        // 1. GET comprehension-checks strips expected_answer from questions
        var getCheckResult = await EconsentEndpoints.GetComprehensionChecksAsync("template-icf", 1, db, subjectContext, ct);
        var checkDto = getCheckResult.Should().BeOfType<Ok<ComprehensionCheckResponse>>().Subject.Value!;
        checkDto.Questions.GetRawText().Should().NotContain("expected_answer");
        checkDto.Questions.GetRawText().Should().NotContain("correct_option");

        // 2. Cross-participant submit-answers is blocked with HTTP 403
        var crossSubmit = await EconsentEndpoints.SubmitComprehensionAnswersAsync(
            "template-icf",
            1,
            new SubmitComprehensionAnswersRequest(SubjectSubB, new Dictionary<string, string> { ["q1"] = "Yes" }, null),
            db,
            subjectContext,
            _clock,
            ct);
        crossSubmit.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 3. Signing before passing comprehension check is rejected with 400
        string livenessToken = BiometricLivenessTokenService.IssueToken(
            SubjectSubA,
            0.99m,
            isLive: true,
            secret: TestSecurityContext.BiometricLivenessKeyBytes,
            now: now);
        var sigTokenUseStore = new ReplayCache(_clock);
        var webhookPublisher = Substitute.For<IEconsentExecutionWebhookPublisher>();
        var captureReq = new CaptureConsentRequest(
            SubjectPseudonym: SubjectSubA,
            StudyId: StudyCodeValue,
            SiteId: SiteCodeValue,
            SignerName: "subject.site101",
            SignerEmail: null,
            SignerRole: "Subject",
            Meaning: "I have read and understood the informed consent",
            SigningReason: null,
            ProtocolVersion: "v1.0",
            DigestSha256: new string('a', 64),
            SignatureData: "typed:subject.site101",
            LivenessToken: livenessToken,
            DeviceTimestamp: now);

        var prematureCapture = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "template-icf",
            1,
            captureReq,
            db,
            subjectContext,
            TestSecurityContext.BiometricLivenessOptions,
            sigTokenUseStore,
            _clock,
            ct,
            webhookPublisher);
        prematureCapture.Should().BeOfType<ValidationProblem>()
            .Which.ProblemDetails.Errors.Should().ContainKey("comprehension");

        // 4. Cross-participant capture-consent is blocked with HTTP 403
        var crossCapture = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "template-icf",
            1,
            captureReq with { SubjectPseudonym = SubjectSubB },
            db,
            subjectContext,
            TestSecurityContext.BiometricLivenessOptions,
            sigTokenUseStore,
            _clock,
            ct,
            webhookPublisher);
        crossCapture.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 5. Failing answer returns passed=false; passing answer unlocks capture-consent and triggers Execution webhook
        var failedAnswers = await EconsentEndpoints.SubmitComprehensionAnswersAsync(
            "template-icf",
            1,
            new SubmitComprehensionAnswersRequest(SubjectSubA, new Dictionary<string, string> { ["q1"] = "No" }, null),
            db,
            subjectContext,
            _clock,
            ct);
        failedAnswers.Should().BeOfType<Ok<SubmitComprehensionAnswersResponse>>()
            .Which.Value!.Passed.Should().BeFalse();

        var passedAnswers = await EconsentEndpoints.SubmitComprehensionAnswersAsync(
            "template-icf",
            1,
            new SubmitComprehensionAnswersRequest(SubjectSubA, new Dictionary<string, string> { ["q1"] = "Yes" }, null),
            db,
            subjectContext,
            _clock,
            ct);
        passedAnswers.Should().BeOfType<Ok<SubmitComprehensionAnswersResponse>>()
            .Which.Value!.Passed.Should().BeTrue();

        var validCapture = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "template-icf",
            1,
            captureReq,
            db,
            subjectContext,
            TestSecurityContext.BiometricLivenessOptions,
            sigTokenUseStore,
            _clock,
            ct,
            webhookPublisher);
        validCapture.Should().BeOfType<Ok<CaptureConsentResponse>>()
            .Which.Value!.Status.Should().Be("ACTIVE");

        await webhookPublisher.Received(1).PublishConsentSignedAsync(
            Arg.Is<EconsentSignedSyncMessage>(m =>
                m.SubjectPseudonym == SubjectSubA
                && m.StudyCode == StudyCodeValue
                && m.SiteCode == SiteCodeValue
                && m.VersionIndex == 1),
            ct);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ReconsentEndpoints_BlockCrossParticipantAccess_CompleteAtomically_AndSyncExecutionViaSignedWebhook()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateEconsentContext();
        DateTimeOffset now = _clock.UtcNow;

        db.ConsentTemplates.Add(new ConsentTemplate
        {
            Id = "tpl-v2",
            TemplateId = "template-icf",
            StudyId = StudyCodeValue,
            TemplateName = "Cardio Trial Amended ICF",
            ProtocolVersion = "v2.0",
            IsPublished = true,
            RequiresReconsent = true,
            VersionIndex = 2,
            CreatedAt = now,
            CreatedBy = "dm-1",
            ReasonForChange = "Amendment v2",
        });
        db.SubjectConsents.Add(new Cadence.Apps.Econsent.Domain.SubjectConsent
        {
            Id = "consent-v1",
            SubjectPseudonym = SubjectSubA,
            StudyId = StudyCodeValue,
            SiteId = SiteCodeValue,
            TemplateId = "template-icf",
            VersionIndex = 1,
            ProtocolVersion = "v1.0",
            SourceContentIdentity = new string('b', 64),
            Status = "ACTIVE",
            ServerTimestamp = now.AddDays(-10),
            CreatedAt = now.AddDays(-10),
            CreatedBy = SubjectSubA,
            ReasonForChange = "Initial consent",
        });
        db.ReconsentRequirements.AddRange(
            new ReconsentRequirement
            {
                Id = "req-subj-a",
                StudyId = StudyCodeValue,
                SiteId = SiteCodeValue,
                TemplateId = "template-icf",
                PriorVersionIndex = 1,
                NewVersionIndex = 2,
                SubjectPseudonym = SubjectSubA,
                Status = "PENDING",
                ChangeSummary = "Added Week 4 blood draw.",
                SubstantiveChangesJson = """[{"id":"diff-1","clause_title":"Blood Draws","change_type":"MODIFIED","previous_text":"Two draws.","updated_text":"Three draws."}]""",
                CreatedAt = now,
                CreatedBy = "dm-1",
                ReasonForChange = "Amendment v2",
            },
            new ReconsentRequirement
            {
                Id = "req-subj-b",
                StudyId = StudyCodeValue,
                SiteId = SiteCodeValue,
                TemplateId = "template-icf",
                PriorVersionIndex = 1,
                NewVersionIndex = 2,
                SubjectPseudonym = SubjectSubB,
                Status = "PENDING",
                ChangeSummary = "Added Week 4 blood draw.",
                SubstantiveChangesJson = "[]",
                CreatedAt = now,
                CreatedBy = "dm-1",
                ReasonForChange = "Amendment v2",
            });
        await db.SaveChangesAsync(ct);

        var subjectAContext = CreateSubjectHttpContext(SubjectSubA, [StudyCodeValue], [SiteCodeValue]);

        // Cross-participant pending read returns HTTP 403
        var crossPending = await EconsentEndpoints.GetPendingReconsentsAsync(StudyCodeValue, SubjectSubB, db, subjectAContext, ct);
        crossPending.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // Own pending read returns only Subject A's requirement
        var ownPending = await EconsentEndpoints.GetPendingReconsentsAsync(StudyCodeValue, SubjectSubA, db, subjectAContext, ct);
        var pendingList = ownPending.Should().BeOfType<Ok<PendingReconsentListResponse>>().Subject.Value!;
        pendingList.PendingItems.Should().ContainSingle(i => i.Id == "req-subj-a" && i.ProtocolVersion == "v2.0");

        // Cross-participant complete-reconsent on Subject B's requirement returns HTTP 403
        var crossComplete = await EconsentEndpoints.CompleteReconsentAsync(
            "req-subj-b",
            new CompleteReconsentRequest(SubjectSubA, StudyCodeValue, SiteCodeValue, "template-icf", 2, "v2.0", "subject.site101", null, "Subject", "Agree", null, null, null, null, now),
            db,
            subjectAContext,
            _clock,
            webhookPublisher: null,
            cancellationToken: ct);
        crossComplete.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // Wire real HttpEconsentExecutionWebhookPublisher into an in-process ExecutionApiFactory host
        EconsentWebhookPayload? capturedExecutionPayload = null;
        var mockExecutionSyncService = Substitute.For<IEconsentWebhookApplicationService>();
        mockExecutionSyncService
            .ApplyConsentSignedAsync(Arg.Do<EconsentWebhookPayload>(p => capturedExecutionPayload = p), Arg.Any<CancellationToken>())
            .Returns(true);

        await using var executionBase = new ExecutionApiFactory();
        await using var executionHost = executionBase.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
                services.PostConfigure<GatewayAuthenticationOptions>(
                    GatewayAuthenticationHandler.SchemeName,
                    opts => opts.TimeProvider = new ClockBackedTimeProvider(_clock));
                services.RemoveAll<IEconsentWebhookApplicationService>();
                services.AddScoped(_ => mockExecutionSyncService);
            });
        });

        using HttpClient executionHttpClient = executionHost.CreateClient();
        var gatewayOpts = Substitute.For<IOptionsMonitor<GatewayAuthenticationOptions>>();
        gatewayOpts.CurrentValue.Returns(new GatewayAuthenticationOptions());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GATEWAY_SECRET"] = TestSecurityContext.GatewaySecret,
            })
            .Build();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = subjectAContext };
        var liveWebhookPublisher = new HttpEconsentExecutionWebhookPublisher(
            executionHttpClient,
            gatewayOpts,
            config,
            _clock,
            NullLogger<HttpEconsentExecutionWebhookPublisher>.Instance,
            httpContextAccessor);

        var completeResult = await EconsentEndpoints.CompleteReconsentAsync(
            "req-subj-a",
            new CompleteReconsentRequest(
                SubjectPseudonym: SubjectSubA,
                StudyCode: StudyCodeValue,
                SiteCode: SiteCodeValue,
                TemplateId: "template-icf",
                VersionIndex: 2,
                ProtocolVersion: "v2.0",
                SignerName: "subject.site101",
                SignerEmail: null,
                SignerRole: "Subject",
                Meaning: "I have read and agree to the amended consent terms",
                SigningReason: null,
                ComprehensionAnswers: null,
                DigestSha256: new string('c', 64),
                SignatureData: "reconsent:subject.site101",
                DeviceTimestamp: now),
            db,
            subjectAContext,
            _clock,
            liveWebhookPublisher,
            ct);

        var completed = completeResult.Should().BeOfType<Ok<CompleteReconsentResponse>>().Subject.Value!;
        completed.Status.Should().Be("COMPLETED");
        completed.VersionIndex.Should().Be(2);

        var updatedReq = await db.ReconsentRequirements.SingleAsync(r => r.Id == "req-subj-a", ct);
        updatedReq.Status.Should().Be("COMPLETED");
        updatedReq.CompletedConsentId.Should().Be(completed.CompletedConsentId);

        var supersededPrior = await db.SubjectConsents.SingleAsync(c => c.Id == "consent-v1", ct);
        supersededPrior.Status.Should().Be("SUPERSEDED");

        capturedExecutionPayload.Should().NotBeNull();
        capturedExecutionPayload!.SubjectCode.Should().Be(SubjectSubA);
        capturedExecutionPayload.StudyCode.Should().Be(StudyCodeValue);
        capturedExecutionPayload.SiteCode.Should().Be(SiteCodeValue);
        capturedExecutionPayload.VersionTag.Should().Be("v2.0");
        capturedExecutionPayload.VersionIndex.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task EconsentWebhookApplicationService_AutoEnrollsMissingSubject_UpsertsSignedConsent_AndClearsPriorReconsentFlags()
    {
        var ct = TestContext.Current.CancellationToken;
        var subjects = Substitute.For<ISubjectRepository>();
        var consents = Substitute.For<IConsentRepository>();
        var auditLog = Substitute.For<IAuditLogWriter>();
        var actorScope = Substitute.For<IAuditActorScope>();

        subjects.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(call.Arg<CancellationToken>()));
        subjects.GetByCodeAsync(SubjectSubA, Arg.Any<CancellationToken>()).Returns((ClinicalSubject?)null);
        subjects.EnrollNextIndexAsync(Arg.Any<StudyCode>(), Arg.Any<CancellationToken>()).Returns(1);

        var priorConsent = ExecutionSubjectConsent.Create(
            new SubjectCode(SubjectSubA),
            new ProtocolVersionRef(new StudyCode(StudyCodeValue), "v1.0", 1),
            icfSigned: true,
            icfSignedDate: _clock.UtcNow.AddDays(-14),
            requiresReconsent: true,
            isPaperOverride: false);
        consents.GetOtherRequiringReconsentAsync(
                Arg.Any<SubjectCode>(),
                Arg.Any<StudyCode>(),
                2,
                Arg.Any<CancellationToken>())
            .Returns([priorConsent]);

        var service = new EconsentWebhookApplicationService(subjects, consents, auditLog, actorScope);
        var result = await service.ApplyConsentSignedAsync(
            new EconsentWebhookPayload(
                SubjectCode: SubjectSubA,
                StudyCode: StudyCodeValue,
                SiteCode: SiteCodeValue,
                VersionTag: "v2.0",
                VersionIndex: 2,
                SignedAt: _clock.UtcNow,
                ConsentId: "consent-v2"),
            ct);

        result.IsSuccess.Should().BeTrue();
        actorScope.Received(1).RunAs(ServiceAccounts.EConsentWebhook);
        await subjects.Received(1).AddAsync(
            Arg.Is<ClinicalSubject>(s => s.SubjectCode.Value == SubjectSubA && s.StudyCode.Value == StudyCodeValue),
            Arg.Any<CancellationToken>());
        await consents.Received(1).AddAsync(
            Arg.Is<ExecutionSubjectConsent>(c =>
                c.SubjectCode.Value == SubjectSubA
                && c.ProtocolVersion.VersionIndex == 2
                && c.IcfSigned
                && !c.RequiresReconsent),
            Arg.Any<CancellationToken>());
        priorConsent.RequiresReconsent.Should().BeFalse("signing amendment v2 clears RequiresReconsent on prior v1");
        await subjects.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private EconsentDbContext CreateEconsentContext()
    {
        var options = new DbContextOptionsBuilder<EconsentDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new EconsentDbContext(options, TestFieldProtection.CreateEncryptor());
    }

    private static DefaultHttpContext CreateSubjectHttpContext(
        string userId,
        IReadOnlyList<string> studyIds,
        IReadOnlyList<string> siteIds)
    {
        var context = new DefaultHttpContext();
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            TenantId = "cadence-local",
            Roles = RoleNormalization.GetNormalizedRoles([RoleNormalization.ROLE_SUBJECT]),
            RawRoles = [RoleNormalization.ROLE_SUBJECT],
            AssignedStudies = studyIds,
            AssignedSites = siteIds,
            ProductFamilies = [nameof(ProductFamily.Engage)],
        };
        return context;
    }

    private sealed class ClockBackedTimeProvider(IClock clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.UtcNow;
    }
}
