using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.FieldProtection;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using FluentValidation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Execution;

/// <summary>
/// End-to-end tests for 30-day participant BYOD activation code issuance and redemption, investigator/coordinator
/// role enforcement via <c>SubjectAuthority.AuthorizeParticipantChange</c>, <c>X-Change-Reason</c> validation,
/// <c>K-IDN-1</c> zero-PII keyed HMAC hashing (<see cref="ILookupHasher"/>), and <c>X-Subject-Code</c> gateway
/// HMAC signature verification.
/// </summary>
public sealed class SubjectActivationCodeTests : IDisposable
{
    private static readonly JsonSerializerOptions SnakeJson = CreateJsonOptions();

    private readonly InMemorySubjectRepository _subjects = new();
    private readonly RecordingAuditLogWriter _auditWriter = new();
    private readonly ILookupHasher _lookupHasher = TestFieldProtection.CreateLookupHasher();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
    private readonly ActivationTestFactory _factory;

    public SubjectActivationCodeTests()
    {
        var seededSubject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-101-001"),
            new StudyCode("CADENCE-101"),
            new SiteCode("SITE-101"),
            enrollmentIndex: 1,
            encryptedDemographics: null,
            stratFactors: null);
        _subjects.AddAsync(seededSubject, CancellationToken.None).GetAwaiter().GetResult();

        _factory = new ActivationTestFactory(_subjects, _auditWriter, _lookupHasher, _clock);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    [KernelRule("K-IDN-1")]
    public async Task IssueAndRedeemActivationCode_SucceedsWithin30Days_AndStoresOnlyKeyedHmacDigests()
    {
        var ct = TestContext.Current.CancellationToken;
        using var crcClient = CreateRoleClient("crc-jane", "crc", changeReason: "Issue BYOD activation code at Visit 1");

        using var issueReq = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/execution/subjects/SUBJ-101-001/activation-code");
        var issueRes = await crcClient.SendAsync(issueReq, ct);
        issueRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var issued = await issueRes.Content.ReadFromJsonAsync<SubjectActivationCodeIssueResponse>(SnakeJson, ct);
        issued.Should().NotBeNull();
        issued!.SubjectId.Value.Should().Be("SUBJ-101-001");
        issued.StudyId.Value.Should().Be("CADENCE-101");
        issued.ExpiresInDays.Should().Be(30);
        issued.ExpiresAt.Should().Be(_clock.UtcNow.AddDays(30));
        issued.ActivationCode.Should().StartWith("CAD-");

        // K-IDN-1: raw activation code never appears in stored token rows or audit entries
        var storedToken = _subjects.ActivationTokens.Should().ContainSingle().Subject;
        storedToken.TokenHash.Should().HaveLength(ILookupHasher.HashLength);
        storedToken.TokenHash.Should().Be(_lookupHasher.ComputeHash(
            SubjectApplicationService.NormalizeActivationCode(issued.ActivationCode),
            SubjectActivationToken.TokenHashPurpose));
        storedToken.TokenHash.Should().NotContain(issued.ActivationCode);
        _auditWriter.Calls.Select(c => JsonSerializer.Serialize(c.NewValues))
            .Should().OnlyContain(d => !d.Contains(issued.ActivationCode, StringComparison.Ordinal));

        // Advance 10 days (still within 30-day window) and redeem as authenticated Subject
        _clock.Advance(TimeSpan.FromDays(10));
        const string subjectOidcSub = "oidc-sub-uuid-987654";
        using var subjectClient = CreateRoleClient(
            subjectOidcSub,
            "Subject",
            changeReason: "Participant linked BYOD device in Subject Portal");

        var redeemRes = await PostJsonAsync(
            subjectClient,
            "/api/v1/execution/subjects/activation-codes/redeem",
            new SubjectActivationCodeRedeemRequest(issued.ActivationCode),
            ct);
        redeemRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var redeemed = await redeemRes.Content.ReadFromJsonAsync<SubjectActivationCodeRedeemResponse>(SnakeJson, ct);
        redeemed.Should().NotBeNull();
        redeemed!.SubjectId.Value.Should().Be("SUBJ-101-001");
        redeemed.StudyId.Value.Should().Be("CADENCE-101");
        redeemed.RedeemedAt.Should().Be(_clock.UtcNow);

        // K-IDN-1: raw OIDC sub never stored in token row
        storedToken.RedeemedByPrincipalHash.Should().HaveLength(ILookupHasher.HashLength);
        storedToken.RedeemedByPrincipalHash.Should().NotContain(subjectOidcSub);

        // GET /subjects/activation-codes/me resolves the bound SubjectCode using the HMAC digest
        var meRes = await subjectClient.GetAsync("/api/v1/execution/subjects/activation-codes/me", ct);
        meRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var meBody = await meRes.Content.ReadFromJsonAsync<SubjectActivationCodeRedeemResponse>(SnakeJson, ct);
        meBody!.SubjectId.Value.Should().Be("SUBJ-101-001");

        // Replaying an already redeemed code returns 422
        var replayRes = await PostJsonAsync(
            subjectClient,
            "/api/v1/execution/subjects/activation-codes/redeem",
            new SubjectActivationCodeRedeemRequest(issued.ActivationCode),
            ct);
        replayRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task RedeemActivationCode_After30DayWindowExpires_Returns422UnprocessableEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        using var investigatorClient = CreateRoleClient("pi-001", "investigator", changeReason: "Issue activation code");

        using var issueReq = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/execution/subjects/SUBJ-101-001/activation-code");
        var issueRes = await investigatorClient.SendAsync(issueReq, ct);
        var issued = await issueRes.Content.ReadFromJsonAsync<SubjectActivationCodeIssueResponse>(SnakeJson, ct);

        _clock.Advance(TimeSpan.FromDays(30).Add(TimeSpan.FromMinutes(1)));

        using var subjectClient = CreateRoleClient("oidc-sub-expired", "Subject", changeReason: "Late redemption attempt");
        var redeemRes = await PostJsonAsync(
            subjectClient,
            "/api/v1/execution/subjects/activation-codes/redeem",
            new SubjectActivationCodeRedeemRequest(issued!.ActivationCode),
            ct);

        redeemRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("sponsor_dm")]
    [InlineData("cra")]
    [InlineData("Subject")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task IssueActivationCode_CallerNotInvestigatorOrCoordinator_Returns403Forbidden(string disallowedRole)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = CreateRoleClient($"caller-{disallowedRole}", disallowedRole, changeReason: "Unauthorized issuance probe");

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/execution/subjects/SUBJ-101-001/activation-code");
        var res = await client.SendAsync(req, ct);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _subjects.ActivationTokens.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task IssueAndRedeemActivationCode_MissingChangeReasonHeader_Returns400BadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var crcNoReason = CreateRoleClient("crc-jane", "crc", changeReason: null);
        using var issueReq = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/execution/subjects/SUBJ-101-001/activation-code");
        (await crcNoReason.SendAsync(issueReq, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var subjectNoReason = CreateRoleClient("subj-1", "Subject", changeReason: null);
        var redeemRes = await PostJsonAsync(
            subjectNoReason,
            "/api/v1/execution/subjects/activation-codes/redeem",
            new SubjectActivationCodeRedeemRequest("CAD-AAAA-BBBB-CCCC"),
            ct);
        redeemRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    [KernelRule("K-IDN-1")]
    public async Task SubjectRepository_SqlitePersistence_StoresOnly64CharHmacHashesAtRest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sqlite = new SqliteConnection("DataSource=:memory:");
        await sqlite.OpenAsync(ct);

        var dbOptions = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(sqlite)
            .Options;

        const string rawCode = "CAD-2345-6789-ABCD";
        const string rawOidcSub = "subject-oidc-uuid-secret";
        string codeHash = _lookupHasher.ComputeHash(
            SubjectApplicationService.NormalizeActivationCode(rawCode),
            SubjectActivationToken.TokenHashPurpose);
        string principalHash = _lookupHasher.ComputeHash(
            rawOidcSub,
            SubjectActivationToken.PrincipalHashPurpose);

        await using (var db = new ExecutionDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync(ct);
            var repo = new SubjectRepository(db);
            var token = SubjectActivationToken.Issue(
                new SubjectCode("SUBJ-101-001"),
                new StudyCode("CADENCE-101"),
                new SiteCode("SITE-101"),
                codeHash,
                _clock.UtcNow,
                "crc-001",
                "Clinic Visit 1");
            token.Redeem(principalHash, _clock.UtcNow.AddMinutes(5));
            await repo.AddActivationTokenAsync(token, ct);
            await repo.SaveChangesAsync(ct);
        }

        await using var readCmd = sqlite.CreateCommand();
        readCmd.CommandText = "SELECT TokenHash, RedeemedByPrincipalHash FROM subject_activation_tokens";
        await using var reader = await readCmd.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).Should().BeTrue();
        string dbTokenHash = reader.GetString(0);
        string dbPrincipalHash = reader.GetString(1);

        dbTokenHash.Should().Be(codeHash).And.HaveLength(64).And.NotContain(rawCode);
        dbPrincipalHash.Should().Be(principalHash).And.HaveLength(64).And.NotContain(rawOidcSub);
    }

    [Fact]
    public void GatewayIdentity_SignsXSubjectCode_AndTamperingBreaksHmacVerification()
    {
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret);
        var identity = new GatewayIdentity(
            UserId: "subj-uuid-001",
            Roles: ["Subject"],
            SiteIds: ["SITE-101"],
            TenantId: "cadence-local",
            StudyIds: ["CADENCE-101"],
            ChangeReason: "Submit diary",
            SubjectCode: "SUBJ-101-001");

        var headers = identity.ToSignedHeaders(_clock.UtcNow, secret);
        headers.Should().ContainKey(GatewayHeaders.SubjectCode)
            .WhoseValue.Should().Be("SUBJ-101-001");

        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: secret,
            changeReason: "Submit diary",
            siteId: headers[GatewayHeaders.SiteId],
            studyIds: headers[GatewayHeaders.StudyIds],
            tenantId: headers[GatewayHeaders.TenantId],
            subjectCode: "SUBJ-101-001").Should().BeTrue();

        // Tampering X-Subject-Code to another participant's code invalidates the HMAC signature
        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: secret,
            changeReason: "Submit diary",
            siteId: headers[GatewayHeaders.SiteId],
            studyIds: headers[GatewayHeaders.StudyIds],
            tenantId: headers[GatewayHeaders.TenantId],
            subjectCode: "SUBJ-101-002").Should().BeFalse();

        // PermissionMatrix grants subject ecoa_notification:read, ecoa_notification:acknowledge, ecoa_activation:redeem
        var subjectPrincipal = new Principal { UserId = "subj-uuid-001", Roles = ["subject"] };
        PermissionMatrix.HasPermission(subjectPrincipal, "ecoa_notification:read").Should().BeTrue();
        PermissionMatrix.HasPermission(subjectPrincipal, "ecoa_notification:acknowledge").Should().BeTrue();
        PermissionMatrix.HasPermission(subjectPrincipal, "ecoa_activation:redeem").Should().BeTrue();
        PermissionMatrix.HasPermission(subjectPrincipal, "subject_lifecycle:manage").Should().BeFalse();
    }

    private HttpClient CreateRoleClient(string userId, string role, string? changeReason)
    {
        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: [role],
            SiteIds: ["SITE-101"],
            TenantId: "cadence-local",
            StudyIds: ["CADENCE-101"],
            ChangeReason: changeReason);
        return _factory.CreateSignedClient(identity);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync<T>(
        HttpClient client,
        string path,
        T payload,
        CancellationToken ct)
    {
        string raw = JsonSerializer.Serialize(payload, SnakeJson);
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(raw, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(req, ct);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        ExecutionJsonSerialization.Configure(options);
        return options;
    }

    private sealed class HttpContextAuditAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentAuditContextAccessor
    {
        public string UserId =>
            (httpContextAccessor.HttpContext?.Items.TryGetValue(GatewayAuthenticationHandler.PrincipalItemKey, out object? p) == true
             && p is Principal principal
             && !string.IsNullOrWhiteSpace(principal.UserId))
                ? principal.UserId
                : throw new AuditActorUnresolvedException();

        public string ChangeReason =>
            (httpContextAccessor.HttpContext?.Items.TryGetValue(GatewayAuthenticationHandler.PrincipalItemKey, out object? p) == true
             && p is Principal principal
             && !string.IsNullOrWhiteSpace(principal.ChangeReason))
                ? principal.ChangeReason
                : "system_operation";
    }

    private sealed class NullRandomizationService : IRandomizationApplicationService
    {
        public Task<RevealedAllocation?> RevealAsync(string subjectIdOrCode, CancellationToken ct) =>
            Task.FromResult<RevealedAllocation?>(null);

        public Task<Result<RandomizationSchemeResponse>> DraftSchemeAsync(RandomizationSchemeDraftRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result<RandomizationSchemeResponse>> ApproveSchemeAsync(Guid schemeId, string changeReason, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<RandomizationSchemeResponse>>> ListSchemesAsync(string? studyId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result<RandomizationResponse>> RandomizeAsync(string subjectIdOrCode, RandomizeSubjectRequest request, string changeReason, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class ActivationTestFactory(
        InMemorySubjectRepository subjects,
        RecordingAuditLogWriter auditWriter,
        ILookupHasher lookupHasher,
        IClock clock) : WebApplicationFactory<Cadence.Apps.Execution.Program>
    {
        protected override IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureWebHost(webHost => webHost
                    .UseTestServer()
                    .UseTestGatewaySecret()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddHttpContextAccessor();
                        services.AddCadenceSecurity();
                        services.ConfigureHttpJsonOptions(options => ExecutionJsonSerialization.Configure(options.SerializerOptions));
                        services.AddProblemDetails(options =>
                        {
                            options.CustomizeProblemDetails = context =>
                            {
                                DomainErrorHttpMapping.ApplyKnownCodeStatus(context.ProblemDetails);
                                if (context.ProblemDetails.Status is int correctedStatus)
                                {
                                    context.HttpContext.Response.StatusCode = correctedStatus;
                                }
                            };
                        });
                        services.AddSingleton<ISubjectRepository>(subjects);
                        services.AddSingleton<IConsentRepository>(new InMemoryConsentRepository());
                        services.AddSingleton<ISiteComplianceRepository>(new InMemorySiteComplianceRepository());
                        services.AddSingleton<IDemographicsEncryptionService>(new FakeDemographicsEncryptionService());
                        services.AddSingleton<IAuditLogWriter>(auditWriter);
                        services.AddScoped<ICurrentAuditContextAccessor, HttpContextAuditAccessor>();
                        services.AddSingleton(clock);
                        services.AddSingleton(lookupHasher);
                        services.AddScoped<ISubjectApplicationService>(sp => new SubjectApplicationService(
                            sp.GetRequiredService<ISubjectRepository>(),
                            sp.GetRequiredService<IConsentRepository>(),
                            sp.GetRequiredService<ISiteComplianceRepository>(),
                            sp.GetRequiredService<IDemographicsEncryptionService>(),
                            sp.GetRequiredService<IAuditLogWriter>(),
                            sp.GetRequiredService<ICurrentAuditContextAccessor>(),
                            screeningEligibility: null,
                            clock: sp.GetRequiredService<IClock>(),
                            lookupHasher: sp.GetRequiredService<ILookupHasher>()));
                        services.AddSingleton<IRandomizationApplicationService>(new NullRandomizationService());
                        services.AddSingleton<IValidator<SubjectCreateRequest>, SubjectCreateRequestValidator>();
                        services.AddSingleton<IValidator<SubjectConsentRequest>, SubjectConsentRequestValidator>();
                        services.AddSingleton<IValidator<SubjectStateUpdateRequest>, SubjectStateUpdateRequestValidator>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseCadenceSecurity();
                        app.UseEndpoints(endpoints => endpoints.MapSubjectEndpoints());
                    }));
    }
}
