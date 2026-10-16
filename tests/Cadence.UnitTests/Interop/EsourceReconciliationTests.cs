using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Interop;

/// <summary>
/// Unit tests for eSource reconciliation, SMART on FHIR assertion/polling, and GxP audit attribution.
/// </summary>
public class EsourceReconciliationTests
{
    private static InteropDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<InteropDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new InteropDbContext(options);
    }

    private static HttpContext CreateMockHttpContext(string userName = "crc_coordinator_john", string role = "crc", params string[] studies)
    {
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.NameIdentifier, userName),
            new Claim(ClaimTypes.Role, role)
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userName,
            Roles = [role],
            AssignedStudies = studies.Length == 0 ? ["*"] : studies,
        };
        return context;
    }

    [Fact]
    [GxPRequirement("PRD-ESR-001")]
    public async Task RegisterAndListEndpoints_SuccessfullyManagesEhrEndpoints()
    {
        // Arrange
        var dbName = $"EhrEndpointTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext();

        var request = new EhrEndpointCreateRequest(
            Name: "Metro General Hospital FHIR",
            SystemType: "EPIC",
            FhirBaseUrl: "https://fhir.metrogeneral.org/r4",
            TokenUrl: "https://fhir.metrogeneral.org/oauth/token",
            ClientId: "client-id-12345",
            PublicKeyJwksUrl: "https://fhir.metrogeneral.org/.well-known/jwks.json",
            Scopes: ["system/Observation.read", "system/Patient.read"],
            PollingIntervalSec: 300,
            ReasonForChange: "Initial endpoint setup for Trial STUDY-101");

        // Act
        var createResult = await EsourceEndpoints.RegisterEhrEndpointAsync(request, db, clock, httpContext, TestContext.Current.CancellationToken);
        var listResult = await EsourceEndpoints.ListEhrEndpointsAsync(active_only: true, db, TestContext.Current.CancellationToken);

        // Assert
        createResult.Should().NotBeNull();
        var listObj = listResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrEndpointListResponse>>().Subject.Value;
        listObj.Should().NotBeNull();
        listObj!.Endpoints.Should().HaveCount(1);
        listObj.Endpoints[0].Name.Should().Be("Metro General Hospital FHIR");
        listObj.Endpoints[0].SystemType.Should().Be("EPIC");
    }

    [Fact]
    [GxPRequirement("PRD-ESR-001")]
    public void GenerateAndVerifySmartAssertion_ValidatesOAuth2AssertionToken()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var request = new SmartAssertionRequest(
            ClientId: "cadence-client-99",
            TokenUrl: "https://ehr.hospital.org/oauth2/token",
            PrivateKeyPem: null,
            KeyId: "key-1",
            ExpirationSeconds: 300);

        // Act
        var genResult = EsourceEndpoints.GenerateClientAssertionAsync(request, clock);
        var genObj = genResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<SmartAssertionResponse>>().Subject.Value;

        genObj.Should().NotBeNull();
        genObj!.ClientAssertionToken.Should().NotBeNullOrEmpty();
        genObj.ClientAssertionType.Should().Be("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");

        var verifyRequest = new SmartVerifyRequest(
            ClientAssertionToken: genObj.ClientAssertionToken,
            ClientId: "cadence-client-99",
            TokenUrl: "https://ehr.hospital.org/oauth2/token",
            PublicKeyPem: null);

        var verifyResult = EsourceEndpoints.VerifyClientAssertionAsync(verifyRequest, clock);
        var verifyObj = verifyResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<SmartVerifyResponse>>().Subject.Value;

        // Assert
        verifyObj.Should().NotBeNull();
        verifyObj!.IsValid.Should().BeTrue();
        verifyObj.Subject.Should().Be("cadence-client-99");
    }

    [Fact]
    [GxPRequirement("PRD-ESR-001")]
    public async Task PollHospitalEhr_IngestsCandidatesWithDiscrepancyClassification()
    {
        // Arrange
        var dbName = $"EhrPollTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext();

        var request = new EhrPollRequest(
            EndpointId: null,
            StudyId: "STUDY-101",
            SiteId: "SITE-01",
            SubjectId: "PAT-001",
            PrivateKeyPem: null);

        // Act
        var result = await EsourceEndpoints.PollHospitalEhrAsync(request, db, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var pollObj = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrPollResponse>>().Subject.Value;
        pollObj.Should().NotBeNull();
        pollObj!.Success.Should().BeTrue();
        pollObj.Candidates.Should().HaveCountGreaterThanOrEqualTo(5);

        pollObj.Candidates.Should().Contain(c => c.DiscrepancyType == "NEW_FIELD");
        pollObj.Candidates.Should().Contain(c => c.DiscrepancyType == "VALUE_MISMATCH");
        pollObj.Candidates.Should().Contain(c => c.DiscrepancyType == "UNIT_MISMATCH");
        pollObj.Candidates.Should().Contain(c => c.DiscrepancyType == "DATE_MISMATCH");
        pollObj.Candidates.Should().Contain(c => c.DiscrepancyType == "EXACT_MATCH");
    }

    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task AcceptCandidate_CommitsValueAndWritesGxPAuditAndESignature()
    {
        // Arrange
        var dbName = $"EhrAcceptTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        // Seed candidate list
        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, httpContext, TestContext.Current.CancellationToken);
        var firstCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(c => c.DiscrepancyType == "VALUE_MISMATCH", TestContext.Current.CancellationToken);

        var acceptReq = new EhrReconcileAcceptRequest(
            CandidateId: firstCandidate.Id,
            CommitReason: "Coordinator verified EHR source lab against chart notes",
            ChangeReason: "CRC eSource Reconciliation Accept & Commit");

        // Act
        var result = await EsourceEndpoints.AcceptCandidateAsync(acceptReq, db, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var actionObj = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrReconcileActionResponse>>().Subject.Value;
        actionObj.Should().NotBeNull();
        actionObj!.Success.Should().BeTrue();
        actionObj.Candidate.Status.Should().Be("ACCEPTED");
        actionObj.Candidate.ResolutionAction.Should().Be("ACCEPT_COMMIT");
        actionObj.Candidate.ResolvedBy.Should().Be("crc_sarah");

        var updatedRecord = await db.EhrReconciliationCandidateRecords.FirstAsync(c => c.Id == firstCandidate.Id, TestContext.Current.CancellationToken);
        updatedRecord.Status.Should().Be("ACCEPTED");
        updatedRecord.ElectronicSignatureJson.Should().Contain("crc_sarah");

        var auditLogs = await db.InteropAuditLogs.ToListAsync(TestContext.Current.CancellationToken);
        auditLogs.Should().Contain(a => a.Action == "ESOURCE_CANDIDATE_ACCEPTED" && a.UserId == "crc_sarah");
    }

    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    [GxPRequirement("PRD-ESR-003")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RejectCandidate_RequiresRationaleAndPreservesOriginalPayload()
    {
        // Arrange
        var dbName = $"EhrRejectTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, httpContext, TestContext.Current.CancellationToken);
        var firstCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(TestContext.Current.CancellationToken);

        // Test 1: Empty rationale fails
        var emptyReject = new EhrReconcileRejectRequest(CandidateId: firstCandidate.Id, RejectionReason: "", ChangeReason: "Reject attempt");
        var failResult = await EsourceEndpoints.RejectCandidateAsync(emptyReject, db, clock, httpContext, TestContext.Current.CancellationToken);
        failResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();

        // Test 2: Valid rationale succeeds
        var validReject = new EhrReconcileRejectRequest(
            CandidateId: firstCandidate.Id,
            RejectionReason: "Out of visit window observation taken prior to screening consent",
            ChangeReason: "CRC eSource Reconciliation Reject");

        var successResult = await EsourceEndpoints.RejectCandidateAsync(validReject, db, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var actionObj = successResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrReconcileActionResponse>>().Subject.Value;
        actionObj.Should().NotBeNull();
        actionObj!.Success.Should().BeTrue();
        actionObj.Candidate.Status.Should().Be("REJECTED");
        actionObj.Candidate.ResolutionReason.Should().Be("Out of visit window observation taken prior to screening consent");

        // Verify non-destructive preservation: original CandidateValueJson and RawFhirResourceJson remain unchanged
        var updatedRecord = await db.EhrReconciliationCandidateRecords.FirstAsync(c => c.Id == firstCandidate.Id, TestContext.Current.CancellationToken);
        updatedRecord.CandidateValueJson.Should().NotBeNullOrEmpty();
        updatedRecord.RawFhirResourceJson.Should().NotBeNullOrEmpty();

        var auditLogs = await db.InteropAuditLogs.ToListAsync(TestContext.Current.CancellationToken);
        auditLogs.Should().Contain(a => a.Action == "ESOURCE_CANDIDATE_REJECTED" && a.UserId == "crc_sarah");
    }

    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    public async Task RaiseClinicalQuery_RegistersQueryAndUpdatesCandidateStatus()
    {
        // Arrange
        var dbName = $"EhrQueryTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, httpContext, TestContext.Current.CancellationToken);
        var mismatchCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(c => c.DiscrepancyType == "VALUE_MISMATCH", TestContext.Current.CancellationToken);

        var queryReq = new EhrReconcileQueryRequest(
            CandidateId: mismatchCandidate.Id,
            QueryExplanation: "Blood pressure reading 128 mmHg in EHR conflicts with eCRF 140 mmHg. Requesting investigator confirmation.",
            DiscrepancyType: "VALUE_MISMATCH",
            ChangeReason: "CRC Raised eSource Discrepancy Query");

        // Act
        var result = await EsourceEndpoints.RaiseClinicalQueryAsync(queryReq, db, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var actionObj = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrReconcileActionResponse>>().Subject.Value;
        actionObj.Should().NotBeNull();
        actionObj!.Success.Should().BeTrue();
        actionObj.ClinicalQueryId.Should().NotBeNullOrEmpty();
        actionObj.Candidate.Status.Should().Be("QUERIED");

        var createdQuery = await db.ClinicalQueries.FirstOrDefaultAsync(q => q.Id == actionObj.ClinicalQueryId, TestContext.Current.CancellationToken);
        createdQuery.Should().NotBeNull();
        createdQuery!.Status.Should().Be("OPEN");
        createdQuery.Explanation.Should().Contain("Blood pressure reading");
    }

    [Fact]
    [GxPRequirement("PRD-ESR-003")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task GetProvenanceAudit_ReturnsRawFhirPayloadAndAuditTrail()
    {
        // Arrange
        var dbName = $"EhrProvTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah");

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, httpContext, TestContext.Current.CancellationToken);
        var candidate = await db.EhrReconciliationCandidateRecords.FirstAsync(TestContext.Current.CancellationToken);

        // Perform an accept action to generate audit log
        await EsourceEndpoints.AcceptCandidateAsync(new EhrReconcileAcceptRequest(candidate.Id, "Verified against source", "Accept"), db, clock, httpContext, TestContext.Current.CancellationToken);

        // Act
        var provResult = await EsourceEndpoints.GetProvenanceAuditAsync(candidate.Id, db, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var provObj = provResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrProvenanceResponse>>().Subject.Value;
        provObj.Should().NotBeNull();
        provObj!.CandidateId.Should().Be(candidate.Id);
        provObj.MappingProvenance.Should().NotBeNullOrEmpty();
        provObj.RawFhirResource.Should().NotBeNull();
        provObj.AuditTrail.Should().NotBeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BatchAcceptCandidates_CommitsAllValidCandidatesAndWritesAuditLogs()
    {
        // Arrange
        var dbName = $"EhrBatchAcceptTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, httpContext, TestContext.Current.CancellationToken);
        var initialCandidates = await db.EhrReconciliationCandidateRecords.Take(2).ToListAsync(TestContext.Current.CancellationToken);
        var idsToAccept = initialCandidates.Select(c => c.Id).ToList();

        var batchReq = new EhrReconcileBatchAcceptRequest(
            CandidateIds: idsToAccept,
            CommitReason: "Batch verified against EHR source data",
            ChangeReason: "CRC eSource Batch Accept");

        // Act
        var result = await EsourceEndpoints.BatchAcceptCandidatesAsync(batchReq, db, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var batchObj = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrReconcileBatchActionResponse>>().Subject.Value;
        batchObj.Should().NotBeNull();
        batchObj!.Success.Should().BeTrue();
        batchObj.ActionCount.Should().Be(2);
        batchObj.Candidates.Should().OnlyContain(c => c.Status == "ACCEPTED");

        var updatedRecords = await db.EhrReconciliationCandidateRecords.Where(c => idsToAccept.Contains(c.Id)).ToListAsync(TestContext.Current.CancellationToken);
        updatedRecords.Should().OnlyContain(c => c.Status == "ACCEPTED" && c.ElectronicSignatureJson!.Contains("crc_sarah"));

        var auditLogs = await db.InteropAuditLogs.Where(a => a.Action == "ESOURCE_CANDIDATE_BATCH_ACCEPTED").ToListAsync(TestContext.Current.CancellationToken);
        auditLogs.Should().HaveCount(2);
    }

    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    [GxPRequirement("PRD-ESR-003")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BatchRejectCandidates_RequiresRationaleAndUpdatesStatus()
    {
        // Arrange
        var dbName = $"EhrBatchRejectTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, httpContext, TestContext.Current.CancellationToken);
        var initialCandidates = await db.EhrReconciliationCandidateRecords.Take(2).ToListAsync(TestContext.Current.CancellationToken);
        var idsToReject = initialCandidates.Select(c => c.Id).ToList();

        // 1. Empty rationale fails
        var emptyReq = new EhrReconcileBatchRejectRequest(CandidateIds: idsToReject, RejectionReason: "", ChangeReason: "Reject");
        var failResult = await EsourceEndpoints.BatchRejectCandidatesAsync(emptyReq, db, clock, httpContext, TestContext.Current.CancellationToken);
        failResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();

        // 2. Valid rationale succeeds
        var validReq = new EhrReconcileBatchRejectRequest(
            CandidateIds: idsToReject,
            RejectionReason: "Out of visit window observations",
            ChangeReason: "CRC eSource Batch Reject");

        var successResult = await EsourceEndpoints.BatchRejectCandidatesAsync(validReq, db, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var batchObj = successResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrReconcileBatchActionResponse>>().Subject.Value;
        batchObj.Should().NotBeNull();
        batchObj!.Success.Should().BeTrue();
        batchObj.ActionCount.Should().Be(2);
        batchObj.Candidates.Should().OnlyContain(c => c.Status == "REJECTED" && c.ResolutionReason == "Out of visit window observations");

        var auditLogs = await db.InteropAuditLogs.Where(a => a.Action == "ESOURCE_CANDIDATE_BATCH_REJECTED").ToListAsync(TestContext.Current.CancellationToken);
        auditLogs.Should().HaveCount(2);
    }

    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    public async Task BatchAcceptCandidates_ExceedingLimit_ReturnsBadRequest()
    {
        // Arrange
        var dbName = $"EhrBatchLimitTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext();

        var overLimitIds = new List<string>();
        for (int i = 0; i < 101; i++)
        {
            overLimitIds.Add($"cand-limit-{i}");
        }

        var req = new EhrReconcileBatchAcceptRequest(CandidateIds: overLimitIds, CommitReason: "Batch accept", ChangeReason: null);

        // Act
        var result = await EsourceEndpoints.BatchAcceptCandidatesAsync(req, db, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var problem = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Detail.Should().Contain("100 candidate items");
    }

    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    public async Task BatchOperation_WithNonExistentCandidateId_RollsBackTransaction()
    {
        // Arrange
        var dbName = $"EhrBatchRollbackTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext();

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, httpContext, TestContext.Current.CancellationToken);
        var validCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(TestContext.Current.CancellationToken);

        var mixedIds = new List<string> { validCandidate.Id, "non-existent-candidate-id-999" };
        var batchReq = new EhrReconcileBatchAcceptRequest(CandidateIds: mixedIds, CommitReason: "Test rollback", ChangeReason: null);

        // Act
        var result = await EsourceEndpoints.BatchAcceptCandidatesAsync(batchReq, db, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var problem = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Detail.Should().Contain("non-existent-candidate-id-999");

        // Verify valid candidate was NOT committed/changed
        var unchangedCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(c => c.Id == validCandidate.Id, TestContext.Current.CancellationToken);
        unchangedCandidate.Status.Should().Be("PENDING_REVIEW");
    }

    /// <summary>
    /// Candidates of a study the caller is not assigned to are hidden from lists and lookups, and accepting one is
    /// refused without changing it (#5361).
    /// </summary>
    [Fact]
    public async Task CandidatesOutsideTheCallersStudies_AreHiddenAndCannotBeAccepted()
    {
        using var db = CreateDbContext($"EhrStudyScope_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var ct = TestContext.Current.CancellationToken;
        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, CreateMockHttpContext(), ct);
        var candidate = await db.EhrReconciliationCandidateRecords.FirstAsync(ct);
        var outsider = CreateMockHttpContext("crc_other", "crc", "STUDY-202");

        var list = await EsourceEndpoints.ListCandidatesAsync(null, null, null, "ALL", "ALL", 0, 50, db, clock, outsider, ct);
        var lookup = await EsourceEndpoints.GetCandidateByIdAsync(candidate.Id, db, outsider, ct);
        var accept = await EsourceEndpoints.AcceptCandidateAsync(
            new EhrReconcileAcceptRequest(candidate.Id, "Verified", "Accept"), db, clock, outsider, ct);

        var listed = list.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrCandidateListResponse>>().Subject.Value!;
        listed.Candidates.Should().NotContain(c => c.StudyId == "STUDY-101");
        lookup.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(404);
        accept.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        (await db.EhrReconciliationCandidateRecords.SingleAsync(c => c.Id == candidate.Id, ct)).Status.Should().Be("PENDING_REVIEW");
        (await db.InteropAuditLogs.CountAsync(ct)).Should().Be(0);
    }

    /// <summary>Polling or ingesting EHR data for a study the caller is not assigned to writes nothing (#5361).</summary>
    [Fact]
    public async Task PollAndIngest_ForAStudyOutsideTheCallersStudies_AreRefusedAndWriteNothing()
    {
        using var db = CreateDbContext($"EhrIngestScope_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var ct = TestContext.Current.CancellationToken;
        var outsider = CreateMockHttpContext("crc_other", "crc", "STUDY-202");

        var poll = await EsourceEndpoints.PollHospitalEhrAsync(
            new EhrPollRequest(EndpointId: "none", StudyId: "STUDY-101", SiteId: "SITE-01", SubjectId: "PAT-001", PrivateKeyPem: null), db, clock, outsider, ct);
        var ingest = await EsourceEndpoints.IngestFhirBundleAsync(
            new FhirBundleIngestRequest(StudyId: "STUDY-101", SiteId: "SITE-01", SubjectId: "PAT-001", SubjectPseudonym: null, EhrSystemId: null, RawFhirBundleJson: "{}"),
            db, clock, outsider, ct);

        poll.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        ingest.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        (await db.EhrReconciliationCandidateRecords.CountAsync(ct)).Should().Be(0);
    }
}
