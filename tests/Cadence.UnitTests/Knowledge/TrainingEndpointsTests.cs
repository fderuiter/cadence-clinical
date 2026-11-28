using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Application;
using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Knowledge;

/// <summary>
/// Verifies Knowledge curriculum authoring, batch assignment against canonical Kernel grants (zero standalone
/// <c>DelegationRecord</c> tables), quiz scoring, and 21 CFR Part 11 <c>05.02.04</c> certificate + Kernel
/// <c>TrainingRecord</c> / <c>Qualification</c> registration.
/// </summary>
public class TrainingEndpointsTests
{
    private const string Study = "CARDIO-2026";
    private const string CurriculumId = "curriculum-v3-delta";

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
    private readonly InMemoryKnowledgeKernelTrustClient _kernelTrust = new();
    private readonly InMemoryEtmfTrainingIngestClient _etmfClient = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TenantId DefaultTenant =>
        TenantId.FromGuid(DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-test"));

    private static KnowledgeDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>()
            .UseInMemoryDatabase($"{dbName}-{Guid.NewGuid():N}")
            .Options;
        var db = new KnowledgeDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task ListCurricula_WithNoCurricula_ReturnsAnEmptyListAndWritesNothing()
    {
        using var db = CreateDbContext(nameof(ListCurricula_WithNoCurricula_ReturnsAnEmptyListAndWritesNothing));

        var result = await Invoke("ListCurriculaAsync", null, null, CreateCurriculumService(db), Caller(), Ct);

        result.Should().BeOfType<Ok<List<TrainingCurriculumDto>>>().Which.Value.Should().BeEmpty();
        (await db.TrainingCurricula.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-TRN-001")]
    public async Task CreateCurriculum_PersistsModulesAndQuizQuestions_AndWritesAuditLog()
    {
        using var db = CreateDbContext(nameof(CreateCurriculum_PersistsModulesAndQuizQuestions_AndWritesAuditLog));
        var createRequest = new CreateTrainingCurriculumRequest(
            StudyId: new KernelStudyCode(Study),
            ProtocolVersion: "3.0",
            ProtocolVersionNumber: 3,
            Title: "Protocol V3.0 Delta Amendment Curriculum",
            Description: "Covers Week 12 window and troponin gating updates.",
            QualificationCode: "protocol_training",
            PassingScore: 0.8,
            Modules:
            [
                new TrainingModuleDto(
                    "mod-1",
                    "Inclusion Criteria Update",
                    "Section 4.1",
                    "Tightens systolic blood pressure threshold.",
                    ["SBP >= 140 mmHg required at screening"],
                    10),
            ],
            QuizQuestions:
            [
                new TrainingQuizQuestionDto(
                    "q-1",
                    "What is the updated screening SBP requirement?",
                    [
                        new TrainingQuizOptionDto("opt-a", ">= 130 mmHg"),
                        new TrainingQuizOptionDto("opt-b", ">= 140 mmHg"),
                    ],
                    "opt-b"),
            ],
            ReasonForChange: "Author V3.0 amendment training curriculum");

        var result = await Invoke("CreateCurriculumAsync", createRequest, CreateCurriculumService(db), Caller(), Ct);

        var created = result.Should().BeOfType<Created<TrainingCurriculumDto>>().Which.Value!;
        created.StudyId.Raw.Should().Be(Study);
        created.ProtocolVersionNumber.Should().Be(3);
        created.QualificationCode.Should().Be("protocol_training");
        created.PassingScore.Should().Be(0.8);
        created.Modules.Should().ContainSingle(m => m.Id == "mod-1");
        created.QuizQuestions.Should().ContainSingle(q => q.Id == "q-1" && q.CorrectOptionId == "opt-b");

        (await db.OrgAuditLogs.CountAsync(a => a.Action == "training.curriculum.create", Ct)).Should().Be(1);
    }

    [Fact]
    public async Task GetCurriculum_ThatDoesNotExist_Returns404()
    {
        using var db = CreateDbContext(nameof(GetCurriculum_ThatDoesNotExist_Returns404));

        var result = await Invoke("GetCurriculumAsync", CurriculumId, CreateCurriculumService(db), Caller(), Ct);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetCurriculum_InAnotherStudy_Returns404()
    {
        using var db = CreateDbContext(nameof(GetCurriculum_InAnotherStudy_Returns404));
        await SeedCurriculumAsync(db);

        var result = await Invoke("GetCurriculumAsync", CurriculumId, CreateCurriculumService(db), Caller("ONCO-2027"), Ct);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetAssignmentDetail_ThatDoesNotExist_Returns404()
    {
        using var db = CreateDbContext(nameof(GetAssignmentDetail_ThatDoesNotExist_Returns404));

        var result = await Invoke("GetAssignmentDetailAsync", "asn-missing", CreateCurriculumService(db), Caller(), Ct);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    [GxPRequirement("PRD-TRN-001")]
    public async Task BatchAssign_ResolvesCanonicalKernelGrantsAndSkipsDuplicates_WithoutDelegationRecordTable()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ResolvesCanonicalKernelGrantsAndSkipsDuplicates_WithoutDelegationRecordTable));
        await SeedCurriculumAsync(db);
        SeedStaffGrants(
            ("user_1", "SITE-101", "Clinical Research Coordinator"),
            ("user_2", "SITE-101", "Clinical Research Coordinator"),
            ("user_3", "SITE-101", "Sub-Investigator"),
            ("user_4", "SITE-102", "Clinical Research Coordinator"));
        db.TrainingAssignments.Add(new TrainingAssignment
        {
            Id = "existing-asn-1",
            CurriculumId = CurriculumId,
            StudyId = Study,
            SiteId = "SITE-101",
            UserId = "user_1",
            UserName = "Staff user_1",
            UserRole = "Clinical Research Coordinator",
            Status = "PENDING",
            CreatedAt = _clock.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var result = await BatchAssign(db, Request(sites: ["SITE-101"], roles: ["Clinical Research Coordinator"]), Caller());

        var response = result.Should().BeOfType<Created<BatchAssignmentResponse>>().Which.Value!;
        response.TotalEligible.Should().Be(2);
        response.TotalAssigned.Should().Be(1);
        response.TotalSkipped.Should().Be(1);
        response.SkippedItems.Should().ContainSingle(s => s.UserId == "user_1");
        response.Assignments.Should().ContainSingle().Which.Should().Match<TrainingAssignmentDto>(a =>
            a.UserId == "user_2" && a.UserName == "Staff user_2" && a.SiteId.Raw == "SITE-101");

        (await db.TrainingAssignments.Select(a => a.UserId).ToListAsync(Ct)).Should().BeEquivalentTo("user_1", "user_2");
        typeof(KnowledgeDbContext).GetProperty("DelegationRecords").Should().BeNull();
    }

    [Fact]
    public async Task BatchAssign_ForAStudyOutsideTheCallersStudies_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForAStudyOutsideTheCallersStudies_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        SeedStaffGrants(("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller("ONCO-2027"));

        await ShouldRefuseAsync(db, result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task BatchAssign_NamingASiteTheCallerDoesNotReach_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_NamingASiteTheCallerDoesNotReach_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        SeedStaffGrants(("user_1", "SITE-102", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-102"]), SiteCaller("SITE-101"));

        await ShouldRefuseAsync(db, result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task BatchAssign_WithNoAudience_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_WithNoAudience_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        SeedStaffGrants(("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task BatchAssign_ForACurriculumThatDoesNotExist_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForACurriculumThatDoesNotExist_IsRefusedAndAssignsNobody));
        SeedStaffGrants(("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task BatchAssign_ForAnotherStudysCurriculum_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForAnotherStudysCurriculum_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db, study: "ONCO-2027");
        SeedStaffGrants(("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task BatchAssign_ForAnInactiveCurriculum_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForAnInactiveCurriculum_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db, isActive: false);
        SeedStaffGrants(("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task BatchAssign_NamingASiteWithNoStaff_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_NamingASiteWithNoStaff_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        SeedStaffGrants(("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101", "SITE-999"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status422UnprocessableEntity);
        ((ProblemHttpResult)result).ProblemDetails.Detail.Should().Contain("SITE-999");
    }

    [Fact]
    public async Task BatchAssign_NamingSomeoneNotInKernelGrants_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_NamingSomeoneNotInKernelGrants_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        SeedStaffGrants(("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(people: ["user_1", "ghost"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status422UnprocessableEntity);
        ((ProblemHttpResult)result).ProblemDetails.Detail.Should().Contain("ghost");
    }

    [Fact]
    public async Task BatchAssign_WhenNoStaffMatchTheFilters_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_WhenNoStaffMatchTheFilters_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        SeedStaffGrants(("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"], roles: ["Pharmacist"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task BatchAssign_OverTheSynchronousLimit_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_OverTheSynchronousLimit_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        SeedStaffGrants(Enumerable.Range(1, 205)
            .Select(i => ($"user_{i}", "SITE-101", "Clinical Research Coordinator")).ToArray());

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status400BadRequest);
        ((ProblemHttpResult)result).ProblemDetails.Detail.Should().Contain("limit of 200 target personnel");
    }

    [Fact]
    [GxPRequirement("PRD-TRN-004")]
    public async Task CompleteAssignment_EvaluatesQuizAndFiles050204CertificateAndKernelQualification()
    {
        using var db = CreateDbContext(nameof(CompleteAssignment_EvaluatesQuizAndFiles050204CertificateAndKernelQualification));
        var service = CreateCurriculumService(db);

        var createdCurriculum = ((Created<TrainingCurriculumDto>)await Invoke(
            "CreateCurriculumAsync",
            new CreateTrainingCurriculumRequest(
                StudyId: Study,
                ProtocolVersion: "3.0",
                ProtocolVersionNumber: 3,
                Title: "V3.0 Amendment Quiz Curriculum",
                Description: "Mandatory site coordinator attestation",
                QualificationCode: "protocol_training",
                PassingScore: 1.0,
                Modules: [new TrainingModuleDto("m1", "Module 1", "Sec 4", "Summary", ["Delta 1"], 5)],
                QuizQuestions:
                [
                    new TrainingQuizQuestionDto(
                        "q1",
                        "Select the required SBP threshold",
                        [new TrainingQuizOptionDto("a", "120"), new TrainingQuizOptionDto("b", "140")],
                        "b"),
                ],
                ReasonForChange: "Author curriculum"),
            service,
            Caller(),
            Ct)).Value!;

        db.TrainingAssignments.Add(new TrainingAssignment
        {
            Id = "asn-crc-1",
            CurriculumId = createdCurriculum.Id,
            StudyId = Study,
            SiteId = "SITE-101",
            UserId = "crc-1",
            UserName = "Sarah Jenkins",
            UserRole = RoleNormalization.ROLE_CRC,
            Status = "PENDING",
            CreatedAt = _clock.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var crcCaller = SiteCaller("SITE-101");
        crcCaller.Items[ElectronicSignatureEndpointFilter.VerifiedTokenItemKey] = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken();

        // 1. Failing quiz score returns 422 without filing eTMF or Kernel qualification
        var failingResult = await Invoke(
            "CompleteAssignmentAsync",
            "asn-crc-1",
            new CompleteTrainingAssignmentRequest(
                QuizAnswers: [new QuizAnswerDto("q1", "a")],
                TimeSpentSeconds: 180,
                SignaturePayload: new SignaturePayloadDto("I attest completion", "Completed V3.0 training")),
            service,
            crcCaller,
            Ct);
        failingResult.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        _etmfClient.IngestedDocuments.Should().BeEmpty();
        _kernelTrust.RecordedQualifications.Should().BeEmpty();

        // 2. Passing quiz score files 05.02.04 certificate in eTMF and registers TrainingRecord + Qualification in Kernel
        var passingResult = await Invoke(
            "CompleteAssignmentAsync",
            "asn-crc-1",
            new CompleteTrainingAssignmentRequest(
                QuizAnswers: [new QuizAnswerDto("q1", "b")],
                TimeSpentSeconds: 240,
                SignaturePayload: new SignaturePayloadDto("I have read, understood, and agree to follow this protocol", "Completed V3.0 training")),
            service,
            crcCaller,
            Ct);
        var completed = passingResult.Should().BeOfType<Ok<CompleteTrainingAssignmentResponse>>().Which.Value!;
        completed.Assignment.Status.Should().Be("COMPLETED");
        completed.QuizScore.Should().Be(1.0);
        completed.EtmfArtifactId.Should().Be("05.02.04");
        completed.EtmfDocumentId.Should().StartWith("DOC-050204-");
        completed.KernelArtifactVersionRef.Should().StartWith("artifact_version:");
        completed.KernelQualificationRef.Should().StartWith("qualification:");

        _etmfClient.IngestedDocuments.Should().ContainSingle(d => d.AssignmentId == "asn-crc-1" && d.SiteCode == "SITE-101");
        _kernelTrust.RecordedQualifications.Should().ContainSingle(q =>
            q.StudyCode == Study && q.HolderId == "crc-1" && q.QualificationCode == "protocol_training");
    }

    private static async Task<IResult> Invoke(string handler, params object?[] arguments)
    {
        var method = typeof(TrainingEndpoints).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static);
        return await (Task<IResult>)method!.Invoke(null, arguments)!;
    }

    private Task<IResult> BatchAssign(KnowledgeDbContext db, BatchAssignCurriculumRequest request, HttpContext caller) =>
        Invoke("BatchAssignCurriculumAsync", request, CreateCurriculumService(db), caller, Ct);

    private ITrainingCurriculumService CreateCurriculumService(KnowledgeDbContext db) =>
        new TrainingCurriculumService(db, new KnowledgeUnitOfWork(db), _kernelTrust, _etmfClient, _clock);

    private static BatchAssignCurriculumRequest Request(
        List<string>? sites = null, List<string>? roles = null, List<string>? people = null) =>
        new(
            CurriculumId,
            new KernelStudyCode(Study),
            sites?.Select(s => new KernelSiteCode(s)).ToList(),
            roles,
            people,
            "Protocol amendment deployment");

    private static async Task ShouldRefuseAsync(KnowledgeDbContext db, IResult result, int statusCode)
    {
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(statusCode);
        (await db.TrainingAssignments.CountAsync(Ct)).Should().Be(0);
        (await db.OrgAuditLogs.CountAsync(Ct)).Should().Be(0);
    }

    private async Task SeedCurriculumAsync(KnowledgeDbContext db, string study = Study, bool isActive = true)
    {
        db.TrainingCurricula.Add(new TrainingCurriculum
        {
            Id = CurriculumId,
            StudyId = new KernelStudyCode(study),
            ProtocolVersion = "3.0",
            ProtocolVersionNumber = 3,
            QualificationCode = "protocol_training",
            PassingScore = 0.8,
            Title = "Protocol V3.0 Delta Amendment Curriculum",
            IsActive = isActive,
            CreatedAt = _clock.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private void SeedStaffGrants(params (string UserId, string SiteId, string Role)[] staff)
    {
        _kernelTrust.Clear();
        foreach (var (userId, siteId, role) in staff)
        {
            _kernelTrust.SeedGrant(DefaultTenant, Study, siteId, userId, $"Staff {userId}", role);
        }
    }

    private static DefaultHttpContext Caller(params string[] studies) => WithPrincipal(new Principal
    {
        UserId = "sponsor-dm-1",
        Roles = ["sponsor_dm"],
        AssignedStudies = studies.Length == 0 ? ["*"] : studies,
    });

    private static DefaultHttpContext SiteCaller(string site) => WithPrincipal(new Principal
    {
        UserId = "crc-1",
        Roles = [RoleNormalization.ROLE_CRC],
        AssignedStudies = [Study],
        AssignedSites = [site],
    });

    private static DefaultHttpContext WithPrincipal(Principal principal)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = principal;
        return httpContext;
    }
}
