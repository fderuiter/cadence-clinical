using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Ctms;

public sealed class DoaEndpointsTests : IAsyncDisposable
{
    private const string SiteId = "SITE-101";
    private const string StudyId = "STUDY-2026-001";

    private readonly CtmsDbContext _db;
    private readonly FakeClock _clock;
    private readonly IHost _host;
    private readonly TestServer _server;

    public DoaEndpointsTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new CtmsDbContext(options);
        _clock = new FakeClock(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseTestGatewaySecret();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IClock>(_clock);
                    services.AddSingleton(_db);
                    services.AddCadenceSecurity();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseCadenceSecurity();
                    app.UseEndpoints(endpoints =>
                    {
                        var ctmsApi = endpoints.MapGroup("/api/v1/ctms");
                        ctmsApi.MapDoaEndpoints();
                    });
                });
            });

        _host = builder.Start();
        _server = _host.GetTestServer();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _db.DisposeAsync();
    }

    private static DefaultHttpContext Caller(
        string role = RoleNormalization.ROLE_SYSADMIN,
        string userId = "user-1",
        string[]? studies = null,
        string[]? sites = null)
    {
        var normalized = RoleNormalization.GetNormalizedRoles([role]);
        bool siteScoped = normalized.Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains);
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            TenantId = TestSecurityContext.TenantCode,
            Roles = normalized,
            RawRoles = [role],
            AssignedStudies = studies ?? [StudyId],
            AssignedSites = sites ?? (siteScoped ? [SiteId] : []),
        };
        return httpContext;
    }

    private HttpClient CreateClient(
        string role = RoleNormalization.ROLE_SYSADMIN,
        string userId = "admin-1",
        string[]? studyIds = null,
        string[]? siteIds = null)
    {
        var normalized = RoleNormalization.GetNormalizedRoles([role]);
        bool siteScoped = normalized.Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains);
        var identity = TestSecurityContext.Identity(userId, role) with
        {
            StudyIds = studyIds ?? [StudyId],
            SiteIds = siteIds ?? (siteScoped ? [SiteId] : []),
        };
        var handler = new TestSecurityContext.GatewaySigningHandler(identity)
        {
            InnerHandler = _server.CreateHandler(),
        };
        return new HttpClient(handler)
        {
            BaseAddress = _server.BaseAddress,
        };
    }

    private async Task SeedSiteAsync(string siteId = SiteId, string siteName = "Site 101", string piName = "Dr. Arthur Pendragon")
    {
        var gate = new CtmsSiteGreenlightGate
        {
            SiteId = siteId,
            SiteName = siteName,
            StudyId = StudyId,
            CountryCode = "US",
            GreenlightCertifiedBy = piName,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "sysadmin",
            ReasonForChange = "Site initialized",
            VersionIndex = 1,
        };
        _db.CtmsSiteGreenlightGates.Add(gate);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task DelegateTasks_WithValidRequest_CreatesDelegationAndAuditLog()
    {
        await SeedSiteAsync();

        var caller = Caller("Clinical Research Coordinator", "crc-1");
        caller.Request.Headers["X-Change-Reason"] = "Initial delegation assignment for data entry";

        var request = new DelegationTaskRequest(
            Site: SiteId,
            StaffUserId: "staff-crc-01",
            TaskCodes: ["CRF_DATA_ENTRY", "INFORMED_CONSENT"],
            StartDate: "2026-11-15",
            ReasonForChange: "Initial delegation assignment for data entry");

        var result = await DoaEndpoints.DelegateTasksAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var created = result.Should().BeOfType<Created<DelegationResponseDto>>().Subject;
        created.Value.Should().NotBeNull();
        created.Value!.Site.Should().Be(SiteId);
        created.Value.StaffUserId.Should().Be("staff-crc-01");
        created.Value.TaskCodes.Should().Equal("CRF_DATA_ENTRY", "INFORMED_CONSENT");
        created.Value.StartDate.Should().Be("2026-11-15");
        created.Value.IsActive.Should().BeTrue();
        created.Value.SignedOff.Should().BeFalse();
        created.Value.ReasonForChange.Should().Be("Initial delegation assignment for data entry");

        // Verify DB persistence
        var delegation = await _db.CtmsDelegations.FindAsync([created.Value.RecordId], TestContext.Current.CancellationToken);
        delegation.Should().NotBeNull();
        delegation!.SiteId.Should().Be(SiteId);
        delegation.StaffUserId.Should().Be("staff-crc-01");
        delegation.IsActive.Should().BeTrue();
        delegation.SignedOff.Should().BeFalse();

        // Verify audit log
        var audit = await _db.CtmsAuditLogs.FirstOrDefaultAsync(
            a => a.Action == "DOA_DELEGATE" && a.UserId == "crc-1", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain("staff-crc-01");
        audit.ReasonForChange.Should().Be("Initial delegation assignment for data entry");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task DelegateTasks_WithNonExistentSite_Returns404NotFound()
    {
        var caller = Caller();
        caller.Request.Headers["X-Change-Reason"] = "Delegation test";

        var request = new DelegationTaskRequest(
            Site: "SITE-UNKNOWN",
            StaffUserId: "staff-1",
            TaskCodes: ["CRF_DATA_ENTRY"],
            StartDate: "2026-11-15",
            ReasonForChange: "Delegation test");

        var result = await DoaEndpoints.DelegateTasksAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        problem.ProblemDetails.Detail.Should().Contain("SITE-UNKNOWN");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task DelegateTasks_WithEmptyJustification_Returns400BadRequest()
    {
        await SeedSiteAsync();

        var caller = Caller();

        var request = new DelegationTaskRequest(
            Site: SiteId,
            StaffUserId: "staff-1",
            TaskCodes: ["CRF_DATA_ENTRY"],
            StartDate: "2026-11-15",
            ReasonForChange: "   ");

        var result = await DoaEndpoints.DelegateTasksAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Detail.Should().Contain("Reason for change is required");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SignOffDelegation_WithValidRecord_EndorsesSignOffAndAudits()
    {
        await SeedSiteAsync();

        var delegation = new CtmsDelegation
        {
            StudyId = StudyId,
            SiteId = SiteId,
            StaffUserId = "staff-crc-01",
            TaskCodes = JsonSerializer.Serialize(new[] { "CRF_DATA_ENTRY" }),
            StartDate = "2026-11-15",
            IsActive = true,
            SignedOff = false,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "crc-1",
            ReasonForChange = "Initial assignment",
            VersionIndex = 1,
        };
        _db.CtmsDelegations.Add(delegation);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller(RoleNormalization.ROLE_INVESTIGATOR, "pi-user");
        caller.Request.Headers["X-Change-Reason"] = "Principal Investigator eSignature review and endorsement";

        var request = new DoaSignOffRequest(
            RecordId: delegation.Id,
            ReasonForChange: "Principal Investigator eSignature review and endorsement");

        var result = await DoaEndpoints.SignOffDelegationAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var okResult = result.Should().BeOfType<Ok<DelegationResponseDto>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.SignedOff.Should().BeTrue();
        okResult.Value.VersionIndex.Should().Be(2);

        // Verify entity in DB
        var updated = await _db.CtmsDelegations.FindAsync([delegation.Id], TestContext.Current.CancellationToken);
        updated!.SignedOff.Should().BeTrue();

        // Verify audit log
        var audit = await _db.CtmsAuditLogs.FirstOrDefaultAsync(
            a => a.Action == "DOA_PI_SIGN_OFF" && a.UserId == "pi-user", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain(delegation.Id);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SignOffDelegation_WithNonExistentRecord_Returns404NotFound()
    {
        var caller = Caller(RoleNormalization.ROLE_INVESTIGATOR, "pi-user");
        caller.Request.Headers["X-Change-Reason"] = "Sign-off attempt";

        var request = new DoaSignOffRequest(
            RecordId: "non-existent-rec-id",
            ReasonForChange: "Sign-off attempt");

        var result = await DoaEndpoints.SignOffDelegationAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SignOffDelegation_WhenAlreadySignedOff_Returns400BadRequest()
    {
        await SeedSiteAsync();

        var delegation = new CtmsDelegation
        {
            StudyId = StudyId,
            SiteId = SiteId,
            StaffUserId = "staff-crc-01",
            TaskCodes = JsonSerializer.Serialize(new[] { "CRF_DATA_ENTRY" }),
            StartDate = "2026-11-15",
            IsActive = true,
            SignedOff = true,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "crc-1",
            ReasonForChange = "Initial assignment",
            VersionIndex = 2,
        };
        _db.CtmsDelegations.Add(delegation);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller(RoleNormalization.ROLE_INVESTIGATOR, "pi-user");

        var request = new DoaSignOffRequest(
            RecordId: delegation.Id,
            ReasonForChange: "Duplicate sign-off attempt");

        var result = await DoaEndpoints.SignOffDelegationAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Detail.Should().Contain("already been signed off");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RevokeDelegation_WithValidRecord_SetsInactiveAndAudits()
    {
        await SeedSiteAsync();

        var delegation = new CtmsDelegation
        {
            StudyId = StudyId,
            SiteId = SiteId,
            StaffUserId = "staff-crc-01",
            TaskCodes = JsonSerializer.Serialize(new[] { "CRF_DATA_ENTRY" }),
            StartDate = "2026-11-15",
            IsActive = true,
            SignedOff = true,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "crc-1",
            ReasonForChange = "Initial assignment",
            VersionIndex = 1,
        };
        _db.CtmsDelegations.Add(delegation);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller(RoleNormalization.ROLE_INVESTIGATOR, "pi-user");

        var request = new RevokeDelegationRequest(
            RecordId: delegation.Id,
            ReasonForChange: "Staff member transitioned off the clinical trial study protocol");

        var result = await DoaEndpoints.RevokeDelegationAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var okResult = result.Should().BeOfType<Ok<DelegationResponseDto>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.IsActive.Should().BeFalse();
        okResult.Value.EndDate.Should().NotBeNullOrWhiteSpace();

        // Verify entity in DB
        var updated = await _db.CtmsDelegations.FindAsync([delegation.Id], TestContext.Current.CancellationToken);
        updated!.IsActive.Should().BeFalse();
        updated.EndDate.Should().Be("2026-11-14");

        // Verify audit log
        var audit = await _db.CtmsAuditLogs.FirstOrDefaultAsync(
            a => a.Action == "DOA_REVOKE" && a.UserId == "pi-user", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain(delegation.Id);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RevokeDelegation_WhenAlreadyRevoked_Returns400BadRequest()
    {
        await SeedSiteAsync();

        var delegation = new CtmsDelegation
        {
            StudyId = StudyId,
            SiteId = SiteId,
            StaffUserId = "staff-crc-01",
            TaskCodes = JsonSerializer.Serialize(new[] { "CRF_DATA_ENTRY" }),
            StartDate = "2026-11-15",
            EndDate = "2026-11-14",
            IsActive = false,
            SignedOff = true,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "crc-1",
            ReasonForChange = "Revoked",
            VersionIndex = 2,
        };
        _db.CtmsDelegations.Add(delegation);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller();

        var request = new RevokeDelegationRequest(
            RecordId: delegation.Id,
            ReasonForChange: "Re-revocation attempt");

        var result = await DoaEndpoints.RevokeDelegationAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Detail.Should().Contain("already inactive or revoked");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task GetSiteDoaLog_ReturnsDelegationsAndAuditHistory()
    {
        await SeedSiteAsync(piName: "Dr. Arthur Pendragon");

        var delegation = new CtmsDelegation
        {
            StudyId = StudyId,
            SiteId = SiteId,
            StaffUserId = "staff-user-42",
            TaskCodes = JsonSerializer.Serialize(new[] { "CRF_DATA_ENTRY", "REGULATORY_DOCS" }),
            StartDate = "2026-11-01",
            IsActive = true,
            SignedOff = false,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "crc-1",
            ReasonForChange = "Initial site staffing",
            VersionIndex = 1,
        };
        _db.CtmsDelegations.Add(delegation);

        var audit = new CtmsAuditLog
        {
            Timestamp = _clock.UtcNow,
            UserId = "crc-1",
            UserRole = "CRC",
            Action = "DOA_DELEGATE",
            Details = $"Delegated tasks to staff {delegation.StaffUserId} at site {SiteId}.",
            StudyId = StudyId,
            SiteId = SiteId,
            ReasonForChange = "Initial site staffing",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "crc-1",
            VersionIndex = 1,
        };
        _db.CtmsAuditLogs.Add(audit);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await DoaEndpoints.GetSiteDoaLogAsync(
            SiteId, _db, Caller(), TestContext.Current.CancellationToken);

        var okResult = result.Should().BeOfType<Ok<DoaLogResponseDto>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.Site.Should().Be(SiteId);
        okResult.Value.PiName.Should().Be("Dr. Arthur Pendragon");
        okResult.Value.DelegatedStaff.Should().HaveCount(1);
        okResult.Value.DelegatedStaff[0].StaffUserId.Should().Be("staff-user-42");
        okResult.Value.DelegatedStaff[0].TaskCodes.Should().Equal("CRF_DATA_ENTRY", "REGULATORY_DOCS");
        okResult.Value.AuditHistory.Should().HaveCount(1);
        okResult.Value.AuditHistory[0].Action.Should().Be("DOA_DELEGATE");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task GetSiteDoaLog_WithNonExistentSite_Returns404NotFound()
    {
        var result = await DoaEndpoints.GetSiteDoaLogAsync(
            "SITE-UNKNOWN", _db, Caller(), TestContext.Current.CancellationToken);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ExportSiteDoaPdf_GeneratesPkcs7SignedPdf_AndPassesVerification()
    {
        await SeedSiteAsync();

        var delegation = new CtmsDelegation
        {
            StudyId = StudyId,
            SiteId = SiteId,
            StaffUserId = "staff-user-42",
            TaskCodes = JsonSerializer.Serialize(new[] { "CRF_DATA_ENTRY" }),
            StartDate = "2026-11-01",
            IsActive = true,
            SignedOff = true,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "crc-1",
            ReasonForChange = "Initial site staffing",
            VersionIndex = 1,
        };
        _db.CtmsDelegations.Add(delegation);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller(RoleNormalization.ROLE_INVESTIGATOR, "investigator-01");

        var result = await DoaEndpoints.ExportSiteDoaPdfAsync(
            SiteId, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();

        var fileResult = result.Should().BeOfType<FileContentHttpResult>().Subject;
        fileResult.ContentType.Should().Be("application/pdf");
        fileResult.FileDownloadName.Should().Be($"DOA_Log_{SiteId}.pdf");
        var pdfBytes = fileResult.FileContents.ToArray();
        pdfBytes.Should().NotBeEmpty();

        // Verify Part 11 Electronic Signature using ESignatureVerifier
        var verifier = new ESignatureVerifier();
        var verification = verifier.VerifyPdf(pdfBytes);
        verification.IsValid.Should().BeTrue();
        verification.Status.Should().Be("VALID");

        // Verify audit log entry
        var exportAudit = await _db.CtmsAuditLogs.FirstOrDefaultAsync(
            a => a.Action == "DOA_EXPORT_PDF" && a.UserId == "investigator-01", TestContext.Current.CancellationToken);
        exportAudit.Should().NotBeNull();
        exportAudit!.Details.Should().Contain(SiteId);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ExportSiteDoaPdf_WithNonExistentSite_Returns404NotFound()
    {
        var caller = Caller();

        var result = await DoaEndpoints.ExportSiteDoaPdfAsync(
            "SITE-NONEXISTENT", _db, caller, _clock, TestContext.Current.CancellationToken);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RouteAuthorization_AdmittedAndForbiddenCallers()
    {
        await SeedSiteAsync();

        // 1. Authorized caller (investigator) can query site log
        using var piClient = CreateClient(RoleNormalization.ROLE_INVESTIGATOR, "pi-1");
        var response = await piClient.GetAsync($"/api/v1/ctms/doa/sites/{SiteId}/log", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Caller without sign-off role (e.g. CRC) is forbidden from calling sign-off
        using var crcClient = CreateClient("Clinical Research Coordinator", "crc-1");
        var signOffPayload = new DoaSignOffRequest("rec-1", "CRC trying to sign off");
        var signOffResponse = await crcClient.PostAsJsonAsync(
            "/api/v1/ctms/doa/sign-off", signOffPayload, TestContext.Current.CancellationToken);
        signOffResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Authorized investigator without X-Sig-Token gets 401 REAUTHENTICATION_REQUIRED; with X-Sig-Token reaches handler (404 for missing record)
        var unauthenticatedSignOff = await piClient.PostAsJsonAsync(
            "/api/v1/ctms/doa/sign-off", signOffPayload, TestContext.Current.CancellationToken);
        unauthenticatedSignOff.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var signedPiReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/doa/sign-off")
        {
            Content = JsonContent.Create(signOffPayload),
        };
        signedPiReq.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                userId: "pi-1",
                action: DoaEndpoints.SignOffAction,
                semanticAction: DoaEndpoints.SignOffSemanticAction,
                roles: [RoleNormalization.ROLE_INVESTIGATOR],
                issuedAt: _clock.UtcNow));
        var piSignOffResponse = await piClient.SendAsync(signedPiReq, TestContext.Current.CancellationToken);
        piSignOffResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. Investigator assigned only to SITE-999 is blocked at route scope middleware on GET /sites/{site_id}/log,
        // and cross-site body mutations on POST /doa/delegate return 403 FORBIDDEN_SCOPE
        using var otherSitePiClient = CreateClient(RoleNormalization.ROLE_INVESTIGATOR, "pi-other", [StudyId], ["SITE-999"]);
        var crossSiteRes = await otherSitePiClient.GetAsync($"/api/v1/ctms/doa/sites/{SiteId}/log", TestContext.Current.CancellationToken);
        crossSiteRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var crossSiteDelegateRes = await otherSitePiClient.PostAsJsonAsync(
            "/api/v1/ctms/doa/delegate",
            new DelegationTaskRequest(SiteId, "staff-99", ["CRF_DATA_ENTRY"], "2026-11-15", "Cross-site attempt"),
            TestContext.Current.CancellationToken);
        crossSiteDelegateRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var crossSiteBody = await crossSiteDelegateRes.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        crossSiteBody.GetProperty("code").GetString().Should().Be(CtmsScopeGuard.ForbiddenCode);

        // 5. Sponsor DM assigned only to OTHER-STUDY receives 403 FORBIDDEN_SCOPE when reading SITE-101 bound to STUDY-2026-001
        using var otherStudyDmClient = CreateClient(RoleNormalization.ROLE_SPONSOR_DM, "dm-other", ["OTHER-STUDY"], []);
        var crossStudyRes = await otherStudyDmClient.GetAsync($"/api/v1/ctms/doa/sites/{SiteId}/log", TestContext.Current.CancellationToken);
        crossStudyRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var crossStudyBody = await crossStudyRes.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        crossStudyBody.GetProperty("code").GetString().Should().Be(CtmsScopeGuard.ForbiddenCode);
    }
}
