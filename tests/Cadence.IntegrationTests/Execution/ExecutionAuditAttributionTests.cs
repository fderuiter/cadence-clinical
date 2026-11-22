using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Drives every mutating execution route through the real composition root as a signed caller and checks that
/// each successful write leaves an audit entry naming that caller, in the same save. Nothing may be recorded
/// against an anonymous "system" actor (#4767, 21 CFR 11.10(e)).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionAuditAttributionTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string Reason = "Audit attribution check";
    private const string Study = "STUDY-AUDIT";

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SubjectWrites_AreEachAuditedAgainstTheCaller()
    {
        factory.SkipIfUnavailable();
        var coordinator = Caller("coordinator", "crc") with { SiteIds = [$"SITE-{_run}"] };
        string subject = $"SUBJ-{_run}";

        await AuditedAsync(coordinator, c => c.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = subject, study_id = Study, demographics = new { name = "Audit Participant", birthdate = "1980-01-01" } },
            Ct));
        await AuditedAsync(coordinator, c => c.PostAsJsonAsync(
            $"/api/v1/execution/subjects/{subject}/consent",
            new { protocol_version = new { study_id = Study, version_tag = "1.0", version_index = 1 }, icf_signed = true, icf_signed_date = "2026-01-01" },
            Ct));
        await AuditedAsync(coordinator, c => c.PutAsJsonAsync(
            $"/api/v1/execution/subjects/{subject}/demographics",
            new { strat_factors = new { site = "B" } },
            Ct));
        await AuditedAsync(coordinator, c => c.DeleteAsync($"/api/v1/execution/subjects/{subject}/demographics", Ct));
        await AuditedAsync(coordinator, c => c.PatchAsJsonAsync(
            $"/api/v1/execution/subjects/{subject}/state",
            new { status = "SCREEN_FAILED" },
            Ct));

        await ShouldHaveNoAnonymousEntriesAsync();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SiteComplianceWrites_AreEachAuditedAgainstTheCaller()
    {
        factory.SkipIfUnavailable();
        // eTMF posts completeness as its signed service account; sponsor operations activate the site (ADR-2516).
        var etmf = Caller("etmf-integration", PermissionMatrix.EtmfServiceRole);
        var manager = Caller("site-manager", "Data Manager");
        string site = $"SITE-{_run}";

        await AuditedAsync(etmf, c => c.PostAsJsonAsync(
            "/api/v1/execution/webhooks/etmf",
            new { study_id = Study, site_id = site, milestone = "SITE_ACTIVATION", is_complete = true, missing_artifacts = Array.Empty<string>() },
            Ct));
        long nowUnix = Cadence.Kernel.Time.SystemClock.Instance.UtcNow.ToUnixTimeSeconds();
        var greenlightToken = Cadence.Security.Authentication.GreenlightTokenValidator.GenerateToken(
            site, Study, "US", "ctms-director", TestSecurityContext.GatewaySecret, nowUnix, nowUnix + 86400);
        await AuditedAsync(manager, c => c.PostAsJsonAsync(
            $"/api/v1/execution/sites/{site}/activate",
            new { study_id = Study, greenlight_token = greenlightToken },
            Ct));

        await ShouldHaveNoAnonymousEntriesAsync();
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    private GatewayIdentity Caller(string name, string role) =>
        TestSecurityContext.Identity($"{name}-{_run}", role) with { ChangeReason = Reason };

    /// <summary>Runs <paramref name="call"/> as <paramref name="caller"/> and requires a new audit entry naming them.</summary>
    private async Task<HttpResponseMessage> AuditedAsync(GatewayIdentity caller, Func<HttpClient, Task<HttpResponseMessage>> call)
    {
        int before = await CountEntriesAsync(caller.UserId);
        using var client = factory.CreateSignedClient(caller);

        HttpResponseMessage response = await call(client);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
        (await CountEntriesAsync(caller.UserId)).Should().BeGreaterThan(before,
            "every successful write must leave an audit entry attributed to its caller");
        return response;
    }

    private async Task<int> CountEntriesAsync(string userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        return await db.AuditLogEntries.AsNoTracking().CountAsync(e => e.UserId == userId, Ct);
    }

    private async Task ShouldHaveNoAnonymousEntriesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var anonymous = await db.AuditLogEntries.AsNoTracking()
            .Where(e => e.UserId == "system" || e.UserId == "")
            .CountAsync(Ct);
        anonymous.Should().Be(0);
    }
}
