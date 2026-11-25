using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Database.Audit;
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

        var caller = Caller(RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR, "pi-user");
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
        var caller = Caller(RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR, "pi-user");
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

        var caller = Caller(RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR, "pi-user");

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
        using var investigatorClient = CreateClient(RoleNormalization.ROLE_INVESTIGATOR, "inv-1");
        var response = await investigatorClient.GetAsync($"/api/v1/ctms/doa/sites/{SiteId}/log", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Callers without PI sign-off role (CRC and base/sub-investigator) are forbidden from calling sign-off
        using var crcClient = CreateClient("Clinical Research Coordinator", "crc-1");
        var signOffPayload = new DoaSignOffRequest("rec-1", "CRC trying to sign off");
        var signOffResponse = await crcClient.PostAsJsonAsync(
            "/api/v1/ctms/doa/sign-off", signOffPayload, TestContext.Current.CancellationToken);
        signOffResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var investigatorSignOffResponse = await investigatorClient.PostAsJsonAsync(
            "/api/v1/ctms/doa/sign-off", signOffPayload, TestContext.Current.CancellationToken);
        investigatorSignOffResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Authorized Principal Investigator requires X-Sig-Token (401 without it) and reaches sign-off endpoint with X-Sig-Token (returns 404 for non-existent record rather than 403 Forbidden)
        using var piClient = CreateClient(RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR, "pi-1");
        var unsignedPiSignOffResponse = await piClient.PostAsJsonAsync(
            DoaEndpoints.SignOffAction, signOffPayload, TestContext.Current.CancellationToken);
        unsignedPiSignOffResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var signedPiReq = new HttpRequestMessage(HttpMethod.Post, DoaEndpoints.SignOffAction)
        {
            Content = JsonContent.Create(signOffPayload),
        };
        signedPiReq.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                "pi-1",
                DoaEndpoints.SignOffAction,
                DoaEndpoints.SignOffSemanticAction,
                [RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR],
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

    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public void PermissionMatrix_CtmsDoaSignOff_RestrictedToPiRolesAndSysadmin()
    {
        static Principal PrincipalForRole(string role) => new()
        {
            UserId = "user-check",
            TenantId = TestSecurityContext.TenantCode,
            Roles = RoleNormalization.GetNormalizedRoles([role]),
            RawRoles = [role],
        };

        string[] admittedRoles =
        [
            RoleNormalization.ROLE_SYSADMIN,
            RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR,
            RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN,
            RoleNormalization.ROLE_LEAD_INVESTIGATOR,
            "Principal Investigator",
        ];

        foreach (string role in admittedRoles)
        {
            var principal = PrincipalForRole(role);
            PermissionMatrix.HasPermission(principal, "ctms_doa:sign_off").Should().BeTrue($"role '{role}' should hold ctms_doa:sign_off");
            PermissionMatrix.HasPermission(principal, "ctms_doa:sign").Should().BeTrue($"role '{role}' should hold ctms_doa:sign");
        }

        string[] deniedRoles =
        [
            "sub_investigator",
            "sub investigator",
            "sub-investigator",
            RoleNormalization.ROLE_INVESTIGATOR,
            RoleNormalization.ROLE_CRC,
            RoleNormalization.ROLE_CRA_CANONICAL,
            "monitor",
            RoleNormalization.ROLE_SPONSOR_DM,
            RoleNormalization.ROLE_SPONSOR_MM,
            RoleNormalization.ROLE_AUDITOR_CANONICAL,
        ];

        foreach (string role in deniedRoles)
        {
            var principal = PrincipalForRole(role);
            PermissionMatrix.HasPermission(principal, "ctms_doa:sign_off").Should().BeFalse($"role '{role}' must not hold ctms_doa:sign_off");
            PermissionMatrix.HasPermission(principal, "ctms_doa:sign").Should().BeFalse($"role '{role}' must not hold ctms_doa:sign");
        }

        var subInvPrincipal = PrincipalForRole("sub_investigator");
        var baseInvPrincipal = PrincipalForRole(RoleNormalization.ROLE_INVESTIGATOR);
        var piPrincipal = PrincipalForRole(RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR);

        foreach (string nonSignAction in new[] { "read", "assign", "create", "revoke", "update", "export" })
        {
            string permission = $"ctms_doa:{nonSignAction}";
            PermissionMatrix.HasPermission(subInvPrincipal, permission).Should().BeTrue();
            PermissionMatrix.HasPermission(baseInvPrincipal, permission).Should().BeTrue();
            PermissionMatrix.HasPermission(piPrincipal, permission).Should().BeTrue();
        }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task SignOffDelegation_ViaProgramWebFactory_ForbidsSubInvestigatorAndAdmitsPrincipalInvestigator()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new CtmsStudySiteScopeIsolationTests.CtmsInMemoryWebFactory();

        string delegationId;
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            seedDb.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate
            {
                SiteId = SiteId,
                SiteName = "Site 101",
                StudyId = StudyId,
                CountryCode = "US",
                GreenlightCertifiedBy = "Dr. Elena Rostova",
                CreatedAt = _clock.UtcNow.UtcDateTime,
                CreatedBy = "seed",
                ReasonForChange = "Test setup",
                VersionIndex = 1,
            });

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
            seedDb.CtmsDelegations.Add(delegation);
            using (AmbientAuditActorScope.BeginScope("svc:test-seed", "Test setup"))
            {
                await seedDb.SaveChangesAsync(ct);
            }

            delegationId = delegation.Id;
        }

        string requestJson = JsonSerializer.Serialize(new
        {
            record_id = delegationId,
            reason_for_change = "Endorsing delegation",
        });

        HttpRequestMessage BuildSignOffRequest(string bodyJson, string userId, string role)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/doa/sign-off")
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Change-Reason", "Endorsing delegation");
            var factoryClock = factory.Services.GetRequiredService<IClock>();
            req.Headers.Add(
                "X-Sig-Token",
                TestSecurityContext.CreateSigToken(
                    userId,
                    "/api/v1/ctms/doa/sign-off",
                    "ctms.doa.sign_off",
                    [role],
                    issuedAt: factoryClock.UtcNow));
            return req;
        }

        // 1. Sub-investigator identity (default scope and explicit site scope) receives HTTP 403 and SignedOff stays false
        using var subInvDefaultClient = SignedClientExtensions.CreateSignedClient(
            factory,
            TestSecurityContext.Identity("sub-inv-1", "sub_investigator"));
        using (var req = BuildSignOffRequest(requestJson, "sub-inv-1", "sub_investigator"))
        {
            using var res = await subInvDefaultClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var subInvSiteClient = SignedClientExtensions.CreateSignedClient(
            factory,
            TestSecurityContext.Identity("sub-inv-1", "sub_investigator") with
            {
                StudyIds = [StudyId],
                SiteIds = [SiteId],
            });
        using (var req = BuildSignOffRequest(requestJson, "sub-inv-1", "sub_investigator"))
        {
            using var res = await subInvSiteClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var baseInvClient = SignedClientExtensions.CreateSignedClient(
            factory,
            TestSecurityContext.Identity("inv-1", "investigator") with
            {
                StudyIds = [StudyId],
                SiteIds = [SiteId],
            });
        using (var req = BuildSignOffRequest(requestJson, "inv-1", "investigator"))
        {
            using var res = await baseInvClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        await using (var verifyForbiddenScope = factory.Services.CreateAsyncScope())
        {
            var verifyDb = verifyForbiddenScope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            var unendorsed = await verifyDb.CtmsDelegations
                .AsNoTracking()
                .SingleAsync(d => d.Id == delegationId, ct);
            unendorsed.SignedOff.Should().BeFalse();
        }

        // 2. Principal Investigator receives HTTP 200 and SignedOff transitions to true
        using var piClient = SignedClientExtensions.CreateSignedClient(
            factory,
            TestSecurityContext.Identity("pi-1", "principal_investigator") with
            {
                StudyIds = [StudyId],
                SiteIds = [SiteId],
            });
        using (var req = BuildSignOffRequest(requestJson, "pi-1", "principal_investigator"))
        {
            req.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(
                    "pi-1",
                    "/api/v1/ctms/doa/sign-off",
                    "ctms.doa.sign_off",
                    [RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR]));
            using var res = await piClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await res.Content.ReadFromJsonAsync<DelegationResponseDto>(ct);
            dto.Should().NotBeNull();
            dto!.SignedOff.Should().BeTrue();
        }

        await using (var verifyOkScope = factory.Services.CreateAsyncScope())
        {
            var verifyDb = verifyOkScope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            var endorsed = await verifyDb.CtmsDelegations
                .AsNoTracking()
                .SingleAsync(d => d.Id == delegationId, ct);
            endorsed.SignedOff.Should().BeTrue();

            var audit = await verifyDb.CtmsAuditLogs
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Action == "DOA_PI_SIGN_OFF" && a.UserId == "pi-1", ct);
            audit.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task FreshSiteDoa_ReturnsExplicitStatuses_AndFilesEtmf050204OnSignOffAndServiceBatchEndorse()
    {
        var ct = TestContext.Current.CancellationToken;
        var filedArtifacts = new List<Cadence.Apps.Ctms.Services.CtmsEtmfFilingRequest>();
        var fakeEtmf = new CapturingCtmsEtmfFilingClient(filedArtifacts);

        // 1. Fresh unseeded site log returns empty entries (200 OK) when explicit study_id is within caller scope
        var crcCaller = Caller("Clinical Research Coordinator", "crc-fresh", [StudyId], ["SITE-FRESH-01"]);
        var freshLogRes = await DoaEndpoints.GetSiteDoaLogAsync(
            "SITE-FRESH-01", _db, crcCaller, ct, studyId: StudyId);
        var freshLog = freshLogRes.Should().BeOfType<Ok<DoaLogResponseDto>>().Subject.Value!;
        freshLog.Site.Should().Be("SITE-FRESH-01");
        freshLog.DelegatedStaff.Should().BeEmpty();

        // 2. Delegate on fresh site creates PENDING_PI_SIGNATURE record without 404
        var delReq = new DelegationTaskRequest(
            Site: "SITE-FRESH-01",
            StaffUserId: "crc-fresh",
            TaskCodes: ["INFORMED_CONSENT"],
            StartDate: "2026-11-20",
            ReasonForChange: "Fresh site startup delegation",
            Study: StudyId,
            StaffName: "Casey Coordinator",
            Role: "Clinical Research Coordinator");
        var createdRes = await DoaEndpoints.DelegateTasksAsync(delReq, _db, crcCaller, _clock, ct);
        var created = createdRes.Should().BeOfType<Created<DelegationResponseDto>>().Subject.Value!;
        created.Status.Should().Be("PENDING_PI_SIGNATURE");
        created.Study.Should().Be(StudyId);

        // 3. PI sign-off transitions status to ACTIVE and files 05.02.04 to eTMF
        var piCaller = Caller(RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR, "pi.site101", [StudyId], ["SITE-FRESH-01"]);
        var signedRes = await DoaEndpoints.SignOffDelegationAsync(
            new DoaSignOffRequest(created.RecordId, "PI step-up endorsement", StudyId),
            _db,
            piCaller,
            _clock,
            ct,
            etmfFilingClient: fakeEtmf);
        var signed = signedRes.Should().BeOfType<Ok<DelegationResponseDto>>().Subject.Value!;
        signed.Status.Should().Be("ACTIVE");
        signed.EtmfArtifactId.Should().Be("DOC-050204-1");
        filedArtifacts.Should().ContainSingle(f => f.ArtifactCode == "05.02.04" && f.SiteCode == "SITE-FRESH-01");

        // 4. Service batch endorsement endorses pending records and files 05.02.04
        var secondRes = await DoaEndpoints.DelegateTasksAsync(
            delReq with { StaffUserId = "subinv-fresh", TaskCodes = ["PHYSICAL_EXAM"] },
            _db,
            crcCaller,
            _clock,
            ct);
        var second = secondRes.Should().BeOfType<Created<DelegationResponseDto>>().Subject.Value!;
        var svcCaller = Caller(PermissionMatrix.KnowledgeServiceRole, "svc:knowledge", [StudyId], []);
        var batchRes = await DoaEndpoints.EndorseSignedBatchAsync(
            new BulkDoaEndorseRequest(StudyId, [second.RecordId], "pi.site101", "Knowledge batch sign-off"),
            _db,
            svcCaller,
            _clock,
            ct,
            etmfFilingClient: fakeEtmf);
        var batchDto = batchRes.Should().BeOfType<Ok<BulkDoaEndorseResponseDto>>().Subject.Value!;
        batchDto.ApprovedCount.Should().Be(1);
        batchDto.Records.Should().ContainSingle().Which.EtmfArtifactId.Should().Be("DOC-050204-2");

        // 5. Revoking transitions status to REVOKED
        var revokedRes = await DoaEndpoints.RevokeDelegationAsync(
            new RevokeDelegationRequest(created.RecordId, "Staff rotated off study", StudyId),
            _db,
            piCaller,
            _clock,
            ct);
        var revoked = revokedRes.Should().BeOfType<Ok<DelegationResponseDto>>().Subject.Value!;
        revoked.Status.Should().Be("REVOKED");
    }

    private sealed class CapturingCtmsEtmfFilingClient(List<Cadence.Apps.Ctms.Services.CtmsEtmfFilingRequest> captured)
        : Cadence.Apps.Ctms.Services.ICtmsEtmfFilingClient
    {
        public Task<string?> FileSignedArtifactAsync(
            Cadence.Apps.Ctms.Services.CtmsEtmfFilingRequest filing,
            System.Threading.CancellationToken ct)
        {
            captured.Add(filing);
            return Task.FromResult<string?>($"DOC-050204-{captured.Count}");
        }
    }
}
