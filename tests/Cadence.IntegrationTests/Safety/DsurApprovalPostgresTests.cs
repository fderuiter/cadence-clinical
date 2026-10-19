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
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Safety;

/// <summary>
/// DSUR compile and approval through the real safety host on PostgreSQL (#5462): the host's audit interceptor writes
/// hash-chained entries naming the verified compiler and approver, and the stored record names the approver as signer.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DsurApprovalPostgresTests(SafetyPostgresApiFactory factory) : IClassFixture<SafetyPostgresApiFactory>
{
    private static readonly GatewayIdentity Compiler = TestSecurityContext.Identity("pg-mm-compiler", "Sponsor Medical Monitor");
    private static readonly GatewayIdentity Approver = TestSecurityContext.Identity("pg-mm-approver", "Sponsor Medical Monitor");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The compiler cannot approve their own report; a second medical monitor with a step-up token and a reason signs and
    /// locks it, and the audit trail names each verified caller with the reason they gave.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CompileThenApprove_OnPostgres_AuditsTheVerifiedCallers()
    {
        factory.SkipIfUnavailable();
        string id = await CompileAsync();

        using (var compilerClient = factory.CreateSignedClient(Compiler))
        {
            using var selfApproval = await ApproveAsync(compilerClient, id, Compiler.UserId);
            selfApproval.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var approverClient = factory.CreateSignedClient(Approver);
        using var approval = await ApproveAsync(approverClient, id, Approver.UserId);
        approval.StatusCode.Should().Be(HttpStatusCode.OK, await approval.Content.ReadAsStringAsync(Ct));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();
        var record = await db.PeriodicSafetyReports.AsNoTracking().SingleAsync(r => r.Id == id, Ct);
        record.Status.Should().Be("LOCKED");
        record.CreatedBy.Should().Be(Compiler.UserId);
        record.ApprovedByUserId.Should().Be(Approver.UserId);

        var entries = await db.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == DsurCompilerEngine.AuditEntityName && e.EntityId == id)
            .ToListAsync(Ct);
        entries.Should().ContainSingle(e => e.Action == DsurCompilerEngine.CompiledAuditAction)
            .Which.UserId.Should().Be(Compiler.UserId);
        var approved = entries.Should().ContainSingle(e => e.Action == DsurCompilerEngine.ApprovedAuditAction).Subject;
        approved.UserId.Should().Be(Approver.UserId);
        approved.ReasonForChange.Should().Be("DSUR medical review complete");
        approved.BlockHash.Should().NotBeNullOrEmpty();
    }

    private static async Task<HttpResponseMessage> ApproveAsync(HttpClient client, string id, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/safety/dsur/{id}/approve")
        {
            Content = JsonContent.Create(new { reasonForChange = "DSUR medical review complete" }),
        };
        request.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(userId, DsurEndpoints.ApproveAction(id), DsurEndpoints.ApproveSemanticAction));
        return await client.SendAsync(request, Ct);
    }

    private async Task<string> CompileAsync()
    {
        using var client = factory.CreateSignedClient(Compiler);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/safety/dsur/compile",
            new
            {
                compoundName = "Cadence-Compound-PG",
                studyId = "STUDY-PG",
                periodStart = "2025-01-01T00:00:00Z",
                periodEnd = "2025-12-31T23:59:59Z",
                reasonForChange = "Annual DSUR compilation",
            },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PeriodicSafetyReportRecord>(Ct))!.Id;
    }
}
