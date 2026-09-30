using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.UnitTests.Execution.Fakes;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Unit tests for ASP.NET Core 10 Minimal API endpoint handlers governing observations, forms, and discrepancy queries.
/// @req:SRS-EDC-003
/// </summary>
public sealed class ObservationAndFormEndpointsTests
{
    private sealed class FakeAuditContextAccessor(string userId = "test_cra_user", string reason = "GxP Audit Reason") : ICurrentAuditContextAccessor
    {
        public string UserId => userId;
        public string ChangeReason => reason;
    }

    private static DefaultHttpContext CreateHttpContext(string role = "cra")
    {
        return new DefaultHttpContext { User = TestSecurityContext.CreatePrincipal("test_user", role) };
    }

    /// <summary>
    /// Validates that ObservationEndpoints.CreateObservationAsync returns 201 Created with Location header.
    /// </summary>
    [Fact]
    public async Task CreateObservation_Handler_ReturnsCreatedResultWithLocation()
    {
        var repo = new InMemoryObservationRepository();
        var subjectRepo = new InMemorySubjectRepository();
        var ucum = new UcumNormalizationService();
        var labRange = new LabRangeService();
        var editChecks = new AstEditCheckEngine();
        var clock = new FakeClock();
        var audit = new FakeAuditContextAccessor();
        var svc = new ObservationApplicationService(repo, subjectRepo, ucum, labRange, editChecks, clock, audit);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            Domain: "VS",
            TestCode: "SYSBP",
            TestName: "Systolic Blood Pressure",
            ObservationDate: FakeClock.DefaultStart.UtcDateTime,
            Value: 120.0,
            Unit: "mmHg");

        var result = await ObservationEndpoints.CreateObservationAsync(request, svc, CancellationToken.None);

        result.Should().BeOfType<Created<ObservationResponse>>();
        var created = (Created<ObservationResponse>)result;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().NotBeNull();
        created.Location.Should().StartWith("/api/v1/execution/observations/");
    }

    /// <summary>
    /// Validates that ObservationEndpoints.VerifyObservationSdvAsync verifies observation and returns 200 OK.
    /// </summary>
    [Fact]
    public async Task VerifyObservationSdv_Handler_ReturnsOkWithVerifiedState()
    {
        var repo = new InMemoryObservationRepository();
        var subjectRepo = new InMemorySubjectRepository();
        var ucum = new UcumNormalizationService();
        var labRange = new LabRangeService();
        var editChecks = new AstEditCheckEngine();
        var clock = new FakeClock();
        var audit = new FakeAuditContextAccessor();

        var svc = new ObservationApplicationService(repo, subjectRepo, ucum, labRange, editChecks, clock, audit);

        var createRes = await svc.CreateAsync(new ObservationCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            Domain: "VS",
            TestCode: "SYSBP",
            TestName: "Systolic Blood Pressure",
            ObservationDate: FakeClock.DefaultStart.UtcDateTime,
            Value: 120.0), CancellationToken.None);

        var obsId = createRes.Value!.Id;

        var result = await ObservationEndpoints.VerifyObservationSdvAsync(obsId, CreateHttpContext(role: "cra"), svc, CancellationToken.None);

        result.Should().BeOfType<Ok<ObservationResponse>>();
        var ok = (Ok<ObservationResponse>)result;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value!.IsSdvVerified.Should().BeTrue();
        ok.Value.SdvVerifiedBy.Should().Be("test_user", "the verifier is the authenticated caller");
    }

    /// <summary>
    /// A site role cannot mark data source-verified or flag it: SDV verify and flag answer 403 and leave the
    /// observation unverified (#4920).
    /// </summary>
    [Theory]
    [InlineData("investigator")]
    [InlineData("crc")]
    [InlineData("subject")]
    [InlineData("auditor")]
    public async Task ObservationSdv_Handlers_WithSiteOrReadOnlyRole_Return403AndDoNotVerify(string role)
    {
        var repo = new InMemoryObservationRepository();
        var svc = new ObservationApplicationService(
            repo, new InMemorySubjectRepository(), new UcumNormalizationService(), new LabRangeService(),
            new AstEditCheckEngine(), new FakeClock(), new FakeAuditContextAccessor());
        var created = await svc.CreateAsync(new ObservationCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            Domain: "VS",
            TestCode: "SYSBP",
            TestName: "Systolic Blood Pressure",
            ObservationDate: FakeClock.DefaultStart.UtcDateTime,
            Value: 120.0), CancellationToken.None);
        string id = created.Value!.Id;

        var verify = await ObservationEndpoints.VerifyObservationSdvAsync(id, CreateHttpContext(role), svc, CancellationToken.None);
        var flag = await ObservationEndpoints.FlagObservationSdvAsync(
            id, new FlagObservationRequest("Source mismatch"), CreateHttpContext(role), svc, CancellationToken.None);

        foreach (var result in new[] { verify, flag })
        {
            var problem = result.Should().BeOfType<ProblemHttpResult>().Which;
            problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            problem.ProblemDetails.Detail.Should().Contain("not authorized to verify source data");
        }

        (await svc.GetByIdAsync(id, CancellationToken.None)).Value!.IsSdvVerified.Should().BeFalse();
    }

    /// <summary>
    /// A caller with no authenticated identity is refused SDV rather than treated as a verifier.
    /// </summary>
    [Fact]
    public async Task ObservationSdv_WithoutAuthenticatedCaller_Returns403()
    {
        var svc = new ObservationApplicationService(
            new InMemoryObservationRepository(), new InMemorySubjectRepository(), new UcumNormalizationService(),
            new LabRangeService(), new AstEditCheckEngine(), new FakeClock(), new FakeAuditContextAccessor());

        var result = await svc.VerifySdvAsync(Guid.NewGuid().ToString(), verifier: null, CancellationToken.None);

        result.Error!.Code.Should().Be("UNAUTHORIZED_SDV_ROLE");
    }

    [Theory]
    [InlineData("cra", true)]
    [InlineData("monitor", true)]
    [InlineData("external_monitor", true)]
    [InlineData("sponsor_dm", true)]
    [InlineData("Data Manager", true)]
    [InlineData("investigator", false)]
    [InlineData("crc", false)]
    [InlineData("sysadmin", false)]
    [InlineData("", false)]
    public void CanVerifySourceData_AllowsMonitorsAndDataManagersOnly(string role, bool expected) =>
        ObservationApplicationService.CanVerifySourceData(role).Should().Be(expected);

    /// <summary>
    /// Validates that FormEndpoints.CreateFormSubmissionAsync returns 201 Created with Location header.
    /// </summary>
    [Fact]
    public async Task CreateFormSubmission_Handler_ReturnsCreatedResult()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeAuditContextAccessor();

        var svc = new FormSubmissionApplicationService(formRepo, obsRepo, ucum, audit);

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}");

        var result = await FormEndpoints.CreateFormSubmissionAsync(request, svc, CancellationToken.None);

        result.Should().BeOfType<Created<FormSubmissionResponse>>();
        var created = (Created<FormSubmissionResponse>)result;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Location.Should().StartWith("/api/v1/execution/forms/");
        created.Value!.Status.Should().Be(FormSubmissionStatus.Draft);
    }

    /// <summary>
    /// Validates that QueryEndpoints.CreateQueryAsync returns 201 Created.
    /// </summary>
    [Fact]
    public async Task CreateQuery_Handler_ReturnsCreatedResult()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var clock = new FakeClock();
        var audit = new FakeAuditContextAccessor();

        var svc = new ClinicalQueryApplicationService(repo, audit, clock);

        var request = new QueryCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            TestCode: "SYSBP",
            Explanation: "Please review SYSBP value.");

        var result = await QueryEndpoints.CreateQueryAsync(request, svc, CancellationToken.None);

        result.Should().BeOfType<Created<ClinicalQueryResponse>>();
        var created = (Created<ClinicalQueryResponse>)result;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value!.Status.Should().Be(QueryStatus.Open);
    }

    /// <summary>
    /// Validates that QueryEndpoints.CloseQueryAsync enforces CRA/DM role authorization.
    /// </summary>
    [Fact]
    public async Task CloseQuery_Handler_WithUnauthorizedRole_ReturnsForbiddenProblemResult()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var clock = new FakeClock();
        var audit = new FakeAuditContextAccessor();

        var svc = new ClinicalQueryApplicationService(repo, audit, clock);

        var query = ClinicalQuery.Raise(
            studyId: new Apps.Execution.Domain.Subjects.StudyCode("STUDY-001"),
            subjectId: new Apps.Execution.Domain.Subjects.SubjectCode("SUBJ-001"),
            testCode: new Apps.Execution.Domain.Observations.TestCode("SYSBP"),
            explanation: "Review needed.").Value!;
        query.Answer("Addressed.", "inv-01");
        await repo.AddAsync(query, CancellationToken.None);

        var httpContext = CreateHttpContext(role: "site_investigator");
        var result = await QueryEndpoints.CloseQueryAsync(
            query.Id.ToString(),
            httpContext,
            svc,
            CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Validates that QueryEndpoints.CloseQueryAsync succeeds when invoked by authorized CRA caller.
    /// </summary>
    [Fact]
    public async Task CloseQuery_Handler_WithCraRole_ReturnsOkResult()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var clock = new FakeClock();
        var audit = new FakeAuditContextAccessor();

        var svc = new ClinicalQueryApplicationService(repo, audit, clock);

        var query = ClinicalQuery.Raise(
            studyId: new Apps.Execution.Domain.Subjects.StudyCode("STUDY-001"),
            subjectId: new Apps.Execution.Domain.Subjects.SubjectCode("SUBJ-001"),
            testCode: new Apps.Execution.Domain.Observations.TestCode("SYSBP"),
            explanation: "Review needed.").Value!;
        query.Answer("Addressed.", "inv-01");
        await repo.AddAsync(query, CancellationToken.None);

        var httpContext = CreateHttpContext(role: "cra");
        var result = await QueryEndpoints.CloseQueryAsync(
            query.Id.ToString(),
            httpContext,
            svc,
            CancellationToken.None);

        result.Should().BeOfType<Ok<ClinicalQueryResponse>>();
        var ok = (Ok<ClinicalQueryResponse>)result;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value!.Status.Should().Be(QueryStatus.Closed);
    }

    /// <summary>
    /// Validates that FormEndpoints.ApproveFormSubmissionAsync succeeds with valid cryptographic signature.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public async Task ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeAuditContextAccessor();

        var svc = new FormSubmissionApplicationService(formRepo, obsRepo, ucum, audit);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var form = FormSubmission.Initialize(
            studyId: new Apps.Execution.Domain.Subjects.StudyCode("STUDY-001"),
            subjectId: new Apps.Execution.Domain.Subjects.SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            initialPayload: payload).Value!;
        form.Complete();
        await formRepo.AddAsync(form, CancellationToken.None);

        var request = FormSubmissionApproveRequest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: FormSubmissionApplicationService.ComputeSha256(payload));

        var httpContext = new DefaultHttpContext { User = TestSecurityContext.CreatePrincipal("pi-user-01", "Principal Investigator") };
        var result = await FormEndpoints.ApproveFormSubmissionAsync(form.Id.ToString(), request, httpContext, svc, CancellationToken.None);

        result.Should().BeOfType<Ok<FormSubmissionResponse>>();
        var ok = (Ok<FormSubmissionResponse>)result;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value!.Status.Should().Be(FormSubmissionStatus.Approved);
        ok.Value!.IsReadonly.Should().BeTrue();
    }

    /// <summary>
    /// Validates that FormEndpoints.ApproveFormSubmissionAsync returns 403 Forbidden when signer role is unauthorized.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public async Task ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeAuditContextAccessor();

        var svc = new FormSubmissionApplicationService(formRepo, obsRepo, ucum, audit);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var form = FormSubmission.Initialize(
            studyId: new Apps.Execution.Domain.Subjects.StudyCode("STUDY-001"),
            subjectId: new Apps.Execution.Domain.Subjects.SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            initialPayload: payload).Value!;
        form.Complete();
        await formRepo.AddAsync(form, CancellationToken.None);

        var request = FormSubmissionApproveRequest.Create(
            SignerId: "user-01",
            SignerRole: "unauthorized_role",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: FormSubmissionApplicationService.ComputeSha256(payload));

        var httpContext = new DefaultHttpContext { User = TestSecurityContext.CreatePrincipal("user-01", "unauthorized_role") };
        var result = await FormEndpoints.ApproveFormSubmissionAsync(form.Id.ToString(), request, httpContext, svc, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Validates that FormEndpoints.UnlockFormSubmissionAsync succeeds and reverts form to Draft.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public async Task UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeAuditContextAccessor(userId: "dm-01", reason: "Sponsor query correction");

        var svc = new FormSubmissionApplicationService(formRepo, obsRepo, ucum, audit);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var form = FormSubmission.Initialize(
            studyId: new Apps.Execution.Domain.Subjects.StudyCode("STUDY-001"),
            subjectId: new Apps.Execution.Domain.Subjects.SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            initialPayload: payload).Value!;
        form.Complete();
        form.Approve(SignatureManifest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: FormSubmissionApplicationService.ComputeSha256(payload)));
        await formRepo.AddAsync(form, CancellationToken.None);

        var result = await FormEndpoints.UnlockFormSubmissionAsync(
            form.Id.ToString(),
            new FormUnlockRequest("Data discrepancy correction requested by DM"),
            svc,
            audit,
            CancellationToken.None);

        result.Should().BeOfType<Ok<FormSubmissionResponse>>();
        var ok = (Ok<FormSubmissionResponse>)result;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value!.Status.Should().Be(FormSubmissionStatus.Draft);
        ok.Value!.IsReadonly.Should().BeFalse();
    }
}
