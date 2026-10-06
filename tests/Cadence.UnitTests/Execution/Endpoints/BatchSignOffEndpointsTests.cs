using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Core.Domain;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Unit tests for multi-subject batch PI electronic sign-off endpoint handlers and application service logic.
/// @req:PRD-SYS-002
/// @req:SRS-EDC-003
/// </summary>
public sealed class BatchSignOffEndpointsTests
{
    private static DefaultHttpContext CreateHttpContext(string role = "pi", string username = "dr_investigator")
    {
        return new DefaultHttpContext { User = TestSecurityContext.CreatePrincipal(username, role) };
    }

    private static FormSubmissionApplicationService CreateService(InMemoryFormSubmissionRepository repo, FakeClock clock)
    {
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor();
        return new FormSubmissionApplicationService(
            formSubmissionRepository: repo,
            observationRepository: obsRepo,
            ucumService: ucum,
            auditContextAccessor: audit,
            clock: clock);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-002")]
    public async Task BatchSignOff_SubjectLevel_ApprovesCompletedSubmissionsAndReturnsSummary()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var svc = CreateService(repo, clock);

        var createReq1 = new FormSubmissionCreateRequest("STUDY-001", "SUBJ-101", "FORM-VS", "SITE-01", "VISIT-01", "1.0.0", "{\"vssbp\": 120, \"observation_date\": \"2026-10-06T10:00:00Z\"}");
        var createReq2 = new FormSubmissionCreateRequest("STUDY-001", "SUBJ-102", "FORM-VS", "SITE-01", "VISIT-01", "1.0.0", "{\"vssbp\": 118, \"observation_date\": \"2026-10-06T10:00:00Z\"}");

        var sub1 = (await svc.CreateAsync(createReq1, CancellationToken.None)).Value!;
        var sub2 = (await svc.CreateAsync(createReq2, CancellationToken.None)).Value!;

        await svc.CompleteAsync(sub1.Id, CancellationToken.None);
        await svc.CompleteAsync(sub2.Id, CancellationToken.None);

        var batchReq = new BatchSignOffRequest(
            StudyId: "STUDY-001",
            TargetType: "SUBJECT",
            TargetIds: new[] { "SUBJ-101", "SUBJ-102" },
            SigningReason: "Principal Investigator Approval");

        var httpContext = CreateHttpContext(role: "pi");

        var result = await FormEndpoints.BatchSignOffAsync(batchReq, httpContext, svc, CancellationToken.None);

        result.Should().BeOfType<Ok<BatchSignOffResponse>>();
        var okResult = (Ok<BatchSignOffResponse>)result;
        var response = okResult.Value!;

        response.Status.Should().Be("success");
        response.ApprovedSubmissionIds.Should().HaveCount(2);
        response.ApprovedSubmissionIds.Should().Contain(sub1.Id);
        response.ApprovedSubmissionIds.Should().Contain(sub2.Id);
        response.SignedFormsCount.Should().Be(2);
        response.SignatureId.Should().StartWith("CERT-SIG-");
        response.ContentDigest.Should().StartWith("sha256-");
        response.AuditTx.Should().StartWith("AUD-TX-");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-002")]
    public async Task BatchSignOff_NonPiRole_RejectsWithForbidden()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var svc = CreateService(repo, clock);

        var batchReq = new BatchSignOffRequest(
            StudyId: "STUDY-001",
            TargetType: "SUBJECT",
            TargetIds: new[] { "SUBJ-101" },
            SigningReason: "Principal Investigator Approval");

        var httpContext = CreateHttpContext(role: "crc");

        var result = await FormEndpoints.BatchSignOffAsync(batchReq, httpContext, svc, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-002")]
    public async Task BatchSignOff_Exceeds100SubjectsCap_ReturnsBadRequest()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var svc = CreateService(repo, clock);

        var targets = new List<string>();
        for (int i = 0; i < 101; i++)
        {
            targets.Add($"SUBJ-{i:D3}");
        }

        var batchReq = new BatchSignOffRequest(
            StudyId: "STUDY-001",
            TargetType: "SUBJECT",
            TargetIds: targets,
            SigningReason: "Principal Investigator Approval");

        var httpContext = CreateHttpContext(role: "pi");

        var result = await FormEndpoints.BatchSignOffAsync(batchReq, httpContext, svc, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
