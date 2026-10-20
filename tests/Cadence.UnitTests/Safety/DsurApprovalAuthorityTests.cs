using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Safety;

/// <summary>
/// DSUR approval through the real safety host (#5462). Approving needs <c>safety_report:approve</c>, a fresh step-up
/// signature token bound to the report, and a reason for change; the signer is the verified caller, never a body field
/// or a default user, and the report's compiler cannot approve it. Before the fix the signer came from the body, fell
/// back to "safety-physician", needed only <c>safety_report:create</c>, no token and no reason, and a
/// <c>/periodic-reports/{id}/sign</c> alias did the same.
/// </summary>
public sealed class DsurApprovalAuthorityTests(DsurApprovalAuthorityTests.SafetyFactory factory)
    : IClassFixture<DsurApprovalAuthorityTests.SafetyFactory>
{
    private static readonly GatewayIdentity Compiler = TestSecurityContext.Identity("mm-compiler", "Sponsor Medical Monitor");
    private static readonly GatewayIdentity Approver = TestSecurityContext.Identity("mm-approver", "Sponsor Medical Monitor");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The compile route records the verified caller as the report's creator.</summary>
    [Fact]
    public async Task Compile_RecordsTheVerifiedCallerAsCreator()
    {
        string id = await CompileAsync();

        (await RecordAsync(id))!.CreatedBy.Should().Be(Compiler.UserId);
    }

    /// <summary>
    /// The signer is the verified caller whatever the body says: a <c>signerUserId</c> in the body is ignored, the stored
    /// signature manifest and approver name the caller, and the audit entry names the caller with the given reason.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-008")]
    public async Task Approve_WithTokenAndReason_SignsAsTheVerifiedCaller()
    {
        string id = await CompileAsync();
        using var client = factory.CreateSignedClient(Approver);
        var body = new { signerUserId = "someone-else", signingReason = "Medical review approval", reasonForChange = "DSUR medical review complete" };

        using var response = await ApproveAsync(client, id, TokenFor(Approver.UserId, id), body);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var record = (await RecordAsync(id))!;
        record.Status.Should().Be("LOCKED");
        record.ApprovedByUserId.Should().Be(Approver.UserId);
        record.EsignatureManifestId.Should().Contain(Approver.UserId).And.NotContain("someone-else");
        var audit = await AuditAsync(id, DsurCompilerEngine.ApprovedAuditAction);
        audit.Should().ContainSingle();
        audit[0].UserId.Should().Be(Approver.UserId);
        audit[0].ReasonForChange.Should().Be("DSUR medical review complete");
    }

    /// <summary>An unsigned (anonymous) request is refused and the report stays a draft.</summary>
    [Fact]
    public async Task Approve_Anonymous_Returns401()
    {
        string id = await CompileAsync();
        using var client = factory.CreateClient();

        using var response = await ApproveAsync(client, id, TokenFor(Approver.UserId, id), Reason());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RecordAsync(id))!.Status.Should().Be("DRAFT");
    }

    /// <summary>Without a step-up token, or with a token for another report, approval needs re-authentication.</summary>
    [Fact]
    [GxPRequirement("PRD-PVG-008")]
    public async Task Approve_WithoutATokenForThisReport_Returns401ReauthenticationRequired()
    {
        string id = await CompileAsync();
        string other = await CompileAsync();
        using var client = factory.CreateSignedClient(Approver);

        using var noToken = await ApproveAsync(client, id, null, Reason());
        using var wrongReport = await ApproveAsync(client, id, TokenFor(Approver.UserId, other), Reason());

        noToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await noToken.Content.ReadAsStringAsync(Ct)).Should().Contain(SigTokenVerifier.ReauthenticationRequired);
        wrongReport.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RecordAsync(id))!.Status.Should().Be("DRAFT");
    }

    /// <summary>
    /// Only holders of <c>safety_report:approve</c> may approve: the system administrator compiles and reads reports but
    /// does not sign them, and other roles hold nothing.
    /// </summary>
    [Theory]
    [GxPRequirement("PRD-PVG-008")]
    [InlineData("sysadmin")]
    [InlineData("CRA")]
    [InlineData("Site Investigator")]
    public async Task Approve_WithoutTheApprovePermission_Returns403(string role)
    {
        string id = await CompileAsync();
        var caller = TestSecurityContext.Identity("not-a-safety-physician", role);
        using var client = factory.CreateSignedClient(caller);

        using var response = await ApproveAsync(client, id, TokenFor(caller.UserId, id), Reason());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RecordAsync(id))!.Status.Should().Be("DRAFT");
    }

    /// <summary>The compiler of a report cannot approve it, even holding the permission and a valid token.</summary>
    [Fact]
    public async Task Approve_ByTheCompiler_Returns403ProblemDetails()
    {
        string id = await CompileAsync();
        using var client = factory.CreateSignedClient(Compiler);

        using var response = await ApproveAsync(client, id, TokenFor(Compiler.UserId, id), Reason());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await RecordAsync(id))!.Status.Should().Be("DRAFT");
    }

    /// <summary>An approval without a reason for change is refused with 400 and the report stays a draft.</summary>
    [Fact]
    public async Task Approve_WithoutReasonForChange_Returns400()
    {
        string id = await CompileAsync();
        using var client = factory.CreateSignedClient(Approver);

        using var response = await ApproveAsync(client, id, TokenFor(Approver.UserId, id), new { signingReason = "Approval" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(id))!.Status.Should().Be("DRAFT");
    }

    /// <summary>A LOCKED report cannot be approved again: 409 Conflict.</summary>
    [Fact]
    [GxPRequirement("PRD-PVG-004")]
    public async Task Approve_ALockedReport_Returns409()
    {
        string id = await CompileAsync();
        using var client = factory.CreateSignedClient(Approver);
        using var first = await ApproveAsync(client, id, TokenFor(Approver.UserId, id), Reason());
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = TestSecurityContext.Identity("mm-second-approver", "Sponsor Medical Monitor");
        using var secondClient = factory.CreateSignedClient(second);

        using var response = await ApproveAsync(secondClient, id, TokenFor(second.UserId, id), Reason());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RecordAsync(id))!.ApprovedByUserId.Should().Be(Approver.UserId);
    }

    /// <summary>The <c>/periodic-reports/{id}/sign</c> alias is gone.</summary>
    [Fact]
    public async Task SignAlias_IsNotMapped()
    {
        string id = await CompileAsync();
        using var client = factory.CreateSignedClient(Approver);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/safety/periodic-reports/{id}/sign") { Content = JsonContent.Create(Reason()) };
        request.Headers.Add(GatewayHeaders.SigToken, TokenFor(Approver.UserId, id));
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await RecordAsync(id))!.Status.Should().Be("DRAFT");
    }

    private static object Reason() => new { reasonForChange = "DSUR medical review complete" };

    private static string TokenFor(string userId, string reportId) =>
        TestSecurityContext.CreateSigToken(userId, DsurEndpoints.ApproveAction(reportId), DsurEndpoints.ApproveSemanticAction);

    private static async Task<HttpResponseMessage> ApproveAsync(HttpClient client, string id, string? token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/safety/dsur/{id}/approve") { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Add(GatewayHeaders.SigToken, token);
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<string> CompileAsync()
    {
        using var client = factory.CreateSignedClient(Compiler);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/safety/dsur/compile",
            new
            {
                compoundName = "Cadence-Compound-77",
                studyId = "STUDY-2025",
                periodStart = "2025-01-01T00:00:00Z",
                periodEnd = "2025-12-31T23:59:59Z",
                reasonForChange = "Annual DSUR compilation",
            },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var record = await response.Content.ReadFromJsonAsync<PeriodicSafetyReportRecord>(Ct);
        return record!.Id;
    }

    private async Task<PeriodicSafetyReportRecord?> RecordAsync(string id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();
        return await db.PeriodicSafetyReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, Ct);
    }

    private async Task<System.Collections.Generic.List<Cadence.Core.Domain.Audit.AuditLogEntry>> AuditAsync(string id, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();
        return await db.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == DsurCompilerEngine.AuditEntityName && e.EntityId == id && e.Action == action)
            .ToListAsync(Ct);
    }

    /// <summary>
    /// The safety host on an isolated EF Core in-memory database, with the same audit interceptor the host registers
    /// (actor: the request's verified caller).
    /// </summary>
    public sealed class SafetyFactory : WebApplicationFactory<Cadence.Apps.Safety.Program>
    {
        private readonly string _databaseName = $"safety-dsur-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                var inMemory = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
                services.RemoveAll<DbContextOptions<SafetyDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<SafetyDbContext>>();
                services.AddDbContext<SafetyDbContext>((sp, options) => options
                    .UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(inMemory)
                    .AddInterceptors(new GxPAuditInterceptor(
                        () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User.FindFirst(CadenceClaimTypes.Subject)?.Value
                            ?? "test-seeding",
                        sp.GetRequiredService<IClock>())));

                // The durable token store needs PostgreSQL; DsurApprovalPostgresTests covers it.
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(sp => new ReplayCache(sp.GetRequiredService<IClock>()));
            });
        }
    }
}
