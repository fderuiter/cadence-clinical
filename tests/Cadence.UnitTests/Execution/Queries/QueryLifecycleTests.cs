using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Compliance;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Queries;

/// <summary>
/// Unit test coverage for clinical discrepancy query 6-state lifecycle (Candidate, Open, Answered, Closed, Reopened, Cancelled),
/// duplicate active coordinate detection, and CRA/DM role validation.
/// </summary>
public sealed class QueryLifecycleTests
{

    /// <summary>
    /// Validates that raising a clinical discrepancy query initializes it in Open status.
    /// </summary>
    [Fact]
    public void RaiseQuery_CreatesQueryInOpenState()
    {
        var result = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            testCode: new TestCode("SYSBP"),
            explanation: "Systolic BP 210 is physiologically extreme.",
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01");

        result.IsSuccess.Should().BeTrue();
        var query = result.Value!;
        query.Status.Should().Be(QueryStatus.Open);
        query.Explanation.Should().Be("Systolic BP 210 is physiologically extreme.");
        query.VersionIndex.Should().Be(1);
    }

    /// <summary>
    /// Validates complete 6-state query lifecycle: Open -> Answered -> Closed -> Reopened -> Answered -> Closed.
    /// </summary>
    [Fact]
    public void QueryLifecycle_FullTransitionSequence_Succeeds()
    {
        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            testCode: new TestCode("SYSBP"),
            explanation: "Please confirm blood pressure value.").Value!;

        // 1. Answer
        var answerRes = query.Answer("Confirmed, patient had severe hypertension episode.", "investigator-01");
        answerRes.IsSuccess.Should().BeTrue();
        query.Status.Should().Be(QueryStatus.Answered);
        query.Response.Should().Be("Confirmed, patient had severe hypertension episode.");
        query.Responder.Should().Be("investigator-01");

        // 2. Close
        var closeTime = FakeClock.DefaultStart;
        var closeRes = query.Close("cra-01", closeTime);
        closeRes.IsSuccess.Should().BeTrue();
        query.Status.Should().Be(QueryStatus.Closed);
        query.Resolver.Should().Be("cra-01");
        query.ResolvedAt.Should().Be(closeTime);

        // 3. Reopen
        var reopenRes = query.Reopen("Additional follow-up reading needed.", "cra-01");
        reopenRes.IsSuccess.Should().BeTrue();
        query.Status.Should().Be(QueryStatus.Reopened);
        query.Resolver.Should().BeNull();

        // 4. Re-answer
        query.Answer("Repeated measurement confirms 140/90.", "investigator-01").IsSuccess.Should().BeTrue();
        query.Status.Should().Be(QueryStatus.Answered);

        // 5. Final Close
        query.Close("cra-01", FakeClock.DefaultStart).IsSuccess.Should().BeTrue();
        query.Status.Should().Be(QueryStatus.Closed);
    }

    /// <summary>
    /// Validates that an open query raised in error can be cancelled with a mandatory reason.
    /// </summary>
    [Fact]
    public void CancelQuery_FromOpen_SucceedsWithReason()
    {
        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            testCode: new TestCode("SYSBP"),
            explanation: "Accidental query creation.").Value!;

        var cancelRes = query.Cancel("Raised in error, wrong subject.", "cra-01");
        cancelRes.IsSuccess.Should().BeTrue();
        query.Status.Should().Be(QueryStatus.Cancelled);
        query.CancellationReason.Should().Be("Raised in error, wrong subject.");

        // Closed or cancelled query cannot be cancelled again
        var reCancel = query.Cancel("Second cancel", "cra-01");
        reCancel.IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// Validates that the application service detects existing active queries on the same target field coordinates.
    /// </summary>
    [Fact]
    public async Task ApplicationService_PreventsDuplicateActiveQueryOnSameCoordinates()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var clock = new FakeClock();
        var audit = new FakeCurrentAuditContextAccessor("cra-user-01", "Query management");

        var service = new ClinicalQueryApplicationService(repo, audit, clock);

        var request = new QueryCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            TestCode: "SYSBP",
            Explanation: "First query on SYSBP",
            VisitId: "VISIT-01",
            Domain: "VS");

        var firstResult = await service.CreateAsync(request, CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        // Attempt second query on same coordinates
        var secondResult = await service.CreateAsync(request, CancellationToken.None);
        secondResult.IsFailure.Should().BeTrue();
        secondResult.Error!.Code.Should().Be("ACTIVE_QUERY_EXISTS");
    }

    /// <summary>
    /// Validates that closing a clinical query requires CRA or Data Manager role authorization.
    /// </summary>
    [Fact]
    public async Task ApplicationService_Close_ValidatesCraOrDmRole()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var clock = new FakeClock();
        var audit = new FakeCurrentAuditContextAccessor("cra-user-01", "Query management");

        var service = new ClinicalQueryApplicationService(repo, audit, clock);

        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            testCode: new TestCode("SYSBP"),
            explanation: "Review needed.").Value!;
        query.Answer("Done.", "inv-01");
        await repo.AddAsync(query, CancellationToken.None);

        // Attempt close with unauthorized investigator role
        var rejectedResult = await service.CloseAsync(
            query.Id.ToString(),
            CancellationToken.None,
            userRole: "site_investigator");

        rejectedResult.IsFailure.Should().BeTrue();
        rejectedResult.Error!.Code.Should().Be("UNAUTHORIZED_QUERY_ROLE");

        // Attempt close with authorized CRA role
        var approvedResult = await service.CloseAsync(
            query.Id.ToString(),
            CancellationToken.None,
            userRole: "cra");

        approvedResult.IsSuccess.Should().BeTrue();
        approvedResult.Value!.Status.Should().Be(QueryStatus.Closed);

        // Attempt close with authorized Data Manager role (with space/mixed case)
        var query2 = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            testCode: new TestCode("SYSBP"),
            explanation: "Review needed 2.").Value!;
        query2.Answer("Done.", "inv-01");
        await repo.AddAsync(query2, CancellationToken.None);

        var dmResult = await service.CloseAsync(
            query2.Id.ToString(),
            CancellationToken.None,
            userRole: "Data Manager");

        dmResult.IsSuccess.Should().BeTrue();
        dmResult.Value!.Status.Should().Be(QueryStatus.Closed);

        // Attempt close with authorized sponsor_dm role
        var query3 = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            testCode: new TestCode("SYSBP"),
            explanation: "Review needed 3.").Value!;
        query3.Answer("Done.", "inv-01");
        await repo.AddAsync(query3, CancellationToken.None);

        var sponsorDmResult = await service.CloseAsync(
            query3.Id.ToString(),
            CancellationToken.None,
            userRole: "sponsor_dm");

        sponsorDmResult.IsSuccess.Should().BeTrue();
        sponsorDmResult.Value!.Status.Should().Be(QueryStatus.Closed);
    }
    /// <summary>
    /// Validates that a caller with no role is denied close, reopen and cancel rather than treated as authorized.
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApplicationService_CloseReopenCancel_WithoutRole_AreDenied(string? role)
    {
        var repo = new InMemoryClinicalQueryRepository();
        var service = new ClinicalQueryApplicationService(repo, new FakeCurrentAuditContextAccessor("cra-user-01", "Query management"), new FakeClock());

        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            testCode: new TestCode("SYSBP"),
            explanation: "Review needed.").Value!;
        query.Answer("Done.", "inv-01");
        await repo.AddAsync(query, CancellationToken.None);

        var close = await service.CloseAsync(query.Id.ToString(), CancellationToken.None, userRole: role);
        var reopen = await service.ReopenAsync(query.Id.ToString(), new QueryReopenRequest("Recheck"), CancellationToken.None, userRole: role);
        var cancel = await service.CancelAsync(query.Id.ToString(), new QueryCancelRequest("Duplicate"), CancellationToken.None, userRole: role);

        close.Error!.Code.Should().Be("UNAUTHORIZED_QUERY_ROLE");
        reopen.Error!.Code.Should().Be("UNAUTHORIZED_QUERY_ROLE");
        cancel.Error!.Code.Should().Be("UNAUTHORIZED_QUERY_ROLE");
        query.Status.Should().Be(QueryStatus.Answered);
    }

    /// <summary>
    /// Validates that query mutations (Create, Answer, Close, Reopen, Cancel) on approved/read-only forms return FORM_LOCKED error.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task ApplicationService_QueryOperationsOnLockedForm_ReturnFormLockedError()
    {
        var queryRepo = new InMemoryClinicalQueryRepository();
        var formRepo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var audit = new FakeCurrentAuditContextAccessor("cra-user-01", "Query management");

        var service = new ClinicalQueryApplicationService(queryRepo, audit, clock, formRepo);

        var studyCode = new StudyCode("STUDY-001");
        var subjectCode = new SubjectCode("SUBJ-001");
        var visitId = "VISIT-01";
        var formId = new FormId("FORM-01");

        // 1. Create an approved (locked) form
        var form = FormSubmission.Initialize(studyCode, subjectCode, formId, new SiteCode("SITE-01"), visitId).Value!;
        form.Complete().IsSuccess.Should().BeTrue();

        var manifest = SignatureManifest.Create(
            SignerId: "pi-user-01",
            SignerRole: "principal_investigator",
            SigningReason: "PI approval and sign-off.",
            SignedAt: clock.UtcNow.UtcDateTime,
            PayloadHash: "hash123");

        form.Approve(manifest).IsSuccess.Should().BeTrue();
        await formRepo.AddAsync(form, CancellationToken.None);

        // 2. CreateAsync on locked form -> should fail with FORM_LOCKED
        var createRequest = new QueryCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            TestCode: "SYSBP",
            Explanation: "Query on locked form",
            VisitId: "VISIT-01",
            FormId: "FORM-01");

        var createResult = await service.CreateAsync(createRequest, CancellationToken.None);
        createResult.IsFailure.Should().BeTrue();
        createResult.Error!.Code.Should().Be("FORM_LOCKED");

        // 3. Add an existing open query associated with this form to test state transitions
        var openQuery = ClinicalQuery.Raise(
            studyId: studyCode,
            subjectId: subjectCode,
            testCode: new TestCode("SYSBP"),
            explanation: "Existing query on locked form",
            visitId: visitId,
            formId: "FORM-01").Value!;
        await queryRepo.AddAsync(openQuery, CancellationToken.None);

        // AnswerAsync -> should fail with FORM_LOCKED
        var answerResult = await service.AnswerAsync(
            openQuery.Id.ToString(),
            new QueryRespondRequest("Answering query"),
            CancellationToken.None);
        answerResult.IsFailure.Should().BeTrue();
        answerResult.Error!.Code.Should().Be("FORM_LOCKED");

        // CloseAsync -> should fail with FORM_LOCKED
        var closeResult = await service.CloseAsync(
            openQuery.Id.ToString(),
            CancellationToken.None,
            userRole: "cra");
        closeResult.IsFailure.Should().BeTrue();
        closeResult.Error!.Code.Should().Be("FORM_LOCKED");

        // CancelAsync -> should fail with FORM_LOCKED
        var cancelResult = await service.CancelAsync(
            openQuery.Id.ToString(),
            new QueryCancelRequest("Cancelling query"),
            CancellationToken.None,
            userRole: "cra");
        cancelResult.IsFailure.Should().BeTrue();
        cancelResult.Error!.Code.Should().Be("FORM_LOCKED");

        // Add an answered query to test ReopenAsync
        var answeredQuery = ClinicalQuery.Raise(
            studyId: studyCode,
            subjectId: subjectCode,
            testCode: new TestCode("DIABP"),
            explanation: "Answered query on locked form",
            visitId: visitId,
            formId: "FORM-01").Value!;
        answeredQuery.Answer("Response", "inv-01");
        await queryRepo.AddAsync(answeredQuery, CancellationToken.None);

        // ReopenAsync -> should fail with FORM_LOCKED
        var reopenResult = await service.ReopenAsync(
            answeredQuery.Id.ToString(),
            new QueryReopenRequest("Reopening query"),
            CancellationToken.None,
            userRole: "cra");
        reopenResult.IsFailure.Should().BeTrue();
        reopenResult.Error!.Code.Should().Be("FORM_LOCKED");
    }

    /// <summary>
    /// Validates that query operations proceed normally when the form is in Draft or Completed status, or unlocked.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task ApplicationService_QueryOperationsOnUnapprovedOrUnlockedForm_Succeed()
    {
        var queryRepo = new InMemoryClinicalQueryRepository();
        var formRepo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var audit = new FakeCurrentAuditContextAccessor("cra-user-01", "Query management");

        var service = new ClinicalQueryApplicationService(queryRepo, audit, clock, formRepo);

        var studyCode = new StudyCode("STUDY-001");
        var subjectCode = new SubjectCode("SUBJ-001");
        var visitId = "VISIT-01";
        var formId = new FormId("FORM-02");

        // Create draft form submission
        var form = FormSubmission.Initialize(studyCode, subjectCode, formId, new SiteCode("SITE-01"), visitId).Value!;
        await formRepo.AddAsync(form, CancellationToken.None);

        // CreateAsync on draft form succeeds
        var createRequest = new QueryCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            TestCode: "SYSBP",
            Explanation: "Query on draft form",
            VisitId: "VISIT-01",
            FormId: "FORM-02");

        var createResult = await service.CreateAsync(createRequest, CancellationToken.None);
        createResult.IsSuccess.Should().BeTrue();
        var queryId = createResult.Value!.Id;

        // AnswerAsync on draft form succeeds
        var answerResult = await service.AnswerAsync(
            queryId,
            new QueryRespondRequest("Answer for draft form query"),
            CancellationToken.None);
        answerResult.IsSuccess.Should().BeTrue();

        // CloseAsync on draft form succeeds
        var closeResult = await service.CloseAsync(
            queryId,
            CancellationToken.None,
            userRole: "cra");
        closeResult.IsSuccess.Should().BeTrue();

        // ReopenAsync on draft form succeeds
        var reopenResult = await service.ReopenAsync(
            queryId,
            new QueryReopenRequest("Reopening draft form query"),
            CancellationToken.None,
            userRole: "cra");
        reopenResult.IsSuccess.Should().BeTrue();

        // CancelAsync on draft form succeeds
        var cancelResult = await service.CancelAsync(
            queryId,
            new QueryCancelRequest("Cancelling draft form query"),
            CancellationToken.None,
            userRole: "cra");
        cancelResult.IsSuccess.Should().BeTrue();
    }
}
