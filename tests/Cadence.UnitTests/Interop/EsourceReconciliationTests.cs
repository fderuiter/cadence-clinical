using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
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

    private static HttpContext CreateMockHttpContext(string userName = "crc_coordinator_john", string role = "Clinical Research Coordinator")
    {
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.NameIdentifier, userName),
            new Claim(ClaimTypes.Role, role)
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
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
        var httpContext = CreateMockHttpContext("crc_sarah", "Clinical Research Coordinator");

        // Seed candidate list
        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, TestContext.Current.CancellationToken);
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
    [GxPRequirement("PRD-ESR-001")]
    [GxPRequirement("PRD-ESR-002")]
    public async Task IngestFhirBundle_TransformsLoincAndNormalizesUcumUnits()
    {
        // Arrange
        var dbName = $"FhirIngestTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        using var execDb = CreateExecutionDbContext(dbName + "_exec");
        var ucum = new UcumNormalizationService();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        // Seed an existing observation in execDb to test VALUE_MISMATCH
        var seedObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-101"),
            subjectId: new SubjectCode("PAT-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            observationDate: new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            value: 120,
            unit: "mmHg",
            siteId: new SiteCode("SITE-01"),
            normalizedValue: 120,
            normalizedUnit: "mmHg");
        execDb.ClinicalObservations.Add(seedObs.Value!);
        await execDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rawFhirBundle = """
        {
          "resourceType": "Bundle",
          "entry": [
            {
              "resource": {
                "resourceType": "Observation",
                "id": "obs-sysbp-001",
                "effectiveDateTime": "2026-10-01T12:00:00Z",
                "code": {
                  "coding": [
                    { "system": "http://loinc.org", "code": "8480-6", "display": "Systolic blood pressure" }
                  ]
                },
                "valueQuantity": { "value": 128, "unit": "mmHg" }
              }
            },
            {
              "resource": {
                "resourceType": "Observation",
                "id": "obs-pulse-002",
                "effectiveDateTime": "2026-10-01T12:00:00Z",
                "code": {
                  "coding": [
                    { "system": "http://loinc.org", "code": "8867-4", "display": "Heart rate" }
                  ]
                },
                "valueQuantity": { "value": 72, "unit": "/min" }
              }
            }
          ]
        }
        """;

        var request = new FhirBundleIngestRequest(
            StudyId: "STUDY-101",
            SiteId: "SITE-01",
            SubjectId: "PAT-001",
            SubjectPseudonym: "PAT-001",
            EhrSystemId: "EHR-HOSPITAL-IMPORT",
            RawFhirBundleJson: rawFhirBundle);

        // Act
        var result = await EsourceEndpoints.IngestFhirBundleAsync(request, db, clock, TestContext.Current.CancellationToken, execDb, ucum);

        // Assert
        var ingestObj = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<FhirBundleIngestResponse>>().Subject.Value;
        ingestObj.Should().NotBeNull();
        ingestObj!.Success.Should().BeTrue();
        ingestObj.CandidateCount.Should().Be(2);

        var candidates = await db.EhrReconciliationCandidateRecords.ToListAsync(TestContext.Current.CancellationToken);
        candidates.Should().HaveCount(2);

        var sysbpCand = candidates.First(c => c.CdashTestcd == "SYSBP");
        sysbpCand.TargetDomain.Should().Be("VS");
        sysbpCand.DiscrepancyType.Should().Be("VALUE_MISMATCH");
        sysbpCand.CandidateValueJson.Should().Be("128");

        var pulseCand = candidates.First(c => c.CdashTestcd == "PULSE");
        pulseCand.TargetDomain.Should().Be("VS");
        pulseCand.DiscrepancyType.Should().Be("NEW_FIELD");
    }

    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    public async Task AcceptCandidate_SynchronizesEcrfInExecutionDbContext()
    {
        // Arrange
        var dbName = $"EhrAcceptExecSync_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        using var execDb = CreateExecutionDbContext(dbName + "_exec");
        var ucum = new UcumNormalizationService();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "Clinical Research Coordinator");

        // Seed a candidate record
        var cand = new EhrReconciliationCandidateRecord
        {
            Id = "cand-pulse-1",
            StudyId = "STUDY-101",
            SiteId = "SITE-01",
            SubjectId = "PAT-001",
            SubjectPseudonym = "PAT-001",
            EhrSystemId = "EHR-HOSPITAL-MAIN",
            FhirResourceType = "Observation",
            FhirResourceId = "obs-pulse-001",
            SourceTimestamp = clock.UtcNow,
            TargetDomain = "VS",
            TargetVariable = "PULSE",
            CdashTestcd = "PULSE",
            CdashTest = "Pulse Rate",
            CandidateValueJson = "75",
            CandidateUnit = "beats/min",
            DiscrepancyType = "NEW_FIELD",
            ConfidenceScore = 0.95,
            MappingProvenance = "LOINC 8867-4 -> VS.PULSE",
            RawFhirResourceJson = "{}",
            Status = "PENDING_REVIEW",
            CreatedAt = clock.UtcNow
        };
        db.EhrReconciliationCandidateRecords.Add(cand);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var acceptReq = new EhrReconcileAcceptRequest(
            CandidateId: cand.Id,
            CommitReason: "Accepted source pulse rate",
            ChangeReason: "CRC Accept");

        // Act
        var result = await EsourceEndpoints.AcceptCandidateAsync(acceptReq, db, clock, httpContext, TestContext.Current.CancellationToken, execDb, ucum);

        // Assert
        var actionObj = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrReconcileActionResponse>>().Subject.Value;
        actionObj!.Success.Should().BeTrue();

        var obs = await execDb.ClinicalObservations.FirstOrDefaultAsync(o => o.TestCode.Value == "PULSE", TestContext.Current.CancellationToken);
        obs.Should().NotBeNull();
        obs!.Value.Should().Be(75);
        obs.Unit.Should().Be("beats/min");
    }

    private static ExecutionDbContext CreateExecutionDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ExecutionDbContext(options);
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
        var httpContext = CreateMockHttpContext("crc_sarah", "Clinical Research Coordinator");

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, TestContext.Current.CancellationToken);
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
        var httpContext = CreateMockHttpContext("crc_sarah", "Clinical Research Coordinator");

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, TestContext.Current.CancellationToken);
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

        await EsourceEndpoints.ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, db, clock, TestContext.Current.CancellationToken);
        var candidate = await db.EhrReconciliationCandidateRecords.FirstAsync(TestContext.Current.CancellationToken);

        // Perform an accept action to generate audit log
        await EsourceEndpoints.AcceptCandidateAsync(new EhrReconcileAcceptRequest(candidate.Id, "Verified against source", "Accept"), db, clock, httpContext, TestContext.Current.CancellationToken);

        // Act
        var provResult = await EsourceEndpoints.GetProvenanceAuditAsync(candidate.Id, db, TestContext.Current.CancellationToken);

        // Assert
        var provObj = provResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrProvenanceResponse>>().Subject.Value;
        provObj.Should().NotBeNull();
        provObj!.CandidateId.Should().Be(candidate.Id);
        provObj.MappingProvenance.Should().NotBeNullOrEmpty();
        provObj.RawFhirResource.Should().NotBeNull();
        provObj.AuditTrail.Should().NotBeEmpty();
    }
}
