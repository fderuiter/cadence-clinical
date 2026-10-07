using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
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
    /// Validates that batch close updates all requested queries atomically for an authorized CRA or DM.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task BatchCloseAsync_WithAuthorizedRole_ClosesAllSelectedQueriesAtomically()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var auditAccessor = new FakeCurrentAuditContextAccessor("cra-user-01", "Batch closing verified answered queries");
        var service = new ClinicalQueryApplicationService(repo, auditAccessor, new FakeClock());

        var q1 = ClinicalQuery.Raise(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), new TestCode("SYSBP"), "Check 1").Value!;
        q1.Answer("Answer 1", "crc-01");
        var q2 = ClinicalQuery.Raise(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), new TestCode("DIABP"), "Check 2").Value!;
        q2.Answer("Answer 2", "crc-01");

        await repo.AddAsync(q1, CancellationToken.None);
        await repo.AddAsync(q2, CancellationToken.None);

        var request = new BatchQueryRequest(new[] { q1.Id.ToString(), q2.Id.ToString() }, "Batch closing verified answered queries");
        var result = await service.BatchCloseAsync(request, CancellationToken.None, userRole: "cra");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(2);
        result.Value.Queries.Should().HaveCount(2);

        var updatedQ1 = await repo.GetByIdAsync(q1.Id, CancellationToken.None);
        var updatedQ2 = await repo.GetByIdAsync(q2.Id, CancellationToken.None);

        updatedQ1!.Status.Should().Be(QueryStatus.Closed);
        updatedQ1.Resolver.Should().Be("cra-user-01");
        updatedQ1.ReasonForChange.Should().Be("Batch closing verified answered queries");

        updatedQ2!.Status.Should().Be(QueryStatus.Closed);
        updatedQ2.Resolver.Should().Be("cra-user-01");
        updatedQ2.ReasonForChange.Should().Be("Batch closing verified answered queries");
    }

    /// <summary>
    /// Validates that batch cancel updates all requested queries atomically for an authorized CRA or DM.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task BatchCancelAsync_WithAuthorizedRole_CancelsAllSelectedQueriesAtomically()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var auditAccessor = new FakeCurrentAuditContextAccessor("dm-user-01", "Batch cancelling obsolete queries");
        var service = new ClinicalQueryApplicationService(repo, auditAccessor, new FakeClock());

        var q1 = ClinicalQuery.Raise(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), new TestCode("SYSBP"), "Obsolete 1").Value!;
        var q2 = ClinicalQuery.Raise(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), new TestCode("DIABP"), "Obsolete 2").Value!;

        await repo.AddAsync(q1, CancellationToken.None);
        await repo.AddAsync(q2, CancellationToken.None);

        var request = new BatchQueryRequest(new[] { q1.Id.ToString(), q2.Id.ToString() }, "Obsolete queries raised in error");
        var result = await service.BatchCancelAsync(request, CancellationToken.None, userRole: "Data Manager");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(2);

        var updatedQ1 = await repo.GetByIdAsync(q1.Id, CancellationToken.None);
        var updatedQ2 = await repo.GetByIdAsync(q2.Id, CancellationToken.None);

        updatedQ1!.Status.Should().Be(QueryStatus.Cancelled);
        updatedQ1.CancellationReason.Should().Be("Obsolete queries raised in error");

        updatedQ2!.Status.Should().Be(QueryStatus.Cancelled);
        updatedQ2.CancellationReason.Should().Be("Obsolete queries raised in error");
    }

    /// <summary>
    /// Validates that unauthorized roles (CRC, Auditor, empty) are rejected with HTTP 403 Forbidden.
    /// </summary>
    [Theory]
    [InlineData("crc")]
    [InlineData("auditor")]
    [InlineData("")]
    public async Task BatchQueryActions_WithUnauthorizedRole_ReturnsForbidden(string role)
    {
        var repo = new InMemoryClinicalQueryRepository();
        var auditAccessor = new FakeCurrentAuditContextAccessor("site-crc", "Testing authorization");
        var service = new ClinicalQueryApplicationService(repo, auditAccessor, new FakeClock());

        var q1 = ClinicalQuery.Raise(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), new TestCode("SYSBP"), "Check").Value!;
        q1.Answer("Answered", "crc");
        await repo.AddAsync(q1, CancellationToken.None);

        var request = new BatchQueryRequest(new[] { q1.Id.ToString() }, "Attempt action");

        var closeResult = await service.BatchCloseAsync(request, CancellationToken.None, userRole: role);
        closeResult.IsFailure.Should().BeTrue();
        closeResult.Error!.Code.Should().Be("UNAUTHORIZED_QUERY_ROLE");

        var cancelResult = await service.BatchCancelAsync(request, CancellationToken.None, userRole: role);
        cancelResult.IsFailure.Should().BeTrue();
        cancelResult.Error!.Code.Should().Be("UNAUTHORIZED_QUERY_ROLE");
    }

    /// <summary>
    /// Validates that batch actions exceeding 100 queries are rejected with BATCH_CAP_EXCEEDED.
    /// </summary>
    [Fact]
    public async Task BatchQueryActions_ExceedingLimit_ReturnsValidationError()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var service = new ClinicalQueryApplicationService(repo, new FakeCurrentAuditContextAccessor("cra-user", "Limit test"), new FakeClock());

        var ids = new string[101];
        for (int i = 0; i < 101; i++)
        {
            ids[i] = Guid.NewGuid().ToString();
        }

        var request = new BatchQueryRequest(ids, "Exceeding batch limit");
        var result = await service.BatchCloseAsync(request, CancellationToken.None, userRole: "cra");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("BATCH_CAP_EXCEEDED");
    }

    /// <summary>
    /// Validates fail-closed behavior when any single query in the batch fails state transition or validation.
    /// </summary>
    [Fact]
    public async Task BatchCloseAsync_WhenSingleQueryIneligible_FailsClosed()
    {
        var repo = new InMemoryClinicalQueryRepository();
        var service = new ClinicalQueryApplicationService(repo, new FakeCurrentAuditContextAccessor("cra-user", "Fail-closed test"), new FakeClock());

        var q1 = ClinicalQuery.Raise(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), new TestCode("SYSBP"), "Valid").Value!;
        q1.Answer("Answer 1", "crc-01");

        var q2 = ClinicalQuery.Raise(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), new TestCode("DIABP"), "Already Closed").Value!;
        q2.Answer("Answer 2", "crc-01");
        q2.Close("cra-01", FakeClock.DefaultStart); // q2 is already Closed!

        await repo.AddAsync(q1, CancellationToken.None);
        await repo.AddAsync(q2, CancellationToken.None);

        var request = new BatchQueryRequest(new[] { q1.Id.ToString(), q2.Id.ToString() }, "Batch closing queries");
        var result = await service.BatchCloseAsync(request, CancellationToken.None, userRole: "cra");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_QUERY_TRANSITION");

        // Verify q1 was not closed (atomic rollback / fail-closed)
        var q1Check = await repo.GetByIdAsync(q1.Id, CancellationToken.None);
        q1Check!.Status.Should().Be(QueryStatus.Answered);
    }
}
