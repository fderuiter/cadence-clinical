using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Cadence.UnitTests.Interop;

/// <summary>
/// Unit tests for eSource reconciliation, SMART on FHIR assertion/polling, and GxP audit attribution. They run on the
/// in-memory provider, so they are not the evidence for PRD-ESR-002/003: <c>EsourceReconciliationPostgresTests</c> runs
/// the same routes on PostgreSQL (#5383, #5324).
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
            AssignedSites = string.Equals(role, "crc", StringComparison.OrdinalIgnoreCase) ? ["SITE-01"] : [],
        };
        return context;
    }

    /// <summary>
    /// A FHIR bundle with a systolic blood pressure and a glucose Observation, plus one Observation without a supported
    /// code that ingestion skips.
    /// </summary>
    private const string SampleBundle = """
        {
          "resourceType": "Bundle",
          "type": "collection",
          "entry": [
            { "resource": { "resourceType": "Patient", "id": "pat-1" } },
            { "resource": { "resourceType": "Observation", "id": "obs-bp-1", "status": "final",
                "code": { "coding": [ { "system": "http://loinc.org", "code": "8480-6", "display": "Systolic blood pressure" } ] },
                "effectiveDateTime": "2026-09-30T08:15:00Z",
                "valueQuantity": { "value": 128, "unit": "mmHg", "system": "http://unitsofmeasure.org", "code": "mm[Hg]" } } },
            { "resource": { "resourceType": "Observation", "id": "obs-gluc-1", "status": "final",
                "code": { "coding": [ { "system": "http://loinc.org", "code": "2339-0" } ] },
                "effectiveDateTime": "2026-09-30T07:00:00Z",
                "valueQuantity": { "value": 105, "unit": "mg/dL" } } },
            { "resource": { "resourceType": "Observation", "id": "obs-other-1", "status": "final",
                "code": { "coding": [ { "system": "http://loinc.org", "code": "0000-0" } ] },
                "effectiveDateTime": "2026-09-30T07:00:00Z",
                "valueQuantity": { "value": 1 } } }
          ]
        }
        """;

    private static EsourceConfigurationService ConfigService(InteropDbContext db, IClock clock, ISmartSigningKeys? keys = null) =>
        new(new InteropUnitOfWork(db), clock, keys);

    private static EsourceIngestionAndQueryService IngestService(InteropDbContext db, IClock? clock = null) =>
        new(new InteropUnitOfWork(db), clock ?? SystemClock.Instance);

    private static EsourceReconciliationService ReconcileService(InteropDbContext db, IClock clock) =>
        new(new InteropUnitOfWork(db), clock);

    private static async Task IngestSampleBundleAsync(InteropDbContext db, FakeClock clock, HttpContext httpContext, CancellationToken ct)
    {
        var result = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-101", "SITE-01", "PAT-001", null, "EPIC", SampleBundle), httpContext, ct);
        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<FhirBundleIngestResponse>>();
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
        var configService = ConfigService(db, clock);
        var createResult = await configService.RegisterEhrEndpointAsync(request, httpContext, TestContext.Current.CancellationToken);
        var listResult = await configService.ListEhrEndpointsAsync(activeOnly: true, TestContext.Current.CancellationToken);

        // Assert
        createResult.Should().NotBeNull();
        var listObj = listResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrEndpointListResponse>>().Subject.Value;
        listObj.Should().NotBeNull();
        listObj!.Endpoints.Should().HaveCount(1);
        listObj.Endpoints[0].Name.Should().Be("Metro General Hospital FHIR");
        listObj.Endpoints[0].SystemType.Should().Be("EPIC");
    }

    private const string TokenUrl = "https://ehr.hospital.org/oauth2/token";

    private static ConfigurationSmartSigningKeys Keys(params (string Ref, string Pem)[] keys) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(keys.ToDictionary(k => $"{ConfigurationSmartSigningKeys.SectionName}:{k.Ref}", k => (string?)k.Pem))
            .Build());

    private static async Task<string> RegisterSmartEndpointAsync(InteropDbContext db, FakeClock clock, string? keyRef)
    {
        var created = await ConfigService(db, clock).RegisterEhrEndpointAsync(
            new EhrEndpointCreateRequest(
                Name: "Metro General", SystemType: "EPIC", FhirBaseUrl: "https://ehr.hospital.org/r4", TokenUrl: TokenUrl,
                ClientId: "cadence-client-99", PublicKeyJwksUrl: null, Scopes: null, PollingIntervalSec: 300,
                ReasonForChange: "SMART endpoint", SigningKeyRef: keyRef, SigningKeyId: "key-1"),
            CreateMockHttpContext(), TestContext.Current.CancellationToken);
        return created.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<Cadence.Apps.Interop.Domain.EhrEndpointRecord>>().Subject.Value!.Id;
    }

    /// <summary>
    /// A client assertion is signed with the endpoint's server-side key: it verifies against that key's public half,
    /// names the endpoint's client and token URL, and the verify route accepts it (#5436).
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-ESR-001")]
    public async Task GenerateAndVerifySmartAssertion_SignsWithTheEndpointsServerSideKey()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext($"Smart_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var rsa = RSA.Create(2048);
        var keys = Keys(("METRO_GENERAL", rsa.ExportPkcs8PrivateKeyPem()));
        string endpointId = await RegisterSmartEndpointAsync(db, clock, "METRO_GENERAL");

        var generated = await ConfigService(db, clock, keys).GenerateClientAssertionAsync(new SmartAssertionRequest(endpointId, 300), ct);
        var assertion = generated.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<SmartAssertionResponse>>().Subject.Value!;

        assertion.ClientAssertionType.Should().Be("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
        using var publicOnly = RSA.Create(rsa.ExportParameters(includePrivateParameters: false));
        new JwtSecurityTokenHandler().ValidateToken(assertion.ClientAssertionToken, new TokenValidationParameters
        {
            ValidIssuer = "cadence-client-99",
            ValidAudience = TokenUrl,
            IssuerSigningKey = new RsaSecurityKey(publicOnly),
            ValidateLifetime = false,
        }, out var validated);
        var jwt = (JwtSecurityToken)validated;
        jwt.Header.Alg.Should().Be(SecurityAlgorithms.RsaSha384);
        jwt.Header.Kid.Should().Be("key-1");
        jwt.Subject.Should().Be("cadence-client-99");

        var verified = await ConfigService(db, clock, keys).VerifyClientAssertionAsync(new SmartVerifyRequest(assertion.ClientAssertionToken, endpointId), ct);
        verified.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<SmartVerifyResponse>>().Subject.Value!.IsValid.Should().BeTrue();
    }

    /// <summary>An assertion signed with any other key, or presented after it expired, does not verify (#5436).</summary>
    [Fact]
    public async Task VerifySmartAssertion_SignedByAnotherKeyOrExpired_IsInvalid()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext($"SmartForeign_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var endpointKey = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        string endpointId = await RegisterSmartEndpointAsync(db, clock, "METRO_GENERAL");
        var keys = Keys(("METRO_GENERAL", endpointKey.ExportPkcs8PrivateKeyPem()));
        var foreignKeys = Keys(("METRO_GENERAL", otherKey.ExportPkcs8PrivateKeyPem()));

        var foreign = await ConfigService(db, clock, foreignKeys).GenerateClientAssertionAsync(new SmartAssertionRequest(endpointId, 60), ct);
        string foreignToken = foreign.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<SmartAssertionResponse>>().Subject.Value!.ClientAssertionToken;
        var own = await ConfigService(db, clock, keys).GenerateClientAssertionAsync(new SmartAssertionRequest(endpointId, 60), ct);
        string ownToken = own.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<SmartAssertionResponse>>().Subject.Value!.ClientAssertionToken;

        var foreignResult = await ConfigService(db, clock, keys).VerifyClientAssertionAsync(new SmartVerifyRequest(foreignToken, endpointId), ct);
        clock.Advance(TimeSpan.FromSeconds(61));
        var expiredResult = await ConfigService(db, clock, keys).VerifyClientAssertionAsync(new SmartVerifyRequest(ownToken, endpointId), ct);

        foreignResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<SmartVerifyResponse>>().Subject.Value!.IsValid.Should().BeFalse();
        expiredResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<SmartVerifyResponse>>().Subject.Value!.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// With no key reference, an unconfigured key, a malformed PEM or a public key only, nothing is signed: the route
    /// answers 422 and never falls back to a generated key (#5436).
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("METRO_GENERAL", null)]
    [InlineData("METRO_GENERAL", "malformed")]
    [InlineData("METRO_GENERAL", "public")]
    public async Task GenerateSmartAssertion_WithoutAUsableKey_IsRefusedAndSignsNothing(string? keyRef, string? key)
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext($"SmartNoKey_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var rsa = RSA.Create(2048);
        // The case names stand in for the PEMs, so no private-key header reaches the test results the secret scanner reads.
        string? configured = key switch
        {
            "public" => rsa.ExportSubjectPublicKeyInfoPem(),
            "malformed" => "-----BEGIN PRIVATE KEY-----\nnot a key\n-----END PRIVATE KEY-----", // pragma: allowlist secret
            _ => null,
        };
        var keys = configured is null ? Keys() : Keys(("METRO_GENERAL", configured));
        string endpointId = await RegisterSmartEndpointAsync(db, clock, keyRef);

        var result = await ConfigService(db, clock, keys).GenerateClientAssertionAsync(new SmartAssertionRequest(endpointId, 300), ct);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(422);
    }

    /// <summary>An unknown endpoint is 404, an assertion lifetime over five minutes is 400, and a bad key reference is refused at registration.</summary>
    [Fact]
    public async Task SmartAssertion_RejectsUnknownEndpointsLongLifetimesAndBadKeyReferences()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext($"SmartBad_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var rsa = RSA.Create(2048);
        var keys = Keys(("METRO_GENERAL", rsa.ExportPkcs8PrivateKeyPem()));
        string endpointId = await RegisterSmartEndpointAsync(db, clock, "METRO_GENERAL");

        var unknown = await ConfigService(db, clock, keys).GenerateClientAssertionAsync(new SmartAssertionRequest("no-such-endpoint", 300), ct);
        var tooLong = await ConfigService(db, clock, keys).GenerateClientAssertionAsync(new SmartAssertionRequest(endpointId, 301), ct);
        var badRef = await ConfigService(db, clock, keys).RegisterEhrEndpointAsync(
            new EhrEndpointCreateRequest("X", null, "https://x/r4", null, null, null, null, 300, null, SigningKeyRef: "../etc/passwd"),
            CreateMockHttpContext(), ct);

        unknown.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(404);
        tooLong.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(400);
        badRef.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(400);
    }

    /// <summary>
    /// A posted FHIR bundle becomes one candidate per supported Observation, carrying the bundle's own value, unit,
    /// time and raw resource, and nothing is claimed about the eCRF (#5432).
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-ESR-001")]
    public async Task IngestFhirBundle_MapsEachSupportedObservationFromTheBundle()
    {
        using var db = CreateDbContext($"EhrIngestTest_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var ct = TestContext.Current.CancellationToken;

        var result = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-101", "SITE-01", "PAT-001", null, "EPIC", SampleBundle), CreateMockHttpContext(), ct);

        var response = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<FhirBundleIngestResponse>>().Subject.Value!;
        response.CandidateCount.Should().Be(2);
        response.Message.Should().Contain("skipped 1");

        var stored = await db.EhrReconciliationCandidateRecords.OrderBy(c => c.TargetVariable).ToListAsync(ct);
        stored.Select(c => c.TargetVariable).Should().Equal("GLUC", "SYSBP");
        var bp = stored.Single(c => c.TargetVariable == "SYSBP");
        bp.FhirResourceId.Should().Be("obs-bp-1");
        bp.CandidateValueJson.Should().Be("128");
        bp.CandidateUnit.Should().Be("mmHg");
        bp.SourceTimestamp.Should().Be(new DateTimeOffset(2026, 9, 30, 8, 15, 0, TimeSpan.Zero));
        bp.CurrentEcrfValueJson.Should().BeNull();
        bp.DiscrepancyType.Should().Be("NOT_COMPARED");
        bp.RawFhirResourceJson.Should().Contain("obs-bp-1");
        stored.Should().OnlyContain(c => c.StudyId == "STUDY-101" && c.SiteId == "SITE-01" && c.EhrSystemId == "EPIC");
    }

    /// <summary>Ingesting the same bundle twice files each resource once.</summary>
    [Fact]
    public async Task IngestFhirBundle_Twice_DoesNotDuplicateCandidates()
    {
        using var db = CreateDbContext($"EhrIngestTwice_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var ct = TestContext.Current.CancellationToken;

        await IngestSampleBundleAsync(db, clock, CreateMockHttpContext(), ct);
        await IngestSampleBundleAsync(db, clock, CreateMockHttpContext(), ct);

        (await db.EhrReconciliationCandidateRecords.CountAsync(ct)).Should().Be(2);
    }

    /// <summary>A payload that is not a FHIR bundle, or a bundle with nothing mappable, is refused and writes nothing.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"resourceType":"Patient","id":"p1"}""")]
    [InlineData("""{"resourceType":"Bundle","entry":[]}""")]
    [InlineData("""{"resourceType":"Bundle","entry":[{"resource":{"resourceType":"Observation","id":"x","code":{"coding":[{"system":"http://loinc.org","code":"0000-0"}]}}}]}""")]
    public async Task IngestFhirBundle_WithNothingToMap_IsRefusedAndWritesNothing(string bundle)
    {
        using var db = CreateDbContext($"EhrIngestBad_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var ct = TestContext.Current.CancellationToken;

        var result = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-101", "SITE-01", "PAT-001", null, "EPIC", bundle), CreateMockHttpContext(), ct);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(400);
        (await db.EhrReconciliationCandidateRecords.CountAsync(ct)).Should().Be(0);
    }

    /// <summary>With no EHR client configured, polling answers 501 instead of inventing candidates (#5432).</summary>
    [Fact]
    public void PollHospitalEhr_WithNoEhrClient_Answers501()
    {
        var result = EsourceEndpoints.PollHospitalEhrAsync(
            new EhrPollRequest(EndpointId: null, StudyId: "STUDY-101", SiteId: "SITE-01", SubjectId: "PAT-001"),
            CreateMockHttpContext());

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(501);
    }

    /// <summary>Listing candidates for a study with none returns an empty page and writes nothing (#5432).</summary>
    [Fact]
    public async Task ListCandidates_OnAnEmptyStudy_ReturnsNothingAndWritesNothing()
    {
        using var db = CreateDbContext($"EhrListEmpty_{Guid.NewGuid():N}");
        var ct = TestContext.Current.CancellationToken;

        var result = await IngestService(db).ListCandidatesAsync("STUDY-101", "PAT-001", "SITE-01", "ALL", "ALL", 0, 50, CreateMockHttpContext(), ct);

        var page = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrCandidateListResponse>>().Subject.Value!;
        page.TotalCount.Should().Be(0);
        page.Candidates.Should().BeEmpty();
        (await db.EhrReconciliationCandidateRecords.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task AcceptCandidate_CommitsValueAndWritesGxPAuditAndESignature()
    {
        // Arrange
        var dbName = $"EhrAcceptTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        // Seed candidate list
        await IngestSampleBundleAsync(db, clock, httpContext, TestContext.Current.CancellationToken);
        var firstCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(c => c.TargetVariable == "SYSBP", TestContext.Current.CancellationToken);

        var acceptReq = new EhrReconcileAcceptRequest(
            CandidateId: firstCandidate.Id,
            CommitReason: "Coordinator verified EHR source lab against chart notes",
            ChangeReason: "CRC eSource Reconciliation Accept & Commit");

        // Act
        var result = await ReconcileService(db, clock).AcceptCandidateAsync(acceptReq, httpContext, TestContext.Current.CancellationToken);

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
    [GxPRequirement("PRD-SYS-001")]
    public async Task RejectCandidate_RequiresRationaleAndPreservesOriginalPayload()
    {
        // Arrange
        var dbName = $"EhrRejectTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        await IngestSampleBundleAsync(db, clock, httpContext, TestContext.Current.CancellationToken);
        var firstCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(TestContext.Current.CancellationToken);

        // Test 1: Empty rationale fails
        var emptyReject = new EhrReconcileRejectRequest(CandidateId: firstCandidate.Id, RejectionReason: "", ChangeReason: "Reject attempt");
        var failResult = await ReconcileService(db, clock).RejectCandidateAsync(emptyReject, httpContext, TestContext.Current.CancellationToken);
        failResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();

        // Test 2: Valid rationale succeeds
        var validReject = new EhrReconcileRejectRequest(
            CandidateId: firstCandidate.Id,
            RejectionReason: "Out of visit window observation taken prior to screening consent",
            ChangeReason: "CRC eSource Reconciliation Reject");

        var successResult = await ReconcileService(db, clock).RejectCandidateAsync(validReject, httpContext, TestContext.Current.CancellationToken);

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
    public async Task RaiseClinicalQuery_RegistersQueryAndUpdatesCandidateStatus()
    {
        // Arrange
        var dbName = $"EhrQueryTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        await IngestSampleBundleAsync(db, clock, httpContext, TestContext.Current.CancellationToken);
        var mismatchCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(c => c.TargetVariable == "SYSBP", TestContext.Current.CancellationToken);

        var queryReq = new EhrReconcileQueryRequest(
            CandidateId: mismatchCandidate.Id,
            QueryExplanation: "Blood pressure reading 128 mmHg in EHR conflicts with eCRF 140 mmHg. Requesting investigator confirmation.",
            DiscrepancyType: "VALUE_MISMATCH",
            ChangeReason: "CRC Raised eSource Discrepancy Query");

        // Act
        var result = await ReconcileService(db, clock).RaiseClinicalQueryAsync(queryReq, httpContext, TestContext.Current.CancellationToken);

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
    [GxPRequirement("PRD-SYS-001")]
    public async Task GetProvenanceAudit_ReturnsRawFhirPayloadAndAuditTrail()
    {
        // Arrange
        var dbName = $"EhrProvTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah");

        await IngestSampleBundleAsync(db, clock, httpContext, TestContext.Current.CancellationToken);
        var candidate = await db.EhrReconciliationCandidateRecords.FirstAsync(TestContext.Current.CancellationToken);

        // Perform an accept action to generate audit log
        await ReconcileService(db, clock).AcceptCandidateAsync(new EhrReconcileAcceptRequest(candidate.Id, "Verified against source", "Accept"), httpContext, TestContext.Current.CancellationToken);

        // Act
        var provResult = await IngestService(db, clock).GetProvenanceAuditAsync(candidate.Id, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var provObj = provResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<EhrProvenanceResponse>>().Subject.Value;
        provObj.Should().NotBeNull();
        provObj!.CandidateId.Should().Be(candidate.Id);
        provObj.MappingProvenance.Should().NotBeNullOrEmpty();
        provObj.RawFhirResource.Should().NotBeNull();
        provObj.AuditTrail.Should().NotBeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BatchAcceptCandidates_CommitsAllValidCandidatesAndWritesAuditLogs()
    {
        // Arrange
        var dbName = $"EhrBatchAcceptTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        await IngestSampleBundleAsync(db, clock, httpContext, TestContext.Current.CancellationToken);
        var initialCandidates = await db.EhrReconciliationCandidateRecords.Take(2).ToListAsync(TestContext.Current.CancellationToken);
        var idsToAccept = initialCandidates.Select(c => c.Id).ToList();

        var batchReq = new EhrReconcileBatchAcceptRequest(
            CandidateIds: idsToAccept,
            CommitReason: "Batch verified against EHR source data",
            ChangeReason: "CRC eSource Batch Accept");

        // Act
        var result = await ReconcileService(db, clock).BatchAcceptCandidatesAsync(batchReq, httpContext, TestContext.Current.CancellationToken);

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
    [GxPRequirement("PRD-SYS-001")]
    public async Task BatchRejectCandidates_RequiresRationaleAndUpdatesStatus()
    {
        // Arrange
        var dbName = $"EhrBatchRejectTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext("crc_sarah", "crc");

        await IngestSampleBundleAsync(db, clock, httpContext, TestContext.Current.CancellationToken);
        var initialCandidates = await db.EhrReconciliationCandidateRecords.Take(2).ToListAsync(TestContext.Current.CancellationToken);
        var idsToReject = initialCandidates.Select(c => c.Id).ToList();

        // 1. Empty rationale fails
        var emptyReq = new EhrReconcileBatchRejectRequest(CandidateIds: idsToReject, RejectionReason: "", ChangeReason: "Reject");
        var failResult = await ReconcileService(db, clock).BatchRejectCandidatesAsync(emptyReq, httpContext, TestContext.Current.CancellationToken);
        failResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();

        // 2. Valid rationale succeeds
        var validReq = new EhrReconcileBatchRejectRequest(
            CandidateIds: idsToReject,
            RejectionReason: "Out of visit window observations",
            ChangeReason: "CRC eSource Batch Reject");

        var successResult = await ReconcileService(db, clock).BatchRejectCandidatesAsync(validReq, httpContext, TestContext.Current.CancellationToken);

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
        var result = await ReconcileService(db, clock).BatchAcceptCandidatesAsync(req, httpContext, TestContext.Current.CancellationToken);

        // Assert
        var problem = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Detail.Should().Contain("100 candidate items");
    }

    [Fact]
    public async Task BatchOperation_WithNonExistentCandidateId_RollsBackTransaction()
    {
        // Arrange
        var dbName = $"EhrBatchRollbackTest_{Guid.NewGuid():N}";
        using var db = CreateDbContext(dbName);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var httpContext = CreateMockHttpContext();

        await IngestSampleBundleAsync(db, clock, httpContext, TestContext.Current.CancellationToken);
        var validCandidate = await db.EhrReconciliationCandidateRecords.FirstAsync(TestContext.Current.CancellationToken);

        var mixedIds = new List<string> { validCandidate.Id, "non-existent-candidate-id-999" };
        var batchReq = new EhrReconcileBatchAcceptRequest(CandidateIds: mixedIds, CommitReason: "Test rollback", ChangeReason: null);

        // Act
        var result = await ReconcileService(db, clock).BatchAcceptCandidatesAsync(batchReq, httpContext, TestContext.Current.CancellationToken);

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
        await IngestSampleBundleAsync(db, clock, CreateMockHttpContext(), ct);
        var candidate = await db.EhrReconciliationCandidateRecords.FirstAsync(ct);
        var outsider = CreateMockHttpContext("crc_other", "crc", "STUDY-202");

        var list = await IngestService(db, clock).ListCandidatesAsync(null, null, null, "ALL", "ALL", 0, 50, outsider, ct);
        var lookup = await IngestService(db, clock).GetCandidateByIdAsync(candidate.Id, outsider, ct);
        var accept = await ReconcileService(db, clock).AcceptCandidateAsync(
            new EhrReconcileAcceptRequest(candidate.Id, "Verified", "Accept"), outsider, ct);

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

        var poll = EsourceEndpoints.PollHospitalEhrAsync(
            new EhrPollRequest(EndpointId: "none", StudyId: "STUDY-101", SiteId: "SITE-01", SubjectId: "PAT-001"), outsider);
        var ingest = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest(StudyId: "STUDY-101", SiteId: "SITE-01", SubjectId: "PAT-001", SubjectPseudonym: null, EhrSystemId: "EPIC", RawFhirBundleJson: SampleBundle),
            outsider, ct);

        poll.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        ingest.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        (await db.EhrReconciliationCandidateRecords.CountAsync(ct)).Should().Be(0);
    }

    private static HttpContext CreateSiteScopedHttpContext(
        string userName,
        string role,
        IReadOnlyList<string> studies,
        IReadOnlyList<string> sites)
    {
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.NameIdentifier, userName),
            new Claim(ClaimTypes.Role, role),
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userName,
            Roles = [role],
            AssignedStudies = studies,
            AssignedSites = sites,
        };
        return context;
    }

    [Fact]
    public async Task IngestFhirBundle_TrimsInputsAndDeduplicatesRepeatedObservationsWithinBundle()
    {
        using var db = CreateDbContext($"EhrIngestTrimDedup_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var ct = TestContext.Current.CancellationToken;
        var site1Caller = CreateSiteScopedHttpContext("crc_site1", "crc", ["STUDY-101"], ["SITE-01"]);

        const string duplicateEntryBundle = """
            {
              "resourceType": "Bundle",
              "type": "collection",
              "entry": [
                { "resource": { "resourceType": "Observation", "id": "obs-bp-1", "status": "final",
                    "code": { "coding": [ { "system": "http://loinc.org", "code": "8480-6" } ] },
                    "effectiveDateTime": "2026-09-30T08:15:00Z",
                    "valueQuantity": { "value": 128, "unit": "mmHg" } } },
                { "resource": { "resourceType": "Observation", "id": "obs-bp-1", "status": "final",
                    "code": { "coding": [ { "system": "http://loinc.org", "code": "8480-6" } ] },
                    "effectiveDateTime": "2026-09-30T08:15:00Z",
                    "valueQuantity": { "value": 130, "unit": "mmHg" } } }
              ]
            }
            """;

        var firstResult = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-101", "SITE-01", "PAT-001", "PSEUDO-001", "EPIC", duplicateEntryBundle),
            site1Caller, ct);

        var firstResponse = firstResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<FhirBundleIngestResponse>>().Subject.Value!;
        firstResponse.CandidateCount.Should().Be(1);
        firstResponse.Candidates.Should().ContainSingle();
        string candidateId = firstResponse.Candidates[0].Id;

        var paddedRetryResult = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("  STUDY-101  ", "  SITE-01  ", "  PAT-001  ", "  PSEUDO-001  ", "  EPIC  ", duplicateEntryBundle),
            site1Caller, ct);

        var retryResponse = paddedRetryResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<FhirBundleIngestResponse>>().Subject.Value!;
        retryResponse.CandidateCount.Should().Be(1);
        retryResponse.Candidates.Should().ContainSingle().Which.Id.Should().Be(candidateId);

        var stored = await db.EhrReconciliationCandidateRecords.SingleAsync(ct);
        stored.StudyId.Should().Be("STUDY-101");
        stored.SiteId.Should().Be("SITE-01");
        stored.SubjectId.Should().Be("PAT-001");
        stored.EhrSystemId.Should().Be("EPIC");
    }

    [Fact]
    public async Task IngestFhirBundle_DistinctSubjectsAcrossSitesSharingResourceId_IngestIndependently()
    {
        using var db = CreateDbContext($"EhrIngestDistinctSites_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var ct = TestContext.Current.CancellationToken;
        var crcSite1 = CreateSiteScopedHttpContext("crc_site1", "crc", ["STUDY-01"], ["SITE-01"]);
        var crcSite2 = CreateSiteScopedHttpContext("crc_site2", "crc", ["STUDY-01"], ["SITE-02"]);

        var site1Result = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-01", "SITE-01", "SUBJ-01", "PSEUDO-01", "EPIC-S1", SampleBundle),
            crcSite1, ct);
        var site2Result = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-01", "SITE-02", "SUBJ-02", "PSEUDO-02", "EPIC-S2", SampleBundle),
            crcSite2, ct);

        site1Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<FhirBundleIngestResponse>>()
            .Subject.Value!.CandidateCount.Should().Be(2);
        site2Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<FhirBundleIngestResponse>>()
            .Subject.Value!.CandidateCount.Should().Be(2);

        (await db.EhrReconciliationCandidateRecords.CountAsync(ct)).Should().Be(4);
        (await db.EhrReconciliationCandidateRecords.CountAsync(c => c.SiteId == "SITE-01" && c.SubjectId == "SUBJ-01", ct)).Should().Be(2);
        (await db.EhrReconciliationCandidateRecords.CountAsync(c => c.SiteId == "SITE-02" && c.SubjectId == "SUBJ-02", ct)).Should().Be(2);
    }

    [Fact]
    public async Task IngestFhirBundle_CrossSiteSubjectCollision_BlocksSingleSiteWith403AndMultiSiteWith409()
    {
        using var db = CreateDbContext($"EhrIngestCrossSiteCollision_{Guid.NewGuid():N}");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var ct = TestContext.Current.CancellationToken;
        var crcSite2 = CreateSiteScopedHttpContext("crc_site2", "crc", ["STUDY-01"], ["SITE-02"]);
        var crcSite1 = CreateSiteScopedHttpContext("crc_site1", "crc", ["STUDY-01"], ["SITE-01"]);
        var crcMultiSite = CreateSiteScopedHttpContext("crc_both", "crc", ["STUDY-01"], ["SITE-01", "SITE-02"]);

        var seedResult = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-01", "SITE-02", "SUBJ-02", "PSEUDO-SITE02-CANARY", "SITE02-HOSPITAL-EHR-CANARY", SampleBundle),
            crcSite2, ct);
        seedResult.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<FhirBundleIngestResponse>>();

        var singleSiteProbe = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-01", "SITE-01", "SUBJ-02", "PROBE", "EPIC-METRO", SampleBundle),
            crcSite1, ct);

        var forbidden = singleSiteProbe.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        forbidden.StatusCode.Should().Be(403);
        forbidden.ProblemDetails.Detail.Should().NotContain("PSEUDO-SITE02-CANARY")
            .And.NotContain("SITE02-HOSPITAL-EHR-CANARY")
            .And.NotContain("SITE-02");

        var multiSiteConflict = await IngestService(db, clock).IngestFhirBundleAsync(
            new FhirBundleIngestRequest("STUDY-01", "SITE-01", "SUBJ-02", "PROBE", "EPIC-METRO", SampleBundle),
            crcMultiSite, ct);

        var conflict = multiSiteConflict.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        conflict.StatusCode.Should().Be(409);
        conflict.ProblemDetails.Detail.Should().NotContain("PSEUDO-SITE02-CANARY")
            .And.NotContain("SITE02-HOSPITAL-EHR-CANARY")
            .And.NotContain("SITE-02");

        (await db.EhrReconciliationCandidateRecords.CountAsync(ct)).Should().Be(2);
        (await db.EhrReconciliationCandidateRecords.AllAsync(c => c.SiteId == "SITE-02", ct)).Should().BeTrue();
    }
}
